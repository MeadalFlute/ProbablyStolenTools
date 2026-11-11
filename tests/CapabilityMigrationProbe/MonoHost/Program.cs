using System.Runtime.InteropServices;

// Offline subprocess only. This native bootstrap must never be part of a mod.
if (args.Length < 3 || args.Length > 4 || args.Length == 4 && args[3] != "--expect-version-rejection")
    throw new ArgumentException("Usage: MonoHost <GAME_DIR> <fixture.dll> <target.dll> [--expect-version-rejection]");
string game = Path.GetFullPath(args[0]);
string managed = Path.Combine(game, "Probably Stolen_Data", "Managed");
IntPtr library = NativeLibrary.Load(Path.Combine(game, "MonoBleedingEdge", "EmbedRuntime", "mono-2.0-bdwgc.dll"));
NativeLibrary.SetDllImportResolver(typeof(Mono).Assembly, (name, _, _) => name == "GameMono" ? library : IntPtr.Zero);
Mono.mono_set_dirs(managed, Path.Combine(game, "MonoBleedingEdge", "etc"));
Mono.mono_set_assemblies_path(managed);
IntPtr domain = Mono.mono_jit_init_version("offline-layout-fixture", "v4.0.30319");
if (domain == IntPtr.Zero) throw new Exception("Mono initialization failed.");
IntPtr assembly = Mono.mono_domain_assembly_open(domain, Path.GetFullPath(args[1]));
if (assembly == IntPtr.Zero) throw new Exception("Fixture load failed.");
IntPtr klass = Mono.mono_class_from_name(Mono.mono_assembly_get_image(assembly), "", "OfflineMonoFixture");
IntPtr method = Mono.mono_class_get_method_from_name(klass, args.Length == 4 ? "RunVersionMismatch" : "Run", 1);
if (method == IntPtr.Zero) throw new Exception("Fixture entry missing.");
IntPtr arguments = Marshal.AllocHGlobal(IntPtr.Size);
try
{
    Marshal.WriteIntPtr(arguments, Mono.mono_string_new(domain, Path.GetFullPath(args[2])));
    Mono.mono_runtime_invoke(method, IntPtr.Zero, arguments, out var exception);
    if (exception != IntPtr.Zero)
    {
        Mono.mono_print_unhandled_exception(exception);
        Environment.ExitCode = 1;
    }
}
finally { Marshal.FreeHGlobal(arguments); }

static class Mono
{
    [DllImport("GameMono", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void mono_set_dirs(string assemblyDir, string configDir);
    [DllImport("GameMono", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void mono_set_assemblies_path(string path);
    [DllImport("GameMono", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr mono_jit_init_version(string name, string version);
    [DllImport("GameMono", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr mono_domain_assembly_open(IntPtr domain, string path);
    [DllImport("GameMono", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr mono_assembly_get_image(IntPtr assembly);
    [DllImport("GameMono", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr mono_class_from_name(IntPtr image, string nameSpace, string name);
    [DllImport("GameMono", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr mono_class_get_method_from_name(IntPtr klass, string name, int count);
    [DllImport("GameMono", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr mono_string_new(IntPtr domain, string value);
    [DllImport("GameMono", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr mono_runtime_invoke(IntPtr method, IntPtr target, IntPtr arguments, out IntPtr exception);
    [DllImport("GameMono", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void mono_print_unhandled_exception(IntPtr exception);
}
