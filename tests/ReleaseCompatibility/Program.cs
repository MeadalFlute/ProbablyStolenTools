using System.Reflection;
using System.Runtime.Loader;
using System.Xml.Linq;
using System.Globalization;
using Tomlet;
using Tomlet.Models;

if (args.Length > 0 && args[0] == "--import-settings")
{
    if (File.Exists(args[2])) throw new IOException("Refusing to overwrite existing full-game settings.");
    var source = TomlParser.ParseFile(args[1]);
    var entries = new List<XElement>();
    foreach (string section in new[] { "AutoAppraisal", "Dialogue", "Inspection", "Panel", "Rewind", "History", "CustomerCheckpoint" })
    {
        if (!source.ContainsKey("ProbablyStolenTools_" + section)) continue;
        var table = source.GetSubTable("ProbablyStolenTools_" + section);
        foreach (var pair in table.Entries)
            entries.Add(new XElement("Entry", new XAttribute("key", section + "." + pair.Key),
                new XAttribute("value", pair.Value.StringValue)));
    }
    foreach (string name in new[] { "Diagnostics", "SelfTest", "PreviewPanel" })
        entries.Add(new XElement("Entry", new XAttribute("key", "Debug." + name), new XAttribute("value", "False")));
    new XDocument(new XElement("ProbablyStolenTools", entries)).Save(args[2]);
    Console.WriteLine("Imported " + entries.Count + " settings; debug automation disabled.");
    return;
}

string gameDir = args[0];
string dll = Path.GetFullPath(args[1]);
string managed = Path.Combine(gameDir, "Probably Stolen_Data", "Managed");
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string path = Path.Combine(managed, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
var game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed, "Assembly-CSharp.dll"));
var mod = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
var types = mod.GetTypes();
var plugin = types.Single(t => t.GetInterfaces().Any(i => i.Name == "IMod"));
Console.WriteLine("IMod entry: " + plugin.FullName);
int hooks = 0;
foreach (var type in types)
{
    var attributes = type.GetCustomAttributesData().Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch").ToArray();
    if (attributes.Length == 0) continue;
    Type target = null;
    string name = null;
    foreach (var attribute in attributes)
        foreach (var argument in attribute.ConstructorArguments)
        {
            if (argument.ArgumentType == typeof(Type)) target = (Type)argument.Value;
            if (argument.ArgumentType == typeof(string)) name = (string)argument.Value;
        }
    if (target == null || name == null) throw new Exception("Unresolved hook: " + type.FullName);
    var candidates = target.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
        .Where(m => m.Name == name).ToArray();
    if (candidates.Length != 1) throw new Exception($"Ambiguous/missing hook {target.Name}.{name}: {candidates.Length}");
    var original = candidates[0];
    foreach (var patch in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
    {
        if (!patch.GetCustomAttributesData().Any(a => a.AttributeType.Namespace == "HarmonyLib")) continue;
        foreach (var parameter in patch.GetParameters())
        {
            Type expected;
            if (parameter.Name == "__state") continue;
            if (parameter.Name == "__exception") expected = typeof(Exception);
            else if (parameter.Name == "__instance") expected = target;
            else if (parameter.Name == "__result") expected = original.ReturnType;
            else if (parameter.Name.StartsWith("__") && int.TryParse(parameter.Name.Substring(2), out int index))
                expected = original.GetParameters()[index].ParameterType;
            else
                expected = original.GetParameters().SingleOrDefault(p => p.Name == parameter.Name)?.ParameterType
                    ?? throw new Exception("Missing injected argument: " + type.Name + "." + parameter.Name);
            if (expected.IsByRef) expected = expected.GetElementType();
            Type actual = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType() : parameter.ParameterType;
            if (!actual.IsAssignableFrom(expected))
                throw new Exception($"Wrong injected type {type.Name}.{parameter.Name}: {actual} / {expected}");
        }
    }
    Console.WriteLine("HOOK " + target.Name + "." + name);
    hooks++;
}
var roots = new Dictionary<string, string[]>
{
    ["BargainUIManager"] = new[] { "trackedClient", "baseTradeRepMultiplier", "discountOffered",
        "discountPercent", "markupOffered", "markupPercent", "markupApplied", "blackmailOffered",
        "blackmailApplied", "buyingMarkupOffered", "buyingMarkupPercent", "buyingDiscountOffered",
        "buyingDiscountPercent", "buyingDiscountApplied", "threatenOffered", "threatenApplied",
        "repPreviewActive", "repPreviewType", "repPreviewPercent" },
    ["DialogUIManager"] = new[] { "currentDialog", "isConversationActiveFlag", "isSkipCurrentDialog",
        "isWaitingForManualAdvance", "isAutoAdvancePending", "isEndPending", "displayingUncompletedDialog",
        "scheduledEndActionLines", "isSecMonologuePlaying", "conversationEpoch", "advanceSerial" },
    ["InspectionUIManager"] = new[] { "currentInspectingItem", "pendingConfrontItemId", "pendingConfrontType", "isEditingAppraisalField" },
    ["NegociationUIManager"] = new[] { "currentNegociatedItem" },
    ["StoreUIManager"] = new[] { "isAboutToStartMainDialog", "isReportedAug", "isTyping" },
    ["StoreShutterButton"] = new[] { "isClosed", "elapsedTime", "duration" },
    ["StoreClientMono"] = new[] { "idleDecisionClient", "idleSuppressed", "idleShakeHorizontal", "defaultIdleAxis",
        "defaultIdleAmplitude", "defaultIdleFrequency", "ignoreOnLeftFlag", "fadeMode", "walkingIn", "idleAnimator",
        "dOTweenPath", "leaveTimeScale" },
    ["IdleAnimator"] = new[] { "amplitude", "frequency", "axis", "originalPosition", "hasOriginal", "timer", "playing" },
    ["PhoneUIManager"] = new[] { "currentNumber", "isCalling", "callingTimer", "callTimerRoll", "isTalking", "sequenceTimer", "currentPhoneClient" },
    ["GameMaster"] = new[] { "worldTags" },
    ["GameItemElement"] = new[] { "validateLock", "appliedMinX", "appliedMinY",
        "appliedOrientation", "appliedGridSize", "appliedFlipped" },
    ["ItemMouseDoubleClickHandler"] = new[] { "lastKey" },
    ["ItemMultiSelectHandler"] = new[] { "selectedItems" }
};
int fields = 0;
foreach (var root in roots)
{
    var type = game.GetType(root.Key, true);
    foreach (var field in root.Value)
    {
        if (type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) == null)
            throw new Exception("Missing snapshot field: " + root.Key + "." + field);
        fields++;
    }
}
Console.WriteLine($"PASS {hooks} hook targets and {fields} snapshot/private fields");
var tweenAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed, "DOTween.dll"));
var pathAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed, "DOTweenPro.dll"));
var tweenType = tweenAssembly.GetType("DG.Tweening.Tween", true);
var pathType = pathAssembly.GetType("DG.Tweening.DOTweenPath", true);
var tweenExtensions = tweenAssembly.GetType("DG.Tweening.TweenExtensions", true);
var customerMono = game.GetType("StoreClientMono", true);
foreach (string name in new[] { "dOTweenPath", "pathRight" })
    if (customerMono.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.FieldType != pathType)
        throw new Exception("Customer path contract changed: " + name);
if (pathType.GetField("tween")?.FieldType != tweenType)
    throw new Exception("Customer tween storage contract changed.");
foreach (string name in new[] { "onPlay", "onPause", "onRewind", "onUpdate", "onStepComplete",
    "onComplete", "onKill", "onWaypointChange", "timeScale", "isBackwards" })
    if (tweenType.GetField(name, BindingFlags.Public | BindingFlags.Instance) == null)
        throw new Exception("Missing customer tween field: " + name);
if (tweenExtensions.GetMethod("Goto", new[] { tweenType, typeof(float), typeof(bool) }) == null
    || tweenExtensions.GetMethod("Elapsed", new[] { tweenType, typeof(bool) }) == null
    || tweenExtensions.GetMethod("IsActive", new[] { tweenType }) == null
    || tweenExtensions.GetMethod("IsPlaying", new[] { tweenType }) == null)
    throw new Exception("Customer tween seek/probe contract changed.");
Console.WriteLine("PASS customer original-path cursor and callback suppression contracts");
var multiSelect = game.GetType("ItemMultiSelectHandler", true);
foreach (string property in new[] { "HasSelection", "IsGroupDragging" })
    if (multiSelect.GetProperty(property)?.PropertyType != typeof(bool))
        throw new Exception("Missing native multi-select property: " + property);
if (multiSelect.GetMethod("Cancel", Type.EmptyTypes)?.ReturnType != typeof(bool)
    || multiSelect.GetField("key")?.FieldType.FullName != "UnityEngine.KeyCode")
    throw new Exception("Native multi-select cancellation/key contract changed.");
Console.WriteLine("PASS native multi-select gesture and cancellation contracts");
var loadedNames = System.IO.Directory.GetFiles(managed, "*.dll")
    .Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
game.GetType("ModLoader").GetField("gameAssemblyNames", BindingFlags.Static | BindingFlags.NonPublic)
    .SetValue(null, loadedNames);
var scanner = game.GetType("ModScanner");
var result = scanner.GetMethod("Scan").Invoke(null, new object[] { new[] { dll }, null, null, false });
var resultType = result.GetType();
foreach (var field in resultType.GetFields(BindingFlags.Public | BindingFlags.Instance))
{
    object value = field.GetValue(result);
    if (value is System.Collections.IEnumerable sequence && value is not string)
        Console.WriteLine("SCAN " + field.Name + ": " + string.Join(", ", sequence.Cast<object>()));
    else Console.WriteLine("SCAN " + field.Name + ": " + value);
}
if ((bool)resultType.GetProperty("Refused").GetValue(result))
    throw new Exception("Official scanner refused the mod.");
var manifest = XDocument.Load(args[2]).Root;
var declared = manifest.Element("Capabilities").Elements("Capability").Select(e => e.Value).ToHashSet();
var detected = ((System.Collections.IEnumerable)resultType.GetField("Capabilities").GetValue(result)).Cast<string>();
foreach (string capability in detected)
    if (!declared.Contains(capability)) throw new Exception("Undeclared capability: " + capability);
Console.WriteLine("PASS official mod scanner and manifest capabilities");

var scopeType = mod.GetType("ProbablyStolenTools.TooltipNameScope", true);
var scopeFlags = BindingFlags.NonPublic | BindingFlags.Static;
var activeScope = scopeType.GetProperty("Active", scopeFlags);
foreach (string patchName in new[] { "TooltipNameScopePatch", "TooltipNameScopeUsePatch", "TooltipNameScopeInsertPatch" })
{
    var patch = mod.GetType("ProbablyStolenTools." + patchName, true);
    var enter = patch.GetMethod("Prefix", scopeFlags);
    var exit = patch.GetMethod("Finalizer", scopeFlags);
    if (exit == null || !exit.GetCustomAttributesData().Any(a => a.AttributeType.Name == "HarmonyFinalizer"))
        throw new Exception("Tooltip scope must exit even when native localization throws: " + patchName);
    for (int i = 0; i < 12; i++)
    {
        object[] state = { false };
        enter.Invoke(null, state);
        if (!(bool)state[0]) throw new Exception("Tooltip prefix did not record its own scope entry.");
    }
    exit.Invoke(null, new object[] { false });
    for (int i = 0; i < 11; i++) exit.Invoke(null, new object[] { true });
    if (!(bool)activeScope.GetValue(null)) throw new Exception("Nested tooltip scope ended too early.");
    try { throw new InvalidOperationException("Modeled native localization failure"); }
    catch (InvalidOperationException) { }
    finally { exit.Invoke(null, new object[] { true }); }
    if ((bool)activeScope.GetValue(null)) throw new Exception("Tooltip scope leaked after native error.");
}
Console.WriteLine("PASS three tooltip exception finalizers and deep nesting do not leak name-replacement scope");

string testPath = Path.Combine(AppContext.BaseDirectory, "PSTools-config-" + Guid.NewGuid().ToString("N") + ".xml");
var configType = mod.GetType("ProbablyStolenTools.ModConfig", true);
var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
object OpenConfig() => configType.GetConstructor(flags, null, new[] { typeof(string) }, null).Invoke(new object[] { testPath });
object Bind(object config, Type type, string key, object value)
    => configType.GetMethod("Bind", flags).MakeGenericMethod(type)
        .Invoke(config, new[] { "Inspection", key, value, "" });
object Read(object entry) => entry.GetType().GetProperty("Value", flags).GetValue(entry);
void Set(object entry, object value) => entry.GetType().GetProperty("Value", flags).SetValue(entry, value);
var culture = CultureInfo.CurrentCulture;
try
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
    var config = OpenConfig();
    var enabled = Bind(config, typeof(bool), "ShowQuickInspection", true);
    var x = Bind(config, typeof(float), "SummaryPositionX", 1f);
    configType.GetProperty("SaveOnConfigSet", flags).SetValue(config, true);
    Set(enabled, false); Set(x, 0.625f);
    var reread = OpenConfig();
    if ((bool)Read(Bind(reread, typeof(bool), "ShowQuickInspection", true))
        || (float)Read(Bind(reread, typeof(float), "SummaryPositionX", 1f)) != 0.625f)
        throw new Exception("Native configuration disk round trip failed.");
    configType.GetMethod("Save", flags).Invoke(reread, null);
    if (!File.Exists(testPath + ".bak")) throw new Exception("Settings backup was not retained.");
    Console.WriteLine("PASS native XML settings immediate save, invariant culture and backup");
}
finally
{
    CultureInfo.CurrentCulture = culture;
    foreach (string path in new[] { testPath, testPath + ".bak", testPath + ".tmp" })
        if (File.Exists(path)) File.Delete(path);
}

// Allocate only plain data objects without constructors, Unity callbacks or save APIs.
var dataFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
object Data(string name) => System.Runtime.Serialization.FormatterServices.GetUninitializedObject(game.GetType(name, true));
var player = Data("PlayerStore");
var station = Data("StoreStation");
var item = Data("GameItemElement");
var cacheFields = new[] { "appliedMinX", "appliedMinY", "appliedOrientation", "appliedGridSize", "appliedFlipped", "validateLock" }
    .Select(name => item.GetType().GetField(name, dataFlags)).ToArray();
foreach (var field in cacheFields)
    field.SetValue(item, field.FieldType == typeof(bool) ? (object)false : 16);
var unitProperty = item.GetType().GetProperty("unitCount");
var originProperty = item.GetType().GetProperty("modifiedXOrigin");
unitProperty.SetValue(item, 2);
originProperty.SetValue(item, 4);
var shape = Data("GridShapeBuilder");
var shapeProperty = item.GetType().GetProperty("modifiedShape");
var shapeNames = new[] { "minX", "minY", "orientation", "flipped", "width", "height" };
object[] shapeValues = { 4, 6, 1, true, 2, 3 };
var shapeFields = shapeNames.Select(name => shape.GetType().GetField("<" + name + ">k__BackingField", dataFlags)).ToArray();
for (int i = 0; i < shapeFields.Length; i++) shapeFields[i].SetValue(shape, shapeValues[i]);
shapeProperty.SetValue(item, shape);
var cashField = player.GetType().GetField("playerCash");
var itemField = player.GetType().GetField("currentNegociatedItem");
var dayField = station.GetType().GetField("dayCounter");
cashField.SetValue(player, 135);
itemField.SetValue(player, item);
dayField.SetValue(station, 12);
var zoomLevels = new Dictionary<string, int> { ["inspection"] = 2, ["store"] = 5, ["negotiation"] = 7 };
player.GetType().GetField("storeUIZoomLevels").SetValue(player, zoomLevels);
player.GetType().GetField("lastSaveTime").SetValue(player, new DateTime(2026, 10, 28));
var items = new[] { item, Data("GameItemElement"), Data("GameItemElement"), Data("GameItemElement") };
var itemArray = Array.CreateInstance(item.GetType(), items.Length);
for (int i = 0; i < items.Length; i++) itemArray.SetValue(items[i], i);
var identityType = mod.GetType("ProbablyStolenTools.ManagedObjects");
var identityMethod = identityType.GetMethod("Id", BindingFlags.Static | BindingFlags.NonPublic);
IntPtr Identity(object value) => (IntPtr)identityMethod.Invoke(null, new[] { value });
var accessType = mod.GetType("ProbablyStolenTools.ManagedStateAccess");
var access = accessType.GetConstructor(dataFlags, null, new[] { typeof(Type), typeof(string) }, null)
    .Invoke(new object[] { Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Object"), "Assembly-CSharp" });
foreach (var root in new[] { player, station, itemArray })
    accessType.GetMethod("AddRoot", dataFlags).Invoke(access, new object[] { Identity(root), Array.Empty<string>() });
var graphType = mod.GetType("ProbablyStolenTools.StateGraph");
object PrepareRepair(object target, bool enabled = true)
    => graphType.GetMethod("PrepareReconciliation", dataFlags).Invoke(target, new object[] { enabled, 128, 8192 });
void ApplyRepair(object plan) => plan.GetType().GetMethod("Apply", dataFlags).Invoke(plan, null);
void VerifyRepair(object target, object plan)
{
    object[] arguments = { plan, (Action)(() => { }), false };
    var result = (System.Collections.ICollection)graphType.GetMethod("VerifyReconciliation", dataFlags)
        .Invoke(target, arguments);
    if (result.Count != 0 || (bool)arguments[2])
        throw new Exception("Native owner repair failed verification or unexpectedly required full fallback.");
}
var graph = graphType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
    .Invoke(null, new object[] { access, new[] { Identity(player), Identity(station), Identity(itemArray) }, 50000, 1000000, 64L * 1024 * 1024 });
try
{
    var slots = (System.Collections.IEnumerable)accessType.GetMethod("Describe", dataFlags).Invoke(access, new object[] { Identity(item) });
    foreach (var slot in slots)
    {
        string name = (string)slot.GetType().GetProperty("Name", dataFlags).GetValue(slot);
        if (cacheFields.Any(field => name == "GameItemElement." + field.Name))
            throw new Exception("Native layout cache leaked into business snapshot: " + name);
    }
    // Repeated verification also exercises lazily compiled readers for actual game/private fields.
    for (int i = 0; i < 40; i++)
    {
        var unchanged = (System.Collections.ICollection)graphType.GetMethod("Differences", dataFlags)
            .Invoke(graph, new object[] { 12 });
        if (unchanged.Count != 0) throw new Exception("Game data changed while warming field readers.");
    }
    var readers = (System.Collections.IDictionary)mod.GetType("ProbablyStolenTools.SnapshotFieldReader")
        .GetField("Cache", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    int compiledReaders = readers.Values.Cast<object>().Count(reader =>
        (bool)reader.GetType().GetProperty("Compiled", dataFlags).GetValue(reader));
    if (compiledReaders == 0) throw new Exception("Actual game field readers were not exercised.");
    var censusAccess = accessType.GetConstructor(dataFlags, null, new[] { typeof(Type), typeof(string) }, null)
        .Invoke(new object[] { Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Object"), "Assembly-CSharp" });
    foreach (var root in new[] { player, station, itemArray })
        accessType.GetMethod("AddRoot", dataFlags).Invoke(censusAccess, new object[] { Identity(root), Array.Empty<string>() });
    var census = graphType.GetMethod("Census", BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, new object[] { censusAccess, new[] { Identity(player), Identity(station), Identity(itemArray) },
            50000, 1000000, 64L * 1024 * 1024 });
    try
    {
        foreach (string name in new[] { "ObjectCount", "SlotCount", "ByteCount" })
            if (!Equals(graphType.GetProperty(name, dataFlags).GetValue(graph),
                graphType.GetProperty(name, dataFlags).GetValue(census)))
                throw new Exception("Actual game census differs in " + name);
        var capturedIds = ((IEnumerable<IntPtr>)graphType.GetProperty("Objects", dataFlags).GetValue(graph)).ToHashSet();
        var censusIds = (IEnumerable<IntPtr>)graphType.GetProperty("Objects", dataFlags).GetValue(census);
        if (!capturedIds.SetEquals(censusIds)) throw new Exception("Actual game census changed object coverage.");
        foreach (var id in capturedIds)
            if (!Equals(graphType.GetMethod("SourceOf", dataFlags).Invoke(graph, new object[] { id }),
                graphType.GetMethod("SourceOf", dataFlags).Invoke(census, new object[] { id })))
                throw new Exception("Actual game census changed source paths.");
        if (((System.Collections.ICollection)graphType.GetField("_entries", dataFlags).GetValue(census)).Count != 0
            || ((System.Collections.IDictionary)accessType.GetField("_savedValues", dataFlags).GetValue(censusAccess)).Count != 0)
            throw new Exception("Actual game census retained unused values.");
        Console.WriteLine("PASS real game census matches capture coverage, source paths and budgets without value tokens");
    }
    finally { ((IDisposable)census).Dispose(); }
    foreach (var element in items)
        if (!(bool)graphType.GetMethod("Contains", dataFlags).Invoke(graph, new object[] { Identity(element) }))
            throw new Exception("An item beyond the first array element was not captured.");
    cashField.SetValue(player, 999);
    itemField.SetValue(player, null);
    dayField.SetValue(station, 20);
    unitProperty.SetValue(item, 0);
    originProperty.SetValue(item, 9);
    shapeProperty.SetValue(item, Data("GridShapeBuilder"));
    foreach (var field in shapeFields)
        field.SetValue(shape, field.FieldType == typeof(bool) ? (object)false : 99);
    foreach (var field in cacheFields)
        field.SetValue(item, field.FieldType == typeof(bool) ? (object)true : 99);
    itemArray.SetValue(null, 1);
    itemArray.SetValue(items[0], 2);
    itemArray.SetValue(null, 3);
    zoomLevels["inspection"] = 20;
    zoomLevels["store"] = 50;
    zoomLevels.Remove("negotiation");
    zoomLevels["new"] = 99;
    graphType.GetMethod("Restore", dataFlags).Invoke(graph, null);
    if ((int)cashField.GetValue(player) != 135 || !ReferenceEquals(itemField.GetValue(player), item)
        || (int)dayField.GetValue(station) != 12 || (int)unitProperty.GetValue(item) != 2
        || (int)originProperty.GetValue(item) != 4)
        throw new Exception("Actual game data graph restore failed.");
    foreach (var field in cacheFields)
        if (!Equals(field.GetValue(item), field.FieldType == typeof(bool) ? (object)true : 99))
            throw new Exception("Native layout cache was incorrectly rewound: " + field.Name);
    if (!ReferenceEquals(shapeProperty.GetValue(item), shape))
        throw new Exception("Actual item modifiedShape identity was not restored.");
    for (int i = 0; i < shapeFields.Length; i++)
        if (!Equals(shapeFields[i].GetValue(shape), shapeValues[i]))
            throw new Exception("Actual item modifiedShape state was not restored: " + shapeNames[i]);
    Console.WriteLine("PASS real GameItemElement excludes six layout-cache fields while retaining shape position/size/rotation/flip and count/origin");
    for (int i = 0; i < items.Length; i++)
        if (!ReferenceEquals(itemArray.GetValue(i), items[i]))
            throw new Exception("Actual game item array element was not restored: " + i);
    if (zoomLevels.Count != 3 || zoomLevels["inspection"] != 2 || zoomLevels["store"] != 5
        || zoomLevels["negotiation"] != 7)
        throw new Exception("Actual game dictionary entries were not all restored.");
    var differences = (System.Collections.ICollection)graphType.GetMethod("Differences", dataFlags).Invoke(graph, new object[] { 12 });
    if (differences.Count != 0) throw new Exception("Actual game data graph has differences after restore.");
    Console.WriteLine("PASS real PlayerStore/StoreStation/GameItemElement graph, four distinct array items and dictionary round trip"
        + " (compiled readers=" + compiledReaders + ")");
    var recursiveAccess = accessType.GetConstructor(dataFlags, null,
        new[] { typeof(Type), typeof(string), typeof(bool), typeof(bool), typeof(bool) }, null)
        .Invoke(new object[] { Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Object"),
            "Assembly-CSharp", true, true, false });
    accessType.GetProperty("TypedScalarComparisons", dataFlags).SetValue(recursiveAccess, false);
    accessType.GetProperty("DirectReferenceComparisons", dataFlags).SetValue(recursiveAccess, false);
    foreach (var root in new[] { player, station, itemArray })
        accessType.GetMethod("AddRoot", dataFlags).Invoke(recursiveAccess, new object[] { Identity(root), Array.Empty<string>() });
    var recursiveGraph = graphType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, new object[] { recursiveAccess, new[] { Identity(player), Identity(station), Identity(itemArray) },
            50000, 1000000, 64L * 1024 * 1024 });
    try
    {
        for (int i = 0; i < 40; i++)
            if (((System.Collections.ICollection)graphType.GetMethod("Differences", dataFlags)
                .Invoke(graph, new object[] { 12 })).Count != 0)
                throw new Exception("Native typed comparison warmup differs.");
        if ((long)graphType.GetProperty("TypedComparisonCount", dataFlags).GetValue(graph) == 0
            || (long)graphType.GetProperty("TypedComparisonCount", dataFlags).GetValue(recursiveGraph) != 0)
            throw new Exception("Native comparison or boxed oracle did not exercise requested modes.");
        if ((long)graphType.GetProperty("ReferenceComparisonCount", dataFlags).GetValue(graph) == 0
            || (long)graphType.GetProperty("ReferenceComparisonCount", dataFlags).GetValue(recursiveGraph) != 0)
            throw new Exception("Native direct reference comparison or global identity oracle was not exercised.");
        cashField.SetValue(player, -11); dayField.SetValue(station, -7); itemArray.SetValue(null, 3);
        zoomLevels["inspection"] = -17;
        var typedDiff = (IEnumerable<string>)graphType.GetMethod("Differences", dataFlags).Invoke(graph, new object[] { 128 });
        var boxedDiff = (IEnumerable<string>)graphType.GetMethod("Differences", dataFlags).Invoke(recursiveGraph, new object[] { 128 });
        if (!typedDiff.Any() || !typedDiff.SequenceEqual(boxedDiff))
            throw new Exception("Native typed and boxed complete difference lists disagree.");
        graphType.GetMethod("Restore", dataFlags).Invoke(graph, null);
        if (((System.Collections.ICollection)graphType.GetMethod("Differences", dataFlags)
            .Invoke(recursiveGraph, new object[] { 12 })).Count != 0)
            throw new Exception("Native typed restore differs from boxed oracle.");
        Console.WriteLine("PASS native typed/boxed scalar comparisons, array indices and dictionary mutations agree after warmup");
        foreach (var writerGraph in new[] { graph, recursiveGraph, graph })
        {
            cashField.SetValue(player, -123); dayField.SetValue(station, -1);
            itemField.SetValue(player, null); unitProperty.SetValue(item, -1);
            itemArray.SetValue(null, 3);
            for (int i = 0; i < 100; i++) zoomLevels["grow-" + i] = i;
            zoomLevels["inspection"] = -1;
            graphType.GetMethod("Restore", dataFlags).Invoke(writerGraph, null);
            foreach (var oracle in new[] { graph, recursiveGraph })
                if (((System.Collections.ICollection)graphType.GetMethod("Differences", dataFlags)
                    .Invoke(oracle, new object[] { 12 })).Count != 0)
                    throw new Exception("Native direct/recursive writer parity failed.");
            if ((int)cashField.GetValue(player) != 135 || (int)dayField.GetValue(station) != 12
                || !ReferenceEquals(itemArray.GetValue(3), items[3]) || zoomLevels.Count != 3
                || zoomLevels["inspection"] != 2 || !ReferenceEquals(itemField.GetValue(player), item))
                throw new Exception("Native direct writer lost original values, identities or collection storage.");
        }
        Console.WriteLine("PASS native direct/recursive writer parity after real field, array and dictionary growth mutations");
        foreach (bool enabled in new[] { true, false, true })
        {
            cashField.SetValue(player, -10); dayField.SetValue(station, -3);
            unitProperty.SetValue(item, -1); itemArray.SetValue(null, 2);
            for (int i = 0; i < 100; i++) zoomLevels["owner-grow-" + i] = i;
            zoomLevels.Remove("store");
            var plan = PrepareRepair(graph, enabled);
            string mode = (string)plan.GetType().GetProperty("Mode", dataFlags).GetValue(plan);
            if (mode != (enabled ? "owners" : "full"))
                throw new Exception("Native owner repair selected an unexpected mode: " + mode);
            ApplyRepair(plan); VerifyRepair(graph, plan);
            foreach (var oracle in new[] { graph, recursiveGraph })
                if (((System.Collections.ICollection)graphType.GetMethod("Differences", dataFlags)
                    .Invoke(oracle, new object[] { 12 })).Count != 0)
                    throw new Exception("Native owner repair differs from the full recursive checkpoint.");
            if (zoomLevels.Count != 3 || zoomLevels["store"] != 5
                || !ReferenceEquals(itemArray.GetValue(2), items[2]))
                throw new Exception("Native owner repair lost original array/collection storage.");
        }
        Console.WriteLine("PASS native owner repair/full fallback parity for actual fields arrays and dictionary growth");
    }
    finally { ((IDisposable)recursiveGraph).Dispose(); }
}
finally { ((IDisposable)graph).Dispose(); }

// Two distinct items deliberately share uniqueId=0; entity boundaries use graph identities, not uniqueId.
var boundaryItems = new[] { Data("GameItemElement"), Data("GameItemElement") };
var sharedItemTypes = new List<string> { "fixture-type-a", "fixture-type-b" };
foreach (var boundaryItem in boundaryItems)
    game.GetType("GameItem", true).GetField("itemTypes", dataFlags).SetValue(boundaryItem, sharedItemTypes);
var boundaryGrid = Data("GameGridInventory");
var boundarySlot = Data("GameSlotInventory");
var boundaryClosure = Data("MiscItemDirectory+<>c__DisplayClass51_0");
boundaryClosure.GetType().GetField("item", dataFlags).SetValue(boundaryClosure, boundaryItems[0]);
var boundaryList = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(game.GetType("GameItem", true)));
foreach (var boundaryItem in boundaryItems) boundaryList.Add(boundaryItem);
boundaryGrid.GetType().GetField("items", dataFlags).SetValue(boundaryGrid, boundaryList);
boundarySlot.GetType().GetField("currentItem", dataFlags).SetValue(boundarySlot, boundaryItems[0]);
boundaryItems[0].GetType().GetProperty("parentInventory").SetValue(boundaryItems[0], boundaryGrid);
var boundaryShape = Data("GridShapeBuilder");
var shapeDataField = boundaryShape.GetType().GetField("data", dataFlags);
var boundaryShapeData = new byte[] { 1, 0, 2, 3, 0, 5 };
shapeDataField.SetValue(boundaryShape, boundaryShapeData);
boundaryShape.GetType().GetProperty("width").SetValue(boundaryShape, 3);
boundaryShape.GetType().GetProperty("height").SetValue(boundaryShape, 2);
boundaryShape.GetType().GetProperty("minX").SetValue(boundaryShape, -7);
boundaryShape.GetType().GetProperty("orientation").SetValue(boundaryShape, 3);
foreach (var boundaryItem in boundaryItems)
{
    boundaryItem.GetType().GetProperty("shape").SetValue(boundaryItem, boundaryShape);
    boundaryItem.GetType().GetProperty("modifiedShape").SetValue(boundaryItem, boundaryShape);
}
var boundaryWindow = Data("PixelWindow");
var windowParents = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(game.GetType("GameItem", true)));
windowParents.Add(boundaryItems[0]); windowParents.Add(boundaryItems[1]); windowParents.Add(boundaryItems[0]);
var linkedStorage = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(game.GetType("GraphNodeStorage", true)));
linkedStorage.Add(boundaryGrid); linkedStorage.Add(boundarySlot);
var windowType = boundaryWindow.GetType();
windowType.GetField("linkedContainerStorage", dataFlags).SetValue(boundaryWindow, linkedStorage);
windowType.GetField("<parentItems>k__BackingField", dataFlags).SetValue(boundaryWindow, windowParents);
windowType.GetField("<childElement>k__BackingField", dataFlags).SetValue(boundaryWindow, boundaryGrid);
windowType.GetField("<titleString>k__BackingField", dataFlags).SetValue(boundaryWindow, "fixture");
windowType.GetField("<transparency>k__BackingField", dataFlags).SetValue(boundaryWindow, -0.0f);
var inventoryType = game.GetType("GameInventory", true);
inventoryType.GetField("<parent>k__BackingField", dataFlags).SetValue(boundaryGrid, boundaryWindow);
inventoryType.GetField("overrideLockInsert", dataFlags).SetValue(boundaryGrid, true);
boundaryGrid.GetType().GetField("<inventoryShape>k__BackingField", dataFlags).SetValue(boundaryGrid, boundaryShape);
boundarySlot.GetType().GetField("stolenUI", dataFlags).SetValue(boundarySlot, boundaryWindow);
game.GetType("GameItem", true).GetField("<contentWindow>k__BackingField", dataFlags).SetValue(boundaryItems[0], boundaryWindow);
Action originalWindowCallback = () => throw new Exception("Window business callback must not run during snapshots.");
windowType.GetField("<openCallback>k__BackingField", dataFlags).SetValue(boundaryWindow, originalWindowCallback);
var boundaryTagSystem = Data("TagSystem");
var boundaryTag = Data("TagState");
var boundaryTagType = boundaryTag.GetType();
var boundaryTagMap = (System.Collections.IDictionary)Activator.CreateInstance(
    typeof(Dictionary<,>).MakeGenericType(typeof(string), boundaryTagType));
boundaryTagMap.Add("runtime-tag", boundaryTag);
boundaryTagSystem.GetType().GetField("<dict>k__BackingField", dataFlags).SetValue(boundaryTagSystem, boundaryTagMap);
game.GetType("TagSystem", true).GetField("<dict>k__BackingField", dataFlags).SetValue(boundaryGrid, boundaryTagMap);
game.GetType("TagSystem", true).GetField("<dict>k__BackingField", dataFlags).SetValue(boundarySlot, boundaryTagMap);
game.GetType("GameItem", true).GetField("<state>k__BackingField", dataFlags).SetValue(boundaryItems[0], boundaryTagSystem);
var boundaryRoots = new[] { boundaryGrid, boundarySlot, boundaryClosure };
var boundaryAccess = accessType.GetConstructor(dataFlags, null, new[] { typeof(Type), typeof(string) }, null)
    .Invoke(new object[] { Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Object"), "Assembly-CSharp" });
mod.GetType("ProbablyStolenTools.SemanticTagBlock", true).GetMethod("Enable", BindingFlags.Static | BindingFlags.NonPublic)
    .Invoke(null, new[] { boundaryAccess });
foreach (var root in boundaryRoots)
    accessType.GetMethod("AddRoot", dataFlags).Invoke(boundaryAccess, new object[] { Identity(root), Array.Empty<string>() });
var boundaryGraph = graphType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
    .Invoke(null, new object[] { boundaryAccess, boundaryRoots.Select(Identity).ToArray(), 50000, 1000000, 64L * 1024 * 1024 });
try
{
    var entityType = mod.GetType("ProbablyStolenTools.SemanticEntityShadow", true);
    var entityCoreType = mod.GetType("ProbablyStolenTools.SemanticEntitySnapshot", true);
    object EntityCapture() => entityType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, new object[] { boundaryGraph, 50000, 1000000, 64L * 1024 * 1024 });
    int EntityDifferences(object shadow)
    {
        var core = entityType.GetField("_snapshot", dataFlags).GetValue(shadow);
        return (int)entityCoreType.GetMethod("Differences", dataFlags).Invoke(core, new object[] { null, 1 });
    }
    int RuntimeDifferences(object shadow)
    {
        var core = entityType.GetField("_runtime", dataFlags).GetValue(shadow);
        return ((System.Collections.ICollection)core.GetType().GetMethod("Differences", dataFlags)
            .Invoke(core, new object[] { 12 })).Count;
    }
    using var entityRecords = (IDisposable)EntityCapture();
    entityType.GetMethod("EnableShapeRestore", dataFlags).Invoke(entityRecords,
        new object[] { boundaryGraph, 50000, 1000000, 64L * 1024 * 1024 });
    bool RestoreWithShapes() => (bool)entityType.GetMethod("TryRestoreShapes", dataFlags)
        .Invoke(entityRecords, new[] { boundaryGraph });
    if ((int)entityType.GetProperty("ShapePilotOwners", dataFlags).GetValue(entityRecords) != 1)
        throw new Exception("Exact shared native shape was not admitted to the entity writer pilot.");
    var independentRuntime = entityType.GetField("_runtime", dataFlags).GetValue(entityRecords);
    if (RuntimeDifferences(entityRecords) != 0
        || (int)independentRuntime.GetType().GetProperty("SlotCount", dataFlags).GetValue(independentRuntime)
            >= (int)graphType.GetProperty("SlotCount", dataFlags).GetValue(boundaryGraph)
        || (int)independentRuntime.GetType().GetProperty("BlockCount", dataFlags).GetValue(independentRuntime) < 1)
        throw new Exception("Native independent runtime payload is missing or includes semantic owner fields.");
    boundaryTagType.GetField("valueInt", dataFlags).SetValue(boundaryTag, 71);
    if (RuntimeDifferences(entityRecords) != 1 || EntityDifferences(entityRecords) != 0)
        throw new Exception("Independent runtime exact tag block was not captured or compared.");
    graphType.GetMethod("Restore", dataFlags).Invoke(boundaryGraph, null);
    if (RuntimeDifferences(entityRecords) != 0)
        throw new Exception("Independent runtime tag block did not match legacy restoration.");
    var boundaryPlan = entityType.GetField("_boundary", dataFlags).GetValue(entityRecords);
    var boundaryPlanType = boundaryPlan.GetType();
    var handleMethod = boundaryPlanType.GetMethod("GetHandle", dataFlags);
    var resolveMethod = boundaryPlanType.GetMethod("Resolve", dataFlags);
    var firstHandle = handleMethod.Invoke(boundaryPlan, new object[] { Identity(boundaryItems[0]) });
    var secondHandle = handleMethod.Invoke(boundaryPlan, new object[] { Identity(boundaryItems[1]) });
    if (!ReferenceEquals(resolveMethod.Invoke(boundaryPlan, new[] { firstHandle, (object)boundaryItems[0].GetType() }), boundaryItems[0])
        || !ReferenceEquals(resolveMethod.Invoke(boundaryPlan, new[] { secondHandle, (object)boundaryItems[1].GetType() }), boundaryItems[1])
        || (int)boundaryPlanType.GetProperty("TotalSlots", dataFlags).GetValue(boundaryPlan)
            != (int)graphType.GetProperty("SlotCount", dataFlags).GetValue(boundaryGraph)
        || (int)boundaryPlanType.GetProperty("RuntimeReachableObjects", dataFlags).GetValue(boundaryPlan)
            != (int)graphType.GetProperty("ObjectCount", dataFlags).GetValue(boundaryGraph)
        || (int)boundaryPlanType.GetProperty("RuntimeHandleLinks", dataFlags).GetValue(boundaryPlan) < 3)
        throw new Exception("Native external handles or complete runtime topology replay lost aliases or slots.");
    using (var secondRecords = (IDisposable)EntityCapture())
    {
        var otherPlan = entityType.GetField("_boundary", dataFlags).GetValue(secondRecords);
        try
        {
            resolveMethod.Invoke(otherPlan, new[] { firstHandle, (object)boundaryItems[0].GetType() });
            throw new Exception("Native external handle leaked across checkpoint scopes.");
        }
        catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
    }
    Console.WriteLine("PASS native scoped external entity handles, same-uniqueId distinction, callback/container alias counts and complete runtime-only topology replay");
    if (EntityDifferences(entityRecords) != 0
        || !((string)entityType.GetProperty("Summary", dataFlags).GetValue(entityRecords)).Contains("shapes=1"))
        throw new Exception("Native entity projection capture did not match exact item/inventory/shape schemas.");
    var boundaryType = mod.GetType("ProbablyStolenTools.SemanticEntityBoundaryShadow", true);
    object AuditBoundary() => boundaryType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, new object[] { boundaryGraph, 50000 });
    long BoundaryCount(object audit, string name)
        => Convert.ToInt64(audit.GetType().GetField(name, dataFlags).GetValue(audit));
    var audit = AuditBoundary();
    if (BoundaryCount(audit, "Items") != 2 || BoundaryCount(audit, "GridInventories") != 1
        || BoundaryCount(audit, "SlotInventories") != 1 || BoundaryCount(audit, "ExternalEntities") != 0
        || BoundaryCount(audit, "RootEntities") != 2 || BoundaryCount(audit, "CallbackIncomingLinks") == 0
        || BoundaryCount(audit, "ArrayIncomingLinks") < 2
        || BoundaryCount(audit, "ScannedSlots") != (int)graphType.GetProperty("SlotCount", dataFlags).GetValue(boundaryGraph))
        throw new Exception("Native entity audit lost item/inventory identities, closure bindings or complete slot coverage.");
    boundaryList.Clear();
    boundaryShapeData[5] = 99;
    boundaryShape.GetType().GetProperty("minX").SetValue(boundaryShape, 10);
    boundaryItems[0].GetType().GetProperty("unitCount").SetValue(boundaryItems[0], 11);
    boundarySlot.GetType().GetField("currentItem", dataFlags).SetValue(boundarySlot, boundaryItems[1]);
    if (EntityDifferences(entityRecords) != 5)
        throw new Exception("Independent native records missed scalar, shape bytes, order/length or same-uniqueId identity mutations.");
    if (RuntimeDifferences(entityRecords) == 0)
        throw new Exception("Independent native runtime payload missed list storage or full shape bytes.");
    var changedAudit = AuditBoundary();
    foreach (string name in new[] { "Items", "ScannedSlots", "EntitySlots", "IncomingRuntimeLinks", "CallbackIncomingLinks", "ArrayIncomingLinks" })
        if (BoundaryCount(changedAudit, name) != BoundaryCount(audit, name))
            throw new Exception("Native entity audit reread changed live topology: " + name);
    if (((System.Collections.ICollection)graphType.GetMethod("Differences", dataFlags)
        .Invoke(boundaryGraph, new object[] { 12 })).Count == 0)
        throw new Exception("Native entity fixture did not change.");
    graphType.GetMethod("Restore", dataFlags).Invoke(boundaryGraph, null);
    if (((System.Collections.ICollection)graphType.GetMethod("Differences", dataFlags)
        .Invoke(boundaryGraph, new object[] { 12 })).Count != 0 || boundaryList.Count != 2
        || EntityDifferences(entityRecords) != 0 || RuntimeDifferences(entityRecords) != 0)
        throw new Exception("Native entity audit changed legacy restoration.");
    for (int iteration = 0; iteration < 6; iteration++)
    {
        boundaryList[0] = boundaryItems[1];
        boundaryShapeData[iteration] = 9;
        shapeDataField.SetValue(boundaryShape, (byte[])boundaryShapeData.Clone());
        boundaryShape.GetType().GetProperty("minX").SetValue(boundaryShape, iteration + 10);
        boundaryShape.GetType().GetProperty("minY").SetValue(boundaryShape, iteration - 10);
        boundaryShape.GetType().GetProperty("width").SetValue(boundaryShape, iteration + 20);
        boundaryShape.GetType().GetProperty("height").SetValue(boundaryShape, iteration + 30);
        boundaryShape.GetType().GetProperty("orientation").SetValue(boundaryShape, iteration % 4);
        boundaryShape.GetType().GetProperty("flipped").SetValue(boundaryShape, true);
        boundaryShape.GetType().GetField("outside", dataFlags).SetValue(boundaryShape, (byte)71);
        if (EntityDifferences(entityRecords) < 3)
            throw new Exception("Native records lost retained shape storage or member identity.");
        if (!RestoreWithShapes()) throw new Exception("Native shape writer unexpectedly fell back to an unavailable plan.");
        if (EntityDifferences(entityRecords) != 0 || RuntimeDifferences(entityRecords) != 0
            || !ReferenceEquals(shapeDataField.GetValue(boundaryShape), boundaryShapeData)
            || ((System.Collections.ICollection)graphType.GetMethod("Differences", dataFlags)
                .Invoke(boundaryGraph, new object[] { 12 })).Count != 0)
            throw new Exception("Native record/legacy oracle mismatch after repeated original-storage restoration.");
    }
    Console.WriteLine("PASS independent read-only native entity records: complete item base schema, grid/slot order and identities, full shared shape bytes, six retained-graph restore cycles");
    Console.WriteLine("PASS native shape entity writer: all eight fields, shared original data identity and every byte, six real entity-write cycles, old graph plus independent runtime and entity oracle agreement");
    entityType.GetMethod("EnableWindowRestore", dataFlags).Invoke(entityRecords,
        new object[] { boundaryGraph, 50000, 1000000, 64L * 1024 * 1024 });
    if ((int)entityType.GetProperty("WindowPilotOwners", dataFlags).GetValue(entityRecords) != 1)
        throw new Exception("Exact native window was not admitted to the entity writer pilot.");
    bool RestoreWithEntities(bool shapes, bool windows) => (bool)entityType.GetMethod("TryRestoreEntities", dataFlags)
        .Invoke(entityRecords, new object[] { boundaryGraph, shapes, windows, false, false });
    var windowWriterType = mod.GetType("ProbablyStolenTools.SemanticWindowWriter", true);
    var writtenWindowFields = (FieldInfo[])windowWriterType.GetField("EntityFields", BindingFlags.Static | BindingFlags.NonPublic)
        .GetValue(null);
    Action neverInvokeWindowCallback = () => throw new Exception("Window restore invoked a business callback.");
    for (int iteration = 0; iteration < 6; iteration++)
    {
        foreach (var field in writtenWindowFields)
        {
            var type = field.FieldType;
            object changed = type == typeof(bool) ? !(bool)field.GetValue(boundaryWindow)
                : type == typeof(int) ? (int)field.GetValue(boundaryWindow) + 17
                : type == typeof(float) ? BitConverter.Int32BitsToSingle(unchecked((int)0x7fc01234))
                : type.IsEnum ? Enum.ToObject(type, 7)
                : type == typeof(string) ? new string("fixture".ToCharArray())
                : null;
            field.SetValue(boundaryWindow, changed);
        }
        windowParents[1] = boundaryItems[0];
        linkedStorage[0] = boundarySlot;
        windowType.GetField("<openCallback>k__BackingField", dataFlags).SetValue(boundaryWindow, neverInvokeWindowCallback);
        windowType.GetField("<closeCallback>k__BackingField", dataFlags).SetValue(boundaryWindow, neverInvokeWindowCallback);
        windowType.GetField("<stateChangeActionFunc>k__BackingField", dataFlags).SetValue(boundaryWindow, neverInvokeWindowCallback);
        windowType.GetField("<focusStamp>k__BackingField", dataFlags).SetValue(boundaryWindow, 71L);
        boundaryShape.GetType().GetProperty("orientation").SetValue(boundaryShape, iteration % 4);
        if (EntityDifferences(entityRecords) == 0 || RuntimeDifferences(entityRecords) == 0)
            throw new Exception("Window writer fixture failed to mutate both business and runtime partitions.");
        // Exercise combined, window-only and shape-only plans against the same complete oracle.
        if (!RestoreWithEntities(iteration % 3 != 1, iteration % 3 != 2)
            || EntityDifferences(entityRecords) != 0 || RuntimeDifferences(entityRecords) != 0
            || !ReferenceEquals(windowType.GetField("<openCallback>k__BackingField", dataFlags).GetValue(boundaryWindow), originalWindowCallback)
            || !ReferenceEquals(windowType.GetField("<parentItems>k__BackingField", dataFlags).GetValue(boundaryWindow), windowParents)
            || !ReferenceEquals(windowType.GetField("linkedContainerStorage", dataFlags).GetValue(boundaryWindow), linkedStorage)
            || BitConverter.SingleToInt32Bits((float)windowType.GetField("<transparency>k__BackingField", dataFlags)
                .GetValue(boundaryWindow)) != unchecked((int)0x80000000)
            || ((System.Collections.ICollection)graphType.GetMethod("Differences", dataFlags)
                .Invoke(boundaryGraph, new object[] { 12 })).Count != 0)
            throw new Exception("Native window partition writer lost business fields, runtime callbacks, float bits or original collections.");
    }
    if (RestoreWithEntities(false, false))
        throw new Exception("Disabled entity writers unexpectedly performed writes.");
    Console.WriteLine("PASS native window entity writer: every business field, original list aliases and contents, signed-zero transparency, runtime callbacks never invoked, six combined/independent writer cycles");
    try
    {
        // Two items fit the candidate budget, but combined shape/item owners do not.
        entityType.GetMethod("EnableItemRestore", dataFlags).Invoke(entityRecords,
            new object[] { boundaryGraph, 2, 1000000, 64L * 1024 * 1024 });
        throw new Exception("Native item writer ignored combined owner-plan budget.");
    }
    catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
    if ((int)entityType.GetProperty("ItemPilotOwners", dataFlags).GetValue(entityRecords) != 0
        || !RestoreWithEntities(true, true) || EntityDifferences(entityRecords) != 0 || RuntimeDifferences(entityRecords) != 0)
        throw new Exception("Failed item plan registration damaged existing shape/window plans.");
    entityType.GetMethod("EnableItemRestore", dataFlags).Invoke(entityRecords,
        new object[] { boundaryGraph, 50000, 1000000, 64L * 1024 * 1024 });
    if ((int)entityType.GetProperty("ItemPilotOwners", dataFlags).GetValue(entityRecords) != 2)
        throw new Exception("Native item writer lost distinct objects sharing uniqueId=0.");
    bool RestoreWithAllEntities(int mask) => (bool)entityType.GetMethod("TryRestoreEntities", dataFlags)
        .Invoke(entityRecords, new object[] { boundaryGraph, (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, (mask & 8) != 0 });
    var itemWriter = (IDisposable)entityType.GetField("_itemWriter", dataFlags).GetValue(entityRecords);
    var fieldWriterType = mod.GetType("ProbablyStolenTools.SemanticFieldEntityWriter", true);
    var writtenItemFields = (FieldInfo[])fieldWriterType.GetField("_entityFields", dataFlags).GetValue(itemWriter);
    var nativeItemCallbackField = game.GetType("GameItemFunc", true).GetField("<onLoaded>k__BackingField", dataFlags);
    var nativeItemActionType = typeof(Action<>).MakeGenericType(game.GetType("GameItem", true));
    var nativeItemThrowMethod = typeof(NativeEntityCallbackFixture).GetMethod("Throw", BindingFlags.Static | BindingFlags.Public)
        .MakeGenericMethod(game.GetType("GameItem", true));
    var neverInvokeItemCallback = Delegate.CreateDelegate(nativeItemActionType, nativeItemThrowMethod);
    for (int iteration = 0; iteration < 14; iteration++)
    {
        foreach (var boundaryItem in boundaryItems)
        {
            foreach (var field in writtenItemFields)
            {
                var type = field.FieldType;
                object changed = type == typeof(bool) ? !(bool)field.GetValue(boundaryItem)
                    : type == typeof(int) ? 71 + iteration
                    : type == typeof(long) ? long.MinValue + iteration
                    : type == typeof(string) ? new string("item-mutated".ToCharArray())
                    : field.GetValue(boundaryItem) != null ? null
                    : type == game.GetType("GameInventory") ? boundarySlot
                    : type == game.GetType("GridShape") ? boundaryShape
                    : type == game.GetType("TagSystem") ? boundaryTagSystem
                    : type == game.GetType("PixelWindow") ? boundaryWindow
                    : type.IsGenericType ? Activator.CreateInstance(type)
                    : type.IsAbstract ? System.Runtime.Serialization.FormatterServices.GetUninitializedObject(
                        game.GetTypes().First(candidate => type.IsAssignableFrom(candidate) && !candidate.IsAbstract && !candidate.ContainsGenericParameters))
                    : System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
                field.SetValue(boundaryItem, changed);
            }
            nativeItemCallbackField.SetValue(boundaryItem, neverInvokeItemCallback);
        }
        sharedItemTypes.Clear(); sharedItemTypes.Add("changed-type");
        boundaryList[0] = boundaryItems[1];
        boundaryShape.GetType().GetProperty("orientation").SetValue(boundaryShape, iteration % 4);
        windowType.GetField("<titleString>k__BackingField", dataFlags).SetValue(boundaryWindow, "changed-window");
        int mask = iteration % 7 + 1;
        if (!RestoreWithAllEntities(mask) || EntityDifferences(entityRecords) != 0 || RuntimeDifferences(entityRecords) != 0
            || nativeItemCallbackField.GetValue(boundaryItems[0]) != null
            || !ReferenceEquals(game.GetType("GameItem").GetField("itemTypes", dataFlags).GetValue(boundaryItems[0]), sharedItemTypes)
            || !ReferenceEquals(game.GetType("GameItem").GetField("itemTypes", dataFlags).GetValue(boundaryItems[1]), sharedItemTypes)
            || sharedItemTypes.Count != 2 || sharedItemTypes[1] != "fixture-type-b"
            || ((System.Collections.ICollection)graphType.GetMethod("Differences", dataFlags)
                .Invoke(boundaryGraph, new object[] { 12 })).Count != 0)
            throw new Exception("Native item writer lost inherited fields, callbacks, list storage or combined-writer coverage, mask=" + mask);
    }
    if (RestoreWithAllEntities(0))
        throw new Exception("All disabled entity writers unexpectedly performed writes.");
    Console.WriteLine("PASS native full item entity writer: all inherited business fields, two zero-uniqueId identities, shared original type-list storage, callbacks never invoked and fourteen cycles covering every nonempty writer combination");
    var itemWriterType = mod.GetType("ProbablyStolenTools.SemanticItemWriter", true);
    foreach (var limits in new[] { (Objects: 1, Slots: 1000000, Bytes: 64L * 1024 * 1024),
        (Objects: 50000, Slots: writtenItemFields.Length * 2 - 1, Bytes: 64L * 1024 * 1024),
        (Objects: 50000, Slots: 1000000, Bytes: (long)writtenItemFields.Length * 2 * 8 - 1) })
    {
        try
        {
            using var unexpected = (IDisposable)itemWriterType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { boundaryGraph, limits.Objects, limits.Slots, limits.Bytes });
            throw new Exception("Native item writer accepted an incomplete object/slot/byte budget.");
        }
        catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
    }
    try
    {
        entityType.GetMethod("EnableRuntimeRestore", dataFlags).Invoke(entityRecords,
            new object[] { boundaryGraph, 50000, 1000000 });
        throw new Exception("Incomplete native entity group admitted independent runtime writes.");
    }
    catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
    if ((int)entityType.GetProperty("RuntimePilotSlots", dataFlags).GetValue(entityRecords) != 0)
        throw new Exception("Failed native runtime preparation published a partial writer.");
    try
    {
        entityType.GetMethod("EnableInventoryRestore", dataFlags).Invoke(entityRecords,
            new object[] { boundaryGraph, 2, 1000000, 64L * 1024 * 1024 });
        throw new Exception("Native inventory family ignored combined owner budget.");
    }
    catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
    if ((int)entityType.GetProperty("InventoryPilotOwners", dataFlags).GetValue(entityRecords) != 0
        || !RestoreWithAllEntities(7) || EntityDifferences(entityRecords) != 0 || RuntimeDifferences(entityRecords) != 0)
        throw new Exception("Failed inventory family damaged an already prepared item/window/shape combination.");
    entityType.GetMethod("EnableInventoryRestore", dataFlags).Invoke(entityRecords,
        new object[] { boundaryGraph, 50000, 1000000, 64L * 1024 * 1024 });
    var gridWriter = (IDisposable)entityType.GetField("_gridWriter", dataFlags).GetValue(entityRecords);
    var slotWriter = (IDisposable)entityType.GetField("_slotWriter", dataFlags).GetValue(entityRecords);
    var gridFields = (FieldInfo[])fieldWriterType.GetField("_entityFields", dataFlags).GetValue(gridWriter);
    var slotFields = (FieldInfo[])fieldWriterType.GetField("_entityFields", dataFlags).GetValue(slotWriter);
    if ((int)entityType.GetProperty("GridPilotOwners", dataFlags).GetValue(entityRecords) != 1
        || (int)entityType.GetProperty("SlotPilotOwners", dataFlags).GetValue(entityRecords) != 1
        || (long)entityType.GetProperty("PilotSlots", dataFlags).GetValue(entityRecords)
            != Convert.ToInt64(boundaryPlanType.GetProperty("EntitySlots", dataFlags).GetValue(boundaryPlan)))
        throw new Exception("Native inventory family did not complete the exact entity field partition.");
    var inventoryFunctionType = game.GetType("GameInventoryFunc", true);
    var inventoryCallback = inventoryFunctionType.GetField("<onSlotAddItemFunc>k__BackingField", dataFlags);
    var inventoryThrowMethod = typeof(NativeEntityCallbackFixture).GetMethod("ThrowInventory", BindingFlags.Static | BindingFlags.Public)
        .MakeGenericMethod(game.GetType("GameItem", true), game.GetType("GameInventory", true), game.GetType("SlotMarker", true));
    var neverInvokeInventoryCallback = Delegate.CreateDelegate(inventoryCallback.FieldType, inventoryThrowMethod);
    var inventoryDict = game.GetType("TagSystem", true).GetField("<dict>k__BackingField", dataFlags);
    for (int iteration = 0; iteration < 30; iteration++)
    {
        foreach (var pair in new[] { (Target: boundaryGrid, Fields: gridFields), (Target: boundarySlot, Fields: slotFields) })
        {
            foreach (var field in pair.Fields)
            {
                var type = field.FieldType;
                object changed = type == typeof(bool) ? !(bool)field.GetValue(pair.Target)
                    : type == typeof(int) ? 31 + iteration
                    : type == typeof(float) ? BitConverter.Int32BitsToSingle(unchecked((int)0x7fc01234))
                    : type == typeof(string) ? new string("inventory-mutated".ToCharArray())
                    : field.GetValue(pair.Target) != null ? null
                    : type.IsGenericType ? Activator.CreateInstance(type)
                    : type.IsInstanceOfType(boundaryWindow) ? boundaryWindow
                    : type.IsInstanceOfType(boundaryShape) ? boundaryShape : boundaryItems[1];
                field.SetValue(pair.Target, changed);
            }
            inventoryCallback.SetValue(pair.Target, neverInvokeInventoryCallback);
            inventoryDict.SetValue(pair.Target, Activator.CreateInstance(inventoryDict.FieldType));
        }
        foreach (string name in new[] { "_lastShapeWidth", "_lastShapeHeight", "_lastShapeHash" })
            boundaryGrid.GetType().GetField(name, dataFlags).SetValue(boundaryGrid, 71);
        boundaryGrid.GetType().GetField("_lastRenderDynamicBackground", dataFlags).SetValue(boundaryGrid, true);
        boundarySlot.GetType().GetField("validateLock", dataFlags).SetValue(boundarySlot, true);
        boundaryList.Clear();
        for (int i = 0; i < 12; i++) boundaryList.Add(boundaryItems[1]);
        boundaryItems[0].GetType().GetProperty("parentInventory").SetValue(boundaryItems[0], boundarySlot);
        boundaryItems[0].GetType().GetProperty("unitCount").SetValue(boundaryItems[0], 77);
        boundaryShape.GetType().GetProperty("orientation").SetValue(boundaryShape, 2);
        windowType.GetField("isLocked", dataFlags).SetValue(boundaryWindow, true);
        int mask = iteration % 15 + 1;
        if (!RestoreWithAllEntities(mask) || EntityDifferences(entityRecords) != 0 || RuntimeDifferences(entityRecords) != 0
            || boundaryList.Count != 2 || !ReferenceEquals(boundaryList[0], boundaryItems[0])
            || !ReferenceEquals(boundaryGrid.GetType().GetField("items", dataFlags).GetValue(boundaryGrid), boundaryList)
            || !ReferenceEquals(boundarySlot.GetType().GetField("currentItem", dataFlags).GetValue(boundarySlot), boundaryItems[0])
            || !ReferenceEquals(boundaryItems[0].GetType().GetProperty("parentInventory").GetValue(boundaryItems[0]), boundaryGrid)
            || !ReferenceEquals(inventoryDict.GetValue(boundaryGrid), boundaryTagMap)
            || !ReferenceEquals(inventoryDict.GetValue(boundarySlot), boundaryTagMap)
            || inventoryCallback.GetValue(boundaryGrid) != null || inventoryCallback.GetValue(boundarySlot) != null
            || ((System.Collections.ICollection)graphType.GetMethod("Differences", dataFlags)
                .Invoke(boundaryGraph, new object[] { 12 })).Count != 0)
            throw new Exception("Native inventory writer lost inherited fields, list storage, aliases or runtime state, mask=" + mask);
    }
    Console.WriteLine("PASS native inventory family: complete grid/slot inherited business fields, original member storage, borrowing/parent/shape links, shared tags, runtime caches and callbacks, thirty cycles covering all fifteen writer combinations");
    var inventoryWriterType = mod.GetType("ProbablyStolenTools.SemanticInventoryWriter", true);
    foreach (var pair in new[] { (Type: boundaryGrid.GetType(), Fields: gridFields), (Type: boundarySlot.GetType(), Fields: slotFields) })
        foreach (var limits in new[] { (Objects: 0, Slots: 1000000, Bytes: 64L * 1024 * 1024),
            (Objects: 50000, Slots: pair.Fields.Length - 1, Bytes: 64L * 1024 * 1024),
            (Objects: 50000, Slots: 1000000, Bytes: (long)pair.Fields.Length * 8 - 1) })
        {
            try
            {
                using var unexpected = (IDisposable)inventoryWriterType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { boundaryGraph, pair.Type, limits.Objects, limits.Slots, limits.Bytes });
                throw new Exception("Native inventory writer accepted incomplete object/slot/byte budget.");
            }
            catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
        }
    entityType.GetMethod("EnableRuntimeRestore", dataFlags).Invoke(entityRecords,
        new object[] { boundaryGraph, 50000, 1000000 });
    bool RestoreWithRuntime(int mask) => (bool)entityType.GetMethod("TryRestoreRuntime", dataFlags).Invoke(entityRecords,
        new object[] { boundaryGraph, (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, (mask & 8) != 0 });
    if ((int)entityType.GetProperty("RuntimePilotSlots", dataFlags).GetValue(entityRecords)
        != (int)graphType.GetProperty("SlotCount", dataFlags).GetValue(boundaryGraph)
            - (int)boundaryPlanType.GetProperty("EntitySlots", dataFlags).GetValue(boundaryPlan))
        throw new Exception("Native runtime write pilot does not cover the complete remaining partition.");
    var originalScalarWrites = graphType.GetProperty("ScalarWrites", dataFlags).GetValue(boundaryGraph);
    var originalTagBuckets = (int[])boundaryTagMap.GetType().GetField("_buckets", dataFlags).GetValue(boundaryTagMap);
    for (int iteration = 0; iteration < 6; iteration++)
    {
        boundaryItems[0].GetType().GetProperty("unitCount").SetValue(boundaryItems[0], 71);
        boundaryItems[0].GetType().GetProperty("parentInventory").SetValue(boundaryItems[0], boundarySlot);
        boundaryGrid.GetType().GetField("items", dataFlags).SetValue(boundaryGrid, null);
        boundarySlot.GetType().GetField("currentItem", dataFlags).SetValue(boundarySlot, null);
        boundaryList.Clear();
        for (int i = 0; i < 12; i++) boundaryList.Add(boundaryItems[1]);
        sharedItemTypes.Clear(); sharedItemTypes.Add("changed");
        boundaryShapeData[5] = 71;
        shapeDataField.SetValue(boundaryShape, new byte[1]);
        boundaryShape.GetType().GetProperty("minX").SetValue(boundaryShape, 81);
        windowType.GetField("isLocked", dataFlags).SetValue(boundaryWindow, true);
        windowType.GetField("<openCallback>k__BackingField", dataFlags).SetValue(boundaryWindow, null);
        windowParents.Clear(); linkedStorage.Clear();
        boundaryClosure.GetType().GetField("item", dataFlags).SetValue(boundaryClosure, boundaryItems[1]);
        boundaryTagType.GetField("valueInt", dataFlags).SetValue(boundaryTag, 99);
        for (int i = 0; i < 12; i++) boundaryTagMap["growth-" + i] = boundaryTag;
        originalTagBuckets[0] = 99;
        inventoryDict.SetValue(boundaryGrid, null); inventoryDict.SetValue(boundarySlot, null);
        inventoryCallback.SetValue(boundaryGrid, neverInvokeInventoryCallback);
        inventoryCallback.SetValue(boundarySlot, neverInvokeInventoryCallback);
        boundaryGrid.GetType().GetField("_lastShapeHash", dataFlags).SetValue(boundaryGrid, 123);
        boundarySlot.GetType().GetField("validateLock", dataFlags).SetValue(boundarySlot, true);
        // Missing any required family must not touch state through the runtime payload.
        for (int mask = 0; mask < 15; mask++)
            if (RestoreWithRuntime(mask) || (int)boundaryItems[0].GetType().GetProperty("unitCount").GetValue(boundaryItems[0]) != 71
                || (int)boundaryTagType.GetField("valueInt", dataFlags).GetValue(boundaryTag) != 99)
                throw new Exception("Partial entity selection admitted native runtime writes, mask=" + mask);
        if (!RestoreWithRuntime(15) || EntityDifferences(entityRecords) != 0 || RuntimeDifferences(entityRecords) != 0
            || ((System.Collections.ICollection)graphType.GetMethod("Differences", dataFlags)
                .Invoke(boundaryGraph, new object[] { 12 })).Count != 0
            || !Equals(graphType.GetProperty("ScalarWrites", dataFlags).GetValue(boundaryGraph), originalScalarWrites)
            || !ReferenceEquals(shapeDataField.GetValue(boundaryShape), boundaryShapeData) || boundaryShapeData[5] != 5
            || boundaryList.Count != 2 || !ReferenceEquals(boundaryList[0], boundaryItems[0])
            || boundaryTagMap.Count != 1 || !boundaryTagMap.Contains("runtime-tag")
            || !ReferenceEquals(boundaryTagMap.GetType().GetField("_buckets", dataFlags).GetValue(boundaryTagMap), originalTagBuckets)
            || !ReferenceEquals(windowType.GetField("<openCallback>k__BackingField", dataFlags).GetValue(boundaryWindow), originalWindowCallback))
            throw new Exception("Native paired entity/runtime restore lost original storage, values, aliases or callback identity.");
    }
    var runtimeWrites = entityType.GetProperty("RuntimeScalarWrites", dataFlags).GetValue(entityRecords);
    if ((long)runtimeWrites.GetType().GetField("Item1").GetValue(runtimeWrites) == 0)
        throw new Exception("Native independent runtime ordinary write diagnostics did not count actual writes.");
    Console.WriteLine("PASS native paired entity/runtime writer: incomplete group rejection, six actual independent storage restores, original tag/int blocks and dictionary capacity, callbacks and closure aliases, all partial switch selections rejected without writes, no ordinary oracle writes");
    boundaryItems[0].GetType().GetProperty("unitCount").SetValue(boundaryItems[0], 29);
    boundaryItems[0].GetType().GetProperty("parentInventory").SetValue(boundaryItems[0], boundarySlot);
    boundaryList.Clear(); boundaryList.Add(boundaryItems[1]); boundaryList.Add(boundaryItems[0]);
    boundarySlot.GetType().GetField("currentItem", dataFlags).SetValue(boundarySlot, boundaryItems[1]);
    boundaryTagType.GetField("valueInt", dataFlags).SetValue(boundaryTag, 43);
    for (int i = 0; i < 6; i++) boundaryTagMap["future-" + i] = boundaryTag;
    boundaryShape.GetType().GetProperty("minX").SetValue(boundaryShape, -11);
    var futureAccess = accessType.GetConstructor(dataFlags, null, new[] { typeof(Type), typeof(string) }, null)
        .Invoke(new object[] { Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Object"), "Assembly-CSharp" });
    mod.GetType("ProbablyStolenTools.SemanticTagBlock", true).GetMethod("Enable", BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, new[] { futureAccess });
    foreach (var root in boundaryRoots)
        accessType.GetMethod("AddRoot", dataFlags).Invoke(futureAccess, new object[] { Identity(root), Array.Empty<string>() });
    using (var futureGraph = (IDisposable)graphType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, new object[] { futureAccess, boundaryRoots.Select(Identity).ToArray(), 50000, 1000000, 64L * 1024 * 1024 }))
    using (var futureRecords = (IDisposable)entityType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, new object[] { futureGraph, 50000, 1000000, 64L * 1024 * 1024 }))
    {
        foreach (string enable in new[] { "EnableShapeRestore", "EnableWindowRestore", "EnableItemRestore", "EnableInventoryRestore" })
            entityType.GetMethod(enable, dataFlags).Invoke(futureRecords,
                new object[] { futureGraph, 50000, 1000000, 64L * 1024 * 1024 });
        entityType.GetMethod("EnableRuntimeRestore", dataFlags).Invoke(futureRecords,
            new object[] { futureGraph, 50000, 1000000 });
        try
        {
            entityType.GetMethod("TryRestoreRuntime", dataFlags).Invoke(futureRecords,
                new object[] { boundaryGraph, true, true, true, true });
            throw new Exception("Future native paired plan crossed checkpoint scopes.");
        }
        catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
        for (int iteration = 0; iteration < 6; iteration++)
        {
            if (!RestoreWithRuntime(15) || EntityDifferences(entityRecords) != 0 || RuntimeDifferences(entityRecords) != 0
                || boundaryTagMap.Count != 1 || !ReferenceEquals(boundaryList[0], boundaryItems[0])
                || ((System.Collections.ICollection)graphType.GetMethod("Differences", dataFlags)
                    .Invoke(boundaryGraph, new object[] { 12 })).Count != 0)
                throw new Exception("Native past paired node lost independent original state.");
            if (!(bool)entityType.GetMethod("TryRestoreRuntime", dataFlags).Invoke(futureRecords,
                    new object[] { futureGraph, true, true, true, true })
                || EntityDifferences(futureRecords) != 0 || RuntimeDifferences(futureRecords) != 0
                || boundaryTagMap.Count != 7 || !ReferenceEquals(boundaryList[0], boundaryItems[1])
                || (int)boundaryItems[0].GetType().GetProperty("unitCount").GetValue(boundaryItems[0]) != 29
                || (int)boundaryTagType.GetField("valueInt", dataFlags).GetValue(boundaryTag) != 43
                || ((System.Collections.ICollection)graphType.GetMethod("Differences", dataFlags)
                    .Invoke(futureGraph, new object[] { 12 })).Count != 0)
                throw new Exception("Native future paired node lost independent original state.");
        }
        RestoreWithRuntime(15);
    }
    if (EntityDifferences(entityRecords) != 0 || RuntimeDifferences(entityRecords) != 0
        || !RestoreWithRuntime(15))
        throw new Exception("Disposing a future native node invalidated the retained past node.");
    Console.WriteLine("PASS native two independent complete entity/runtime checkpoints: six past/future round trips, different dictionary capacities and member order, cross-node rejection and future disposal isolation");
    gridWriter.Dispose(); slotWriter.Dispose();
    boundaryItems[0].GetType().GetProperty("unitCount").SetValue(boundaryItems[0], 77);
    boundaryShape.GetType().GetProperty("minX").SetValue(boundaryShape, 81);
    windowType.GetField("isLocked", dataFlags).SetValue(boundaryWindow, true);
    try
    {
        RestoreWithAllEntities(15);
        throw new Exception("Disposed empty-owner inventory writers bypassed preflight.");
    }
    catch (TargetInvocationException e) when (e.InnerException is ObjectDisposedException) { }
    boundaryTagType.GetField("valueInt", dataFlags).SetValue(boundaryTag, 99);
    try
    {
        RestoreWithRuntime(15);
        throw new Exception("Native runtime wrote before disposed inventory preflight.");
    }
    catch (TargetInvocationException e) when (e.InnerException is ObjectDisposedException) { }
    if ((int)boundaryItems[0].GetType().GetProperty("unitCount").GetValue(boundaryItems[0]) != 77
        || (int)boundaryShape.GetType().GetProperty("minX").GetValue(boundaryShape) != 81
        || !(bool)windowType.GetField("isLocked", dataFlags).GetValue(boundaryWindow)
        || (int)boundaryTagType.GetField("valueInt", dataFlags).GetValue(boundaryTag) != 99
        || !RestoreWithAllEntities(7) || EntityDifferences(entityRecords) != 0 || RuntimeDifferences(entityRecords) != 0)
        throw new Exception("Inventory preflight wrote earlier state or damaged inventory-disabled plans.");
    Console.WriteLine("PASS native inventory family budgets, atomic registration, both disposed writers checked before every business write and original writer combinations retained");
    foreach (string rootName in new[] { "GameGridInventory", "GameSlotInventory", "PixelWindow" })
    {
        var root = Data(rootName);
        var singleAccess = accessType.GetConstructor(dataFlags, null, new[] { typeof(Type), typeof(string) }, null)
            .Invoke(new object[] { Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Object"), "Assembly-CSharp" });
        accessType.GetMethod("AddRoot", dataFlags).Invoke(singleAccess, new object[] { Identity(root), Array.Empty<string>() });
        using var singleGraph = (IDisposable)graphType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { singleAccess, new[] { Identity(root) }, 50000, 1000000, 64L * 1024 * 1024 });
        using var singleRecords = (IDisposable)entityType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { singleGraph, 50000, 1000000, 64L * 1024 * 1024 });
        entityType.GetMethod("EnableInventoryRestore", dataFlags).Invoke(singleRecords,
            new object[] { singleGraph, 50000, 1000000, 64L * 1024 * 1024 });
        bool hasInventory = rootName != "PixelWindow";
        if ((int)entityType.GetProperty("GridPilotOwners", dataFlags).GetValue(singleRecords)
                != (rootName == "GameGridInventory" ? 1 : 0)
            || (int)entityType.GetProperty("SlotPilotOwners", dataFlags).GetValue(singleRecords)
                != (rootName == "GameSlotInventory" ? 1 : 0))
            throw new Exception("Single-type inventory family admitted missing types: " + rootName);
        var changedField = hasInventory ? inventoryType.GetField("overrideLockInsert", dataFlags)
            : windowType.GetField("isLocked", dataFlags);
        changedField.SetValue(root, true);
        bool restored = (bool)entityType.GetMethod("TryRestoreEntities", dataFlags)
            .Invoke(singleRecords, new object[] { singleGraph, false, false, false, true });
        if (restored != hasInventory || (!restored && !(bool)changedField.GetValue(root)))
            throw new Exception("Empty inventory family wrote state or single-type family was unavailable: " + rootName);
        if (!restored) graphType.GetMethod("Restore", dataFlags).Invoke(singleGraph, null);
        if ((bool)changedField.GetValue(root) || EntityDifferences(singleRecords) != 0 || RuntimeDifferences(singleRecords) != 0
            || ((System.Collections.ICollection)graphType.GetMethod("Differences", dataFlags)
                .Invoke(singleGraph, new object[] { 12 })).Count != 0)
            throw new Exception("Single-type inventory family lost complete original state: " + rootName);
        if (hasInventory)
        {
            entityType.GetMethod("EnableRuntimeRestore", dataFlags).Invoke(singleRecords,
                new object[] { singleGraph, 50000, 1000000 });
            changedField.SetValue(root, true);
            if (!(bool)entityType.GetMethod("TryRestoreRuntime", dataFlags)
                    .Invoke(singleRecords, new object[] { singleGraph, false, false, false, true })
                || (bool)changedField.GetValue(root) || EntityDifferences(singleRecords) != 0 || RuntimeDifferences(singleRecords) != 0)
                throw new Exception("Native runtime pair unnecessarily required absent entity families: " + rootName);
        }
        else
        {
            try
            {
                entityType.GetMethod("EnableRuntimeRestore", dataFlags).Invoke(singleRecords,
                    new object[] { singleGraph, 50000, 1000000 });
                throw new Exception("Empty native writer group admitted runtime writes.");
            }
            catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
        }
    }
    Console.WriteLine("PASS native inventory family admits grid-only and slot-only roots and paired runtime independently, absent types do not require switches, and empty groups leave ordinary restoration available without writes");
    itemWriter.Dispose();
    boundaryShape.GetType().GetProperty("minX").SetValue(boundaryShape, 81);
    windowType.GetField("isLocked", dataFlags).SetValue(boundaryWindow, true);
    boundaryItems[0].GetType().GetProperty("unitCount").SetValue(boundaryItems[0], 77);
    try
    {
        RestoreWithAllEntities(7);
        throw new Exception("Disposed native item writer remained writable.");
    }
    catch (TargetInvocationException e) when (e.InnerException is ObjectDisposedException) { }
    if ((int)boundaryShape.GetType().GetProperty("minX").GetValue(boundaryShape) != 81
        || !(bool)windowType.GetField("isLocked", dataFlags).GetValue(boundaryWindow)
        || (int)boundaryItems[0].GetType().GetProperty("unitCount").GetValue(boundaryItems[0]) != 77)
        throw new Exception("Item preflight ran after shape, window or legacy writes.");
    // Disabling an invalid writer must still leave previously prepared combinations usable.
    if (!RestoreWithAllEntities(3) || EntityDifferences(entityRecords) != 0 || RuntimeDifferences(entityRecords) != 0)
        throw new Exception("Disabling disposed item writer damaged shape/window-only restoration.");
    Console.WriteLine("PASS native item budgets, atomic plan registration failure and all-writer preflight preserve earlier writer combinations");
    Console.WriteLine("PASS native entity boundary audit: distinct zero-uniqueId items, grid/slot membership, native closure aliases, captured-only topology and unchanged legacy restoration");
    if (!((string)entityType.GetProperty("Summary", dataFlags).GetValue(entityRecords)).Contains("windows=1"))
        throw new Exception("Native inventory/window links were not independently recorded.");
    inventoryType.GetField("<parent>k__BackingField", dataFlags).SetValue(boundaryGrid, null);
    inventoryType.GetField("overrideLockInsert", dataFlags).SetValue(boundaryGrid, false);
    boundaryGrid.GetType().GetField("<inventoryShape>k__BackingField", dataFlags).SetValue(boundaryGrid, null);
    boundarySlot.GetType().GetField("stolenUI", dataFlags).SetValue(boundarySlot, null);
    boundarySlot.GetType().GetField("defaultOrientation", dataFlags).SetValue(boundarySlot, 3);
    windowType.GetField("<childElement>k__BackingField", dataFlags).SetValue(boundaryWindow, boundarySlot);
    windowType.GetField("isLocked", dataFlags).SetValue(boundaryWindow, true);
    var styleField = windowType.GetField("style", dataFlags);
    styleField.SetValue(boundaryWindow, Enum.ToObject(styleField.FieldType, 7));
    windowParents[1] = boundaryItems[0];
    linkedStorage[0] = boundarySlot;
    if (EntityDifferences(entityRecords) != 10)
        throw new Exception("Native inventory/window records missed parent locks shape borrowing style or ordered alias links.");
    graphType.GetMethod("Restore", dataFlags).Invoke(boundaryGraph, null);
    boundaryGrid.GetType().GetField("_lastShapeHash", dataFlags).SetValue(boundaryGrid, 123);
    boundarySlot.GetType().GetField("validateLock", dataFlags).SetValue(boundarySlot, true);
    Action changedWindowCallback = () => throw new Exception("Changed callback must not run.");
    windowType.GetField("<openCallback>k__BackingField", dataFlags).SetValue(boundaryWindow, changedWindowCallback);
    if (EntityDifferences(entityRecords) != 0
        || RuntimeDifferences(entityRecords) != 3
        || ((System.Collections.ICollection)graphType.GetMethod("Differences", dataFlags)
            .Invoke(boundaryGraph, new object[] { 12 })).Count != 3)
        throw new Exception("Explicit runtime partition was dropped from the legacy oracle or entered entity records.");
    graphType.GetMethod("Restore", dataFlags).Invoke(boundaryGraph, null);
    if (EntityDifferences(entityRecords) != 0
        || RuntimeDifferences(entityRecords) != 0
        || !ReferenceEquals(windowType.GetField("<openCallback>k__BackingField", dataFlags).GetValue(boundaryWindow), originalWindowCallback)
        || ((System.Collections.ICollection)graphType.GetMethod("Differences", dataFlags)
            .Invoke(boundaryGraph, new object[] { 12 })).Count != 0)
        throw new Exception("Inventory/window/runtime original identities did not survive legacy restoration.");
    Console.WriteLine("PASS native complete inventory/window field partitions, ten independent relationship mutations and legacy-only callback/cache restoration without business callbacks");
    var elementType = boundaryItems[0].GetType();
    foreach (string name in new[] { "fakePinOverlayVisible", "markHiddenWithBackground", "destroyed" })
        elementType.GetField(name, dataFlags).SetValue(boundaryItems[0], true);
    elementType.GetField("<tempShape>k__BackingField", dataFlags).SetValue(boundaryItems[0], boundaryShape);
    if (EntityDifferences(entityRecords) != 4)
        throw new Exception("Full item hierarchy missed derived state and temporary shape identity.");
    graphType.GetMethod("Restore", dataFlags).Invoke(boundaryGraph, null);
    var itemFunctionType = game.GetType("GameItemFunc", true);
    var callbackField = itemFunctionType.GetField("<onLoaded>k__BackingField", dataFlags);
    // The generic callback needs an exact signature; bind a fixture method without invoking it.
    var actionItemType = typeof(Action<>).MakeGenericType(game.GetType("GameItem", true));
    var throwingMethod = typeof(NativeEntityCallbackFixture).GetMethod("Throw", BindingFlags.Static | BindingFlags.Public)
        .MakeGenericMethod(game.GetType("GameItem", true));
    callbackField.SetValue(boundaryItems[0], Delegate.CreateDelegate(actionItemType, throwingMethod));
    foreach (string name in new[] { "appliedMinX", "appliedMinY", "appliedOrientation", "appliedGridSize" })
        elementType.GetField(name, dataFlags).SetValue(boundaryItems[0], 321);
    foreach (string name in new[] { "appliedFlipped", "validateLock" })
        elementType.GetField(name, dataFlags).SetValue(boundaryItems[0], true);
    if (EntityDifferences(entityRecords) != 0 || RuntimeDifferences(entityRecords) != 1
        || ((System.Collections.ICollection)graphType.GetMethod("Differences", dataFlags)
            .Invoke(boundaryGraph, new object[] { 12 })).Count != 1)
        throw new Exception("Full item classification lost callbacks or reintroduced excluded geometry caches.");
    graphType.GetMethod("Restore", dataFlags).Invoke(boundaryGraph, null);
    if (callbackField.GetValue(boundaryItems[0]) != null || EntityDifferences(entityRecords) != 0)
        throw new Exception("Full item runtime callback identity did not restore.");
    Console.WriteLine("PASS complete native item hierarchy: four derived mutations, callback legacy coverage, all six geometry-cache exclusions and multiword field coverage");
    var fieldPlans = (Array)mod.GetType("ProbablyStolenTools.SemanticInventorySchemas", true)
        .GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
    var entityOnlyGridFields = fieldPlans.Cast<object>()
        .Where(plan => new[] { "GameGridInventory", "GameInventory", "GameInventoryFunc", "TagSystem" }
            .Contains(((Type)plan.GetType().GetField("Type", dataFlags).GetValue(plan)).Name))
        .SelectMany(plan => (FieldInfo[])plan.GetType().GetField("EntityFields", dataFlags).GetValue(plan))
        .Select(field => field.Name).ToArray();
    var entityOnlyAccess = accessType.GetConstructor(dataFlags, null, new[] { typeof(Type), typeof(string) }, null)
        .Invoke(new object[] { Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Object"), "Assembly-CSharp" });
    accessType.GetMethod("AddRoot", dataFlags).Invoke(entityOnlyAccess,
        new object[] { Identity(boundaryGrid), entityOnlyGridFields });
    using var entityOnlyGraph = (IDisposable)graphType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, new object[] { entityOnlyAccess, new[] { Identity(boundaryGrid) }, 50000, 1000000, 64L * 1024 * 1024 });
    try
    {
        using var unexpected = (IDisposable)entityType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { entityOnlyGraph, 50000, 1000000, 64L * 1024 * 1024 });
        throw new Exception("Complete entity fields incorrectly certified a graph with runtime inventory fields removed.");
    }
    catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException
        && e.InnerException.Message.Contains("not fully covered")) { }
    Console.WriteLine("PASS native inventory classification rejects entity-complete roots missing legacy-only callback/cache/tag fields");
    var selectedEntityAccess = accessType.GetConstructor(dataFlags, null, new[] { typeof(Type), typeof(string) }, null)
        .Invoke(new object[] { Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Object"), "Assembly-CSharp" });
    accessType.GetMethod("AddRoot", dataFlags).Invoke(selectedEntityAccess,
        new object[] { Identity(boundaryItems[0]), new[] { "<unitCount>k__BackingField" } });
    using var selectedEntityGraph = (IDisposable)graphType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, new object[] { selectedEntityAccess, new[] { Identity(boundaryItems[0]) }, 50000, 1000000, 64L * 1024 * 1024 });
    try
    {
        using var unexpected = (IDisposable)entityType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { selectedEntityGraph, 50000, 1000000, 64L * 1024 * 1024 });
        throw new Exception("Entity records accepted a partial legacy root as complete base coverage.");
    }
    catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
    try
    {
        using var unexpected = (IDisposable)entityType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { boundaryGraph, 1, 1000000, 64L * 1024 * 1024 });
        throw new Exception("Entity record projection index ignored its object budget.");
    }
    catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
    if (EntityDifferences(entityRecords) != 0
        || ((System.Collections.ICollection)graphType.GetMethod("Differences", dataFlags)
            .Invoke(boundaryGraph, new object[] { 12 })).Count != 0)
        throw new Exception("Failed entity record capture affected an older checkpoint.");
    Console.WriteLine("PASS native entity records reject filtered coverage and index limits without invalidating retained records or legacy checkpoints");
    var shapeWriterType = mod.GetType("ProbablyStolenTools.SemanticShapeWriter", true);
    try
    {
        using var unexpected = (IDisposable)shapeWriterType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { boundaryGraph, 50000, 7, 64L * 1024 * 1024 });
        throw new Exception("Native shape writer accepted incomplete record budget.");
    }
    catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
    try
    {
        using var unexpected = (IDisposable)windowWriterType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { boundaryGraph, 50000, writtenWindowFields.Length - 1, 64L * 1024 * 1024 });
        throw new Exception("Native window writer accepted incomplete record budget.");
    }
    catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
    var windowWriter = (IDisposable)entityType.GetField("_windowWriter", dataFlags).GetValue(entityRecords);
    windowWriter.Dispose();
    boundaryShape.GetType().GetProperty("minX").SetValue(boundaryShape, 81);
    boundaryItems[0].GetType().GetProperty("unitCount").SetValue(boundaryItems[0], 77);
    try
    {
        RestoreWithEntities(true, true);
        throw new Exception("Disposed native window writer remained writable in a combined plan.");
    }
    catch (TargetInvocationException e) when (e.InnerException is ObjectDisposedException) { }
    if ((int)boundaryShape.GetType().GetProperty("minX").GetValue(boundaryShape) != 81
        || (int)boundaryItems[0].GetType().GetProperty("unitCount").GetValue(boundaryItems[0]) != 77)
        throw new Exception("Window writer validation ran after shape or legacy business writes.");
    graphType.GetMethod("Restore", dataFlags).Invoke(boundaryGraph, null);
    Console.WriteLine("PASS native window budgets and combined-writer preflight reject before any shape or legacy write");
    var shapeWriter = (IDisposable)entityType.GetField("_shapeWriter", dataFlags).GetValue(entityRecords);
    shapeWriter.Dispose();
    boundaryItems[0].GetType().GetProperty("unitCount").SetValue(boundaryItems[0], 77);
    try
    {
        RestoreWithShapes();
        throw new Exception("Disposed native entity writer remained writable.");
    }
    catch (TargetInvocationException e) when (e.InnerException is ObjectDisposedException) { }
    if ((int)boundaryItems[0].GetType().GetProperty("unitCount").GetValue(boundaryItems[0]) != 77)
        throw new Exception("Invalid shape writer was checked after old graph business writes.");
    graphType.GetMethod("Restore", dataFlags).Invoke(boundaryGraph, null);
    Console.WriteLine("PASS native shape writer limits and disposed-writer preflight leave legacy full restoration available without hidden write retry");
    ((IDisposable)boundaryGraph).Dispose();
    if (RuntimeDifferences(entityRecords) != 0 || EntityDifferences(entityRecords) != 0)
        throw new Exception("Independent native runtime values or entity retention depended on old graph lifetime.");
    Console.WriteLine("PASS independent native runtime payload: full remaining fields, six restore cycles, callback/cache mutations, original backing storage and valid values after oracle disposal");
}
finally { ((IDisposable)boundaryGraph).Dispose(); }

// Exercise the read-only semantic adapter against installed game types, without Unity or save APIs.
var tagType = game.GetType("TagState", true);
var systemType = game.GetType("TagSystem", true);
var mapType = typeof(Dictionary<,>).MakeGenericType(typeof(string), tagType);
var tagMap = (System.Collections.IDictionary)Activator.CreateInstance(mapType);
var tagSystem = Data("TagSystem");
var secondSystem = Data("GameGridInventory");
var effectSystem = Data("CombatAbilityEffect");
var mapField = systemType.GetField("<dict>k__BackingField", dataFlags);
mapField.SetValue(tagSystem, tagMap);
mapField.SetValue(secondSystem, tagMap);
mapField.SetValue(effectSystem, tagMap);
var tagState = Data("TagState");
var modifierState = Data("ItemStateModifier");
object[] tagValues = { false, "native text", 0, BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)),
    0L, BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000000001)), false, "inactive", "Inactive" };
string[] tagNames = { "valueEnabled", "internalValueString", "valueInt", "valueFloat", "valueLong",
    "valueDouble", "valueBool", "<identifier>k__BackingField", "<identifierName>k__BackingField" };
var tagFields = tagNames.Select(name => tagType.GetField(name, dataFlags)).ToArray();
foreach (var state in new[] { tagState, modifierState })
    for (int i = 0; i < tagFields.Length; i++) tagFields[i].SetValue(state, tagValues[i]);
tagFields[1].SetValue(modifierState, null);
tagMap.Add("inactive", tagState);
tagMap.Add("alias", tagState);
tagMap.Add("null", null);
tagMap.Add("modifier", modifierState);
var tagRoots = Array.CreateInstance(systemType, 3);
tagRoots.SetValue(tagSystem, 0);
tagRoots.SetValue(secondSystem, 1);
tagRoots.SetValue(effectSystem, 2);
var tagAccess = accessType.GetConstructor(dataFlags, null,
    new[] { typeof(Type), typeof(string), typeof(bool), typeof(bool), typeof(bool) }, null)
    .Invoke(new object[] { Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Object"),
        "Assembly-CSharp", true, true, false });
var capturedEnumerator = tagMap.GetEnumerator();
capturedEnumerator.MoveNext();
var tagFixture = new object[] { tagRoots, tagMap,
    mapType.GetField("_entries", dataFlags).GetValue(tagMap), tagMap.Keys, tagMap.Values,
    capturedEnumerator, Data("TagState") };
accessType.GetMethod("AddRoot", dataFlags).Invoke(tagAccess, new object[] { Identity(tagFixture), Array.Empty<string>() });
var legacyTagGraph = graphType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
    .Invoke(null, new object[] { tagAccess, new[] { Identity(tagFixture) }, 50000, 1000000, 64L * 1024 * 1024 });
var compactType = mod.GetType("ProbablyStolenTools.SemanticTagBlock", true);
var compactAccess = accessType.GetConstructor(dataFlags, null,
    new[] { typeof(Type), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(bool) }, null)
    .Invoke(new object[] { Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Object"),
        "Assembly-CSharp", true, true, true, true });
compactType.GetMethod("Enable", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { compactAccess });
accessType.GetMethod("AddRoot", dataFlags).Invoke(compactAccess, new object[] { Identity(tagFixture), Array.Empty<string>() });
var tagGraph = graphType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
    .Invoke(null, new object[] { compactAccess, new[] { Identity(tagFixture) }, 50000, 1000000, 64L * 1024 * 1024 });
object shadow = null;
try
{
    int nativeIntArrays = (int)accessType.GetProperty("IntArrayBlockCount", dataFlags).GetValue(compactAccess);
    if (nativeIntArrays == 0
        || (int)graphType.GetProperty("BlockCount", dataFlags).GetValue(tagGraph) - nativeIntArrays != 2)
        throw new Exception("Exact native TagState and standalone state must compress; ItemStateModifier must fall back.");
    foreach (bool directCensus in new[] { false, true })
    {
        var censusAccess = accessType.GetConstructor(dataFlags, null, new[] { typeof(Type), typeof(string) }, null)
            .Invoke(new object[] { Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Object"), "Assembly-CSharp" });
        if (directCensus) compactType.GetMethod("EnableCensus", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new[] { censusAccess });
        accessType.GetMethod("AddRoot", dataFlags).Invoke(censusAccess,
            new object[] { Identity(tagFixture), Array.Empty<string>() });
        var censusGraph = graphType.GetMethod("Census", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { censusAccess, new[] { Identity(tagFixture) }, 50000, 1000000, 64L * 1024 * 1024 });
        try
        {
            if ((int)accessType.GetProperty("CensusValueObjectCount", dataFlags).GetValue(censusAccess) != (directCensus ? 2 : 0))
                throw new Exception("Native census probe must include standalone/exact tags but exclude ItemStateModifier.");
            foreach (string metric in new[] { "ObjectCount", "SlotCount", "ByteCount" })
                if (!Equals(graphType.GetProperty(metric, dataFlags).GetValue(censusGraph),
                    graphType.GetProperty(metric, dataFlags).GetValue(legacyTagGraph)))
                    throw new Exception("Native census probe changed coverage/budget: " + metric);
            foreach (var id in (IEnumerable<IntPtr>)graphType.GetProperty("Objects", dataFlags).GetValue(legacyTagGraph))
                if (!(bool)graphType.GetMethod("Contains", dataFlags).Invoke(censusGraph, new object[] { id })
                    || !Equals(graphType.GetMethod("SourceOf", dataFlags).Invoke(censusGraph, new object[] { id }),
                        graphType.GetMethod("SourceOf", dataFlags).Invoke(legacyTagGraph, new object[] { id })))
                    throw new Exception("Native census probe changed identities or reference discovery order.");
            if ((int)graphType.GetProperty("BlockCount", dataFlags).GetValue(censusGraph) != 0
                || ((System.Collections.IDictionary)accessType.GetField("_savedValues", dataFlags).GetValue(censusAccess)).Count != 0
                || ((System.Collections.IDictionary)accessType.GetField("_values", dataFlags).GetValue(censusAccess)).Count != 0)
                throw new Exception("Native census probe retained block/scalar snapshot payloads.");
        }
        finally { ((IDisposable)censusGraph).Dispose(); }
    }
    var probeAccess = accessType.GetConstructor(dataFlags, null, new[] { typeof(Type), typeof(string) }, null)
        .Invoke(new object[] { null, "Assembly-CSharp" });
    try
    {
        compactType.GetMethod("EnableCensus", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { probeAccess });
        var probe = (Delegate)accessType.GetField("CensusValuesProbe", dataFlags).GetValue(probeAccess);
        var noSlots = Array.CreateInstance(mod.GetType("ProbablyStolenTools.StateSlot"), 0);
        if ((bool)probe.DynamicInvoke(tagState, noSlots) || (bool)probe.DynamicInvoke(modifierState, noSlots))
            throw new Exception("Native census probe accepted a mismatched description or subclass.");
        accessType.GetMethod("AddRoot", dataFlags).Invoke(probeAccess, new object[] { Identity(tagState), new[] { "valueInt" } });
        var selectedCensus = graphType.GetMethod("Census", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { probeAccess, new[] { Identity(tagState) }, 10, 10, 1000L });
        try
        {
            if ((int)accessType.GetProperty("CensusValueObjectCount", dataFlags).GetValue(probeAccess) != 0
                || (int)graphType.GetProperty("SlotCount", dataFlags).GetValue(selectedCensus) != 1)
                throw new Exception("Native census probe expanded a selected root.");
        }
        finally { ((IDisposable)selectedCensus).Dispose(); }
    }
    finally { ((IDisposable)probeAccess).Dispose(); }
    Console.WriteLine("PASS native exact tag census probe/ordinary parity: all identities, source paths, budgets, standalone tags, subtype/schema/selected-root fallback and no retained scalar payloads");
    foreach (string metric in new[] { "ObjectCount", "SlotCount", "ByteCount" })
        if (!Equals(graphType.GetProperty(metric, dataFlags).GetValue(tagGraph),
            graphType.GetProperty(metric, dataFlags).GetValue(legacyTagGraph)))
            throw new Exception("Compact native budget/coverage mismatch: " + metric);
    foreach (var id in (System.Collections.IEnumerable)graphType.GetProperty("Objects", dataFlags).GetValue(legacyTagGraph))
        if (!(bool)graphType.GetMethod("Contains", dataFlags).Invoke(tagGraph, new[] { id })
            || !Equals(graphType.GetMethod("SourceOf", dataFlags).Invoke(tagGraph, new[] { id }),
                graphType.GetMethod("SourceOf", dataFlags).Invoke(legacyTagGraph, new[] { id })))
            throw new Exception("Compact native identity or discovery source mismatch.");
    var shadowType = mod.GetType("ProbablyStolenTools.SemanticTagShadow", true);
    shadow = shadowType.GetMethod("Capture", BindingFlags.NonPublic | BindingFlags.Static)
        .Invoke(null, new object[] { tagGraph, 50000, 1000000, 64L * 1024 * 1024 });
    var semantic = shadowType.GetField("_snapshot", dataFlags).GetValue(shadow);
    int Diff(object target, Type type) => ((System.Collections.ICollection)type.GetMethod("Differences", dataFlags)
        .Invoke(target, new object[] { 12 })).Count;
    if (Diff(tagGraph, graphType) != 0 || Diff(semantic, semantic.GetType()) != 0)
        throw new Exception("Semantic probing modified native tag data.");
    if ((int)semantic.GetType().GetProperty("TagCount", dataFlags).GetValue(semantic) != 2
        || (int)semantic.GetType().GetProperty("EntryCount", dataFlags).GetValue(semantic) != 4)
        throw new Exception("Semantic adapter dropped disabled, null or aliased entries.");
    string summary = (string)shadowType.GetProperty("Summary", dataFlags).GetValue(shadow);
    if (!summary.Contains("scope=tag-base-fields extraFields=legacy-only")
        || !summary.Contains("derivedSystems=2 derivedStates=1")
        || !summary.Contains("GameGridInventory:1") || !summary.Contains("ItemStateModifier:1"))
        throw new Exception("Semantic summary must disclose native subclass projection scope: " + summary);
    string ownership = (string)shadowType.GetProperty("OwnershipSummary", dataFlags).GetValue(shadow);
    foreach (string expected in new[] { "scope=captured-graph-only pruningAllowed=false", "sharedMaps=1",
        "mapExternalEdges=4", "storageExternalEdges=1", "viewExternalEdges=2", "standaloneStates=1",
        "unknownMapSlots=0" })
        if (!ownership.Contains(expected)) throw new Exception("Native tag ownership audit missed " + expected + ": " + ownership);
    object[] changed = { true, "", 42, 0f, 99L, 1d, true, "changed", "Changed" };
    foreach (var state in new[] { tagState, modifierState })
        for (int i = 0; i < tagFields.Length; i++)
        {
            tagFields[i].SetValue(state, changed[i]);
            if (Diff(semantic, semantic.GetType()) == 0 || Diff(tagGraph, graphType) == 0)
                throw new Exception("Native semantic reader missed field: " + state.GetType().Name + "." + tagNames[i]);
            var attributed = ((System.Collections.IEnumerable)graphType.GetMethod("DifferencesWithSources", dataFlags)
                .Invoke(tagGraph, new object[] { 12 })).Cast<string>().ToArray();
            if (attributed.Length != 1 || !attributed[0].Contains("type=" + state.GetType().FullName + " ")
                || !attributed[0].Contains("field=TagState." + tagNames[i] + " ")
                || !attributed[0].Contains("storage=" + (ReferenceEquals(state, tagState) ? "block" : "field"))
                || !attributed[0].Contains("source=root -> "))
                throw new Exception("Native difference source missed compact/fallback field: " + tagNames[i]);
            var repair = PrepareRepair(tagGraph);
            ApplyRepair(repair); VerifyRepair(tagGraph, repair);
            if (Diff(semantic, semantic.GetType()) != 0 || Diff(legacyTagGraph, graphType) != 0)
                throw new Exception("Native semantic field did not restore: " + tagNames[i]);
        }
    foreach (var state in new[] { tagState, modifierState })
        foreach (int index in new[] { 1, 7, 8 })
        {
            string expected = (string)tagFields[index].GetValue(state);
            if (expected == null) continue;
            object identityShadow = shadowType.GetMethod("Capture", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { tagGraph, 50000, 1000000, 64L * 1024 * 1024 });
            try
            {
                var identitySemantic = shadowType.GetField("_snapshot", dataFlags).GetValue(identityShadow);
                tagFields[index].SetValue(state, new string(expected.ToCharArray()));
                if (Diff(identitySemantic, identitySemantic.GetType()) == 0 || Diff(tagGraph, graphType) == 0)
                    throw new Exception("Native semantic reader missed equal-text replacement: " + tagNames[index]);
            }
            finally
            {
                ((IDisposable)identityShadow).Dispose();
                graphType.GetMethod("Restore", dataFlags).Invoke(tagGraph, null);
            }
        }
    var priorityField = modifierState.GetType().GetField("priority", dataFlags);
    var durationField = effectSystem.GetType().GetField("<duration>k__BackingField", dataFlags);
    priorityField.SetValue(modifierState, 99);
    durationField.SetValue(effectSystem, 7);
    if (Diff(semantic, semantic.GetType()) != 0 || Diff(tagGraph, graphType) == 0)
        throw new Exception("Subclass extra fields must remain explicitly outside semantic comparison and inside legacy coverage.");
    graphType.GetMethod("Restore", dataFlags).Invoke(tagGraph, null);
    if ((int)priorityField.GetValue(modifierState) != 0 || (int)durationField.GetValue(effectSystem) != 0)
        throw new Exception("Legacy restore lost subclass extra fields.");
    for (int round = 0; round < 2; round++)
    {
        for (int i = 0; i < tagFields.Length; i++) tagFields[i].SetValue(tagState, changed[i]);
        tagMap.Remove("inactive");
        for (int i = 0; i < 100; i++) tagMap["new-" + i] = Data("TagState");
        mapField.SetValue(tagSystem, Activator.CreateInstance(mapType));
        var mapDetails = ((System.Collections.IEnumerable)graphType.GetMethod("DifferencesWithSources", dataFlags)
            .Invoke(tagGraph, new object[] { 12 })).Cast<string>().ToArray();
        if (mapDetails.Length == 0 || mapDetails.Any(s => s.Contains("Version="))
            || !mapDetails.Any(s => s.Contains("field=System.Collections.Generic.Dictionary`2<...>._version ")))
            throw new Exception("Native generic dictionary diagnostic lost its version field suffix.");
        if (Diff(semantic, semantic.GetType()) == 0)
            throw new Exception("Semantic adapter missed native tag mutations.");
        var repair = PrepareRepair(tagGraph);
        ApplyRepair(repair); VerifyRepair(tagGraph, repair);
        if (Diff(tagGraph, graphType) != 0 || Diff(legacyTagGraph, graphType) != 0 || Diff(semantic, semantic.GetType()) != 0
            || !ReferenceEquals(mapField.GetValue(tagSystem), tagMap)
            || !ReferenceEquals(mapField.GetValue(secondSystem), tagMap)
            || !ReferenceEquals(mapField.GetValue(effectSystem), tagMap))
            throw new Exception("Native tag restore did not match the read-only semantic checkpoint.");
    }
    var logicalEntries = new List<(object Key, object Value)>();
    foreach (var key in tagMap.Keys) logicalEntries.Add((key, tagMap[key]));
    for (int i = 0; i < 100; i++) tagMap["growth-" + i] = Data("TagState");
    tagMap.Clear();
    foreach (var pair in logicalEntries) tagMap.Add(pair.Key, pair.Value);
    if (Diff(semantic, semantic.GetType()) != 0 || Diff(tagGraph, graphType) == 0)
        throw new Exception("Native counterexample did not distinguish logical values from backing storage/version.");
    bool invalidated = false;
    try { capturedEnumerator.MoveNext(); }
    catch (InvalidOperationException) { invalidated = true; }
    if (!invalidated) throw new Exception("Native fixture enumerator should be invalid after logical reconstruction.");
    graphType.GetMethod("Restore", dataFlags).Invoke(tagGraph, null);
    if (Diff(semantic, semantic.GetType()) != 0 || Diff(tagGraph, graphType) != 0)
        throw new Exception("Native legacy fallback did not repair the logical reconstruction counterexample.");
    // Synthetic third-party subclasses must still fail closed, with an actionable type name.
    var externalModule = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
        new AssemblyName("SemanticExternalFixture"), System.Reflection.Emit.AssemblyBuilderAccess.Run)
        .DefineDynamicModule("Fixture");
    foreach (var parent in new[] { systemType, tagType })
    {
        var builder = externalModule.DefineType("External" + parent.Name, TypeAttributes.Public, parent);
        var constructor = builder.DefineConstructor(MethodAttributes.Public,
            CallingConventions.Standard, new[] { typeof(string), typeof(string) });
        var il = constructor.GetILGenerator();
        il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
        if (parent == tagType)
        {
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_2);
        }
        il.Emit(System.Reflection.Emit.OpCodes.Call, parent.GetConstructor(parent == tagType
            ? new[] { typeof(string), typeof(string) } : Type.EmptyTypes));
        il.Emit(System.Reflection.Emit.OpCodes.Ret);
        var externalType = builder.CreateType();
        bool refused = false;
        try
        {
            shadowType.GetMethod("CheckNativeType", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { externalType, parent });
        }
        catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException
            && e.InnerException.Message.Contains(externalType.FullName)) { refused = true; }
        if (!refused) throw new Exception("Third-party tag subtype was not refused: " + externalType.FullName);
    }
    if (Diff(tagGraph, graphType) != 0 || Diff(semantic, semantic.GetType()) != 0)
        throw new Exception("Subtype refusal invalidated retained checkpoints.");
    // The standalone exact state starts with null backing fields, outside the semantic shadow's map scope.
    foreach (var state in new[] { tagState, tagFixture[6] })
        for (int i = 0; i < tagFields.Length; i++)
        {
            object expected = tagFields[i].GetValue(state);
            tagFields[i].SetValue(state, changed[i]);
            bool unchanged = tagFields[i].FieldType == typeof(string) ? ReferenceEquals(expected, changed[i])
                : expected is float f ? BitConverter.SingleToInt32Bits(f) == BitConverter.SingleToInt32Bits((float)changed[i])
                : expected is double d ? BitConverter.DoubleToInt64Bits(d) == BitConverter.DoubleToInt64Bits((double)changed[i])
                : Equals(expected, changed[i]);
            if (unchanged) continue;
            if (Diff(tagGraph, graphType) == 0)
                throw new Exception("Compact native coverage missed standalone field " + tagNames[i]);
            graphType.GetMethod("Restore", dataFlags).Invoke(tagGraph, null);
            object actual = tagFields[i].GetValue(state);
            bool equal = tagFields[i].FieldType == typeof(string) ? ReferenceEquals(expected, actual)
                : expected is float single ? BitConverter.SingleToInt32Bits(single) == BitConverter.SingleToInt32Bits((float)actual)
                : expected is double number ? BitConverter.DoubleToInt64Bits(number) == BitConverter.DoubleToInt64Bits((double)actual)
                : Equals(expected, actual);
            if (!equal || Diff(legacyTagGraph, graphType) != 0)
                throw new Exception("Compact native restore changed field bits/null/identity " + tagNames[i]);
        }
    // Explicitly verify exact null text: the public setter would incorrectly normalize this to empty.
    if (tagFields[1].GetValue(tagFixture[6]) != null)
        throw new Exception("Compact exact null text was normalized.");
    var filteredAccess = accessType.GetConstructor(dataFlags, null, new[] { typeof(Type), typeof(string) }, null)
        .Invoke(new object[] { null, "Assembly-CSharp" });
    compactType.GetMethod("Enable", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { filteredAccess });
    accessType.GetMethod("AddRoot", dataFlags).Invoke(filteredAccess, new object[] { Identity(tagState), new[] { "valueInt" } });
    var filteredGraph = graphType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, new object[] { filteredAccess, new[] { Identity(tagState) }, 100, 100, 1000L });
    try
    {
        if ((int)graphType.GetProperty("BlockCount", dataFlags).GetValue(filteredGraph) != 0
            || (int)graphType.GetProperty("SlotCount", dataFlags).GetValue(filteredGraph) != 1)
            throw new Exception("Compact native path expanded a selected root.");
    }
    finally { ((IDisposable)filteredGraph).Dispose(); }
    // Retained checkpoints own their backend; changing capture mode cannot reinterpret their payload.
    accessType.GetField("BlockCapture", dataFlags).SetValue(compactAccess, null);
    tagFields[2].SetValue(tagState, 81);
    var futureAccess = accessType.GetConstructor(dataFlags, null, new[] { typeof(Type), typeof(string) }, null)
        .Invoke(new object[] { null, "Assembly-CSharp" });
    accessType.GetMethod("AddRoot", dataFlags).Invoke(futureAccess, new object[] { Identity(tagFixture), Array.Empty<string>() });
    var futureGraph = graphType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, new object[] { futureAccess, new[] { Identity(tagFixture) }, 50000, 1000000, 64L * 1024 * 1024 });
    try
    {
        for (int i = 0; i < 3; i++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers();
            graphType.GetMethod("Restore", dataFlags).Invoke(tagGraph, null);
            if ((int)tagFields[2].GetValue(tagState) != 0 || Diff(legacyTagGraph, graphType) != 0)
                throw new Exception("Retained compact past checkpoint failed.");
            graphType.GetMethod("Restore", dataFlags).Invoke(futureGraph, null);
            if ((int)tagFields[2].GetValue(tagState) != 81 || Diff(futureGraph, graphType) != 0)
                throw new Exception("Retained legacy future checkpoint failed.");
        }
    }
    finally
    {
        ((IDisposable)futureGraph).Dispose();
        graphType.GetMethod("Restore", dataFlags).Invoke(tagGraph, null);
    }
    Console.WriteLine("PASS installed native hybrid tag payload: legacy identity/budget/field parity, all nine fields, exact null/bit/reference restore, subclass/selected-root fallback, captured topology audit and mixed-backend retained checkpoints");
    Console.WriteLine("PASS installed native int array blocks combined with tag blocks preserve dictionary buckets, full recursive oracle and ownership audit");
}
finally
{
    ((IDisposable)shadow)?.Dispose();
    ((IDisposable)tagGraph).Dispose();
    ((IDisposable)legacyTagGraph).Dispose();
}

if (args.Contains("--benchmark-tags") || args.Contains("--benchmark-writes") || args.Contains("--benchmark-reconcile")
    || args.Contains("--benchmark-int-arrays") || args.Contains("--benchmark-census-tags")
    || args.Contains("--benchmark-scalar-writes") || args.Contains("--benchmark-typed-comparisons")
    || args.Contains("--benchmark-reference-comparisons"))
{
    bool benchmarkWrites = args.Contains("--benchmark-writes");
    bool benchmarkReconcile = args.Contains("--benchmark-reconcile");
    bool benchmarkIntArrays = args.Contains("--benchmark-int-arrays");
    bool benchmarkCensus = args.Contains("--benchmark-census-tags");
    bool benchmarkScalars = args.Contains("--benchmark-scalar-writes");
    bool benchmarkComparisons = args.Contains("--benchmark-typed-comparisons");
    bool benchmarkReferences = args.Contains("--benchmark-reference-comparisons");
    bool coldComparisons = benchmarkComparisons && args.Contains("--cold-comparisons");
    var readerType = mod.GetType("ProbablyStolenTools.SnapshotFieldReader", true);
    bool denseScalars = benchmarkScalars && args.Contains("--dense-scalars");
    // Isolated native business objects on .NET 6, not a Unity/Mono frame-time measurement.
    var fixture = Array.CreateInstance(systemType, 1024);
    for (int i = 0; i < fixture.Length; i++)
    {
        var system = Data("TagSystem");
        var map = (System.Collections.IDictionary)Activator.CreateInstance(mapType);
        mapField.SetValue(system, map);
        for (int j = 0; j < 16; j++)
        {
            var state = Data(j == 0 && i % 64 == 0 ? "ItemStateModifier" : "TagState");
            for (int k = 0; k < tagFields.Length; k++) tagFields[k].SetValue(state, tagValues[k]);
            tagFields[2].SetValue(state, (i + j) % 41);
            map.Add("tag-" + j, state); map.Add("alias-" + j, state);
        }
        fixture.SetValue(system, i);
    }
    var samples = new Dictionary<bool, List<(double Capture, double Verify, double Restore, long Allocated, long RestoreAllocated, double Scan, double Census, long CensusAllocated, long VerifyAllocated)>>
        { [false] = new(), [true] = new() };
    long? expectedSlots = null, expectedBytes = null, expectedObjects = null;
    var captureMethod = graphType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic);
    var verifyMethod = graphType.GetMethod("Differences", dataFlags);
    var restoreMethod = graphType.GetMethod("Restore", dataFlags);
    var sourceMethod = graphType.GetMethod("DifferencesWithSources", dataFlags);
    var censusMethod = graphType.GetMethod("Census", BindingFlags.Static | BindingFlags.NonPublic);
    var applyMethod = graphType.GetNestedType("Reconciliation", BindingFlags.NonPublic).GetMethod("Apply", dataFlags);
    int repairedSlots = 0, intBlocks = 0, intSlots = 0, probedTags = 0;
    var scalarCounts = new Dictionary<bool, (long Checked, long Skipped)>();
    var typedCounts = new Dictionary<bool, long>();
    var comparisonCompileTimes = new Dictionary<bool, double>();
    int sampleCount = benchmarkIntArrays || benchmarkCensus || benchmarkWrites || benchmarkScalars || benchmarkComparisons || benchmarkReferences ? 9 : 5;
    int warmups = benchmarkIntArrays || benchmarkCensus || benchmarkWrites || benchmarkScalars || benchmarkComparisons || benchmarkReferences ? 3 : 1;
    for (int round = -warmups; round < sampleCount; round++)
        foreach (bool compact in round % 2 == 0 ? new[] { true, false } : new[] { false, true })
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            if (coldComparisons) readerType.GetMethod("ClearCache", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            var benchAccess = accessType.GetConstructor(dataFlags, null,
                new[] { typeof(Type), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(bool) }, null)
                .Invoke(new object[] { null, "Assembly-CSharp", true, true, !benchmarkWrites || compact,
                    benchmarkReferences || benchmarkComparisons || benchmarkScalars || benchmarkIntArrays && compact });
            // Keep the old identity oracle in every other benchmark, so previous comparisons stay comparable.
            accessType.GetProperty("DirectReferenceComparisons", dataFlags).SetValue(benchAccess, benchmarkReferences && compact);
            if (benchmarkComparisons)
                accessType.GetProperty("TypedScalarComparisons", dataFlags).SetValue(benchAccess, compact);
            if (benchmarkScalars)
                accessType.GetProperty("SkipUnchangedScalars", dataFlags).SetValue(benchAccess, compact);
            if (!denseScalars && (benchmarkReferences || benchmarkReconcile || benchmarkWrites || benchmarkIntArrays || benchmarkCensus || benchmarkScalars || benchmarkComparisons || compact)) compactType.GetMethod("Enable", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new[] { benchAccess });
            accessType.GetMethod("AddRoot", dataFlags).Invoke(benchAccess, new object[] { Identity(fixture), Array.Empty<string>() });
            long before = GC.GetAllocatedBytesForCurrentThread();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            object benchGraph = captureMethod.Invoke(null, new object[] { benchAccess, new[] { Identity(fixture) },
                200000, 3000000, 256L * 1024 * 1024 });
            double captured = clock.Elapsed.TotalMilliseconds;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            try
            {
                long slots = Convert.ToInt64(graphType.GetProperty("SlotCount", dataFlags).GetValue(benchGraph));
                long bytes = (long)graphType.GetProperty("ByteCount", dataFlags).GetValue(benchGraph);
                long objects = Convert.ToInt64(graphType.GetProperty("ObjectCount", dataFlags).GetValue(benchGraph));
                expectedSlots ??= slots; expectedBytes ??= bytes; expectedObjects ??= objects;
                if (slots != expectedSlots || bytes != expectedBytes || objects != expectedObjects)
                    throw new Exception("Native benchmark hybrid coverage or logical budget differs.");
                long beforeVerify = GC.GetAllocatedBytesForCurrentThread();
                long compileBefore = (long)readerType.GetProperty("CompareCompileTicks", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                clock.Restart();
                int differences = ((System.Collections.ICollection)verifyMethod.Invoke(benchGraph, new object[] { 12 })).Count;
                double verified = clock.Elapsed.TotalMilliseconds;
                long verifyAllocated = GC.GetAllocatedBytesForCurrentThread() - beforeVerify;
                comparisonCompileTimes[compact] = ((long)readerType.GetProperty("CompareCompileTicks", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)
                    - compileBefore) * (1000d / System.Diagnostics.Stopwatch.Frequency);
                if (differences != 0) throw new Exception("Native benchmark capture verification failed.");
                double censusMs = 0;
                long censusAllocated = 0;
                if (benchmarkIntArrays || benchmarkCensus)
                {
                    if (benchmarkIntArrays && compact)
                    {
                        intBlocks = (int)accessType.GetProperty("IntArrayBlockCount", dataFlags).GetValue(benchAccess);
                        intSlots = (int)accessType.GetProperty("IntArraySlotCount", dataFlags).GetValue(benchAccess);
                    }
                    if (benchmarkIntArrays && ((int)accessType.GetProperty("IntArrayBlockCount", dataFlags).GetValue(benchAccess) > 0) != compact)
                        throw new Exception("Native int array benchmark did not exercise the requested payload mode.");
                    var censusAccess = accessType.GetConstructor(dataFlags, null,
                        new[] { typeof(Type), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(bool) }, null)
                        .Invoke(new object[] { null, "Assembly-CSharp", true, true, true, compact });
                    accessType.GetMethod("AddRoot", dataFlags).Invoke(censusAccess,
                        new object[] { Identity(fixture), Array.Empty<string>() });
                    if (benchmarkCensus && compact)
                        compactType.GetMethod("EnableCensus", BindingFlags.Static | BindingFlags.NonPublic)
                            .Invoke(null, new[] { censusAccess });
                    long beforeCensus = GC.GetAllocatedBytesForCurrentThread();
                    clock.Restart();
                    var censusGraph = censusMethod.Invoke(null, new object[] { censusAccess, new[] { Identity(fixture) },
                        200000, 3000000, 256L * 1024 * 1024 });
                    censusMs = clock.Elapsed.TotalMilliseconds;
                    censusAllocated = GC.GetAllocatedBytesForCurrentThread() - beforeCensus;
                    try
                    {
                        if (benchmarkCensus)
                        {
                            int count = (int)accessType.GetProperty("CensusValueObjectCount", dataFlags).GetValue(censusAccess);
                            if ((count > 0) != compact) throw new Exception("Census benchmark did not exercise the requested mode.");
                            if (compact) probedTags = count;
                        }
                        if (Convert.ToInt64(graphType.GetProperty("SlotCount", dataFlags).GetValue(censusGraph)) != slots
                            || Convert.ToInt64(graphType.GetProperty("ObjectCount", dataFlags).GetValue(censusGraph)) != objects
                            || (long)graphType.GetProperty("ByteCount", dataFlags).GetValue(censusGraph) != bytes)
                            throw new Exception("Native int array benchmark census coverage or budgets differ.");
                    }
                    finally { ((IDisposable)censusGraph).Dispose(); }
                }
                if (benchmarkReconcile)
                {
                    // Sparse refresh-like mutation: one tag plus one grown dictionary, not all tags.
                    var map = (System.Collections.IDictionary)mapField.GetValue(fixture.GetValue(0));
                    tagFields[2].SetValue(map["tag-1"], -1);
                    for (int i = 0; i < 80; i++) map["refresh-" + i] = Data("TagState");
                }
                else
                    foreach (object system in fixture)
                        foreach (var state in ((System.Collections.IDictionary)mapField.GetValue(system)).Values)
                        {
                            tagFields[2].SetValue(state, -1);
                            if (denseScalars)
                            {
                                tagFields[0].SetValue(state, true);
                                tagFields[3].SetValue(state, 0f);
                                tagFields[4].SetValue(state, 1L);
                                tagFields[5].SetValue(state, 0d);
                                tagFields[6].SetValue(state, true);
                            }
                        }
                long beforeRestore = GC.GetAllocatedBytesForCurrentThread();
                double scanned = 0;
                object repairPlan = null;
                clock.Restart();
                if (benchmarkReconcile)
                {
                    if (compact)
                    {
                        repairPlan = PrepareRepair(benchGraph);
                        if ((string)repairPlan.GetType().GetProperty("Mode", dataFlags).GetValue(repairPlan) != "owners")
                            throw new Exception("Sparse native benchmark unexpectedly fell back.");
                        repairedSlots = (int)repairPlan.GetType().GetProperty("PlannedSlots", dataFlags).GetValue(repairPlan);
                    }
                    else if (((System.Collections.ICollection)sourceMethod.Invoke(benchGraph, new object[] { 12 })).Count == 0)
                        throw new Exception("Sparse native benchmark mutation was not detected.");
                    scanned = clock.Elapsed.TotalMilliseconds;
                    clock.Restart();
                }
                if (repairPlan != null) applyMethod.Invoke(repairPlan, null);
                else restoreMethod.Invoke(benchGraph, null);
                double restored = clock.Elapsed.TotalMilliseconds;
                long restoreAllocated = GC.GetAllocatedBytesForCurrentThread() - beforeRestore;
                if (benchmarkComparisons)
                {
                    long count = (long)accessType.GetProperty("TypedComparisonCount", dataFlags).GetValue(benchAccess);
                    if ((count > 0) != compact) throw new Exception("Typed comparison benchmark did not exercise the requested mode.");
                    typedCounts[compact] = count;
                }
                if (benchmarkReferences)
                {
                    long count = (long)accessType.GetProperty("ReferenceComparisonCount", dataFlags).GetValue(benchAccess);
                    if ((count > 0) != compact) throw new Exception("Reference benchmark did not exercise the requested mode.");
                    typedCounts[compact] = count;
                }
                if (benchmarkScalars)
                {
                    var counts = ((long Checked, long Skipped))accessType.GetProperty("ScalarWrites", dataFlags).GetValue(benchAccess);
                    if ((counts.Checked > 0) != compact || (counts.Skipped > 0) != compact)
                        throw new Exception("Scalar write benchmark did not exercise the requested mode.");
                    scalarCounts[compact] = counts;
                }
                if (repairPlan != null) VerifyRepair(benchGraph, repairPlan);
                if (((System.Collections.ICollection)verifyMethod.Invoke(benchGraph, new object[] { 12 })).Count != 0)
                    throw new Exception("Native benchmark restoration verification failed.");
                if (round >= 0) samples[compact].Add((captured, verified, restored, allocated, restoreAllocated, scanned,
                    censusMs, censusAllocated, verifyAllocated));
            }
            finally { ((IDisposable)benchGraph).Dispose(); }
        }
    double Median(IEnumerable<double> values) => values.OrderBy(v => v).ElementAt(sampleCount / 2);
    foreach (bool compact in new[] { false, true })
    {
        var values = samples[compact];
        string mode = benchmarkReconcile ? (compact ? "hybrid-owner-repair" : "hybrid-full-reconcile")
            : benchmarkWrites ? (compact ? "hybrid-direct-write" : "hybrid-recursive-write")
            : (compact ? "hybrid" : "legacy");
        if (benchmarkReferences)
            Console.WriteLine(FormattableString.Invariant(
                $"REFERENCE_COMPARE_BENCH runtime=net6-not-Unity tieredCompilation={Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default"} direct={compact} samples={sampleCount} objects={expectedObjects} slots={expectedSlots} bytes={expectedBytes} referenceComparisons={typedCounts[compact]} captureMs={Median(values.Select(v => v.Capture)):F2} verifyMs={Median(values.Select(v => v.Verify)):F2} restoreMs={Median(values.Select(v => v.Restore)):F2} captureAllocatedBytes={Median(values.Select(v => (double)v.Allocated)):F0} verifyAllocatedBytes={Median(values.Select(v => (double)v.VerifyAllocated)):F0} restoreAllocatedBytes={Median(values.Select(v => (double)v.RestoreAllocated)):F0}"));
        else if (benchmarkComparisons)
            Console.WriteLine(FormattableString.Invariant(
                $"TYPED_COMPARE_BENCH runtime=net6-not-Unity tieredCompilation={Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default"} typed={compact} cold={coldComparisons} samples={sampleCount} objects={expectedObjects} slots={expectedSlots} bytes={expectedBytes} typedComparisons={typedCounts[compact]} lastVerifyCompileMs={comparisonCompileTimes[compact]:F2} captureMs={Median(values.Select(v => v.Capture)):F2} verifyMs={Median(values.Select(v => v.Verify)):F2} restoreMs={Median(values.Select(v => v.Restore)):F2} captureAllocatedBytes={Median(values.Select(v => (double)v.Allocated)):F0} verifyAllocatedBytes={Median(values.Select(v => (double)v.VerifyAllocated)):F0} restoreAllocatedBytes={Median(values.Select(v => (double)v.RestoreAllocated)):F0}"));
        else if (benchmarkScalars)
            Console.WriteLine(FormattableString.Invariant(
                $"SCALAR_WRITE_BENCH runtime=net6-not-Unity tieredCompilation={Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default"} guard={compact} dense={denseScalars} samples={sampleCount} objects={expectedObjects} slots={expectedSlots} bytes={expectedBytes} checked={scalarCounts[compact].Checked} skipped={scalarCounts[compact].Skipped} captureMs={Median(values.Select(v => v.Capture)):F2} verifyMs={Median(values.Select(v => v.Verify)):F2} restoreMs={Median(values.Select(v => v.Restore)):F2} captureAllocatedBytes={Median(values.Select(v => (double)v.Allocated)):F0} restoreAllocatedBytes={Median(values.Select(v => (double)v.RestoreAllocated)):F0}"));
        else if (benchmarkCensus)
            Console.WriteLine(FormattableString.Invariant(
                $"CENSUS_TAG_BENCH runtime=net6-not-Unity tieredCompilation={Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default"} direct={compact} samples={sampleCount} objects={expectedObjects} slots={expectedSlots} bytes={expectedBytes} exactTagProbes={(compact ? probedTags : 0)} censusMs={Median(values.Select(v => v.Census)):F2} censusAllocatedBytes={Median(values.Select(v => (double)v.CensusAllocated)):F0}"));
        else if (benchmarkIntArrays)
            Console.WriteLine(FormattableString.Invariant(
                $"INT_ARRAY_BENCH runtime=net6-not-Unity tieredCompilation={Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default"} compact={compact} samples={sampleCount} objects={expectedObjects} slots={expectedSlots} bytes={expectedBytes} intArrayBlocks={(compact ? intBlocks : 0)} intArraySlots={(compact ? intSlots : 0)} captureMs={Median(values.Select(v => v.Capture)):F2} censusMs={Median(values.Select(v => v.Census)):F2} verifyMs={Median(values.Select(v => v.Verify)):F2} restoreMs={Median(values.Select(v => v.Restore)):F2} captureAllocatedBytes={Median(values.Select(v => (double)v.Allocated)):F0} censusAllocatedBytes={Median(values.Select(v => (double)v.CensusAllocated)):F0} restoreAllocatedBytes={Median(values.Select(v => (double)v.RestoreAllocated)):F0}"));
        else if (benchmarkReconcile)
            Console.WriteLine(FormattableString.Invariant(
                $"RECONCILE_BENCH runtime=net6-not-Unity mode={mode} samples=5 objects={expectedObjects} slots={expectedSlots} plannedSlots={(compact ? repairedSlots : expectedSlots)} scanMs={Median(values.Select(v => v.Scan)):F2} writeMs={Median(values.Select(v => v.Restore)):F2} scanWriteMs={Median(values.Select(v => v.Scan + v.Restore)):F2} scanWriteAllocatedBytes={Median(values.Select(v => (double)v.RestoreAllocated)):F0}"));
        else
            Console.WriteLine(FormattableString.Invariant(
                $"TAG_BLOCK_BENCH runtime=net6-not-Unity mode={mode} samples={sampleCount} objects={expectedObjects} slots={expectedSlots} bytes={expectedBytes} captureMs={Median(values.Select(v => v.Capture)):F2} verifyMs={Median(values.Select(v => v.Verify)):F2} restoreMs={Median(values.Select(v => v.Restore)):F2} captureAllocatedBytes={Median(values.Select(v => (double)v.Allocated)):F0} restoreAllocatedBytes={Median(values.Select(v => (double)v.RestoreAllocated)):F0}"));
    }
}

public static class NativeEntityCallbackFixture
{
    public static void Throw<T>(T item) => throw new Exception("Item business callback must not run.");
    public static void ThrowInventory<TItem, TInventory, TSlot>(TItem item, TInventory inventory, TSlot slot)
        => throw new Exception("Inventory business callback must not run.");
}
