using System.Collections;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;

if (args.Length != 3)
    throw new ArgumentException("Usage: Scanner <GAME_DIR> <probe.dll> clean|private|negative");
string managed = Path.Combine(args[0], "Probably Stolen_Data", "Managed");
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string path = Path.Combine(managed, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
var game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed, "Assembly-CSharp.dll"));
game.GetType("ModLoader").GetField("gameAssemblyNames", BindingFlags.Static | BindingFlags.NonPublic)
    .SetValue(null, Directory.GetFiles(managed, "*.dll").Select(Path.GetFileNameWithoutExtension)
        .ToHashSet(StringComparer.OrdinalIgnoreCase));
var result = game.GetType("ModScanner").GetMethod("Scan")
    .Invoke(null, new object[] { new[] { Path.GetFullPath(args[1]) }, null, null, false });
foreach (var field in result.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
{
    object value = field.GetValue(result);
    Console.WriteLine(field.Name + ": " + (value is IEnumerable sequence && value is not string
        ? string.Join(" | ", sequence.Cast<object>()) : value));
}
string[] List(string name) => ((IEnumerable)result.GetType().GetField(name).GetValue(result))
    .Cast<object>().Select(v => v.ToString()).ToArray();
var expected = args[2] switch
{
    "clean" => Array.Empty<string>(),
    "private" => new[] { "reflection-external" },
    "negative" => new[] { "reflection-external", "dynamic-code" },
    _ => throw new ArgumentException("Unknown expectation.")
};
if (!List("Capabilities").ToHashSet().SetEquals(expected))
    throw new Exception("Unexpected capability set.");
if (args[2] == "clean" && List("Findings").Length != 0)
    throw new Exception("Clean probe must not produce findings.");
if (List("HardFlags").Length != 0 || List("Pending").Length != 0
    || result.GetType().GetField("Error").GetValue(result) is string { Length: > 0 })
    throw new Exception("Unexpected hard flag, pending finding or scanner error.");
if (args[2] == "private" && List("Findings").Length != 3)
    throw new Exception("Each private nested-layout path must produce a finding.");
if (args[2] == "negative" && List("Findings").Length != 4)
    throw new Exception("Each negative control must produce a finding.");
Console.WriteLine("PASS scanner expectation: " + args[2]);

// Inspect the game's BCL metadata, not the .NET host's different collection/RNG layouts.
foreach (string assembly in new[] { "mscorlib", "System.Core" })
{
    using var stream = File.OpenRead(Path.Combine(managed, assembly + ".dll"));
    using var pe = new PEReader(stream);
    var reader = pe.GetMetadataReader();
    string TypeName(TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        string name = reader.GetString(definition.Name);
        var parent = definition.GetDeclaringType();
        return parent.IsNil ? reader.GetString(definition.Namespace) + "." + name
            : TypeName(parent) + "+" + name;
    }
    foreach (var handle in reader.TypeDefinitions)
    {
        string name = TypeName(handle);
        if (name != "System.Random" && !name.StartsWith("System.Collections.Generic.Dictionary`2")
            && !name.StartsWith("System.Collections.Generic.HashSet`1")
            && name != "System.Collections.Generic.List`1") continue;
        var definition = reader.GetTypeDefinition(handle);
        var fields = definition.GetFields().Select(reader.GetFieldDefinition)
            .Where(f => (f.Attributes & FieldAttributes.Static) == 0)
            .Select(f => reader.GetString(f.Name)
                + ((f.Attributes & FieldAttributes.InitOnly) != 0 ? "[readonly]" : ""));
        Console.WriteLine("TARGET " + assembly + " " + name + ": " + string.Join(",", fields));
    }
}
var gameTypes = game.GetTypes();
var closures = gameTypes.Where(t => t.Name.Contains("DisplayClass", StringComparison.Ordinal)).ToArray();
Console.WriteLine("Game display-class definitions (not a live graph census): " + closures.Length);
var closure = game.GetType("GameItem+<>c__DisplayClass187_0", true);
if (closure.GetField("modId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null)
    throw new Exception("Literal closure fixture mismatch; actual fields: "
        + string.Join(",", closure.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(f => f.Name)));
var readonlyFields = gameTypes.SelectMany(t => t.GetFields(BindingFlags.Instance | BindingFlags.Public
    | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)).Where(f => f.IsInitOnly).ToArray();
Console.WriteLine("Game readonly instance fields (not necessarily reachable): " + readonlyFields.Length);
foreach (var field in readonlyFields.Take(12))
    Console.WriteLine("READONLY " + field.DeclaringType.FullName + "." + field.Name);

var difficulty = game.GetType("Difficulty", true);
var fixture = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(difficulty);
var readonlyField = difficulty.GetField("notes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
if (readonlyField?.IsInitOnly != true) throw new Exception("Readonly fixture changed.");
try
{
    game.GetType("ModReflect").GetMethod("Set").Invoke(null, new[] { fixture, "notes", null });
    throw new Exception("ModReflect unexpectedly accepted a readonly write.");
}
catch (TargetInvocationException e) when (e.InnerException?.GetType().Name == "ModReflectAccessException"
    && e.InnerException.Message.Contains("read-only", StringComparison.Ordinal))
{
    Console.WriteLine("PASS ModReflect rejects readonly game fields; literal field adapters require a separate write contract.");
}
