using System.Collections;
using System.Reflection;
using ProbablyStolenTools;

internal static class SnapshotArrayBudgetTests
{
    private struct Cell
    {
        internal int Number;
        internal (long Value, object Link) Pair;
    }

    private static ManagedStateAccess Access(object root)
    {
        var access = new ManagedStateAccess(gameAssembly: typeof(Cell).Assembly.GetName().Name);
        access.AddRoot(ManagedObjects.Id(root));
        return access;
    }

    private static int Count(ManagedStateAccess access, string field) =>
        ((IDictionary)typeof(ManagedStateAccess).GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(access)).Count;

    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("large arrays fail slot and byte budgets before per-element descriptor allocation", () =>
        {
            var root = new int[262144];
            // Warm schema/code paths before checking the allocation ceiling.
            var small = new[] { 1 };
            using (var warm = Access(small))
                using (var graph = StateGraph.Capture(warm, new[] { ManagedObjects.Id(small) })) { }
            foreach (bool slotLimit in new[] { true, false })
            {
                var access = Access(root);
                long before = GC.GetAllocatedBytesForCurrentThread();
                SnapshotLimitException failure = null;
                try
                {
                    using var graph = StateGraph.Capture(access, new[] { ManagedObjects.Id(root) },
                        maxSlots: slotLimit ? 1 : 3000000, maxBytes: slotLimit ? 268435456 : 1);
                }
                catch (SnapshotLimitException e) { failure = e; }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                check(failure != null && failure.Message.Contains(slotLimit ? "Snapshot slot" : "Snapshot byte"));
                check(failure.Message.Contains("array requires slots=262144, bytes=2097152 before expansion"));
                check(allocated < 1024 * 1024);
                check(Count(access, "_retained") == 0 && Count(access, "_storage") == 0
                    && Count(access, "_savedValues") == 0);
                check(root.All(value => value == 0));
                Console.WriteLine($"ARRAY_BUDGET {(slotLimit ? "slot" : "byte")} allocatedBytes={allocated}");
            }
        });
        test("array budgets use remaining graph capacity and retain source diagnostics", () =>
        {
            var first = new[] { 11 };
            var later = new[] { 22, 33 };
            var root = new object[] { first, later };
            foreach (bool slotLimit in new[] { true, false })
            {
                var access = Access(root);
                SnapshotLimitException failure = null;
                try
                {
                    using var graph = StateGraph.Capture(access, new[] { ManagedObjects.Id(root) },
                        maxSlots: slotLimit ? 4 : 100, maxBytes: slotLimit ? 1000 : 32);
                }
                catch (SnapshotLimitException e) { failure = e; }
                check(failure != null && failure.Message.Contains("slots=3/") && failure.Message.Contains("bytes=24/"));
                check(failure.Message.Contains("root -> System.Object[][1] -> array requires slots=2, bytes=16"));
                check(first[0] == 11 && later.SequenceEqual(new[] { 22, 33 }));
                check(Count(access, "_retained") == 0);
            }
        });
        test("exact array budgets preserve all nested struct values and reference identities", () =>
        {
            var one = new object();
            var two = new object();
            var root = new[] {
                new Cell { Number = 7, Pair = (101, one) },
                new Cell { Number = 9, Pair = (202, two) } };
            using var access = Access(root);
            using var graph = StateGraph.Capture(access, new[] { ManagedObjects.Id(root) },
                maxObjects: 3, maxSlots: 6, maxBytes: 4 * 8 + 2 * IntPtr.Size);
            check(graph.ObjectCount == 3 && graph.SlotCount == 6 && graph.Contains(ManagedObjects.Id(two)));
            root[0] = new Cell { Number = -1 };
            root[1] = new Cell { Pair = (-2, new object()) };
            graph.Restore();
            check(root[0].Number == 7 && root[0].Pair.Value == 101 && ReferenceEquals(root[0].Pair.Link, one));
            check(root[1].Number == 9 && root[1].Pair.Value == 202 && ReferenceEquals(root[1].Pair.Link, two));
            check(graph.Differences().Count == 0);
        });
        test("empty and unsupported arrays retain their existing budget-independent contract", () =>
        {
            var empty = Array.Empty<IntPtr>();
            using (var access = Access(empty))
            using (var graph = StateGraph.Capture(access, new[] { ManagedObjects.Id(empty) }, maxSlots: 0, maxBytes: 0))
                check(graph.SlotCount == 0 && graph.ByteCount == 0);
            foreach (var root in new Array[] { new int[2, 2], new IntPtr[1], new int[262145],
                Array.CreateInstance(typeof(int), new[] { 3 }, new[] { 1 }) })
            {
                var access = Access(root);
                Exception failure = null;
                try { using var graph = StateGraph.Capture(access, new[] { ManagedObjects.Id(root) }, maxSlots: 0, maxBytes: 0); }
                catch (InvalidOperationException e) { failure = e; }
                check(failure != null && failure is not SnapshotLimitException);
                check(Count(access, "_retained") == 0);
            }
        });
        test("an early rejected array never invalidates older retained checkpoints", () =>
        {
            var root = new[] { 7, 8 };
            using var firstAccess = Access(root);
            using var first = StateGraph.Capture(firstAccess, new[] { ManagedObjects.Id(root) });
            root[0] = 99;
            var rejected = Access(root);
            bool failed = false;
            try { using var second = StateGraph.Capture(rejected, new[] { ManagedObjects.Id(root) }, maxSlots: 1); }
            catch (SnapshotLimitException) { failed = true; }
            check(failed && Count(rejected, "_storage") == 0);
            first.Restore();
            check(root.SequenceEqual(new[] { 7, 8 }) && first.Differences().Count == 0);
        });
    }
}
