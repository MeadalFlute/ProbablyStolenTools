using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length < 2) { Console.WriteLine("usage: ILDump <dll> <type> [method]"); return; }
var asm = AssemblyDefinition.ReadAssembly(args[0]);
if (args.Length >= 3 && args[2] == "--ldstr")
{
    string needle = args[3];
    foreach (var t in asm.MainModule.GetTypes())
        foreach (var m in t.Methods)
        {
            if (!m.HasBody) continue;
            foreach (var ins in m.Body.Instructions)
                if (ins.OpCode == OpCodes.Ldstr && ins.Operand is string s && s.Contains(needle))
                {
                    Console.WriteLine($"{t.FullName}.{m.Name} -> \"{s}\"");
                    break;
                }
        }
    return;
}
if (args.Length >= 3 && args[2] == "--callers")
{
    string target = args[3];
    foreach (var t in asm.MainModule.GetTypes())
        foreach (var m in t.Methods)
        {
            if (!m.HasBody) continue;
            foreach (var ins in m.Body.Instructions)
                if (ins.Operand is MethodReference mr && mr.Name == target)
                {
                    Console.WriteLine($"{t.FullName}.{m.Name} -> {mr.DeclaringType.Name}::{mr.Name}");
                    break;
                }
        }
    return;
}
var type = asm.MainModule.GetTypes().FirstOrDefault(t => t.FullName == args[1] || t.Name == args[1]);
if (type == null) { Console.WriteLine("type not found"); return; }
foreach (var m in type.Methods)
{
    if (args.Length >= 3 && m.Name != args[2]) continue;
    if (!m.HasBody) continue;
    Console.WriteLine($"=== {type.FullName}.{m.Name}({string.Join(",", m.Parameters.Select(p => p.ParameterType.Name + " " + p.Name))})");
    foreach (var ins in m.Body.Instructions)
    {
        string op = ins.OpCode.Name;
        string arg = ins.Operand switch
        {
            null => "",
            string s => "\"" + s + "\"",
            MethodReference mr => mr.DeclaringType.Name + "::" + mr.Name,
            FieldReference fr => fr.DeclaringType.Name + "::" + fr.Name,
            Instruction target => "IL_" + target.Offset.ToString("x4"),
            _ => ins.Operand.ToString()
        };
        if (op.StartsWith("call") || op.StartsWith("ldstr") || op.StartsWith("ldfld") || op.StartsWith("stfld") || op.StartsWith("br") || op.StartsWith("ldc"))
            Console.WriteLine($"  IL_{ins.Offset:x4} {op} {arg}");
    }
}
