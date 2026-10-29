using System;

namespace ProbablyStolenTools
{
    internal static class RuntimeIdentity
    {
#if MONO
        internal static IntPtr Id(object value) => ManagedObjects.Id(value);
#else
        internal static IntPtr Id(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase value)
            => value?.Pointer ?? IntPtr.Zero;
#endif
    }
}
