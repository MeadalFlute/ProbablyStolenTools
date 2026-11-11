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
        internal int ReferenceReads;
        internal TestBlock(Item target) { _target = target; _link = target.Link; _value = target.Value; }
        public IntPtr ReferenceAt(StateSlot slot) { ReferenceReads++; return ManagedObjects.Id(_link); }
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
        test("composed entity plans match fresh complete preparation for all fifteen writer combinations", () =>
        {
            var items = Enumerable.Range(1, 4).Select(i => new Item { Value = i }).ToArray();
            for (int i = 0; i < items.Length; i++) items[i].Link = items[(i + 1) % items.Length];
            using var graph = Capture(items[0]);
            IStateEntityWriter[] writers = { new Writer(items[0]), new PartitionWriter(items[1]),
                new PartitionWriter(items[2], true), new Writer(items[3]) };
            var parts = writers.Select(writer => graph.PrepareEntityRestore(writer, 4, 8)).ToArray();
            for (int mask = 1; mask < 16; mask++)
            {
                var indexes = Enumerable.Range(0, 4).Where(i => (mask & (1 << i)) != 0).ToArray();
                var composed = graph.ComposeEntityRestores(indexes.Select(i => parts[i]), 4, 8);
                var fresh = graph.PrepareEntityRestores(indexes.Select(i => writers[i]), 4, 8);
                foreach (var item in items) { item.Value = 41; item.Link = null; }
                composed.Apply(graph);
                check(graph.Differences().Count == 0 && composed.MetadataSlots == fresh.MetadataSlots
                    && composed.Writers.SequenceEqual(fresh.Writers));
                foreach (var item in items) { item.Value = 42; item.Link = items[0]; }
                fresh.Apply(graph);
                check(graph.Differences().Count == 0);
            }
        });
        test("composed entity plans enforce summed complete metadata budgets including legacy fields", () =>
        {
            var other = new Item();
            var target = new Item { Link = other };
            using var graph = Capture(target);
            var first = graph.PrepareEntityRestore(new PartitionWriter(target), 100, 100);
            var second = graph.PrepareEntityRestore(new Writer(other), 100, 100);
            Throws(() => graph.ComposeEntityRestores(new[] { first, second }, 1, 4), check);
            Throws(() => graph.ComposeEntityRestores(new[] { first, second }, 2, 3), check);
            Throws(() => graph.ComposeEntityRestores(new[] { first }, 1, 1), check);
            var plan = graph.ComposeEntityRestores(new[] { first, second }, 2, 4);
            check(plan.MetadataSlots == 4);
            target.Value = 42;
            plan.Apply(graph);
            check(graph.Differences().Count == 0);
        });
        test("composed entity plans reject duplicates overlaps empty groups and foreign checkpoints without writes", () =>
        {
            var target = new Item();
            using var graph = Capture(target);
            var first = graph.PrepareEntityRestore(new Writer(target), 100, 100);
            var overlap = graph.PrepareEntityRestore(new PartitionWriter(target), 100, 100);
            Throws(() => graph.ComposeEntityRestores(new[] { first, first }, 100, 100), check);
            Throws(() => graph.ComposeEntityRestores(new[] { first, overlap }, 100, 100), check);
            Throws(() => graph.ComposeEntityRestores(Array.Empty<StateGraph.EntityRestorePlan>(), 100, 100), check);
            Throws(() => graph.ComposeEntityRestores(new StateGraph.EntityRestorePlan[] { null }, 100, 100), check);
            Throws(() => graph.ComposeEntityRestores(null, 100, 100), check);
            target.Value = 42;
            using var later = Capture(target);
            Throws(() => later.ComposeEntityRestores(new[] { first }, 100, 100), check);
            check(target.Value == 42);
            first.Apply(graph);
            check(graph.Differences().Count == 0);
        });
        test("composition uses frozen schema metadata and never rereads captured block references", () =>
        {
            var other = new Item { Value = 2 };
            var target = new Item { Link = other };
            var access = new ManagedStateAccess(null, typeof(Item).Assembly.GetName().Name);
            access.AddRoot(ManagedObjects.Id(target));
            var blocks = new List<RepairableBlock>();
            access.BlockCapture = (owner, _) =>
            {
                var block = new RepairableBlock((Item)owner);
                blocks.Add(block);
                return block;
            };
            using var graph = StateGraph.Capture(access, new[] { ManagedObjects.Id(target) });
            var firstWriter = new Writer(target);
            var first = graph.PrepareEntityRestore(firstWriter, 100, 100);
            var second = graph.PrepareEntityRestore(new Writer(other), 100, 100);
            int referenceReads = blocks.Sum(block => block.ReferenceReads);
            firstWriter.Owners = Array.Empty<IntPtr>();
            firstWriter.Fields = Array.Empty<StateEntityField>();
            var plan = graph.ComposeEntityRestores(new[] { first, second }, 2, 4);
            check(blocks.Sum(block => block.ReferenceReads) == referenceReads && plan.MetadataSlots == 4);
            target.Value = 42; other.Value = 43; target.Link = null;
            plan.Apply(graph);
            check(graph.Differences().Count == 0);
        });
        test("composition validates every participant and failed candidates cannot damage earlier plans", () =>
        {
            var other = new Item { Value = 2 };
            var target = new Item { Link = other };
            using var graph = Capture(target);
            var firstWriter = new Writer(target);
            var secondWriter = new Writer(other);
            var first = graph.PrepareEntityRestore(firstWriter, 100, 100);
            var second = graph.PrepareEntityRestore(secondWriter, 100, 100);
            target.Value = 42; other.Value = 43; target.Link = null;
            secondWriter.ValidateAction = () => throw new InvalidOperationException("Disposed writer");
            Throws(() => graph.ComposeEntityRestores(new[] { first, second }, 100, 100), check);
            check(target.Value == 42 && other.Value == 43 && target.Link == null);
            first.Apply(graph);
            check(graph.Differences().Count == 0);
        });
        test("composed plans preflight all writers before every write and keep scalar reference ordering", () =>
        {
            var other = new Item { Value = 2 };
            var target = new Item { Link = other };
            using var graph = Capture(target);
            var firstWriter = new PartitionWriter(target);
            var secondWriter = new Writer(other);
            var first = graph.PrepareEntityRestore(firstWriter, 100, 100);
            var second = graph.PrepareEntityRestore(secondWriter, 100, 100);
            var plan = graph.ComposeEntityRestores(new[] { first, second }, 100, 100);
            firstWriter.ValuesAction = () => check(target.Link == null && other.Link == target);
            secondWriter.ValuesAction = () => check(target.Value == 1 && target.Link == null);
            firstWriter.ReferencesAction = () => check(target.Value == 1 && other.Value == 2);
            target.Value = 41; other.Value = 42; target.Link = null; other.Link = target;
            secondWriter.ValidateAction = () => throw new InvalidOperationException("Disposed writer");
            Throws(() => plan.Apply(graph), check);
            check(target.Value == 41 && other.Value == 42 && target.Link == null);
            secondWriter.ValidateAction = null;
            plan.Apply(graph);
            check(graph.Differences().Count == 0);
        });
        test("composed plans preserve explicit ordering and compose recursively without changing schemas", () =>
        {
            var other = new Item { Value = 2 };
            var target = new Item { Link = other };
            using var graph = Capture(target);
            var firstWriter = new Writer(target);
            var secondWriter = new Writer(other);
            var first = graph.PrepareEntityRestore(firstWriter, 100, 100);
            var second = graph.PrepareEntityRestore(secondWriter, 100, 100);
            var composed = graph.ComposeEntityRestores(new[] { second, first }, 2, 4);
            var nested = graph.ComposeEntityRestores(new[] { composed }, 2, 4);
            check(nested.Writers.SequenceEqual(new[] { secondWriter, firstWriter }) && nested.MetadataSlots == 4);
            var order = new List<string>();
            firstWriter.ValuesAction = () => order.Add("first-values");
            firstWriter.ReferencesAction = () => order.Add("first-references");
            secondWriter.ValuesAction = () => order.Add("second-values");
            secondWriter.ReferencesAction = () => order.Add("second-references");
            nested.Apply(graph);
            check(order.SequenceEqual(new[] { "second-values", "first-values", "second-references", "first-references" }));
        });
        test("composed checkpoint plans reject disposed graphs and remain isolated across past future nodes", () =>
        {
            var target = new Item();
            var graph = Capture(target);
            var first = graph.PrepareEntityRestore(new Writer(target), 100, 100);
            var composed = graph.ComposeEntityRestores(new[] { first }, 100, 100);
            target.Value = 42;
            using var later = Capture(target);
            var future = later.ComposeEntityRestores(new[] { later.PrepareEntityRestore(new Writer(target), 100, 100) }, 100, 100);
            for (int i = 0; i < 6; i++)
            {
                composed.Apply(graph); check(target.Value == 1);
                future.Apply(later); check(target.Value == 42);
            }
            graph.Dispose();
            try { graph.ComposeEntityRestores(new[] { first }, 100, 100); throw new Exception("Expected disposal"); }
            catch (ObjectDisposedException) { }
            try { composed.Apply(graph); throw new Exception("Expected disposal"); }
            catch (ObjectDisposedException) { }
            target.Value = 99;
            future.Apply(later);
            check(target.Value == 42);
        });
    }
}
