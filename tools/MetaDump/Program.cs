using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if (args.Length < 2) { Console.WriteLine("usage: MetaDump <dll> <filter...>"); return; }
using var fs = File.OpenRead(args[0]);
using var pe = new PEReader(fs);
var md = pe.GetMetadataReader();
var filters = args.Skip(1).ToArray();
var prov = new TypeNameProvider(md);

foreach (var th in md.TypeDefinitions)
{
    var td = md.GetTypeDefinition(th);
    var name = md.GetString(td.Name);
    var ns = md.GetString(td.Namespace);
    var full = string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    if (!filters.Any(f => full.Contains(f, StringComparison.OrdinalIgnoreCase))) continue;
    Console.WriteLine("== " + full);
    foreach (var fh in td.GetFields())
    {
        var fd = md.GetFieldDefinition(fh);
        string t;
        try { t = fd.DecodeSignature(prov, null); } catch { t = "?"; }
        Console.WriteLine($"   field {t} {md.GetString(fd.Name)}");
    }
    foreach (var mh in td.GetMethods())
    {
        var m = md.GetMethodDefinition(mh);
        string sig;
        try
        {
            var s = m.DecodeSignature(prov, null);
            sig = string.Join(",", s.ParameterTypes) + " -> " + s.ReturnType;
        }
        catch { sig = "?"; }
        Console.WriteLine($"   method {md.GetString(m.Name)}({sig})");
    }
}

sealed class TypeNameProvider : ISignatureTypeProvider<string, object>
{
    private readonly MetadataReader _md;
    public TypeNameProvider(MetadataReader md) { _md = md; }
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
    public string GetByReferenceType(string elementType) => "ref " + elementType;
    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        => genericType.Split('`')[0] + "<" + string.Join(",", typeArguments) + ">";
    public string GetGenericMethodParameter(object genericContext, int index) => "M" + index;
    public string GetGenericTypeParameter(object genericContext, int index) => "T" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType;
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var td = reader.GetTypeDefinition(handle);
        var n = reader.GetString(td.Name);
        var ns = reader.GetString(td.Namespace);
        return string.IsNullOrEmpty(ns) ? n : ns + "." + n;
    }
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var tr = reader.GetTypeReference(handle);
        var n = reader.GetString(tr.Name);
        var ns = reader.GetString(tr.Namespace);
        return string.IsNullOrEmpty(ns) ? n : ns + "." + n;
    }
    public string GetTypeFromSpecification(MetadataReader reader, object genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        try { return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext); }
        catch { return "spec"; }
    }
}
