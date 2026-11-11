using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Reflection;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

if (args.Length < 2 || args.Length > 3 || args.Length == 3
    && args[2] != "--host" && args[2] != "--test-mismatched-version")
    throw new ArgumentException("Usage: StaticSnapshotLayouts <GAME_DIR> <output.dll> [--host|--test-mismatched-version]");
string managed = Path.Combine(Path.GetFullPath(args[0]), "Probably Stolen_Data", "Managed");
string output = Path.GetFullPath(args[1]);
if (output.StartsWith(managed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new IOException("Refusing to write a generated table into the game's managed assemblies.");
bool host = args.Length == 3 && args[2] == "--host";
bool mismatch = args.Length == 3 && args[2] == "--test-mismatched-version";
using var core = ModuleDefinition.ReadModule(host ? typeof(object).Assembly.Location : Path.Combine(managed, "mscorlib.dll"));
using var collections = ModuleDefinition.ReadModule(host ? typeof(HashSet<>).Assembly.Location : Path.Combine(managed, "System.Core.dll"));
using var game = ModuleDefinition.ReadModule(Path.Combine(managed, "Assembly-CSharp.dll"));
using var unity = ModuleDefinition.ReadModule(Path.Combine(managed, "UnityEngine.CoreModule.dll"));
using var module = ModuleDefinition.CreateModule("ProbablyStolenTools.StaticLayouts", ModuleKind.Dll);
module.Assembly.Name.Version = new Version(1, 0, 0, 0);
var holder = new TypeDefinition("ProbablyStolenTools.Generated", "SnapshotLayouts",
    TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object);
module.Types.Add(holder);
TypeDefinition Core(string name) => core.GetType(name) ?? throw new Exception("Missing core type " + name);
var typeType = Core("System.Type");
var fieldType = module.ImportReference(Core("System.Reflection.FieldInfo"));
var methodType = module.ImportReference(Core("System.Reflection.MethodInfo"));
var getType = module.ImportReference(typeType.Methods.Single(m => m.Name == "GetTypeFromHandle"));
var getField = module.ImportReference(typeType.Methods.Single(m => m.Name == "GetField"
    && m.Parameters.Count == 2 && m.Parameters[1].ParameterType.Name == "BindingFlags"));
var getMethod = module.ImportReference(Core("System.Reflection.MethodBase").Methods.Single(m => m.Name == "GetMethodFromHandle"
    && m.Parameters.Count == 1));

// These types belong to the game's trust layer, not gameplay snapshot state.
var guards = new HashSet<string>(StringComparer.Ordinal)
{
    "ModLoader", "ModTrust", "ModTrustRecord", "ModFingerprint", "ModTrustList", "ModScanner",
    "ModCapability", "ModSecurityDebug", "ModSteamRules", "ModSteamGuard", "ModSteamAuth",
    "ModWorkshop", "SteamManager", "PaperBookRegistry", "ModReflect"
};
TypeDefinition Outer(TypeDefinition t) => t.DeclaringType == null ? t : Outer(t.DeclaringType);
bool GameType(TypeDefinition t)
{
    var outer = Outer(t);
    return t.Name != "<Module>" && !guards.Contains(outer.Name)
        && !outer.Name.StartsWith("ES3", StringComparison.Ordinal);
}
bool FrameworkType(TypeDefinition t)
{
    var outer = Outer(t);
    return outer.Namespace == "System.Collections.Generic"
        || outer.Namespace == "System" && (outer.Name.StartsWith("Tuple`", StringComparison.Ordinal)
            || outer.Name.StartsWith("ValueTuple", StringComparison.Ordinal)
            || new[] { "Random", "Object", "ValueType", "Enum", "DateTime", "DateTimeOffset", "TimeSpan", "Guid" }
                .Contains(outer.Name));
}
bool UnityType(TypeDefinition t) => t.IsValueType && t.Namespace == "UnityEngine"
    && new[] { "Vector", "Color", "Rect", "Quaternion" }.Any(p => t.Name.StartsWith(p, StringComparison.Ordinal));
var types = game.GetTypes().Where(GameType)
    .Concat(core.GetTypes().Where(FrameworkType))
    .Concat(collections.GetTypes().Where(FrameworkType))
    .Concat(unity.GetTypes().Where(UnityType))
    .GroupBy(t => t.Module.Name + ":" + t.FullName, StringComparer.Ordinal).Select(g => g.First())
    .OrderBy(t => t.Module.Assembly.Name.Name, StringComparer.Ordinal)
    .ThenBy(t => t.FullName, StringComparer.Ordinal).ToArray();

MethodDefinition ArrayMethod(string name, TypeReference element)
{
    var method = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.Static, new ArrayType(element));
    holder.Methods.Add(method);
    return method;
}
void BeginArray(ILProcessor il, int count, TypeReference element)
{
    il.Emit(OpCodes.Ldc_I4, count);
    il.Emit(OpCodes.Newarr, element);
}
void ArrayIndex(ILProcessor il, int index)
{
    il.Emit(OpCodes.Dup);
    il.Emit(OpCodes.Ldc_I4, index);
}

var typesMethod = ArrayMethod("KnownTypes", module.ImportReference(typeType));
var typesIl = typesMethod.Body.GetILProcessor();
BeginArray(typesIl, types.Length, module.ImportReference(typeType));
var factoriesMethod = ArrayMethod("KnownFactories", methodType);
var factoriesIl = factoriesMethod.Body.GetILProcessor();
BeginArray(factoriesIl, types.Length, methodType);
int fieldsTotal = 0;
for (int i = 0; i < types.Length; i++)
{
    var type = types[i];
    var reference = module.ImportReference(type);
    var factory = ArrayMethod("Fields" + i.ToString("D5"), fieldType);
    TypeReference closed = reference;
    if (type.HasGenericParameters)
    {
        var instance = new GenericInstanceType(reference);
        foreach (var parameter in type.GenericParameters)
        {
            var generic = new GenericParameter("T" + parameter.Position, factory);
            factory.GenericParameters.Add(generic);
            instance.GenericArguments.Add(generic);
        }
        closed = instance;
    }
    var fields = type.Fields.Where(f => !f.IsStatic).ToArray();
    fieldsTotal += fields.Length;
    var il = factory.Body.GetILProcessor();
    BeginArray(il, fields.Length, fieldType);
    for (int j = 0; j < fields.Length; j++)
    {
        ArrayIndex(il, j);
        il.Emit(OpCodes.Ldtoken, closed);
        il.Emit(OpCodes.Call, getType);
        il.Emit(OpCodes.Ldstr, fields[j].Name);
        il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.DeclaredOnly | BindingFlags.Instance
            | BindingFlags.Public | BindingFlags.NonPublic));
        il.Emit(OpCodes.Callvirt, getField);
        il.Emit(OpCodes.Stelem_Ref);
    }
    il.Emit(OpCodes.Ret);

    ArrayIndex(typesIl, i);
    typesIl.Emit(OpCodes.Ldtoken, reference);
    typesIl.Emit(OpCodes.Call, getType);
    typesIl.Emit(OpCodes.Stelem_Ref);
    ArrayIndex(factoriesIl, i);
    factoriesIl.Emit(OpCodes.Ldtoken, factory);
    factoriesIl.Emit(OpCodes.Call, getMethod);
    factoriesIl.Emit(OpCodes.Castclass, methodType);
    factoriesIl.Emit(OpCodes.Stelem_Ref);
}
typesIl.Emit(OpCodes.Ret);
factoriesIl.Emit(OpCodes.Ret);
var modules = types.Select(t => t.Module).Distinct().OrderBy(m => m.Name, StringComparer.Ordinal).ToArray();
var versions = ArrayMethod("ExpectedModules", module.TypeSystem.String);
var versionIl = versions.Body.GetILProcessor();
BeginArray(versionIl, modules.Length * 2, module.TypeSystem.String);
for (int i = 0; i < modules.Length; i++)
{
    ArrayIndex(versionIl, i * 2);
    versionIl.Emit(OpCodes.Ldstr, modules[i].Name);
    versionIl.Emit(OpCodes.Stelem_Ref);
    ArrayIndex(versionIl, i * 2 + 1);
    versionIl.Emit(OpCodes.Ldstr, (mismatch && i == 0 ? Guid.Empty : modules[i].Mvid).ToString("D"));
    versionIl.Emit(OpCodes.Stelem_Ref);
}
versionIl.Emit(OpCodes.Ret);
Directory.CreateDirectory(Path.GetDirectoryName(output));
// No player or game files are written: the output is a build artifact only.
module.Mvid = DeterministicMvid();
module.Write(output, new WriterParameters { Timestamp = 0 });
Console.WriteLine($"Static layouts generated: hostOnly={host} mismatchTest={mismatch} types={types.Length} fields={fieldsTotal} modules={modules.Length} output={output}");

Guid DeterministicMvid()
{
    using var hash = System.Security.Cryptography.SHA256.Create();
    string text = "static-layouts-v1\nmismatch=" + mismatch + "\n" + string.Join("\n", modules.Select(m => m.Name + ":" + m.Mvid))
        + "\n" + string.Join("\n", types.Select(t => t.FullName + ":"
            + string.Join(",", t.Fields.Where(f => !f.IsStatic).Select(f => f.Name + ":" + f.FieldType.FullName))));
    byte[] bytes = hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
    return new Guid(bytes.Take(16).ToArray());
}
