using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Reflection;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

if (args.Length != 2) throw new ArgumentException("Usage: LiteralTokens <GAME_DIR> <new-output.dll>");
string managed = Path.Combine(args[0], "Probably Stolen_Data", "Managed");
string output = Path.GetFullPath(args[1]);
if (File.Exists(output)) throw new IOException("Refusing to overwrite an existing probe.");
using var core = ModuleDefinition.ReadModule(Path.Combine(managed, "mscorlib.dll"));
using var collections = ModuleDefinition.ReadModule(Path.Combine(managed, "System.Core.dll"));
using var module = ModuleDefinition.CreateModule("LiteralTypeTokens", ModuleKind.Dll);
var holder = new TypeDefinition("", "LiteralTypeTokens",
    TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object);
module.Types.Add(holder);
var typeType = core.GetType("System.Type");
var getType = module.ImportReference(typeType.Methods.Single(m => m.Name == "GetTypeFromHandle"));
var getField = module.ImportReference(typeType.Methods.Single(m => m.Name == "GetField"
    && m.Parameters.Count == 2 && m.Parameters[1].ParameterType.Name == "BindingFlags"));

void Add(TypeDefinition parent, string nested, string field, string name, int arity)
{
    var method = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.Static, getField.ReturnType);
    for (int i = 0; i < arity; i++) method.GenericParameters.Add(new GenericParameter("T" + i, method));
    var token = new GenericInstanceType(module.ImportReference(parent.NestedTypes.Single(t => t.Name == nested)));
    foreach (var parameter in method.GenericParameters) token.GenericArguments.Add(parameter);
    holder.Methods.Add(method);
    var il = method.Body.GetILProcessor();
    il.Emit(OpCodes.Ldtoken, token);
    il.Emit(OpCodes.Call, getType);
    il.Emit(OpCodes.Ldstr, field);
    il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
    il.Emit(OpCodes.Callvirt, getField);
    il.Emit(OpCodes.Ret);
}
Add(core.GetType("System.Collections.Generic.Dictionary`2"), "Entry", "value", "DictionaryValue", 2);
Add(core.GetType("System.Collections.Generic.Dictionary`2"), "Entry", "next", "DictionaryNext", 2);
Add(collections.GetType("System.Collections.Generic.HashSet`1"), "Slot", "value", "HashSetValue", 1);
Directory.CreateDirectory(Path.GetDirectoryName(output));
module.Write(output);
Console.WriteLine("Generated explicit literal type tokens: " + output);
