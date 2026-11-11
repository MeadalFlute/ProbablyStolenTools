using System.Collections;
using System.Reflection;
using ProbablyStolenTools;

internal static class SnapshotBlockTests
{
    private class Node
    {
        internal int Number;
        internal bool Flag;
        internal Node Link;
    }
    private sealed class Derived : Node { internal int Extra; }
    private sealed class Block : IStateBlock
    {
        private readonly Node _target;
        private readonly int _number;
        private readonly bool _flag;
        private readonly Node _link;
        private readonly IntPtr _reference;
        internal int Reads, Writes;
        internal bool FailRead, FailWrite;
        internal Action BeforeReferenceWrite;
        internal Block(Node target)
        {
            _target = target; _number = target.Number; _flag = target.Flag; _link = target.Link;
            _reference = ManagedObjects.Id(_link);
        }
        public IntPtr ReferenceAt(StateSlot slot) => _reference;
        public void RestoreValues()
        {
            if (FailWrite) throw new InvalidOperationException("block write");
            Writes++; _target.Number = _number; _target.Flag = _flag;
        }
        public void RestoreReferences()
        {
            BeforeReferenceWrite?.Invoke();
            Writes++; _target.Link = _link;
        }
        public void CollectDifferences(List<string> result, int max)
        {
            void Add(bool equal, string name)
            {
                Reads++;
                if (FailRead) throw new InvalidOperationException("block read");
                if (!equal && result.Count < max) result.Add(typeof(Node).FullName + "." + name);
            }
            Add(_target.Number == _number, "Number");
            Add(_target.Flag == _flag, "Flag");
            Add(ReferenceEquals(_target.Link, _link), "Link");
        }
    }
    private static ManagedStateAccess Access(List<Block> blocks = null)
    {
        var access = new ManagedStateAccess(gameAssembly: typeof(Node).Assembly.GetName().Name);
        if (blocks != null)
            access.BlockCapture = (target, slots) =>
            {
                if (target.GetType() != typeof(Node)) return null;
                var block = new Block((Node)target); blocks.Add(block); return block;
            };
        return access;
    }
    private static StateGraph Capture(object root, List<Block> blocks = null)
    {
        var access = Access(blocks);
        var id = ManagedObjects.Id(root); access.AddRoot(id);
        return StateGraph.Capture(access, new[] { id });
    }
    private static List<string> Topology(StateGraph graph)
    {
        var slots = new List<string>();
        graph.VisitCapturedSlots((owner, slot, link) => slots.Add($"{owner}:{slot}:{link}"));
        slots.Sort(StringComparer.Ordinal);
        return slots;
    }
    private static void Throws<T>(Action action, Action<bool> check) where T : Exception
    {
        bool failed = false;
        try { action(); } catch (T) { failed = true; }
        check(failed);
    }
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("compact blocks preserve mixed graph identities paths topology and budgets", () =>
        {
            var a = new Node { Number = 3, Flag = true };
            var b = new Node { Number = 7, Link = a }; a.Link = b;
            var map = new Dictionary<string, Node> { ["a"] = a, ["alias"] = a, ["b"] = b };
            var root = new object[] { map, a, b, map.Keys, map.Values, map.GetEnumerator() };
            using var legacy = Capture(root);
            var blocks = new List<Block>();
            using var compact = Capture(root, blocks);
            check(compact.BlockCount == 2 && compact.ObjectCount == legacy.ObjectCount
                && compact.SlotCount == legacy.SlotCount && compact.ByteCount == legacy.ByteCount);
            check(Topology(compact).SequenceEqual(Topology(legacy)));
            foreach (var id in legacy.Objects)
                check(compact.Contains(id) && compact.SourceOf(id) == legacy.SourceOf(id)
                    && compact.IsCapturedRoot(id) == legacy.IsCapturedRoot(id));
            a.Number = 99; a.Flag = false; a.Link = null; b.Link = b;
            for (int i = 0; i < 100; i++) map["new" + i] = new Node();
            check(compact.Differences().Count > 0);
            compact.Restore();
            check(legacy.Differences().Count == 0 && compact.Differences().Count == 0);
            check(ReferenceEquals(a.Link, b) && ReferenceEquals(b.Link, a));
        });
        test("compact references discover otherwise unreachable targets and enforce discovery budget", () =>
        {
            var a = new Node { Link = new Node { Link = new Node() } };
            using var graph = Capture(a, new List<Block>());
            check(graph.ObjectCount == 3 && graph.BlockCount == 3);
            var access = Access(new List<Block>());
            var id = ManagedObjects.Id(a); access.AddRoot(id);
            Throws<SnapshotLimitException>(() => StateGraph.Capture(access, new[] { id }, maxObjects: 2), check);
            check(access.BlockCapture == null && a.Link.Link != null);
        });
        test("compact slot and byte boundaries match the legacy logical budget", () =>
        {
            var a = new Node();
            using var expected = Capture(a);
            foreach (bool compact in new[] { false, true })
                foreach (bool bytes in new[] { false, true })
                    foreach (bool fits in new[] { false, true })
                    {
                        var access = Access(compact ? new List<Block>() : null);
                        var id = ManagedObjects.Id(a); access.AddRoot(id);
                        int slots = expected.SlotCount - (!bytes && !fits ? 1 : 0);
                        long budget = expected.ByteCount - (bytes && !fits ? 1 : 0);
                        if (fits)
                        {
                            using var graph = StateGraph.Capture(access, new[] { id }, maxSlots: slots, maxBytes: budget);
                            check(graph.ByteCount == expected.ByteCount);
                        }
                        else Throws<SnapshotLimitException>(() => StateGraph.Capture(access,
                            new[] { id }, maxSlots: slots, maxBytes: budget), check);
                    }
        });
        test("compact mode preserves selected roots and falls back for subclasses", () =>
        {
            var a = new Node { Number = 1, Flag = true };
            var access = Access(new List<Block>());
            var id = ManagedObjects.Id(a); access.AddRoot(id, "Number");
            using var graph = StateGraph.Capture(access, new[] { id });
            check(graph.BlockCount == 0 && graph.SlotCount == 1);
            a.Number = 9; a.Flag = false; graph.Restore();
            check(a.Number == 1 && !a.Flag);
            var derived = new Derived { Number = 2, Extra = 5 };
            using var inherited = Capture(derived, new List<Block>());
            check(inherited.BlockCount == 0 && inherited.SlotCount == 4);
            derived.Extra = 9; derived.Number = 6; inherited.Restore();
            check(derived.Extra == 5 && derived.Number == 2);
        });
        test("census does not construct compact payload or retain tokens", () =>
        {
            var a = new Node { Number = 1, Link = new Node() };
            var blocks = new List<Block>(); var access = Access(blocks);
            var id = ManagedObjects.Id(a); access.AddRoot(id);
            using var graph = StateGraph.Census(access, new[] { id });
            using var legacy = Capture(a);
            check(blocks.Count == 0 && graph.BlockCount == 0 && graph.ByteCount == legacy.ByteCount
                && graph.SlotCount == legacy.SlotCount);
            var values = (IDictionary)typeof(ManagedStateAccess).GetField("_savedValues",
                BindingFlags.NonPublic | BindingFlags.Instance).GetValue(access);
            check(values.Count == 0);
            Throws<InvalidOperationException>(() => graph.Restore(), check);
        });
        test("compact differences scans all fields and all blocks beyond diagnostic cap", () =>
        {
            var nodes = Enumerable.Range(0, 20).Select(i => new Node()).ToArray();
            var blocks = new List<Block>();
            using var graph = Capture(nodes, blocks);
            foreach (var node in nodes) { node.Number = 8; node.Flag = true; node.Link = node; }
            check(graph.Differences(1).Count == 1 && blocks.Sum(b => b.Reads) == 60);
            blocks[^1].FailRead = true;
            Throws<InvalidOperationException>(() => graph.Differences(1), check);
            check(blocks.All(b => b.Writes == 0));
        });
        test("compact payload remains owned by each checkpoint after factory is disabled", () =>
        {
            var a = new Node { Number = 1 };
            var access = Access(new List<Block>());
            var id = ManagedObjects.Id(a); access.AddRoot(id);
            using var past = StateGraph.Capture(access, new[] { id });
            access.BlockCapture = null;
            a.Number = 7; a.Link = a;
            using var future = Capture(a);
            for (int i = 0; i < 3; i++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers();
                past.Restore(); check(a.Number == 1 && a.Link == null && past.Differences().Count == 0);
                future.Restore(); check(a.Number == 7 && ReferenceEquals(a.Link, a) && future.Differences().Count == 0);
            }
        });
        test("compact payload restores every scalar before any compact reference", () =>
        {
            var a = new Node { Number = 1 }; var b = new Node { Number = 2, Link = a };
            var blocks = new List<Block>(); using var graph = Capture(new[] { a, b }, blocks);
            a.Number = 8; b.Number = 9;
            foreach (var block in blocks) block.BeforeReferenceWrite = () => check(a.Number == 1 && b.Number == 2);
            graph.Restore(); check(graph.Differences().Count == 0);
        });
        test("compact capture exception releases partial graph and leaves existing checkpoint usable", () =>
        {
            var a = new Node { Number = 5 };
            using var old = Capture(a);
            var access = Access();
            access.BlockCapture = (_, _) => throw new InvalidOperationException("capture");
            var id = ManagedObjects.Id(a); access.AddRoot(id);
            Throws<InvalidOperationException>(() => StateGraph.Capture(access, new[] { id }), check);
            check(access.BlockCapture == null);
            a.Number = 8; old.Restore(); check(a.Number == 5);
        });
        test("compact write failure propagates without silent legacy retry and disposal rejects use", () =>
        {
            var a = new Node { Number = 1 };
            var blocks = new List<Block>(); var graph = Capture(a, blocks);
            a.Number = 5; blocks[0].FailWrite = true;
            Throws<InvalidOperationException>(() => graph.Restore(), check);
            check(a.Number == 5 && blocks[0].Writes == 0);
            blocks[0].FailWrite = false; graph.Restore(); check(a.Number == 1);
            graph.Dispose(); graph.Dispose(); check(graph.BlockCount == 0);
            Throws<ObjectDisposedException>(() => graph.Differences(), check);
            Throws<ObjectDisposedException>(() => graph.VisitCapturedSlots((_, _, _) => { }), check);
        });
    }
}
