using System.Collections;
using System.Reflection;
using ProbablyStolenTools;

internal static class SnapshotCensusValueTests
{
    private class Node
    {
        internal int Number;
        internal bool Enabled;
        internal object Link;
    }
    private sealed class Derived : Node { internal int Extra; }

    private static ManagedStateAccess Access()
        => new(gameAssembly: typeof(Node).Assembly.GetName().Name);
    private static bool Probe(object target, IReadOnlyList<StateSlot> slots)
    {
        if (target.GetType() != typeof(Node) || slots.Count != 3) return false;
        var node = (Node)target;
        GC.KeepAlive((node.Number, node.Enabled)); return true;
    }
    private static StateGraph Walk(object root, ManagedStateAccess access, bool capture, params string[] fields)
    {
        var id = ManagedObjects.Id(root); access.AddRoot(id, fields);
        return capture ? StateGraph.Capture(access, new[] { id }) : StateGraph.Census(access, new[] { id });
    }
    private static int Tokens(ManagedStateAccess access)
        => ((IDictionary)typeof(ManagedStateAccess).GetField("_savedValues",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(access)).Count;

    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("typed census scalar probes preserve all reference identities source paths and logical budgets", () =>
        {
            var first = new Node { Number = 7, Enabled = true }; var second = new Node { Number = 31 };
            first.Link = second; second.Link = first;
            var root = new object[] { first, second, first, null };
            using var ordinaryAccess = Access(); using var graph = Walk(root, ordinaryAccess, true);
            var access = Access(); access.CensusValuesProbe = Probe;
            using (var census = Walk(root, access, false))
            {
                check(census.ObjectCount == graph.ObjectCount && census.SlotCount == graph.SlotCount
                    && census.ByteCount == graph.ByteCount && census.Objects.ToHashSet().SetEquals(graph.Objects));
                foreach (var id in graph.Objects) check(graph.SourceOf(id) == census.SourceOf(id));
                check(access.CensusValueObjectCount == 2 && census.BlockCount == 0 && Tokens(access) == 0);
                check(first.Number == 7 && second.Number == 31 && ReferenceEquals(second.Link, first)
                    && graph.Differences().Count == 0);
            }
            check(access.CensusValueObjectCount == 0 && access.CensusValuesProbe == null);
        });
        test("capture never invokes census-only scalar probes or replaces checkpoint payloads", () =>
        {
            var item = new Node { Number = 7 };
            var access = Access();
            access.CensusValuesProbe = (_, _) => throw new Exception("capture must not call census reader");
            using var graph = Walk(item, access, true);
            item.Number = -1; graph.Restore();
            check(item.Number == 7 && graph.Differences().Count == 0 && access.CensusValueObjectCount == 0);
        });
        test("census subclasses mismatched descriptions and filtered roots retain ordinary field reads", () =>
        {
            var derived = new Derived { Number = 7, Extra = 19 };
            var selected = new Node { Number = 31 };
            var access = Access(); access.CensusValuesProbe = Probe;
            var a = ManagedObjects.Id(derived); var b = ManagedObjects.Id(selected);
            access.AddRoot(a); access.AddRoot(b, "Number");
            using var census = StateGraph.Census(access, new[] { a, b });
            check(census.SlotCount == 5 && access.CensusValueObjectCount == 0 && Tokens(access) == 0);
            var mismatch = Access();
            mismatch.CensusValuesProbe = (_, slots) => slots.Count == 99;
            using var fallback = Walk(selected, mismatch, false);
            check(fallback.SlotCount == 3 && mismatch.CensusValueObjectCount == 0);
        });
        test("typed census probes enforce exact scalar pointer budgets and object discovery limits", () =>
        {
            var node = new Node { Number = 7 };
            long bytes = 16 + IntPtr.Size;
            foreach (string limit in new[] { "accepted", "slots", "bytes", "objects" })
            {
                var access = Access(); access.CensusValuesProbe = Probe;
                var id = ManagedObjects.Id(node); access.AddRoot(id);
                node.Link = limit == "objects" ? new Node() : null;
                bool rejected = false;
                try
                {
                    using var census = StateGraph.Census(access, new[] { id }, 1,
                        limit == "slots" ? 2 : 3, limit == "bytes" ? bytes - 1 : bytes);
                    check(census.ByteCount == bytes && census.SlotCount == 3 && Tokens(access) == 0);
                }
                catch (SnapshotLimitException) { rejected = true; }
                check(rejected == (limit != "accepted") && access.CensusValueObjectCount == 0 && Tokens(access) == 0);
            }
        });
        test("late typed census read failures propagate release partial census and preserve retained checkpoints", () =>
        {
            var items = Enumerable.Range(0, 40).Select(i => new Node { Number = i }).ToArray();
            using var savedAccess = Access(); using var saved = Walk(items, savedAccess, true);
            var access = Access();
            int reads = 0;
            access.CensusValuesProbe = (target, slots) =>
            {
                if (target is Node node)
                {
                    reads++;
                    if (node.Number == 39) throw new InvalidOperationException("late probe failure");
                }
                return Probe(target, slots);
            };
            bool failed = false;
            try { using var census = Walk(items, access, false); }
            catch (InvalidOperationException e) { failed = e.Message == "late probe failure"; }
            check(failed && reads == 40 && Tokens(access) == 0 && access.CensusValueObjectCount == 0);
            items[39].Number = -1; saved.Restore(); check(items[39].Number == 39 && saved.Differences().Count == 0);
        });
        test("census without probe support retains legacy per-slot behavior", () =>
        {
            var node = new Node { Number = 7, Enabled = true, Link = new Node() };
            using var access = Access(); using var census = Walk(node, access, false);
            check(census.SlotCount == 6 && access.CensusValueObjectCount == 0 && Tokens(access) == 0);
        });
    }
}
