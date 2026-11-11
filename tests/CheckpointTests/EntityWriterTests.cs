using ProbablyStolenTools;

internal static class EntityWriterTests
{
    private sealed class Item { public int Value = 1; public Item Link = null; }
    private sealed class Writer : IStateEntityWriter
    {
        private readonly Item _target, _link;
        private readonly int _value;
        internal Action ValidateAction, ValuesAction, ReferencesAction;
        public IReadOnlyCollection<IntPtr> Owners { get; set; }
        public IReadOnlyList<StateEntityField> Fields { get; set; }
        internal Writer(Item target)
        {
            _target = target; _value = target.Value; _link = target.Link;
            Owners = new[] { ManagedObjects.Id(target) };
            Fields = new[] { new StateEntityField(Name("Value"), false), new StateEntityField(Name("Link"), true) };
        }
        public void Validate() => ValidateAction?.Invoke();
        public void RestoreValues() { ValuesAction?.Invoke(); _target.Value = _value; }
        public void RestoreReferences() { ReferencesAction?.Invoke(); _target.Link = _link; }
    }
    private static string Name(string field) => typeof(Item).FullName + "." + field;
    private static StateGraph Capture(Item target, params string[] fields)
    {
        var access = new ManagedStateAccess(null, typeof(Item).Assembly.GetName().Name);
        access.AddRoot(ManagedObjects.Id(target), fields);
        return StateGraph.Capture(access, new[] { ManagedObjects.Id(target) });
    }
    private static void Throws(Action action, Action<bool> check)
    {
        try { action(); throw new Exception("Expected writer rejection."); }
        catch (InvalidOperationException) { check(true); }
    }
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("entity writer restores one full owner with all other old graph state preserved", () =>
        {
            var other = new Item { Value = 2 };
            var target = new Item { Link = other };
            using var graph = Capture(target);
            var writer = new Writer(target);
            var plan = graph.PrepareEntityRestore(writer, 100, 100);
            for (int i = 0; i < 6; i++)
            {
                target.Value = 31; other.Value = 42; target.Link = null;
                plan.Apply(graph);
                check(graph.Differences().Count == 0 && ReferenceEquals(target.Link, other));
            }
        });
        test("entity writer keeps all scalar writes before every reference write", () =>
        {
            var other = new Item { Value = 2 };
            var target = new Item { Link = other };
            using var graph = Capture(target);
            var writer = new Writer(target);
            var plan = graph.PrepareEntityRestore(writer, 100, 100);
            writer.ValuesAction = () => check(other.Value == 2 && target.Link == null);
            writer.ReferencesAction = () => check(target.Value == 1 && other.Value == 2);
            other.Value = 41; target.Value = 42; target.Link = null;
            plan.Apply(graph);
            check(graph.Differences().Count == 0);
        });
        test("entity writer validation rejects before any graph field is written", () =>
        {
            var other = new Item { Value = 2 };
            var target = new Item { Link = other };
            using var graph = Capture(target);
            var writer = new Writer(target);
            var plan = graph.PrepareEntityRestore(writer, 100, 100);
            other.Value = 41; target.Value = 42; target.Link = null;
            writer.ValidateAction = () => throw new InvalidOperationException("Invalid identity");
            Throws(() => plan.Apply(graph), check);
            check(other.Value == 41 && target.Value == 42 && target.Link == null);
            graph.Restore();
            check(graph.Differences().Count == 0);
        });
        test("entity writer failures propagate without retrying legacy writes", () =>
        {
            var target = new Item();
            using var graph = Capture(target);
            var writer = new Writer(target);
            var plan = graph.PrepareEntityRestore(writer, 100, 100);
            writer.ValuesAction = () => throw new InvalidOperationException("Writer failed");
            target.Value = 17;
            Throws(() => plan.Apply(graph), check);
            check(target.Value == 17);
            graph.Restore();
            check(graph.Differences().Count == 0);
        });
        test("entity writer rejects incomplete mistyped duplicate and extra schema fields", () =>
        {
            var target = new Item();
            using var graph = Capture(target);
            var writer = new Writer(target);
            foreach (var fields in new[]
            {
                new[] { new StateEntityField(Name("Value"), false) },
                new[] { new StateEntityField(Name("Value"), true), new StateEntityField(Name("Link"), true) },
                new[] { new StateEntityField(Name("Value"), false), new StateEntityField(Name("Value"), false) },
                new[] { new StateEntityField(Name("Value"), false), new StateEntityField(Name("Link"), true), new StateEntityField("unknown", false) }
            })
            {
                writer.Fields = fields;
                Throws(() => graph.PrepareEntityRestore(writer, 100, 100), check);
            }
            check(graph.Differences().Count == 0);
        });
        test("entity writer rejects missing duplicated owners filtered roots and metadata limits", () =>
        {
            var target = new Item();
            using var graph = Capture(target);
            var writer = new Writer(target);
            Throws(() => graph.PrepareEntityRestore(writer, 0, 100), check);
            Throws(() => graph.PrepareEntityRestore(writer, 100, 1), check);
            var id = ManagedObjects.Id(target);
            writer.Owners = new[] { id, id };
            Throws(() => graph.PrepareEntityRestore(writer, 100, 100), check);
            writer.Owners = new[] { ManagedObjects.Id(new Item()) };
            Throws(() => graph.PrepareEntityRestore(writer, 100, 100), check);
            writer.Owners = new[] { id };
            using var filtered = Capture(target, "Value");
            Throws(() => filtered.PrepareEntityRestore(writer, 100, 100), check);
        });
        test("entity writer plan cannot cross checkpoints and old whole graph remains a full oracle", () =>
        {
            var target = new Item();
            using var first = Capture(target);
            var plan = first.PrepareEntityRestore(new Writer(target), 100, 100);
            target.Value = 11;
            using var second = Capture(target);
            Throws(() => plan.Apply(second), check);
            check(target.Value == 11);
            plan.Apply(first);
            check(first.Differences().Count == 0 && second.Differences().Count == 1);
            second.Restore();
            check(second.Differences().Count == 0);
        });
        test("incorrect entity writer remains visible to full old graph verification", () =>
        {
            var target = new Item();
            using var graph = Capture(target);
            var writer = new Writer(target);
            var plan = graph.PrepareEntityRestore(writer, 100, 100);
            writer.ReferencesAction = () => target.Value = 99;
            plan.Apply(graph);
            check(graph.Differences().Count == 1);
            graph.Restore();
            check(graph.Differences().Count == 0);
        });
    }
}
