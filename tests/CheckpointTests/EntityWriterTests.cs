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
    private sealed class PartitionWriter : IStateEntityWriter, IStateEntityPartitionWriter
    {
        private readonly Item _target;
        private readonly int _value;
        private readonly Item _link;
        private readonly bool _referenceOnly;
        internal Action ValidateAction, ValuesAction, ReferencesAction;
        public IReadOnlyCollection<IntPtr> Owners { get; set; }
        public IReadOnlyList<StateEntityField> Fields { get; set; }
        public IReadOnlyList<StateEntityField> LegacyFields { get; set; }
        internal PartitionWriter(Item target, bool referenceOnly = false)
        {
            _target = target; _value = target.Value; _link = target.Link; _referenceOnly = referenceOnly;
            Owners = new[] { ManagedObjects.Id(target) };
            Fields = new[] { referenceOnly ? new StateEntityField(Name("Link"), true) : new StateEntityField(Name("Value"), false) };
            LegacyFields = new[] { referenceOnly ? new StateEntityField(Name("Value"), false) : new StateEntityField(Name("Link"), true) };
        }
        public void Validate() => ValidateAction?.Invoke();
        public void RestoreValues() { ValuesAction?.Invoke(); if (!_referenceOnly) _target.Value = _value; }
        public void RestoreReferences() { ReferencesAction?.Invoke(); if (_referenceOnly) _target.Link = _link; }
    }
    private class TestBlock : IStateBlock
    {
        private readonly Item _target, _link;
        private readonly int _value;
        internal TestBlock(Item target) { _target = target; _link = target.Link; _value = target.Value; }
        public IntPtr ReferenceAt(StateSlot slot) => ManagedObjects.Id(_link);
        public void RestoreValues() => _target.Value = _value;
        public void RestoreReferences() => _target.Link = _link;
        public int CollectDifferences(List<string> result, int max)
        {
            int count = 0;
            if (_target.Value != _value) count++;
            if (!ReferenceEquals(_target.Link, _link)) count++;
            if (count != 0 && result.Count < max) result.Add("block");
            return count;
        }
    }
    private sealed class RepairableBlock : TestBlock, IStateOwnerRepairBlock
    {
        internal RepairableBlock(Item target) : base(target) { }
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
            writer.ValuesAction = () => check(other.Value == 2 && target.Link == null && target.Value == 42);
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
        test("partition writer restores business scalars while legacy retains runtime links", () =>
        {
            var other = new Item { Value = 2 };
            var target = new Item { Link = other };
            using var graph = Capture(target);
            var writer = new PartitionWriter(target);
            var plan = graph.PrepareEntityRestore(writer, 100, 100);
            writer.ValuesAction = () => check(other.Value == 2 && target.Link == null && target.Value == 31);
            writer.ReferencesAction = () => check(ReferenceEquals(target.Link, other));
            for (int i = 0; i < 6; i++)
            {
                target.Value = 31; other.Value = 42; target.Link = null;
                plan.Apply(graph);
                check(graph.Differences().Count == 0);
            }
        });
        test("partition writer rejects omitted duplicate extra and mistyped runtime coverage", () =>
        {
            var target = new Item();
            using var graph = Capture(target);
            var writer = new PartitionWriter(target);
            foreach (var legacy in new[]
            {
                Array.Empty<StateEntityField>(),
                new[] { new StateEntityField(Name("Link"), false) },
                new[] { new StateEntityField(Name("Value"), false), new StateEntityField(Name("Link"), true) },
                new[] { new StateEntityField(Name("Link"), true), new StateEntityField("unknown", false) }
            })
            {
                writer.LegacyFields = legacy;
                Throws(() => graph.PrepareEntityRestore(writer, 100, 100), check);
            }
            check(graph.Differences().Count == 0);
        });
        test("partition writer rejects filtered roots missing either business or runtime fields", () =>
        {
            var target = new Item();
            foreach (string field in new[] { "Value", "Link" })
            {
                using var graph = Capture(target, field);
                Throws(() => graph.PrepareEntityRestore(new PartitionWriter(target), 100, 100), check);
            }
        });
        test("mixed writer group keeps all values before references across both writer kinds", () =>
        {
            var other = new Item { Value = 2 };
            var target = new Item { Link = other };
            using var graph = Capture(target);
            var first = new PartitionWriter(target);
            var second = new Writer(other);
            var plan = graph.PrepareEntityRestores(new IStateEntityWriter[] { first, second }, 100, 100);
            first.ValuesAction = () => check(target.Value == 41 && target.Link == null && other.Link == target);
            second.ValuesAction = () => check(target.Value == 1 && target.Link == null);
            first.ReferencesAction = () => check(other.Value == 2 && ReferenceEquals(target.Link, other));
            second.ReferencesAction = () => check(target.Value == 1 && other.Value == 2);
            target.Value = 41; other.Value = 42; target.Link = null; other.Link = target;
            plan.Apply(graph);
            check(graph.Differences().Count == 0);
        });
        test("mixed writer group validates every writer before any business write", () =>
        {
            var other = new Item { Value = 2 };
            var target = new Item { Link = other };
            using var graph = Capture(target);
            var first = new PartitionWriter(target);
            var second = new Writer(other);
            var plan = graph.PrepareEntityRestores(new IStateEntityWriter[] { first, second }, 100, 100);
            second.ValidateAction = () => throw new InvalidOperationException("Disposed second writer");
            target.Value = 41; other.Value = 42; target.Link = null;
            Throws(() => plan.Apply(graph), check);
            check(target.Value == 41 && other.Value == 42 && target.Link == null);
        });
        test("mixed writer group enforces combined budgets and disjoint owners", () =>
        {
            var other = new Item();
            var target = new Item { Link = other };
            using var graph = Capture(target);
            var writers = new IStateEntityWriter[] { new PartitionWriter(target), new Writer(other) };
            Throws(() => graph.PrepareEntityRestores(writers, 1, 100), check);
            Throws(() => graph.PrepareEntityRestores(writers, 100, 3), check);
            Throws(() => graph.PrepareEntityRestores(new IStateEntityWriter[] { writers[0], new Writer(target) }, 100, 100), check);
            Throws(() => graph.PrepareEntityRestores(Array.Empty<IStateEntityWriter>(), 100, 100), check);
            Throws(() => graph.PrepareEntityRestores(new IStateEntityWriter[] { null }, 100, 100), check);
            graph.PrepareEntityRestores(writers, 2, 4).Apply(graph);
            check(graph.Differences().Count == 0);
        });
        test("partition plan freezes metadata and remains bound to its original checkpoint", () =>
        {
            var target = new Item();
            using var graph = Capture(target);
            var writer = new PartitionWriter(target);
            var plan = graph.PrepareEntityRestore(writer, 100, 100);
            writer.Owners = Array.Empty<IntPtr>(); writer.Fields = Array.Empty<StateEntityField>();
            writer.LegacyFields = Array.Empty<StateEntityField>();
            target.Value = 17;
            using var later = Capture(target);
            Throws(() => plan.Apply(later), check);
            check(target.Value == 17);
            plan.Apply(graph);
            check(graph.Differences().Count == 0 && later.Differences().Count == 1);
        });
        test("partition writer errors propagate and incorrect writes are exposed by old oracle", () =>
        {
            var target = new Item();
            using var graph = Capture(target);
            var writer = new PartitionWriter(target);
            var plan = graph.PrepareEntityRestore(writer, 100, 100);
            writer.ValuesAction = () => throw new InvalidOperationException("Write failed");
            writer.ValidateAction = () => { };
            target.Value = 17;
            Throws(() => plan.Apply(graph), check);
            check(target.Value == 17);
            writer.ValuesAction = null;
            writer.ReferencesAction = () => target.Value = 42;
            plan.Apply(graph);
            check(graph.Differences().Count == 1);
            graph.Restore();
            check(graph.Differences().Count == 0);
        });
        test("reference partition keeps legacy scalar writes and skips only selected links", () =>
        {
            var other = new Item { Value = 2 };
            var target = new Item { Link = other };
            using var graph = Capture(target);
            var writer = new PartitionWriter(target, true);
            var plan = graph.PrepareEntityRestore(writer, 100, 100);
            writer.ValuesAction = () => check(target.Value == 1 && other.Value == 2 && target.Link == null);
            writer.ReferencesAction = () => check(target.Value == 1 && target.Link == null);
            target.Value = 41; other.Value = 42; target.Link = null;
            plan.Apply(graph);
            check(graph.Differences().Count == 0 && ReferenceEquals(target.Link, other));
        });
        test("entity partitions reject partial blocks and unknown whole-owner block replacement", () =>
        {
            var target = new Item();
            foreach (bool known in new[] { false, true })
            {
                var access = new ManagedStateAccess(null, typeof(Item).Assembly.GetName().Name);
                access.AddRoot(ManagedObjects.Id(target));
                access.BlockCapture = (owner, slots) => owner is Item item
                    ? known ? new RepairableBlock(item) : new TestBlock(item) : null;
                using var graph = StateGraph.Capture(access, new[] { ManagedObjects.Id(target) });
                Throws(() => graph.PrepareEntityRestore(new PartitionWriter(target), 100, 100), check);
                if (!known) Throws(() => graph.PrepareEntityRestore(new Writer(target), 100, 100), check);
                else
                {
                    var plan = graph.PrepareEntityRestore(new Writer(target), 100, 100);
                    target.Value = 41;
                    plan.Apply(graph);
                    check(graph.Differences().Count == 0);
                }
            }
        });
    }
}
