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
        "defaultIdleAmplitude", "defaultIdleFrequency", "ignoreOnLeftFlag", "fadeMode", "walkingIn", "idleAnimator" },
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
    foreach (var root in new[] { player, station, itemArray })
        accessType.GetMethod("AddRoot", dataFlags).Invoke(recursiveAccess, new object[] { Identity(root), Array.Empty<string>() });
    var recursiveGraph = graphType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, new object[] { recursiveAccess, new[] { Identity(player), Identity(station), Identity(itemArray) },
            50000, 1000000, 64L * 1024 * 1024 });
    try
    {
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
    || args.Contains("--benchmark-int-arrays"))
{
    bool benchmarkWrites = args.Contains("--benchmark-writes");
    bool benchmarkReconcile = args.Contains("--benchmark-reconcile");
    bool benchmarkIntArrays = args.Contains("--benchmark-int-arrays");
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
    var samples = new Dictionary<bool, List<(double Capture, double Verify, double Restore, long Allocated, long RestoreAllocated, double Scan, double Census, long CensusAllocated)>>
        { [false] = new(), [true] = new() };
    long? expectedSlots = null, expectedBytes = null, expectedObjects = null;
    var captureMethod = graphType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic);
    var verifyMethod = graphType.GetMethod("Differences", dataFlags);
    var restoreMethod = graphType.GetMethod("Restore", dataFlags);
    var sourceMethod = graphType.GetMethod("DifferencesWithSources", dataFlags);
    var censusMethod = graphType.GetMethod("Census", BindingFlags.Static | BindingFlags.NonPublic);
    var applyMethod = graphType.GetNestedType("Reconciliation", BindingFlags.NonPublic).GetMethod("Apply", dataFlags);
    int repairedSlots = 0, intBlocks = 0, intSlots = 0;
    int sampleCount = benchmarkIntArrays ? 9 : 5;
    int warmups = benchmarkIntArrays ? 3 : 1;
    for (int round = -warmups; round < sampleCount; round++)
        foreach (bool compact in round % 2 == 0 ? new[] { true, false } : new[] { false, true })
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var benchAccess = accessType.GetConstructor(dataFlags, null,
                new[] { typeof(Type), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(bool) }, null)
                .Invoke(new object[] { null, "Assembly-CSharp", true, true, !benchmarkWrites || compact,
                    benchmarkIntArrays && compact });
            if (benchmarkReconcile || benchmarkWrites || benchmarkIntArrays || compact) compactType.GetMethod("Enable", BindingFlags.Static | BindingFlags.NonPublic)
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
                clock.Restart();
                int differences = ((System.Collections.ICollection)verifyMethod.Invoke(benchGraph, new object[] { 12 })).Count;
                double verified = clock.Elapsed.TotalMilliseconds;
                if (differences != 0) throw new Exception("Native benchmark capture verification failed.");
                double censusMs = 0;
                long censusAllocated = 0;
                if (benchmarkIntArrays)
                {
                    if (compact)
                    {
                        intBlocks = (int)accessType.GetProperty("IntArrayBlockCount", dataFlags).GetValue(benchAccess);
                        intSlots = (int)accessType.GetProperty("IntArraySlotCount", dataFlags).GetValue(benchAccess);
                    }
                    if (((int)accessType.GetProperty("IntArrayBlockCount", dataFlags).GetValue(benchAccess) > 0) != compact)
                        throw new Exception("Native int array benchmark did not exercise the requested payload mode.");
                    var censusAccess = accessType.GetConstructor(dataFlags, null,
                        new[] { typeof(Type), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(bool) }, null)
                        .Invoke(new object[] { null, "Assembly-CSharp", true, true, true, compact });
                    accessType.GetMethod("AddRoot", dataFlags).Invoke(censusAccess,
                        new object[] { Identity(fixture), Array.Empty<string>() });
                    long beforeCensus = GC.GetAllocatedBytesForCurrentThread();
                    clock.Restart();
                    var censusGraph = censusMethod.Invoke(null, new object[] { censusAccess, new[] { Identity(fixture) },
                        200000, 3000000, 256L * 1024 * 1024 });
                    censusMs = clock.Elapsed.TotalMilliseconds;
                    censusAllocated = GC.GetAllocatedBytesForCurrentThread() - beforeCensus;
                    try
                    {
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
                            tagFields[2].SetValue(state, -1);
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
                if (repairPlan != null) VerifyRepair(benchGraph, repairPlan);
                if (((System.Collections.ICollection)verifyMethod.Invoke(benchGraph, new object[] { 12 })).Count != 0)
                    throw new Exception("Native benchmark restoration verification failed.");
                if (round >= 0) samples[compact].Add((captured, verified, restored, allocated, restoreAllocated, scanned,
                    censusMs, censusAllocated));
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
        if (benchmarkIntArrays)
            Console.WriteLine(FormattableString.Invariant(
                $"INT_ARRAY_BENCH runtime=net6-not-Unity tieredCompilation={Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default"} compact={compact} samples={sampleCount} objects={expectedObjects} slots={expectedSlots} bytes={expectedBytes} intArrayBlocks={(compact ? intBlocks : 0)} intArraySlots={(compact ? intSlots : 0)} captureMs={Median(values.Select(v => v.Capture)):F2} censusMs={Median(values.Select(v => v.Census)):F2} verifyMs={Median(values.Select(v => v.Verify)):F2} restoreMs={Median(values.Select(v => v.Restore)):F2} captureAllocatedBytes={Median(values.Select(v => (double)v.Allocated)):F0} censusAllocatedBytes={Median(values.Select(v => (double)v.CensusAllocated)):F0} restoreAllocatedBytes={Median(values.Select(v => (double)v.RestoreAllocated)):F0}"));
        else if (benchmarkReconcile)
            Console.WriteLine(FormattableString.Invariant(
                $"RECONCILE_BENCH runtime=net6-not-Unity mode={mode} samples=5 objects={expectedObjects} slots={expectedSlots} plannedSlots={(compact ? repairedSlots : expectedSlots)} scanMs={Median(values.Select(v => v.Scan)):F2} writeMs={Median(values.Select(v => v.Restore)):F2} scanWriteMs={Median(values.Select(v => v.Scan + v.Restore)):F2} scanWriteAllocatedBytes={Median(values.Select(v => (double)v.RestoreAllocated)):F0}"));
        else
            Console.WriteLine(FormattableString.Invariant(
                $"TAG_BLOCK_BENCH runtime=net6-not-Unity mode={mode} samples=5 objects={expectedObjects} slots={expectedSlots} bytes={expectedBytes} captureMs={Median(values.Select(v => v.Capture)):F2} verifyMs={Median(values.Select(v => v.Verify)):F2} restoreMs={Median(values.Select(v => v.Restore)):F2} captureAllocatedBytes={Median(values.Select(v => (double)v.Allocated)):F0} restoreAllocatedBytes={Median(values.Select(v => (double)v.RestoreAllocated)):F0}"));
    }
}
