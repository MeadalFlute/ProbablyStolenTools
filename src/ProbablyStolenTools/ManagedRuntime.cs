using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace System.Runtime.CompilerServices
{
    internal sealed class IsExternalInit { }
}

namespace ProbablyStolenTools
{
    // IDs are weak, stable managed identities, never addresses in the moving GC heap.
    internal static class ManagedObjects
    {
        private sealed class Identity
        {
            internal readonly IntPtr Id;
            internal Identity(IntPtr id) { Id = id; }
        }

        private static readonly ConditionalWeakTable<object, Identity> Ids = new();
        private static readonly System.Collections.Generic.Dictionary<IntPtr, WeakReference<object>> Objects = new();
        private static long _next;

        internal static IntPtr Id(object value)
        {
            if (ReferenceEquals(value, null)) return IntPtr.Zero;
            var identity = Ids.GetValue(value, obj =>
            {
                var id = new IntPtr(Interlocked.Increment(ref _next));
                Objects[id] = new WeakReference<object>(obj);
                if (Objects.Count % 4096 == 0)
                {
                    var dead = new System.Collections.Generic.List<IntPtr>();
                    foreach (var pair in Objects)
                        if (!pair.Value.TryGetTarget(out _)) dead.Add(pair.Key);
                    foreach (var key in dead) Objects.Remove(key);
                }
                return new Identity(id);
            });
            return identity.Id;
        }

        internal static object Get(IntPtr id)
        {
            if (id == IntPtr.Zero) return null;
            if (Objects.TryGetValue(id, out var weak) && weak.TryGetTarget(out var value)) return value;
            throw new InvalidOperationException("Managed snapshot object expired.");
        }

        internal static T Cast<T>(this object value) where T : class => (T)value;
        internal static T TryCast<T>(this object value) where T : class => value as T;
    }
}
