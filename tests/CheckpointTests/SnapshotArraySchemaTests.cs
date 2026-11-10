using System.Collections;
using System.Diagnostics;
using System.Reflection;
using ProbablyStolenTools;

internal static class SnapshotArraySchemaTests
{
    private struct Cell
    {
        internal int Number;
        internal (long Amount, object Link) Pair;
    }

    private static ManagedStateAccess Access(bool reuse = true, bool fast = true) =>
        new(null, typeof(Cell).Assembly.GetName().Name, fast, reuse);

    private static StateGraph Capture(object root, ManagedStateAccess access)
    {
        var id = ManagedObjects.Id(root);
        access.AddRoot(id);
        return StateGraph.Capture(access, new[] { id });
    }

    private static IReadOnlyList<StateSlot> Describe(ManagedStateAccess access, object value)
    {
        var id = ManagedObjects.Id(value);
        access.Retain(id);
        return access.Describe(id);
    }

    private static T Field<T>(ManagedStateAccess access, string name) =>
        (T)typeof(ManagedStateAccess).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(access);

    private static object Stores(ManagedStateAccess access, object value) =>
        Field<IDictionary>(access, "_storage")[ManagedObjects.Id(value)];

    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("equal array shapes share only immutable descriptors and keep every instance value", () =>
        {
            foreach (bool fast in new[] { false, true })
            {
                using var access = Access(fast: fast);
                var first = Enumerable.Range(0, 80).ToArray();
                var second = Enumerable.Range(1000, 80).ToArray();
                var a = Describe(access, first);
                var b = Describe(access, second);
                check(ReferenceEquals(a, b) && ReferenceEquals(Stores(access, first), Stores(access, second)));
                using var old = Access(reuse: false, fast: fast);
                var expected = Describe(old, first);
                var expectedSecond = Describe(old, second);
                check(a.SequenceEqual(expected));
                for (int i = 0; i < a.Count; i++)
                {
                    var one = access.ReadValue(ManagedObjects.Id(first), a[i]);
                    var two = access.ReadValue(ManagedObjects.Id(second), b[i]);
                    check(!one.SequenceEqual(two));
                    check(one.SequenceEqual(old.ReadValue(ManagedObjects.Id(first), expected[i])));
                    check(two.SequenceEqual(old.ReadValue(ManagedObjects.Id(second), expectedSecond[i])));
                }
                check(access.ArraySchemaSummary.Contains("hits=1,slotsReused=80"));
                check(!ReferenceEquals(a, Describe(access, new int[79])));
                check(!ReferenceEquals(a, Describe(access, new long[80])));
                using var independent = Access(fast: fast);
                check(!ReferenceEquals(a, Describe(independent, first)));
            }
        });
        test("shared nested struct array descriptors preserve indices references and simultaneous checkpoints", () =>
        {
            var links = new[] { new object(), new object(), new object(), new object() };
            var a = new[] { new Cell { Number = 1, Pair = (10, links[0]) }, new Cell { Number = 2, Pair = (20, links[1]) } };
            var b = new[] { new Cell { Number = 3, Pair = (30, links[2]) }, new Cell { Number = 4, Pair = (40, links[3]) } };
            var root = new[] { a, b };
            using var firstAccess = Access();
            using var first = Capture(root, firstAccess);
            check(ReferenceEquals(Stores(firstAccess, a), Stores(firstAccess, b)));
            a[1] = new Cell { Number = 22, Pair = (220, links[3]) };
            b[0] = new Cell { Number = 33, Pair = (330, links[0]) };
            using var secondAccess = Access();
            using var second = Capture(root, secondAccess);
            check(!ReferenceEquals(Stores(firstAccess, a), Stores(secondAccess, a)));
            for (int i = 0; i < 4; i++)
            {
                Array.Clear(a); Array.Clear(b); root[1] = new Cell[2];
                first.Restore();
                check(ReferenceEquals(root[1], b) && a[1].Number == 2 && a[1].Pair.Amount == 20
                    && ReferenceEquals(a[1].Pair.Link, links[1]));
                check(b[0].Number == 3 && b[0].Pair.Amount == 30 && ReferenceEquals(b[0].Pair.Link, links[2]));
                check(first.Differences().Count == 0);
                second.Restore();
                check(a[1].Number == 22 && a[1].Pair.Amount == 220 && ReferenceEquals(a[1].Pair.Link, links[3]));
                check(b[0].Number == 33 && b[0].Pair.Amount == 330 && ReferenceEquals(b[0].Pair.Link, links[0]));
                check(second.Differences().Count == 0);
            }
            firstAccess.Dispose();
            check(Field<IDictionary>(firstAccess, "_arraySchemas").Count == 0);
            b[0].Number = -1;
            second.Restore();
            check(b[0].Number == 33 && second.Differences().Count == 0);
        });
        test("cached array descriptions still reject insufficient remaining budgets before reuse", () =>
        {
            using var access = Access();
            var a = new[] { 1, 2 };
            var b = new[] { 3, 4 };
            var slots = Describe(access, a);
            access.Retain(ManagedObjects.Id(b));
            foreach (bool slotLimit in new[] { true, false })
            {
                bool failed = false;
                try { ((IBudgetedStateDescription)access).Describe(ManagedObjects.Id(b), slotLimit ? 1 : 2, slotLimit ? 16 : 15); }
                catch (SnapshotDescriptionLimitException e)
                { failed = e.RequiredSlots == 2 && e.RequiredBytes == 16 && e.Kind == (slotLimit ? "slot" : "byte"); }
                check(failed);
            }
            check(ReferenceEquals(slots, ((IBudgetedStateDescription)access).Describe(ManagedObjects.Id(b), 2, 16)));
        });
        test("array schema cache is bounded and uncached arrays retain full descriptors", () =>
        {
            using var access = Access();
            var huge = new int[65537];
            var a = Describe(access, huge);
            check(Field<IDictionary>(access, "_arraySchemas").Count == 0);
            check(a.Count == 65537 && !ReferenceEquals(a, Describe(access, new int[65537])));
            var full = new int[65536];
            var b = Describe(access, full);
            check(Field<int>(access, "_arraySchemaSlots") == 65536);
            var small = new[] { 7 };
            var c = Describe(access, small);
            check(!ReferenceEquals(c, Describe(access, new[] { 8 })));
            check(ReferenceEquals(b, Describe(access, new int[65536])));
            check(Field<int>(access, "_arraySchemaSlots") == 65536);
            // Empty structs have zero slots; enforce the shape count cap independently.
            using var zero = Access();
            for (int i = 1; i <= 4100; i++) check(Describe(zero, new ValueTuple[i]).Count == 0);
            check(Field<IDictionary>(zero, "_arraySchemas").Count == 4096);
        });
        test("shared schemas retain dictionary capacity and match uncached graph coverage", () =>
        {
            var root = Fixture(80);
            using var cachedAccess = Access();
            using var uncachedAccess = Access(reuse: false);
            using var cached = Capture(root, cachedAccess);
            using var uncached = Capture(root, uncachedAccess);
            check(cached.ObjectCount == uncached.ObjectCount && cached.SlotCount == uncached.SlotCount
                && cached.ByteCount == uncached.ByteCount && cached.Objects.ToHashSet().SetEquals(uncached.Objects));
            foreach (var id in cached.Objects) check(cached.SourceOf(id) == uncached.SourceOf(id));
            foreach (var dictionary in root)
            {
                dictionary.Clear();
                for (int i = 0; i < 100; i++) dictionary[1000 + i] = new Cell { Number = -i };
            }
            cached.Restore();
            check(uncached.Differences().Count == 0);
            for (int i = 0; i < root.Length; i++)
                check(root[i].Count == 6 && root[i][4].Number == i * 10 + 4
                    && root[i][4].Pair.Amount == i * 100 + 4 && root[i][4].Pair.Link != null);
            check(Field<int>(cachedAccess, "_arraySchemaHits") > 0);
        });
    }

    private static Dictionary<int, Cell>[] Fixture(int count)
    {
        var root = Enumerable.Range(0, count).Select(_ => new Dictionary<int, Cell>()).ToArray();
        for (int i = 0; i < count; i++)
        {
            for (int j = 0; j < 8; j++)
                root[i][j] = new Cell { Number = i * 10 + j, Pair = (i * 100 + j, new object()) };
            root[i].Remove(1); root[i].Remove(6);
        }
        return root;
    }

    internal static void Benchmark()
    {
        var root = Fixture(2000);
        var times = new[] { new List<double>(), new List<double>() };
        var allocations = new[] { new List<long>(), new List<long>() };
        for (int run = 0; run < 7; run++)
        {
            foreach (int mode in run % 2 == 0 ? new[] { 0, 1 } : new[] { 1, 0 })
            {
                GC.Collect(); GC.WaitForPendingFinalizers();
                var access = Access(reuse: mode == 1);
                long before = GC.GetAllocatedBytesForCurrentThread();
                var clock = Stopwatch.StartNew();
                using var graph = Capture(root, access);
                if (graph.Differences().Count != 0) throw new Exception("Array schema benchmark differs.");
                clock.Stop();
                if (run > 0)
                {
                    times[mode].Add(clock.Elapsed.TotalMilliseconds);
                    allocations[mode].Add(GC.GetAllocatedBytesForCurrentThread() - before);
                }
                if (run == 6)
                    Console.WriteLine($"ARRAY_SCHEMA_COVERAGE mode={mode} objects={graph.ObjectCount} slots={graph.SlotCount} " +
                        $"bytes={graph.ByteCount} {access.ArraySchemaSummary}");
            }
        }
        for (int mode = 0; mode < 2; mode++)
        {
            times[mode].Sort(); allocations[mode].Sort();
            Console.WriteLine($"ARRAY_SCHEMA_BENCH {(mode == 0 ? "uncached-step2" : "shared-step3")} " +
                $"captureVerifyMedianMs={(times[mode][2] + times[mode][3]) / 2:0.00} " +
                $"allocatedMedianBytes={(allocations[mode][2] + allocations[mode][3]) / 2} " +
                "(NET6 synthetic repeated dictionaries; full capture and verification; not Unity Mono)");
        }
    }
}
