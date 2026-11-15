using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

public static partial class OfflineMonoFixture
{
    private sealed class LegacyIdentityRegistry
    {
        private sealed class Identity
        {
            internal IntPtr Id;
        }
        private readonly ConditionalWeakTable<object, Identity> _ids = new ConditionalWeakTable<object, Identity>();
        private readonly Dictionary<IntPtr, WeakReference<object>> _objects = new Dictionary<IntPtr, WeakReference<object>>();
        private long _next;
        internal long Visited;
        internal IntPtr Id(object value) => _ids.GetValue(value, obj =>
        {
            var id = new IntPtr(++_next);
            _objects[id] = new WeakReference<object>(obj);
            if (_objects.Count % 4096 == 0)
            {
                Visited += _objects.Count;
                var dead = new List<IntPtr>();
                foreach (var pair in _objects)
                    if (!pair.Value.TryGetTarget(out _)) dead.Add(pair.Key);
                foreach (var key in dead) _objects.Remove(key);
            }
            return new Identity { Id = id };
        }).Id;
    }

    private static void CheckManagedIdentities(Assembly mod)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var type = mod.GetType("ProbablyStolenTools.ManagedObjects", true);
        var id = (Func<object, IntPtr>)Delegate.CreateDelegate(typeof(Func<object, IntPtr>), type.GetMethod("Id", flags));
        var get = (Func<IntPtr, object>)Delegate.CreateDelegate(typeof(Func<IntPtr, object>), type.GetMethod("Get", flags));
        long Metric(string name) => (long)type.GetProperty(name, flags).GetValue(null);
        var legacy = new LegacyIdentityRegistry();
        var retained = new object[185714];
        for (int i = 0; i < retained.Length; i++) retained[i] = new object();
        long visited = Metric("SweepVisited");
        var clock = Stopwatch.StartNew();
        var actual = new IntPtr[retained.Length];
        for (int i = 0; i < retained.Length; i++) actual[i] = id(retained[i]);
        double amortized = clock.Elapsed.TotalMilliseconds;
        long sweepVisits = Metric("SweepVisited") - visited;
        clock.Restart();
        foreach (var item in retained) legacy.Id(item);
        double previous = clock.Elapsed.TotalMilliseconds;
        if (sweepVisits > retained.Length * 3L)
            throw new Exception("Identity pruning is no longer amortized over newly allocated identities.");
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        for (int i = 0; i < retained.Length; i++)
            if (id(retained[i]) != actual[i] || !ReferenceEquals(get(actual[i]), retained[i]))
                throw new Exception("Live identity changed during pruning/compacting GC.");
        if (id(null) != IntPtr.Zero || get(IntPtr.Zero) != null)
            throw new Exception("Null identity changed.");
        long count = Metric("SweepCount");
        foreach (var item in retained) id(item);
        if (Metric("SweepCount") != count)
            throw new Exception("Repeated captures must not schedule identity sweeps without new objects.");
        Console.WriteLine("IDENTITY_BENCH runtime=game-Mono-offline-not-Unity objects=" + retained.Length
            + " legacyMs=" + previous.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
            + " amortizedMs=" + amortized.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
            + " legacyVisited=" + legacy.Visited + " amortizedVisited=" + sweepVisits);
        GC.KeepAlive(retained);
        Console.WriteLine("PASS actual game Mono identity registry: bounded cumulative scans, retained identities across GC, repeat lookup without sweeps and null identity.");
    }
}
