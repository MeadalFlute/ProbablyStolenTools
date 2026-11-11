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

    // Model physical-size descriptors and very wide structs without changing production schemas.
    private static void ElementSchema(ManagedStateAccess access, Type elementType, int size, int stride = 1)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var schemaType = typeof(ManagedStateAccess).GetNestedType("TypeSchema", BindingFlags.NonPublic);
        var storageType = typeof(ManagedStateAccess).GetNestedType("Storage", BindingFlags.NonPublic);
        var stores = Array.CreateInstance(storageType, stride);
        var slots = Enumerable.Range(0, stride).Select(i => new StateSlot(i, size, false, "modeled" + i)).ToArray();
        var schema = schemaType.GetConstructor(flags, null, new[] { typeof(StateSlot[]), stores.GetType() }, null)
            .Invoke(new object[] { slots, stores });
        var schemas = (IDictionary)typeof(ManagedStateAccess).GetField("_elementSchemas", flags).GetValue(access);
        schemas[elementType] = schema;
    }

    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("byte bool and decimal array budgets match actual eight-byte tokens in capture and census", () =>
        {
            foreach (Array root in new Array[] { new byte[] { 1, 2 }, new bool[] { true, false }, new decimal[] { 1m, 2m } })
                foreach (bool census in new[] { false, true })
                    foreach (long budget in new long[] { 15, 16 })
                    {
                        var access = Access(root);
                        SnapshotLimitException failure = null;
                        try
                        {
                            using var graph = census
                                ? StateGraph.Census(access, new[] { ManagedObjects.Id(root) }, maxBytes: budget)
                                : StateGraph.Capture(access, new[] { ManagedObjects.Id(root) }, maxBytes: budget);
                            check(budget == 16 && graph.ByteCount == 16 && graph.SlotCount == 2);
                            if (!census) check(graph.Differences().Count == 0);
                        }
                        catch (SnapshotLimitException e) { failure = e; }
                        check(budget == 16 ? failure == null : failure != null &&
                            failure.Message.Contains("slots=0/") && failure.Message.Contains("array requires slots=2, bytes=16"));
                    }
        });
        test("array preallocation budgets ignore modeled physical scalar sizes", () =>
        {
            foreach (int physicalSize in new[] { 1, 16 })
            {
                var root = new int[2];
                using var access = Access(root);
                access.Retain(ManagedObjects.Id(root));
                ElementSchema(access, typeof(int), physicalSize);
                bool failed = false;
                try { ((IBudgetedStateDescription)access).Describe(ManagedObjects.Id(root), 2, 15); }
                catch (SnapshotDescriptionLimitException e)
                {
                    failed = e.Kind == "byte" && e.RequiredSlots == 2 && e.RequiredBytes == 16
                        && e.Message.Contains("before expansion") && e.Message.Contains("bytes=16");
                }
                check(failed && Count(access, "_storage") == 0);
                check(((IBudgetedStateDescription)access).Describe(ManagedObjects.Id(root), 2, 16).Count == 2);
                // A shape-cache hit must check the same logical budget before reuse.
                failed = false;
                try { ((IBudgetedStateDescription)access).Describe(ManagedObjects.Id(root), 2, 15); }
                catch (SnapshotDescriptionLimitException) { failed = true; }
                check(failed);
            }
        });
        test("array slot products beyond Int32 fail with full snapshot diagnostics rather than overflow", () =>
        {
            var root = new int[262144];
            var access = Access(root);
            ElementSchema(access, typeof(int), 1, 9000);
            SnapshotLimitException failure = null;
            try
            {
                using var graph = StateGraph.Capture(access, new[] { ManagedObjects.Id(root) },
                    maxSlots: int.MaxValue, maxBytes: long.MaxValue);
            }
            catch (SnapshotLimitException e) { failure = e; }
            check(failure != null && failure.Message.Contains("Snapshot slot limit exceeded")
                && failure.Message.Contains("array requires slots=2359296000, bytes=18874368000 before expansion"));
            check(Count(access, "_storage") == 0 && Count(access, "_retained") == 0);
        });
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
