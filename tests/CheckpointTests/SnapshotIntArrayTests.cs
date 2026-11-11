using System.Collections;
using System.Reflection;
using ProbablyStolenTools;

internal static class SnapshotIntArrayTests
{
    private sealed class Holder
    {
        internal int[] Values;
        internal object Alias;
    }
    private static ManagedStateAccess Access(bool compact)
        => new(null, typeof(Holder).Assembly.GetName().Name, true, true, true, compact);
    private static StateGraph Capture(object root, bool compact, params string[] fields)
    {
        var access = Access(compact);
        var id = ManagedObjects.Id(root); access.AddRoot(id, fields);
        return StateGraph.Capture(access, new[] { id });
    }
    private static StateGraph CaptureOracle(object root)
    {
        var access = new LegacyManagedStateAccess(gameAssembly: typeof(Holder).Assembly.GetName().Name);
        var id = ManagedObjects.Id(root); access.AddRoot(id);
        return StateGraph.Capture(access, new[] { id });
    }
    private static object Field(object target, string name)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void SameCoverage(StateGraph expected, StateGraph actual, Action<bool> check)
    {
        check(expected.ObjectCount == actual.ObjectCount && expected.SlotCount == actual.SlotCount
            && expected.ByteCount == actual.ByteCount && expected.Objects.ToHashSet().SetEquals(actual.Objects));
        foreach (var id in expected.Objects) check(expected.SourceOf(id) == actual.SourceOf(id));
    }

    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("int array blocks restore all elements and preserve exact identities budgets and paths", () =>
        {
            var values = new[] { int.MinValue, -19, 0, 91, int.MaxValue };
            var expected = (int[])values.Clone();
            var holder = new Holder { Values = values, Alias = values };
            var root = new object[] { holder, values, values };
            using var oracle = Capture(root, false);
            using var recursive = CaptureOracle(root);
            using var graph = Capture(root, true);
            SameCoverage(oracle, graph, check); check(graph.BlockCount == 1);
            for (int i = 0; i < 4; i++)
            {
                Array.Fill(values, 7);
                holder.Values = new int[100]; holder.Alias = null; root[1] = null;
                graph.Restore();
                check(values.SequenceEqual(expected) && ReferenceEquals(holder.Values, values)
                    && ReferenceEquals(holder.Alias, values) && ReferenceEquals(root[1], values)
                    && graph.Differences().Count == 0 && oracle.Differences().Count == 0
                    && recursive.Differences().Count == 0);
            }
        });
        test("int array blocks retain independent clones for equal shapes and repeated past future restores", () =>
        {
            var first = new[] { 7, 19, 31 }; var second = new[] { -3, -8, -11 };
            var root = new[] { first, second };
            using var past = Capture(root, true);
            first[0] = 91; second[2] = 92;
            using var future = Capture(root, true);
            using var oracle = Capture(root, false);
            for (int i = 0; i < 5; i++)
            {
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                past.Restore(); check(first[0] == 7 && second[2] == -11 && past.Differences().Count == 0);
                future.Restore(); check(first[0] == 91 && second[2] == 92 && oracle.Differences().Count == 0);
            }
        });
        test("int block differences read all elements and count beyond diagnostic limits", () =>
        {
            var values = Enumerable.Range(1, 40).ToArray();
            using var graph = Capture(values, true);
            Array.Fill(values, -1);
            check(graph.Differences(0).Count == 0 && graph.Differences(3).Count == 3);
            var plan = graph.PrepareReconciliation();
            check(plan.Mode == "owners" && plan.ChangedSlots == 40 && plan.PlannedSlots == 40
                && plan.Differences.Count == 12);
            plan.Apply();
            check(graph.VerifyReconciliation(plan, () => { }, out var fallback).Count == 0 && !fallback
                && values.SequenceEqual(Enumerable.Range(1, 40)));
        });
        test("int block diagnostics preserve array indices owner sources and captured slot visitation", () =>
        {
            var values = new[] { 7, 19, 31 };
            var holder = new Holder { Values = values };
            using var oracle = Capture(holder, false);
            using var graph = Capture(holder, true);
            var expected = new List<(IntPtr, StateSlot, IntPtr)>();
            var actual = new List<(IntPtr, StateSlot, IntPtr)>();
            oracle.VisitCapturedSlots((o, s, r) => expected.Add((o, s, r)));
            graph.VisitCapturedSlots((o, s, r) => actual.Add((o, s, r)));
            check(expected.Count == actual.Count && expected.All(actual.Contains));
            values[2] = -1;
            check(graph.Differences().Single() == "System.Int32[][2]");
            var detail = graph.DifferencesWithSources().Single();
            check(detail.Contains("storage=block") && detail.Contains("field=System.Int32[][2]")
                && detail.Contains("source=root -> ") && detail.Contains(".Values"));
            graph.Restore(); check(oracle.Differences().Count == 0);
        });
        test("empty and non-int arrays keep ordinary descriptions and payloads", () =>
        {
            var root = new object[] { Array.Empty<int>(), new byte[] { 1, 2 }, new long[] { 7, 19 },
                new uint[] { 11, 23 }, new int?[] { null, 31 }, new[] { (Number: 7, Link: new object()) } };
            using var oracle = Capture(root, false);
            using var graph = Capture(root, true);
            SameCoverage(oracle, graph, check); check(graph.BlockCount == 0);
            ((byte[])root[1])[1] = 99; ((int?[])root[4])[0] = 7;
            graph.Restore(); check(oracle.Differences().Count == 0);
        });
        test("selected int array roots fall back without expanding a new payload surface", () =>
        {
            var values = new[] { 7, 19 };
            using var oracle = Capture(values, false, "selected");
            using var graph = Capture(values, true, "selected");
            SameCoverage(oracle, graph, check); check(graph.BlockCount == 0);
            values[1] = 91; graph.Restore(); check(values[1] == 19);
        });
        test("int array payload capture happens only after complete preallocation budget checks", () =>
        {
            foreach (bool compact in new[] { false, true })
                foreach (string limit in new[] { "slot", "byte" })
                {
                    var values = new int[200000];
                    var access = Access(compact);
                    var id = ManagedObjects.Id(values); access.AddRoot(id);
                    bool rejected = false;
                    try
                    {
                        using var graph = StateGraph.Capture(access, new[] { id }, 10,
                            limit == "slot" ? 199999 : 200000, limit == "byte" ? 1599999 : 1600000);
                    }
                    catch (SnapshotLimitException e) { rejected = e.Message.Contains("before expansion"); }
                    check(rejected && access.IntArrayBlockCount == 0
                        && ((IDictionary)Field(access, "_retained")).Count == 0);
                }
        });
        test("int array logical budgets remain eight bytes per slot including exact limits", () =>
        {
            var values = new[] { 7, 19, 31 };
            var access = Access(true);
            var id = ManagedObjects.Id(values); access.AddRoot(id);
            using var graph = StateGraph.Capture(access, new[] { id }, 1, 3, 24);
            check(graph.SlotCount == 3 && graph.ByteCount == 24 && graph.BlockCount == 1
                && access.IntArrayBlockCount == 1 && access.IntArraySlotCount == 3);
            values[2] = -1; graph.Restore(); check(values[2] == 31);
        });
        test("int payload mode preserves unsupported array failures before constructing a block", () =>
        {
            foreach (Array values in new Array[] { new int[2, 3],
                Array.CreateInstance(typeof(int), new[] { 3 }, new[] { 1 }), new int[262145] })
            {
                var failures = new List<string>();
                foreach (bool compact in new[] { false, true })
                {
                    var access = Access(compact);
                    var id = ManagedObjects.Id(values); access.AddRoot(id);
                    try { using var graph = StateGraph.Capture(access, new[] { id }); }
                    catch (InvalidOperationException e) { failures.Add(e.Message); }
                    check(access.IntArrayBlockCount == 0 && access.IntArraySlotCount == 0);
                }
                check(failures.Count == 2 && failures[0] == "Unsupported snapshot array."
                    && failures[0] == failures[1]);
            }
        });
        test("later int payload capture failure cannot change or invalidate an older retained block", () =>
        {
            var values = new[] { 7, 19 };
            using var past = Capture(values, true);
            var root = new object[] { values, new int[100] };
            var access = Access(true);
            var id = ManagedObjects.Id(root); access.AddRoot(id);
            bool rejected = false;
            try { using var graph = StateGraph.Capture(access, new[] { id }, 10, 10, 1000); }
            catch (SnapshotLimitException) { rejected = true; }
            check(rejected && access.IntArrayBlockCount == 0 && access.IntArraySlotCount == 0);
            values[0] = -1;
            past.Restore(); check(values[0] == 7 && past.Differences().Count == 0);
        });
        test("census ignores int array block configuration and still probes all original slots", () =>
        {
            var root = new object[] { new[] { 7, 19, 31 }, new[] { -1, -8 } };
            using var graph = Capture(root, true);
            var access = Access(true);
            var id = ManagedObjects.Id(root); access.AddRoot(id);
            using var census = StateGraph.Census(access, new[] { id });
            SameCoverage(graph, census, check);
            check(census.BlockCount == 0 && access.IntArrayBlockCount == 0
                && ((IDictionary)Field(access, "_values")).Count == 0);
        });
        test("complete int owners obey repair slot bounds and full fallback restores every element", () =>
        {
            var values = Enumerable.Range(1, 20).ToArray();
            using var graph = Capture(values, true);
            values[19] = -1;
            var plan = graph.PrepareReconciliation(maxSlots: 19);
            check(plan.Mode == "full" && plan.Reason == "slot-limit" && plan.ChangedSlots == 1);
            plan.Apply();
            check(graph.VerifyReconciliation(plan, () => { }, out var fallback).Count == 0 && !fallback
                && values[19] == 20);
        });
        test("int array owner repair final full fallback retains scalar and reference restoration", () =>
        {
            var values = new[] { 7, 19 };
            var holder = new Holder { Values = values, Alias = values };
            using var graph = Capture(holder, true);
            using var oracle = Capture(holder, false);
            values[1] = -1;
            var plan = graph.PrepareReconciliation();
            plan.Apply();
            values[0] = 91; holder.Values = new int[7];
            int callbacks = 0;
            check(graph.VerifyReconciliation(plan, () => callbacks++, out var fallback).Count == 0
                && fallback && callbacks == 1 && ReferenceEquals(holder.Values, values)
                && values[0] == 7 && values[1] == 19 && oracle.Differences().Count == 0);
        });
        test("int payloads preserve dictionary storage aliases views active enumerators and unused buckets", () =>
        {
            var map = new Dictionary<int, Holder>(31);
            for (int i = 0; i < 12; i++) map.Add(i, new Holder { Values = new[] { i, i + 100 } });
            map.Remove(3); map.Remove(7);
            var buckets = (int[])Field(map, "_buckets");
            var entries = Field(map, "_entries");
            var savedBuckets = (int[])buckets.Clone();
            var enumerator = map.GetEnumerator(); enumerator.MoveNext();
            var root = new object[] { map, buckets, entries, map.Keys, map.Values, enumerator };
            using var oracle = Capture(root, false);
            using var recursive = CaptureOracle(root);
            using var graph = Capture(root, true);
            SameCoverage(oracle, graph, check); check(graph.BlockCount == 11);
            for (int round = 0; round < 3; round++)
            {
                map.Remove(1); map[2].Values[0] = -1;
                for (int i = 20; i < 150; i++) map[i] = new Holder { Values = new[] { -i } };
                Array.Fill(buckets, -7);
                var plan = graph.PrepareReconciliation();
                check(plan.Mode == "owners");
                plan.Apply();
                check(graph.VerifyReconciliation(plan, () => { }, out var fallback).Count == 0 && !fallback);
                check(map.Count == 10 && map[2].Values[0] == 2 && ReferenceEquals(Field(map, "_buckets"), buckets)
                    && ReferenceEquals(Field(map, "_entries"), entries) && buckets.SequenceEqual(savedBuckets)
                    && oracle.Differences().Count == 0 && recursive.Differences().Count == 0);
                var restoredEnumerator = (Dictionary<int, Holder>.Enumerator)root[5];
                check(restoredEnumerator.MoveNext());
            }
        });
        test("int array identity graph detects new references even when replacement contents match", () =>
        {
            var values = new[] { 7, 19 };
            var holder = new Holder { Values = values };
            using var graph = Capture(holder, true);
            holder.Values = (int[])values.Clone();
            check(graph.Differences().Count == 1);
            var plan = graph.PrepareReconciliation();
            plan.Apply(); check(ReferenceEquals(holder.Values, values) && graph.Differences().Count == 0);
        });
        test("retained int blocks do not depend on later payload factory availability", () =>
        {
            var values = new[] { 7, 19 };
            var access = Access(true);
            var id = ManagedObjects.Id(values); access.AddRoot(id);
            var graph = StateGraph.Capture(access, new[] { id });
            access.BlockCapture = (_, _) => throw new InvalidOperationException("must not recapture");
            values[0] = 91; graph.Restore(); check(values[0] == 7 && graph.Differences().Count == 0);
            graph.Dispose(); graph.Dispose();
            check(access.IntArrayBlockCount == 0 && access.IntArraySlotCount == 0
                && ((IDictionary)Field(access, "_retained")).Count == 0);
            bool rejected = false;
            try { graph.Restore(); } catch (ObjectDisposedException) { rejected = true; }
            check(rejected);
        });
        test("seeded mixed int payload graphs remain identical to ordinary checkpoints through growth", () =>
        {
            var random = new Random(71219);
            var holders = Enumerable.Range(0, 17).Select(_ => new Holder
                { Values = Enumerable.Range(0, random.Next(1, 20)).Select(_ => random.Next()).ToArray() }).ToArray();
            holders[1].Values = holders[0].Values;
            var root = new object[] { holders, new List<Holder>(holders) };
            using var oracle = Capture(root, false);
            using var recursive = CaptureOracle(root);
            using var graph = Capture(root, true);
            for (int round = 0; round < 30; round++)
            {
                foreach (var holder in holders)
                {
                    for (int i = 0; i < holder.Values.Length; i++) holder.Values[i] = random.Next();
                    holder.Alias = new object();
                }
                ((List<Holder>)root[1]).Add(new Holder { Values = new int[100] });
                if (round % 2 == 0) graph.Restore();
                else
                {
                    var plan = graph.PrepareReconciliation();
                    plan.Apply();
                    check(graph.VerifyReconciliation(plan, () => { }, out var fallback).Count == 0 && !fallback);
                }
                check(oracle.Differences().Count == 0 && recursive.Differences().Count == 0 && graph.Differences().Count == 0
                    && ReferenceEquals(holders[1].Values, holders[0].Values));
            }
        });
    }
}
