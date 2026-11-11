using ProbablyStolenTools;

internal static class FieldEntityWriterTests
{
    private enum Mode : long { First = 1 }
    private class Base { public long Price = 7000000001; public Action Callback; }
    private class Item : Base
    {
        public int Count = 3;
        public float Fraction = -0.0f;
        public double Exact = BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000001234));
        public Mode Style = (Mode)long.MinValue;
        public string Name = "original";
        public Item Link;
        public List<Item> Members = new();
        public int Layout = 41;
        public int GameplayGetter => throw new Exception("Gameplay getter must not run.");
    }
    private sealed class Derived : Item { public int Extra = 7; }
    private struct Custom { public int Value; }
    private sealed class Unsupported { public Custom State = new() { Value = 7 }; }
    private sealed class Writer : SemanticFieldEntityWriter
    {
        internal Writer(Type type, params SemanticEntityFieldPlan[] plans) : base(type, plans) { }
        internal void CaptureFrom(StateGraph graph) => CaptureRecords(graph, 100, 1000, 8000);
        internal static Writer Capture(StateGraph graph, int maxObjects = 100, int maxSlots = 1000, long maxBytes = 8000)
        {
            var writer = new Writer(typeof(Item), Plans());
            writer.CaptureRecords(graph, maxObjects, maxSlots, maxBytes);
            return writer;
        }
    }
    private static SemanticEntityFieldPlan[] Plans() => new[]
    {
        new SemanticEntityFieldPlan(typeof(Item),
            new("Count", typeof(int), SemanticFieldRole.Entity),
            new("Fraction", typeof(float), SemanticFieldRole.Entity),
            new("Exact", typeof(double), SemanticFieldRole.Entity),
            new("Style", typeof(Mode), SemanticFieldRole.Entity),
            new("Name", typeof(string), SemanticFieldRole.Entity),
            new("Link", typeof(Item), SemanticFieldRole.Entity),
            new("Members", typeof(List<Item>), SemanticFieldRole.Entity),
            new("Layout", typeof(int), SemanticFieldRole.Visual)),
        new SemanticEntityFieldPlan(typeof(Base),
            new("Price", typeof(long), SemanticFieldRole.Entity),
            new("Callback", typeof(Action), SemanticFieldRole.Runtime))
    };
    private static StateGraph Capture(params Item[] items)
    {
        var access = new ManagedStateAccess(null, typeof(Item).Assembly.GetName().Name);
        var fields = Plans().SelectMany(plan => plan.RequiredSlots).Select(name => name[(name.LastIndexOf('.') + 1)..]).ToArray();
        foreach (var item in items) access.AddRoot(ManagedObjects.Id(item),
            item is Derived ? fields.Append("Extra").ToArray() : fields);
        return StateGraph.Capture(access, items.Select(ManagedObjects.Id));
    }
    private static void Throws(Action action, Action<bool> check)
    {
        try { action(); throw new Exception("Expected field writer rejection."); }
        catch (InvalidOperationException) { check(true); }
    }

    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("field entity writer restores full inherited business fields with runtime and collection storage on old graph", () =>
        {
            Action callback = () => throw new Exception("Do not invoke callbacks.");
            var first = new Item { Callback = callback };
            var second = new Item { Count = 9, Link = first };
            first.Link = second; first.Members.Add(first); first.Members.Add(second);
            var originalList = first.Members;
            using var graph = Capture(first, second);
            using var writer = Writer.Capture(graph);
            var plan = graph.PrepareEntityRestore(writer, 100, 1000);
            for (int i = 0; i < 6; i++)
            {
                first.Count = 81; first.Price = -1; first.Name = new string("original".ToCharArray());
                first.Link = first; originalList.Clear(); originalList.Add(first); first.Members = new();
                first.Callback = null; first.Layout = 99; second.Count = 82;
                plan.Apply(graph);
                check(graph.Differences().Count == 0 && ReferenceEquals(first.Link, second)
                    && ReferenceEquals(first.Members, originalList) && originalList.Count == 2
                    && ReferenceEquals(first.Callback, callback) && first.Layout == 99);
            }
        });
        test("field entity writer preserves float double NaN signed zero full enum and wide integer values", () =>
        {
            var item = new Item();
            using var graph = Capture(item);
            using var writer = Writer.Capture(graph);
            var plan = graph.PrepareEntityRestore(writer, 100, 1000);
            item.Fraction = 0.0f; item.Exact = double.NaN; item.Style = Mode.First; item.Price = 1;
            plan.Apply(graph);
            check(BitConverter.SingleToInt32Bits(item.Fraction) == unchecked((int)0x80000000)
                && BitConverter.DoubleToInt64Bits(item.Exact) == unchecked((long)0x7ff8000000001234)
                && item.Style == (Mode)long.MinValue && item.Price == 7000000001 && graph.Differences().Count == 0);
        });
        test("field entity writer admits exact types only and leaves subclass state to legacy", () =>
        {
            var item = new Item();
            var derived = new Derived();
            using var graph = Capture(item, derived);
            using var writer = Writer.Capture(graph);
            check(writer.Owners.Count == 1);
            derived.Count = 71; derived.Extra = 72; item.Count = 81;
            writer.RestoreValues(); writer.RestoreReferences();
            check(item.Count == 3 && derived.Count == 71 && derived.Extra == 72);
            graph.PrepareEntityRestore(writer, 100, 1000).Apply(graph);
            check(derived.Count == 3 && derived.Extra == 7 && graph.Differences().Count == 0);
        });
        test("field entity writer object slot and logical byte boundaries are checked before oversized records", () =>
        {
            var item = new Item();
            using var graph = Capture(item);
            using var exact = Writer.Capture(graph, 1, 8, 5 * 8 + 3 * IntPtr.Size);
            check(exact.LogicalSlots == 8 && exact.LogicalBytes == 5 * 8 + 3 * IntPtr.Size);
            Throws(() => Writer.Capture(graph, 0), check);
            Throws(() => Writer.Capture(graph, 1, 7), check);
            Throws(() => Writer.Capture(graph, 1, 8, exact.LogicalBytes - 1), check);
            check(graph.Differences().Count == 0);
        });
        test("field entity writer rejects changed graph and references outside original coverage", () =>
        {
            var item = new Item();
            using var graph = Capture(item);
            item.Count = 81;
            Throws(() => Writer.Capture(graph), check);
            graph.Restore();
            item.Link = new Item();
            Throws(() => Writer.Capture(graph), check);
            graph.Restore();
            check(graph.Differences().Count == 0);
        });
        test("field entity writer disposal rejects before ordinary or other entity writes", () =>
        {
            var item = new Item();
            using var graph = Capture(item);
            var writer = Writer.Capture(graph);
            var plan = graph.PrepareEntityRestore(writer, 100, 1000);
            writer.Dispose(); item.Count = 81; item.Price = -1; item.Callback = () => { };
            var changedCallback = item.Callback;
            try { plan.Apply(graph); throw new Exception("Disposed field writer accepted."); }
            catch (ObjectDisposedException) { check(true); }
            check(item.Count == 81 && item.Price == -1 && ReferenceEquals(item.Callback, changedCallback));
            graph.Restore();
            check(graph.Differences().Count == 0);
        });
        test("field entity writer snapshots keep separate past future payloads after old graphs are disposed", () =>
        {
            var item = new Item();
            var firstGraph = Capture(item);
            using var past = Writer.Capture(firstGraph);
            item.Count = 81; item.Name = "future";
            var secondGraph = Capture(item);
            using var future = Writer.Capture(secondGraph);
            firstGraph.Dispose(); secondGraph.Dispose();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            past.RestoreValues(); past.RestoreReferences();
            check(item.Count == 3 && item.Name == "original");
            future.RestoreValues(); future.RestoreReferences();
            check(item.Count == 81 && item.Name == "future");
        });
        test("field entity writer rejects incomplete inheritance plans and filtered runtime coverage", () =>
        {
            var item = new Item();
            using var graph = Capture(item);
            using var incomplete = new Writer(typeof(Item), Plans()[0]);
            incomplete.CaptureFrom(graph);
            // A complete writer prepared against an incomplete root must also fail coverage.
            var access = new ManagedStateAccess(null, typeof(Item).Assembly.GetName().Name);
            access.AddRoot(ManagedObjects.Id(item), Plans().SelectMany(plan => plan.RequiredSlots)
                .Select(name => name[(name.LastIndexOf('.') + 1)..]).Where(name => name != "Callback").ToArray());
            using var filtered = StateGraph.Capture(access, new[] { ManagedObjects.Id(item) });
            using var writer = Writer.Capture(filtered);
            Throws(() => filtered.PrepareEntityRestore(writer, 100, 1000), check);
            Throws(() => graph.PrepareEntityRestore(incomplete, 100, 1000), check);
        });
        test("field entity writer rejects unsupported scalars and unrelated or duplicate manifests", () =>
        {
            var unsupported = new SemanticEntityFieldPlan(typeof(Unsupported),
                new SemanticFieldRule("State", typeof(Custom), SemanticFieldRole.Entity));
            Throws(() => new Writer(typeof(Unsupported), unsupported), check);
            Throws(() => new Writer(typeof(Item), Plans()[0], Plans()[0]), check);
            Throws(() => new Writer(typeof(Item), Plans()[0], unsupported), check);
            Throws(() => new Writer(typeof(Item), Array.Empty<SemanticEntityFieldPlan>()), check);
        });
        test("field entity writer plans cannot cross snapshots even when original identities match", () =>
        {
            var item = new Item();
            using var first = Capture(item);
            using var writer = Writer.Capture(first);
            var plan = first.PrepareEntityRestore(writer, 100, 1000);
            item.Count = 81;
            using var future = Capture(item);
            Throws(() => plan.Apply(future), check);
            check(item.Count == 81);
            plan.Apply(first);
            check(first.Differences().Count == 0 && future.Differences().Count != 0);
        });
        test("field entity writers reject recapture without erasing existing payloads including empty captures", () =>
        {
            var item = new Item();
            using var graph = Capture(item);
            using var writer = Writer.Capture(graph);
            item.Count = 81;
            Throws(() => writer.CaptureFrom(graph), check);
            check(writer.Owners.Count == 1);
            writer.RestoreValues(); writer.RestoreReferences();
            check(item.Count == 3 && graph.Differences().Count == 0);
            using var empty = Capture();
            using var emptyWriter = Writer.Capture(empty);
            Throws(() => emptyWriter.CaptureFrom(empty), check);
            check(emptyWriter.Owners.Count == 0);
        });
    }
}
