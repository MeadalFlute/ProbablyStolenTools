using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 3 || args[2] is not ("poison" or "unknown" or "alter"))
    throw new ArgumentException("Usage: MergedLayoutFixture <source.dll> <output.dll> <poison|unknown|alter>");
if (Path.GetFullPath(args[0]).Equals(Path.GetFullPath(args[1]), StringComparison.OrdinalIgnoreCase))
    throw new IOException("Never mutate a source fixture.");
using var module = ModuleDefinition.ReadModule(args[0]);
var registry = module.GetType("ProbablyStolenTools.Generated.SnapshotLayouts");
if (args[2] == "unknown")
{
    var values = registry.Methods.Single(m => m.Name == "ExpectedModules").Body.Instructions
        .Where(i => i.OpCode == OpCodes.Ldstr).ToArray();
    int game = Array.FindIndex(values, i => (string)i.Operand == "Assembly-CSharp.dll");
    values[game + 1].Operand = Guid.NewGuid().ToString("D");
}
else if (args[2] == "poison")
{
    var token = registry.Methods.Single(m => m.Name == "KnownTypes").Body.Instructions
        .First(i => i.OpCode == OpCodes.Ldtoken && ((TypeReference)i.Operand).FullName != "PlayerStore");
    token.Operand = new TypeReference("", "MissingInactiveProfileSentinel", module, ((TypeReference)token.Operand).Scope);
}
else
{
    registry.Methods.Where(m => m.Name.StartsWith("Fields", StringComparison.Ordinal))
        .SelectMany(m => m.Body.Instructions).First(i => i.OpCode == OpCodes.Ldstr).Operand = "MissingChangedField";
}
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1])));
module.Write(args[1]);
