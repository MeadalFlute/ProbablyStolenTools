using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.Serialization;

// Exercise the built beta against real mod DTOs. No mod entrypoints, gameplay callbacks,
// static-state roots, save APIs or running game process are used.
if (args.Length != 3) throw new ArgumentException("Usage: <game directory> <workshop app directory> <beta DLL>");
string managed = Path.Combine(args[0], "Probably Stolen_Data", "Managed");
var paths = Directory.GetFiles(managed, "*.dll")
    .Concat(Directory.GetFiles(args[1], "*.dll", SearchOption.AllDirectories))
    .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
AssemblyLoadContext.Default.Resolving += (_, name) => paths.TryGetValue(name.Name, out string path)
    ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
Assembly Load(string name) => AssemblyLoadContext.Default.LoadFromAssemblyPath(paths[name]);
var game = Load("Assembly-CSharp");
var corp = Load("CorpPresence");
var pen = Load("Pen_mod_SelfLearning");
var cyber = Load("CyberElectronics");
var mod = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[2]));
const BindingFlags instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
const BindingFlags hiddenStatic = BindingFlags.NonPublic | BindingFlags.Static;
var accessType = mod.GetType("ProbablyStolenTools.ManagedStateAccess", true);
var graphType = mod.GetType("ProbablyStolenTools.StateGraph", true);
var identity = mod.GetType("ProbablyStolenTools.ManagedObjects", true).GetMethod("Id", hiddenStatic);
var unity = Load("UnityEngine.CoreModule").GetType("UnityEngine.Object", true);
IntPtr Id(object value) => (IntPtr)identity.Invoke(null, new[] { value });
object Data(Type type) => FormatterServices.GetUninitializedObject(type);
FieldInfo Field(object value, string name)
{
    for (var type = value.GetType(); type != null; type = type.BaseType)
    {
        var field = type.GetField(name, instance | BindingFlags.DeclaredOnly)
            ?? type.GetField("<" + name + ">k__BackingField", instance | BindingFlags.DeclaredOnly);
        if (field != null) return field;
    }
    throw new Exception("Missing field: " + value.GetType().FullName + "." + name);
}
void Set(object value, string name, object data) => Field(value, name).SetValue(value, data);
object Get(object value, string name) => Field(value, name).GetValue(value);
void Check(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
Snapshot Capture(object value)
{
    var holder = new Holder { Payload = value };
    var access = accessType.GetConstructor(instance, null, new[] { typeof(Type), typeof(string) }, null)
        .Invoke(new object[] { unity, "Assembly-CSharp" });
    accessType.GetMethod("AddRoot", instance).Invoke(access, new object[] { Id(holder), Array.Empty<string>() });
    object graph = graphType.GetMethod("Capture", hiddenStatic).Invoke(null,
        new object[] { access, new[] { Id(holder) }, 50000, 1000000, 64L * 1024 * 1024 });
    return new Snapshot(graph, graphType);
}
int passed = 0;
void Case(string name, Action run) { run(); passed++; Console.WriteLine("PASS " + name); }

Case("CorpPresence data and dictionary edits restore through a single holder root", () =>
{
    var state = Data(corp.GetType("CorpPresence.CorpState", true));
    var character = Data(corp.GetType("CorpPresence.CharacterState", true));
    var map = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>)
        .MakeGenericType(typeof(string), character.GetType()));
    map.Add("probe", character);
    Set(state, "RunId", "probe"); Set(state, "Characters", map); Set(character, "Chapter", 1);
    Set(character, "QueuedDay", 5);
    using var graph = Capture(state);
    Set(character, "Chapter", 8); Set(character, "QueuedDay", 9); map.Clear();
    graph.Restore();
    Check((int)Get(character, "Chapter") == 1 && (int)Get(character, "QueuedDay") == 5);
    Check(map.Count == 1 && ReferenceEquals(map["probe"], character) && graph.DifferenceCount() == 0);
});
Case("CorpPresence real one-shot closure state restores without registering its type", () =>
{
    var type = corp.GetTypes().First(t => t.FullName.StartsWith("CorpPresence.Engine+<>c__DisplayClass")
        && t.GetFields(instance).Any(f => f.Name == "done" && f.FieldType == typeof(bool)));
    var closure = Data(type);
    using var graph = Capture(closure);
    Set(closure, "done", true); graph.Restore();
    Check(!(bool)Get(closure, "done") && graph.DifferenceCount() == 0);
});
Case("Pen machine closure automatically reaches and restores native item data", () =>
{
    var type = pen.GetTypes().First(t => t.FullName.StartsWith("PenSelfLearning.SelfLearnWindow+<>c__DisplayClass")
        && t.GetFields(instance).Any(f => f.Name == "machine"));
    var closure = Data(type);
    var item = Data(game.GetType("GameItemElement", true));
    Set(closure, "machine", item); Set(item, "uniqueId", 711);
    using var graph = Capture(closure);
    Set(item, "uniqueId", 999); graph.Restore();
    Check((int)Get(item, "uniqueId") == 711 && graph.DifferenceCount() == 0);
});
Case("CyberElectronics supply data and original array contents automatically restore", () =>
{
    var data = Data(cyber.GetType("CyberElectronics.NativeSupply+Decision", true));
    var items = new[] { "ce_phone" };
    Set(data, "selected", true); Set(data, "delivered", 1); Set(data, "items", items);
    using var first = Capture(data);
    Set(data, "delivered", 2);
    using var second = Capture(data);
    Set(data, "delivered", 5); items[0] = "future"; Set(data, "items", new[] { "replacement" });
    first.Restore();
    Check((int)Get(data, "delivered") == 1 && ReferenceEquals(Get(data, "items"), items) && items[0] == "ce_phone");
    second.Restore();
    Check((int)Get(data, "delivered") == 2 && second.DifferenceCount() == 0);
});
Case("native modData still restores additions, removals and serialized string values", () =>
{
    var player = Data(game.GetType("PlayerStore", true));
    var data = new Dictionary<string, string> { ["CorpPresence"] = "old", ["ce.skills.v1"] = "original" };
    Set(player, "modData", data);
    using var graph = Capture(player);
    data.Remove("CorpPresence"); data["ce.skills.v1"] = "future"; data["new"] = "future";
    graph.Restore();
    Check(data.Count == 2 && data["CorpPresence"] == "old" && data["ce.skills.v1"] == "original");
});
Console.WriteLine($"{passed} real-mod data scenarios passed. This is not Unity gameplay acceptance.");

sealed class Holder { internal object Payload; }
sealed class Snapshot : IDisposable
{
    private readonly object _graph;
    private readonly Type _type;
    private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
    internal Snapshot(object graph, Type type) { _graph = graph; _type = type; }
    internal void Restore() => _type.GetMethod("Restore", Instance).Invoke(_graph, null);
    internal int DifferenceCount() => ((ICollection)_type.GetMethod("Differences", Instance)
        .Invoke(_graph, new object[] { 12 })).Count;
    public void Dispose() => ((IDisposable)_graph).Dispose();
}
