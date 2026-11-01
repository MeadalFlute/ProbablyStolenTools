using System.Diagnostics;
using ProbablyStolenTools;

internal static class SnapshotOptimizationTests
{
    private class BaseItem
    {
        private readonly int _baseId;
        protected BaseItem(int id) { _baseId = id; }
        internal int BaseId => _baseId;
    }

    private sealed class Item : BaseItem
    {
        internal int Id, Amount;
        internal Item Link;
        internal Item(int id) : base(id + 1000) { Id = id; Amount = id * 10; }
    }

    private struct Cell
    {
        internal int X;
        internal (int Y, Item Item) Inner;
    }

    private sealed class Inventory
    {
        internal Item[] Items;
        internal Cell[] Cells;
        internal List<Item> List = new();
        internal Dictionary<int, Item> Map = new();
        internal Action Callbacks;
    }

    private static string GameAssembly => typeof(Item).Assembly.GetName().Name;

    private static StateGraph Capture(object root, bool legacy, params string[] fields)
        => CaptureWithReaders(root, legacy, true, fields);

    private static StateGraph CaptureWithReaders(object root, bool legacy, bool fastReads, params string[] fields)
    {
        var id = ManagedObjects.Id(root);
        if (legacy)
        {
            var access = new LegacyManagedStateAccess(gameAssembly: GameAssembly);
            access.AddRoot(id, fields);
            return StateGraph.Capture(access, new[] { id });
        }
        else
        {
            var access = new ManagedStateAccess(null, GameAssembly, fastReads);
            access.AddRoot(id, fields);
            return StateGraph.Capture(access, new[] { id });
        }
    }

    private static Inventory Fixture(int count)
    {
        var items = Enumerable.Range(1, count).Select(i => new Item(i)).ToArray();
        var inventory = new Inventory
        {
            Items = items,
            Cells = items.Select((item, i) => new Cell { X = i, Inner = (i + 1, item) }).ToArray(),
        };
        for (int i = 0; i < items.Length; i++)
        {
            items[i].Link = items[(i + 1) % items.Length];
            inventory.List.Add(items[i]);
            inventory.Map.Add(i * 17, items[i]);
        }
        // Exercise free dictionary entries and capacity beyond Count, not just packed arrays.
        for (int i = 3; i < count; i += 7) inventory.Map.Remove(i * 17);
        if (count > 1)
        {
            var first = items[0];
            var last = items[^1];
            inventory.Callbacks = () => first.Amount++;
            inventory.Callbacks += () => last.Amount++;
        }
        return inventory;
    }

    private static void Equivalent(object root, Action<bool> check)
    {
        var rootId = ManagedObjects.Id(root);
        using var old = new LegacyManagedStateAccess(gameAssembly: GameAssembly);
        using var current = new ManagedStateAccess(gameAssembly: GameAssembly);
        old.AddRoot(rootId);
        current.AddRoot(rootId);
        var visited = new HashSet<IntPtr>();
        var paths = new Dictionary<IntPtr, string>();
        // Independent old dequeue-time deduplication also verifies production queue changes.
        var queue = new Queue<(IntPtr Id, string Path)>();
        queue.Enqueue((rootId, "root"));
        while (queue.TryDequeue(out var entry))
        {
            var (id, source) = entry;
            if (!visited.Add(id)) continue;
            paths.Add(id, source);
            old.Retain(id);
            current.Retain(id);
            var expected = old.Describe(id);
            var actual = current.Describe(id);
            check(expected.SequenceEqual(actual));
            for (int i = 0; i < expected.Count; i++)
            {
                if (expected[i].IsReference)
                {
                    var reference = old.ReadReference(id, expected[i]);
                    check(reference == current.ReadReference(id, actual[i]));
                    if (reference != IntPtr.Zero) queue.Enqueue((reference, source + " -> " + expected[i].Name));
                }
                else
                    check(old.ReadValue(id, expected[i]).SequenceEqual(current.ReadValue(id, actual[i])));
            }
        }
        old.Validate();
        current.Validate();
        using var oldGraph = Capture(root, true);
        using var newGraph = Capture(root, false);
        check(oldGraph.ObjectCount == newGraph.ObjectCount && oldGraph.SlotCount == newGraph.SlotCount
            && oldGraph.ByteCount == newGraph.ByteCount && oldGraph.Objects.ToHashSet().SetEquals(newGraph.Objects));
        foreach (var id in oldGraph.Objects)
            check(oldGraph.SourceOf(id) == newGraph.SourceOf(id) && paths[id] == newGraph.SourceOf(id));
    }

    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("optimized descriptions and all field values match the pre-optimization backend", () =>
        {
            foreach (int count in new[] { 0, 1, 2, 9, 137 }) Equivalent(Fixture(count), check);
        });
        test("array elements beyond index zero retain distinct references and nested struct values", () =>
        {
            var inventory = Fixture(37);
            var originalItems = inventory.Items;
            var originalCells = inventory.Cells;
            using var graph = Capture(inventory, false);
            check(originalItems.All(item => graph.Contains(ManagedObjects.Id(item))));
            for (int i = 0; i < originalItems.Length; i++)
            {
                originalItems[i].Amount = -i;
                originalItems[i].Link = null;
                originalCells[i] = new Cell { X = -100, Inner = (-200, new Item(-1)) };
            }
            inventory.Items = new[] { new Item(99) };
            inventory.Cells = Array.Empty<Cell>();
            inventory.List.Clear();
            inventory.Map.Clear();
            graph.Restore();
            check(ReferenceEquals(inventory.Items, originalItems) && ReferenceEquals(inventory.Cells, originalCells));
            for (int i = 0; i < originalItems.Length; i++)
            {
                var item = originalItems[i];
                check(item.Id == i + 1 && item.Amount == (i + 1) * 10 && item.BaseId == i + 1001);
                check(ReferenceEquals(item.Link, originalItems[(i + 1) % originalItems.Length]));
                check(originalCells[i].X == i && originalCells[i].Inner.Y == i + 1
                    && ReferenceEquals(originalCells[i].Inner.Item, item));
                check(ReferenceEquals(inventory.List[i], item));
            }
            check(inventory.Map.Count == 32 && ReferenceEquals(inventory.Map[36 * 17], originalItems[36]));
            check(graph.Differences().Count == 0);
        });
        test("selected and full roots of the same type do not share a filtered schema", () =>
        {
            var selected = new Item(1);
            var complete = new Item(2);
            var root = new[] { selected, complete };
            using var access = new ManagedStateAccess(gameAssembly: GameAssembly);
            access.AddRoot(ManagedObjects.Id(root));
            access.AddRoot(ManagedObjects.Id(selected), nameof(Item.Amount));
            using var graph = StateGraph.Capture(access, new[] { ManagedObjects.Id(root), ManagedObjects.Id(selected) });
            selected.Id = 30; selected.Amount = 40;
            complete.Id = 50; complete.Amount = 60;
            graph.Restore();
            check(selected.Id == 30 && selected.Amount == 10 && complete.Id == 2 && complete.Amount == 20);
        });
        test("schemas and token bytes never leak instance state across simultaneous checkpoints", () =>
        {
            var root = Fixture(12);
            using var first = Capture(root, false);
            root.Items[7].Amount = 700;
            root.Cells[9].Inner.Y = 900;
            root.List.RemoveAt(4);
            root.Map.Remove(0);
            using var second = Capture(root, false);
            root.Items[7].Amount = 7000;
            root.List.Add(new Item(99));
            for (int i = 0; i < 3; i++)
            {
                first.Restore();
                check(root.Items[7].Amount == 80 && root.Cells[9].Inner.Y == 10 && root.List.Count == 12
                    && ReferenceEquals(root.Map[0], root.Items[0]));
                check(first.Differences().Count == 0);
                second.Restore();
                check(root.Items[7].Amount == 700 && root.Cells[9].Inner.Y == 900 && root.List.Count == 11
                    && !root.Map.ContainsKey(0));
                check(second.Differences().Count == 0);
            }
        });
        test("primitive nullable and jagged arrays match the reference and restore every element", () =>
        {
            var numbers = new[] { 7, 23, 101, -9 };
            var optional = new int?[] { null, 4, null, 19 };
            var root = new object[] { numbers, optional, new[] { numbers, new[] { 999 } } };
            Equivalent(root, check);
            using var graph = Capture(root, false);
            Array.Fill(numbers, 0);
            optional[0] = 11; optional[1] = null; optional[3] = 0;
            graph.Restore();
            check(numbers.SequenceEqual(new[] { 7, 23, 101, -9 }));
            check(optional.SequenceEqual(new int?[] { null, 4, null, 19 }));
        });
        test("empty arrays and invalid array shapes preserve the reference capture contract", () =>
        {
            Equivalent(Array.Empty<IntPtr>(), check);
            foreach (Array array in new Array[] { new int[2, 2], new IntPtr[1],
                Array.CreateInstance(typeof(int), new[] { 3 }, new[] { 1 }) })
            {
                foreach (bool legacy in new[] { true, false })
                {
                    bool failed = false;
                    try { using var graph = Capture(array, legacy); }
                    catch (InvalidOperationException) { failed = true; }
                    check(failed);
                }
            }
        });
        test("compiled readers match reflection after mutations and cached code never binds an instance", () =>
        {
            var root = Fixture(25);
            SnapshotFieldReader.ClearCache();
            using var compiled = CaptureWithReaders(root, false, true);
            using var reflected = CaptureWithReaders(root, false, false);
            var field = typeof(Item).GetField(nameof(Item.Amount),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var reader = SnapshotFieldReader.Get(typeof(Item), "reader-check", new[] { field }, false);
            check(!reader.Compiled);
            for (int i = 0; i < 40; i++) reader.TryRead(root.Items[i % root.Items.Length], -1, out _);
            check(reader.Compiled);
            check(reader.TryRead(root.Items[0], -1, out var first) && (int)first == 10);
            check(reader.TryRead(root.Items[1], -1, out var second) && (int)second == 20);
            root.Items[7].Amount = -1;
            root.Cells[12].Inner.Y = -10;
            root.Items[9].Link = null;
            check(compiled.Differences().SequenceEqual(reflected.Differences()));
            check(compiled.Differences().Count == 3);
            compiled.Restore();
            check(reflected.Differences().Count == 0);
            root.Items[7].Amount = 999;
            reflected.Restore();
            check(compiled.Differences().Count == 0 && root.Items[7].Amount == 80);
            SnapshotFieldReader.ClearCache();
            using var other = Capture(Fixture(25), false);
            root.Items[7].Amount = -2;
            compiled.Restore();
            check(root.Items[7].Amount == 80 && other.Differences().Count == 0);
        });
        test("unsupported reader generation falls back and scalar arrays preserve nullable values and bit patterns", () =>
        {
            var rejected = SnapshotFieldReader.Get(typeof(string), "unsupported-array", Array.Empty<System.Reflection.FieldInfo>(), true);
            for (int i = 0; i < 50; i++) check(!rejected.TryRead("plain text", 0, out _));
            check(!rejected.Compiled);
            var singles = Enumerable.Range(0, 80).Select(i => BitConverter.Int32BitsToSingle(
                i % 3 == 0 ? unchecked((int)0x80000000) : unchecked((int)0x7FC00000) + i)).ToArray();
            var bits = singles.Select(BitConverter.SingleToInt32Bits).ToArray();
            var optional = Enumerable.Range(0, 80).Select(i => i % 3 == 0 ? (int?)null : i).ToArray();
            var money = Enumerable.Range(0, 80).Select(i => (decimal)i / 10).ToArray();
            var days = Enumerable.Range(0, 80).Select(i => DateTime.UnixEpoch.AddDays(i)).ToArray();
            var modes = Enumerable.Range(0, 80).Select(i => (DayOfWeek)(i % 7)).ToArray();
            var root = new object[] { singles, optional, money, days, modes };
            Equivalent(root, check);
            using var graph = Capture(root, false);
            Array.Fill(singles, 0f); Array.Fill(optional, 999);
            Array.Fill(money, -1m); Array.Fill(days, DateTime.MinValue); Array.Fill(modes, DayOfWeek.Sunday);
            graph.Restore();
            check(singles.Select(BitConverter.SingleToInt32Bits).SequenceEqual(bits));
            check(optional[0] == null && optional[79] == 79 && money[79] == 7.9m
                && days[79] == DateTime.UnixEpoch.AddDays(79) && modes[79] == DayOfWeek.Tuesday);
            check(graph.Differences().Count == 0);
        });
    }

    internal static void Benchmark()
    {
        var root = Fixture(9000);
        Equivalent(root, condition => { if (!condition) throw new Exception("Benchmark coverage differs."); });
        var times = Enumerable.Range(0, 4).Select(_ => new List<double>()).ToArray();
        var allocations = Enumerable.Range(0, 4).Select(_ => new List<long>()).ToArray();
        int objects = 0, slots = 0;
        for (int run = 0; run < 6; run++)
        {
            // Alternate order to avoid giving one backend all of the later warmed-up runs.
            foreach (int backend in run % 2 == 0 ? new[] { 0, 1, 2, 3 } : new[] { 3, 2, 1, 0 })
            {
                if (backend >= 2) SnapshotFieldReader.ClearCache();
                if (backend == 3)
                {
                    using var warm = Capture(root, false);
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
                long startBytes = GC.GetAllocatedBytesForCurrentThread();
                var timer = Stopwatch.StartNew();
                using var graph = CaptureWithReaders(root, backend == 0, backend >= 2);
                if (graph.Differences().Count != 0) throw new Exception("Benchmark verification failed.");
                timer.Stop();
                objects = graph.ObjectCount;
                slots = graph.SlotCount;
                if (run > 0)
                {
                    times[backend].Add(timer.Elapsed.TotalMilliseconds);
                    allocations[backend].Add(GC.GetAllocatedBytesForCurrentThread() - startBytes);
                }
            }
        }
        string[] names = { "legacy", "cached-reflection", "compiled-cold", "compiled-warm" };
        for (int backend = 0; backend < 4; backend++)
        {
            times[backend].Sort(); allocations[backend].Sort();
            Console.WriteLine($"BENCH {names[backend]} objects={objects} slots={slots} " +
                $"capture+verify median={times[backend][2]:0.0}ms allocated={allocations[backend][2] / 1048576.0:0.00}MiB " +
                "(NET6 synthetic graph; same StateGraph; cold per-snapshot schema; code cache as labelled)");
        }
    }
}
