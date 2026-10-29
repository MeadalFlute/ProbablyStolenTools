using ProbablyStolenTools;

internal static class ManagedSnapshotTests
{
    private sealed class Node
    {
        internal int Number;
        internal Node First, Second;
        internal List<Node> Items = new();
        internal Dictionary<string, Node> Map = new();
        internal (int Number, Node Link) Pair;
        internal Action Callback;
        internal int? Optional;
        internal float Fraction;
    }

    private static StateGraph Capture(object root, params string[] fields)
    {
        var access = new ManagedStateAccess(gameAssembly: typeof(Node).Assembly.GetName().Name);
        var id = ManagedObjects.Id(root);
        access.AddRoot(id, fields);
        return StateGraph.Capture(access, new[] { id });
    }

    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("managed cycle, shared links and original identity survive compacting GC", () =>
        {
            var root = new Node { Number = 1 };
            var child = new Node { Number = 2, First = root };
            root.First = root.Second = child;
            using var graph = Capture(root);
            var identity = ManagedObjects.Id(child);
            root.First = new Node(); child.Number = 30;
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            graph.Restore();
            check(ReferenceEquals(root.First, root.Second) && ReferenceEquals(root.First, child));
            check(child.Number == 2 && ReferenceEquals(child.First, root) && ManagedObjects.Id(child) == identity);
            check(graph.Differences().Count == 0);
        });
        test("managed list growth, dictionary deletion and insertion restore original backing storage", () =>
        {
            var first = new Node { Number = 7 };
            var root = new Node();
            root.Items.Add(first); root.Map["first"] = first;
            using var graph = Capture(root);
            root.Items.Clear();
            for (int i = 0; i < 30; i++) root.Items.Add(new Node { Number = i });
            root.Map.Remove("first"); root.Map["other"] = new Node(); first.Number = 90;
            graph.Restore();
            check(root.Items.Count == 1 && ReferenceEquals(root.Items[0], first));
            check(root.Map.Count == 1 && ReferenceEquals(root.Map["first"], first) && first.Number == 7);
            check(graph.Differences().Count == 0);
        });
        test("managed structs containing references restore nested values and links", () =>
        {
            var child = new Node { Number = 4 };
            var root = new Node { Pair = (8, child) };
            using var graph = Capture(root);
            root.Pair = (99, new Node()); child.Number = 40;
            graph.Restore();
            check(root.Pair.Number == 8 && ReferenceEquals(root.Pair.Link, child) && child.Number == 4);
            check(graph.Differences().Count == 0);
        });
        test("managed multicast callbacks retain identity and restore captured business state", () =>
        {
            var first = new Node { Number = 4 };
            var second = new Node { Number = 6 };
            Action callback = () => first.Number++;
            callback += () => second.Number++;
            var root = new Node { Callback = callback };
            using var graph = Capture(root);
            callback();
            root.Callback = () => { };
            graph.Restore();
            check(ReferenceEquals(root.Callback, callback) && first.Number == 4 && second.Number == 6);
            root.Callback();
            check(first.Number == 5 && second.Number == 7);
        });
        test("managed random generator repeats the same sequence after restore", () =>
        {
            var random = new Random(4701);
            using var graph = Capture(random);
            var expected = Enumerable.Range(0, 12).Select(_ => random.Next()).ToArray();
            graph.Restore();
            check(expected.SequenceEqual(Enumerable.Range(0, 12).Select(_ => random.Next())));
        });
        test("managed selected root fields leave excluded live state alone", () =>
        {
            var root = new Node { Number = 3, First = new Node { Number = 4 } };
            using var graph = Capture(root, nameof(Node.Number));
            root.Number = 9; root.First.Number = 44;
            graph.Restore();
            check(root.Number == 3 && root.First.Number == 44);
            bool failed = false;
            try { using var invalid = Capture(root, "missingField"); }
            catch (InvalidOperationException) { failed = true; }
            check(failed);
        });
        test("managed retained checkpoints restore back and forward without deleting futures", () =>
        {
            var root = new Node { Number = 1 };
            using var first = Capture(root);
            root.Number = 2;
            using var second = Capture(root);
            root.Number = 3;
            first.Restore(); check(root.Number == 1);
            second.Restore(); check(root.Number == 2);
            first.Restore(); check(root.Number == 1 && first.Differences().Count == 0);
        });
        test("managed disposed graphs reject restores", () =>
        {
            var root = new Node { Number = 1 };
            var graph = Capture(root);
            graph.Dispose();
            bool failed = false;
            try { graph.Restore(); }
            catch (ObjectDisposedException) { failed = true; }
            check(failed);
        });
        test("managed nullable scalars restore both present and absent states", () =>
        {
            var root = new Node { Optional = 13 };
            using var present = Capture(root);
            root.Optional = null;
            using var absent = Capture(root);
            present.Restore(); check(root.Optional == 13);
            absent.Restore(); check(root.Optional == null);
            present.Restore(); check(root.Optional == 13);
        });
        test("managed floating point snapshots preserve signed zero bit patterns", () =>
        {
            var root = new Node { Fraction = BitConverter.Int32BitsToSingle(int.MinValue) };
            using var negative = Capture(root);
            root.Fraction = 0f;
            check(negative.Differences().Count > 0);
            negative.Restore();
            check(BitConverter.SingleToInt32Bits(root.Fraction) == int.MinValue);
        });
    }
}
