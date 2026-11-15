using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text;
using MA = Mono.Cecil.MethodAttributes;
using TA = Mono.Cecil.TypeAttributes;

// Offline only: the resulting helper contains ordinary static IL, never a loader.
if (args.Length == 3 && args[0] == "--verify-profile")
{
    VerifyProfile(args[1], args[2]);
    return;
}
if (args.Length < 3)
    throw new ArgumentException("Usage: MergeStaticLayouts <output.dll> <profile1.dll> <profile2.dll> [...]");
string output = Path.GetFullPath(args[0]);
string[] inputs = args.Skip(1).Select(Path.GetFullPath).ToArray();
if (inputs.Contains(output, StringComparer.OrdinalIgnoreCase))
    throw new IOException("Output must not overwrite an input.");
using var target = ModuleDefinition.CreateModule("ProbablyStolenTools.StaticLayouts", ModuleKind.Dll);
target.Assembly.Name.Version = new Version(1, 0, 0, 0);
var holder = new TypeDefinition("ProbablyStolenTools.Generated", "SnapshotLayouts",
    TA.Public | TA.Abstract | TA.Sealed, target.TypeSystem.Object);
target.Types.Add(holder);
var sources = inputs.Select(ModuleDefinition.ReadModule).ToArray();
try
{
    var fingerprints = new HashSet<string>(StringComparer.Ordinal);
    var shared = new Dictionary<string, MethodDefinition>(StringComparer.Ordinal);
    var profiles = new List<(string Mvid, Dictionary<string, MethodDefinition> Methods)>();
    int originalFactories = 0;
    MethodReference getType = null;
    TypeReference anchor = null;
    foreach (var source in sources)
    {
        if (source.Assembly.Name.Name != target.Assembly.Name.Name)
            throw new InvalidDataException("Not a static layout helper: " + source.FileName);
        var registry = source.GetType(holder.FullName)
            ?? throw new InvalidDataException("Missing layout registry.");
        var expected = registry.Methods.Single(m => m.Name == "ExpectedModules");
        var strings = expected.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldstr)
            .Select(i => (string)i.Operand).ToArray();
        if (strings.Length != 8 || strings.Distinct(StringComparer.Ordinal).Count() != strings.Length)
            throw new InvalidDataException("Expected four distinct module fingerprints.");
        var modules = new Dictionary<string, Guid>(StringComparer.Ordinal);
        for (int i = 0; i < strings.Length; i += 2) modules.Add(strings[i], Guid.Parse(strings[i + 1]));
        if (!modules.TryGetValue("Assembly-CSharp.dll", out var gameMvid) || gameMvid == Guid.Empty
            || !fingerprints.Add(gameMvid.ToString("D")))
            throw new InvalidDataException("Missing, empty or ambiguous game fingerprint.");
        var known = registry.Methods.Single(m => m.Name == "KnownTypes");
        anchor ??= target.ImportReference(known.Body.Instructions
            .Where(i => i.OpCode == OpCodes.Ldtoken).Select(i => (TypeReference)i.Operand)
            .Single(t => t.FullName == "PlayerStore"));
        getType ??= target.ImportReference((MethodReference)known.Body.Instructions
            .First(i => i.OpCode == OpCodes.Call).Operand);
        var map = new Dictionary<MethodDefinition, MethodDefinition>();
        foreach (var factory in registry.Methods.Where(m => m.Name.StartsWith("Fields", StringComparison.Ordinal)))
        {
            originalFactories++;
            string key = Key(factory);
            if (!shared.TryGetValue(key, out var copy))
            {
                copy = Skeleton(factory, "Fields" + shared.Count.ToString("D5"));
                CopyBody(factory, copy, map);
                shared.Add(key, copy);
            }
            map.Add(factory, copy);
        }
        var entry = new Dictionary<string, MethodDefinition>(StringComparer.Ordinal);
        foreach (string name in new[] { "ExpectedModules", "KnownTypes", "KnownFactories" })
        {
            var original = registry.Methods.Single(m => m.Name == name);
            var copy = Skeleton(original, "Profile" + profiles.Count + name);
            CopyBody(original, copy, map);
            entry.Add(name, copy);
        }
        profiles.Add((gameMvid.ToString("D"), entry));
        Console.WriteLine($"Profile={profiles.Count - 1} gameMvid={gameMvid:D} input={source.FileName}");
    }

    var coreScope = getType.DeclaringType.Scope;
    TypeReference Core(string ns, string name, bool value = false) => new(ns, name, target, coreScope, value);
    var reflectionModule = Core("System.Reflection", "Module");
    var guid = Core("System", "Guid", true);
    var systemString = Core("System", "String");
    MethodReference Instance(string name, TypeReference result, TypeReference owner) =>
        new(name, result, owner) { HasThis = true };
    var moduleGetter = Instance("get_Module", reflectionModule, Core("System.Reflection", "MemberInfo"));
    var mvidGetter = Instance("get_ModuleVersionId", guid, reflectionModule);
    var guidString = Instance("ToString", systemString, guid);
    var equal = new MethodReference("op_Equality", Core("System", "Boolean", true), systemString);
    equal.Parameters.Add(new ParameterDefinition(systemString));
    equal.Parameters.Add(new ParameterDefinition(systemString));
    var select = new MethodDefinition("SelectProfile", MA.Private | MA.Static, target.TypeSystem.Int32);
    holder.Methods.Add(select);
    select.Body.InitLocals = true;
    select.Body.Variables.Add(new VariableDefinition(guid));
    select.Body.Variables.Add(new VariableDefinition(systemString));
    var il = select.Body.GetILProcessor();
    il.Emit(OpCodes.Ldtoken, anchor);
    il.Emit(OpCodes.Call, getType);
    il.Emit(OpCodes.Callvirt, moduleGetter);
    il.Emit(OpCodes.Callvirt, mvidGetter);
    il.Emit(OpCodes.Stloc_0);
    il.Emit(OpCodes.Ldloca_S, select.Body.Variables[0]);
    il.Emit(OpCodes.Call, guidString);
    il.Emit(OpCodes.Stloc_1);
    for (int i = 0; i < profiles.Count; i++)
    {
        var next = Instruction.Create(OpCodes.Nop);
        il.Emit(OpCodes.Ldloc_1);
        il.Emit(OpCodes.Ldstr, profiles[i].Mvid);
        il.Emit(OpCodes.Call, equal);
        il.Emit(OpCodes.Brfalse, next);
        il.Emit(OpCodes.Ldc_I4, i);
        il.Emit(OpCodes.Ret);
        il.Append(next);
    }
    il.Emit(OpCodes.Ldc_I4_M1);
    il.Emit(OpCodes.Ret);
    foreach (string name in new[] { "ExpectedModules", "KnownTypes", "KnownFactories" })
    {
        var wrapper = new MethodDefinition(name, MA.Public | MA.Static, profiles[0].Methods[name].ReturnType);
        wrapper.ImplAttributes |= MethodImplAttributes.NoInlining;
        holder.Methods.Add(wrapper);
        var body = wrapper.Body.GetILProcessor();
        var labels = profiles.Select(_ => Instruction.Create(OpCodes.Nop)).ToArray();
        body.Emit(OpCodes.Call, select);
        body.Emit(OpCodes.Switch, labels);
        // Preserve the main DLL's detailed mismatch diagnostic without resolving game tokens.
        if (name == "ExpectedModules") body.Emit(OpCodes.Call, profiles[0].Methods[name]);
        else
        {
            body.Emit(OpCodes.Ldc_I4_0);
            body.Emit(OpCodes.Newarr, ((ArrayType)wrapper.ReturnType).ElementType);
        }
        body.Emit(OpCodes.Ret);
        for (int i = 0; i < profiles.Count; i++)
        {
            body.Append(labels[i]);
            body.Emit(OpCodes.Call, profiles[i].Methods[name]);
            body.Emit(OpCodes.Ret);
        }
    }
    Directory.CreateDirectory(Path.GetDirectoryName(output));
    byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes("merged-layouts-v1\n"
        + string.Join("\n", inputs.Select(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))))));
    target.Mvid = new Guid(digest.Take(16).ToArray());
    target.Write(output, new WriterParameters { Timestamp = 0 });
    foreach (string input in inputs) VerifyProfile(output, input);
    var report = new
    {
        Format = "merged-layouts-v1",
        Inputs = inputs.Select((p, index) => new
        {
            Profile = index, File = Path.GetFileName(p),
            SHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))),
            GameMvid = profiles[index].Mvid, Bytes = new FileInfo(p).Length
        }).ToArray(),
        FactoriesBefore = originalFactories, FactoriesAfter = shared.Count,
        Bytes = new FileInfo(output).Length,
        SHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output)))
    };
    File.WriteAllText(output + ".profiles.json", System.Text.Json.JsonSerializer.Serialize(report,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Merged profiles={profiles.Count} factoriesBefore={originalFactories} factoriesAfter={shared.Count}"
        + $" inputBytes={inputs.Sum(p => new FileInfo(p).Length)} outputBytes={new FileInfo(output).Length}");
}
finally
{
    foreach (var source in sources) source.Dispose();
}

MethodDefinition Skeleton(MethodDefinition original, string name)
{
    if (!original.IsStatic || original.GenericParameters.Any(p => p.HasConstraints
        || p.Attributes != GenericParameterAttributes.NonVariant))
        throw new InvalidDataException("Unsupported generated method signature: " + original.FullName);
    var copy = new MethodDefinition(name, MA.Public | MA.Static, target.ImportReference(original.ReturnType));
    copy.ImplAttributes |= MethodImplAttributes.NoInlining;
    holder.Methods.Add(copy);
    foreach (var parameter in original.GenericParameters)
        copy.GenericParameters.Add(new GenericParameter(parameter.Name, copy));
    return copy;
}

void CopyBody(MethodDefinition original, MethodDefinition copy, Dictionary<MethodDefinition, MethodDefinition> map)
{
    if (original.Parameters.Count != 0 || original.Body.HasVariables || original.Body.HasExceptionHandlers)
        throw new InvalidDataException("Unsupported generated method shape: " + original.FullName);
    TypeReference ImportType(TypeReference type) => type switch
    {
        GenericParameter p when p.Type == GenericParameterType.Method => copy.GenericParameters[p.Position],
        GenericInstanceType g => Generic(g),
        _ => target.ImportReference(type)
    };
    TypeReference Generic(GenericInstanceType type)
    {
        var result = new GenericInstanceType(target.ImportReference(type.ElementType));
        foreach (var argument in type.GenericArguments) result.GenericArguments.Add(ImportType(argument));
        return result;
    }
    var il = copy.Body.GetILProcessor();
    foreach (var instruction in original.Body.Instructions)
    {
        if (instruction.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch or FlowControl.Throw)
            throw new InvalidDataException("Only flat generated tables can be merged.");
        switch (instruction.Operand)
        {
            case null: il.Emit(instruction.OpCode); break;
            case int value: il.Emit(instruction.OpCode, value); break;
            case string value: il.Emit(instruction.OpCode, value); break;
            case TypeReference type: il.Emit(instruction.OpCode, ImportType(type)); break;
            case MethodDefinition method when map.TryGetValue(method, out var mapped):
                il.Emit(instruction.OpCode, mapped); break;
            case MethodReference method when method.DeclaringType.FullName == holder.FullName:
                throw new InvalidDataException("Unmapped local factory: " + method.FullName);
            case MethodReference method: il.Emit(instruction.OpCode, target.ImportReference(method)); break;
            default: throw new InvalidDataException("Unsupported table operand: " + instruction);
        }
    }
}

static string Key(MethodDefinition method, bool normalizeFactories = false)
{
    return System.Text.Json.JsonSerializer.Serialize(new[]
        { method.GenericParameters.Count.ToString(), TypeKey(method.ReturnType) }
        .Concat(method.Body.Instructions.Select(i => i.OpCode.Code + ":" + (i.Operand switch
        {
            TypeReference type => TypeKey(type),
            MethodReference reference when normalizeFactories && reference.DeclaringType.FullName
                == "ProbablyStolenTools.Generated.SnapshotLayouts" => "factory:" + Key(reference.Resolve()),
            MethodReference reference => TypeKey(reference.DeclaringType) + ":" + reference.FullName,
            null => "",
            _ => i.Operand.ToString()
        }))));
}

static string TypeKey(TypeReference type) => type switch
{
    GenericParameter p => "!" + p.Type + p.Position,
    GenericInstanceType g => TypeKey(g.ElementType) + "<" + string.Join(",", g.GenericArguments.Select(TypeKey)) + ">",
    _ => type.Scope + ":" + type.FullName
};

static void VerifyProfile(string mergedPath, string sourcePath)
{
    using var merged = ModuleDefinition.ReadModule(mergedPath);
    using var source = ModuleDefinition.ReadModule(sourcePath);
    var original = source.GetType("ProbablyStolenTools.Generated.SnapshotLayouts");
    var combined = merged.GetType(original.FullName);
    string[] Strings(MethodDefinition method) => method.Body.Instructions
        .Where(i => i.OpCode == OpCodes.Ldstr).Select(i => (string)i.Operand).ToArray();
    var modules = Strings(original.Methods.Single(m => m.Name == "ExpectedModules"));
    var matches = combined.Methods.Where(m => m.Name.StartsWith("Profile", StringComparison.Ordinal)
        && m.Name.EndsWith("ExpectedModules", StringComparison.Ordinal) && Strings(m).SequenceEqual(modules)).ToArray();
    if (matches.Length != 1) throw new InvalidDataException("Merged helper does not contain exactly one matching profile.");
    string prefix = matches[0].Name[..^"ExpectedModules".Length];
    foreach (string name in new[] { "ExpectedModules", "KnownTypes", "KnownFactories" })
        if (Key(original.Methods.Single(m => m.Name == name), true)
            != Key(combined.Methods.Single(m => m.Name == prefix + name), true))
            throw new InvalidDataException("Merged profile changed registry IL or field factory IL.");
    var oldTypes = original.Methods.Single(m => m.Name == "KnownTypes").Body.Instructions
        .Where(i => i.OpCode == OpCodes.Ldtoken).Select(i => TypeKey((TypeReference)i.Operand)).ToArray();
    var newTypes = combined.Methods.Single(m => m.Name == prefix + "KnownTypes").Body.Instructions
        .Where(i => i.OpCode == OpCodes.Ldtoken).Select(i => TypeKey((TypeReference)i.Operand)).ToArray();
    string[] Factories(TypeDefinition type, string name) => type.Methods.Single(m => m.Name == name)
        .Body.Instructions.Where(i => i.OpCode == OpCodes.Ldtoken)
        .Select(i => Key(((MethodReference)i.Operand).Resolve())).ToArray();
    var oldFactories = Factories(original, "KnownFactories");
    var newFactories = Factories(combined, prefix + "KnownFactories");
    if (oldTypes.Length == 0 || oldTypes.Length != oldFactories.Length
        || !oldTypes.SequenceEqual(newTypes) || !oldFactories.SequenceEqual(newFactories))
        throw new InvalidDataException("Merged profile changed type order, field factory IL or generic coverage.");
    Console.WriteLine($"PASS exact profile preservation types={oldTypes.Length} factories={oldFactories.Length} source={sourcePath}");
}
