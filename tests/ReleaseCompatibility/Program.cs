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
            if (parameter.Name == "__instance") expected = target;
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
    ["ItemMouseDoubleClickHandler"] = new[] { "lastKey" }
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
}
finally { ((IDisposable)graph).Dispose(); }
