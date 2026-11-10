using System.Collections;
using System.Diagnostics;
using System.Reflection;
using ProbablyStolenTools;

internal static class SnapshotCensusTests
{
    private sealed class Node
    {
        internal int Number;
        internal Node Link;
        internal (int Number, Node Link) Pair;
        internal Dictionary<int, Node> Map = new();
        internal Action Callback;
        internal float? Optional;
    }

    private static ManagedStateAccess Access(bool fast = true)
        => new(null, typeof(Node).Assembly.GetName().Name, fast);

    private static T Field<T>(object obj, string name)
        => (T)obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(obj);

    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("census matches full graph identities paths slot and byte budgets without retaining values", () =>
        {
            foreach (bool fast in new[] { false, true })
            {
                var child = new Node { Number = 41, Optional = -0f };
                var root = new Node { Number = 7, Link = child, Pair = (9, child) };
                child.Link = root;
                root.Map.Add(1, child); root.Map.Add(2, root); root.Map.Remove(1);
                root.Callback = () => child.Number++;
                root.Callback += () => root.Number++;
                var id = ManagedObjects.Id(root);
                var fullAccess = Access(fast);
                var censusAccess = Access(fast);
                fullAccess.AddRoot(id); censusAccess.AddRoot(id);
                using var full = StateGraph.Capture(fullAccess, new[] { id });
                using var census = StateGraph.Census(censusAccess, new[] { id });
                check(full.ObjectCount == census.ObjectCount && full.SlotCount == census.SlotCount
                    && full.ByteCount == census.ByteCount && full.Objects.ToHashSet().SetEquals(census.Objects));
                foreach (var obj in full.Objects) check(full.SourceOf(obj) == census.SourceOf(obj));
                check(Field<ICollection>(census, "_entries").Count == 0);
                check(Field<IDictionary>(censusAccess, "_values").Count == 0
                    && Field<IDictionary>(censusAccess, "_savedValues").Count == 0);
                check(Field<IDictionary>(fullAccess, "_values").Count > 0);
                root.Number = 99; root.Callback();
                full.Restore();
                check(root.Number == 7 && child.Number == 41 && full.Differences().Count == 0);
            }
        });
        test("census cannot masquerade as an empty checkpoint or perform any writes", () =>
        {
            var access = new ProbeAccess();
            var graph = StateGraph.Census(access, new[] { (IntPtr)1 });
            foreach (Action action in new Action[] { () => graph.Restore(), () => graph.Differences() })
            {
                bool rejected = false;
                try { action(); } catch (InvalidOperationException) { rejected = true; }
                check(rejected);
            }
            check(access.Writes == 0 && access.Probes == 4 && access.Reads == 0 && access.Validations == 1);
            graph.Dispose(); graph.Dispose();
            check(access.Disposals == 1);
            bool disposed = false;
            try { graph.Differences(); } catch (ObjectDisposedException) { disposed = true; }
            check(disposed);
        });
        test("census probes every scalar and a late unreadable field aborts before any write", () =>
        {
            var access = new ProbeAccess { FailAt = 4 };
            bool failed = false;
            try { using var graph = StateGraph.Census(access, new[] { (IntPtr)1 }); }
            catch (InvalidOperationException e) { failed = e.Message == "unreadable scalar"; }
            check(failed && access.Probes == 4 && access.Reads == 0 && access.Writes == 0
                && access.Disposals == 1 && access.Validations == 0);
        });
        test("census fallback reads real byte lengths and propagates read and validation failures", () =>
        {
            var access = new ByteAccess();
            using (var graph = StateGraph.Census(access, new[] { (IntPtr)1 }))
                check(graph.SlotCount == 4 && graph.ByteCount == 52 && access.Reads == 4);
            foreach (bool failValidation in new[] { false, true })
            {
                var failed = new ByteAccess { FailRead = !failValidation, FailValidation = failValidation };
                bool rejected = false;
                try { using var graph = StateGraph.Census(failed, new[] { (IntPtr)1 }); }
                catch (InvalidOperationException) { rejected = true; }
                check(rejected && failed.Disposals == 1 && failed.Writes == 0);
            }
        });
        test("census and capture enforce identical scalar array and discovery limits", () =>
        {
            foreach (string budget in new[] { "object", "slot", "byte" })
            {
                string[] failures = new string[2];
                for (int mode = 0; mode < 2; mode++)
                {
                    var root = new object[] { new[] { 1, 2, 3 }, new[] { 4, 5, 6 } };
                    var access = Access();
                    var id = ManagedObjects.Id(root);
                    access.AddRoot(id);
                    try
                    {
                        using var graph = mode == 0
                            ? StateGraph.Capture(access, new[] { id }, budget == "object" ? 2 : 20,
                                budget == "slot" ? 7 : 100, budget == "byte" ? 55 : 1000)
                            : StateGraph.Census(access, new[] { id }, budget == "object" ? 2 : 20,
                                budget == "slot" ? 7 : 100, budget == "byte" ? 55 : 1000);
                    }
                    catch (SnapshotLimitException e) { failures[mode] = e.Message; }
                    check(Field<IDictionary>(access, "_retained").Count == 0);
                }
                check(failures[0] != null && failures[0] == failures[1]);
            }
        });
        test("selected-root census keeps the same coverage and cannot invalidate a retained checkpoint", () =>
        {
            var root = new Node { Number = 8, Link = new Node { Number = 12 } };
            var id = ManagedObjects.Id(root);
            var fullAccess = Access(); fullAccess.AddRoot(id, "Number");
            using var full = StateGraph.Capture(fullAccess, new[] { id });
            var censusAccess = Access(); censusAccess.AddRoot(id, "Number");
            using (var census = StateGraph.Census(censusAccess, new[] { id }, 1, 1, 8))
                check(census.ObjectCount == 1 && census.SlotCount == 1 && census.ByteCount == 8);
            root.Number = 90;
            full.Restore();
            check(root.Number == 8 && root.Link.Number == 12 && full.Differences().Count == 0);
        });
    }

    private class ByteAccess : IStateAccess
    {
        internal int Reads, Writes, Disposals, Validations;
        internal bool FailRead, FailValidation;
        public void Retain(IntPtr id) { }
        public IReadOnlyList<StateSlot> Describe(IntPtr id)
            => Enumerable.Range(0, 4).Select(i => new StateSlot(i, 8, false, "scalar" + i)).ToArray();
        public byte[] ReadValue(IntPtr id, StateSlot slot)
        {
            Reads++;
            if (FailRead) throw new InvalidOperationException("unreadable fallback scalar");
            return new byte[13];
        }
        public IntPtr ReadReference(IntPtr id, StateSlot slot) => throw new NotSupportedException();
        public void WriteValue(IntPtr id, StateSlot slot, byte[] value) { Writes++; }
        public void WriteReference(IntPtr id, StateSlot slot, IntPtr value) { Writes++; }
        public void Validate()
        {
            Validations++;
            if (FailValidation) throw new InvalidOperationException("identity changed");
        }
        public void Dispose() { Disposals++; }
    }

    private sealed class ProbeAccess : ByteAccess, IStateValueProbe
    {
        internal int Probes, FailAt;
        public int ProbeValue(IntPtr id, StateSlot slot)
        {
            if (++Probes == FailAt) throw new InvalidOperationException("unreadable scalar");
            return 13;
        }
    }

    internal static void Benchmark()
    {
        var root = Enumerable.Range(0, 12000).Select(i =>
            new Node { Number = i, Pair = (i * 2, null), Optional = i / 7f }).ToArray();
        var samples = new[] { new List<double>(), new List<double>() };
        var allocated = new[] { new List<long>(), new List<long>() };
        for (int run = 0; run < 7; run++)
            foreach (int mode in run % 2 == 0 ? new[] { 0, 1 } : new[] { 1, 0 })
            {
                var access = Access();
                var id = ManagedObjects.Id(root);
                access.AddRoot(id);
                GC.Collect(); GC.WaitForPendingFinalizers();
                long before = GC.GetAllocatedBytesForCurrentThread();
                var clock = Stopwatch.StartNew();
                using var graph = mode == 0 ? StateGraph.Capture(access, new[] { id })
                    : StateGraph.Census(access, new[] { id });
                clock.Stop();
                if (run > 0)
                {
                    samples[mode].Add(clock.Elapsed.TotalMilliseconds);
                    allocated[mode].Add(GC.GetAllocatedBytesForCurrentThread() - before);
                }
                if (run == 6) Console.WriteLine($"CENSUS_COVERAGE mode={mode} objects={graph.ObjectCount} " +
                    $"slots={graph.SlotCount} bytes={graph.ByteCount}");
            }
        for (int mode = 0; mode < 2; mode++)
        {
            samples[mode].Sort(); allocated[mode].Sort();
            Console.WriteLine($"CENSUS_BENCH {(mode == 0 ? "capture" : "census")} " +
                $"medianMs={(samples[mode][2] + samples[mode][3]) / 2:0.00} " +
                $"allocatedMedianBytes={(allocated[mode][2] + allocated[mode][3]) / 2} (NET6 synthetic, not Unity Mono)");
        }
    }
}
