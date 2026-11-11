using System.Reflection;
using ProbablyStolenTools;

internal static class SemanticTagOwnershipTests
{
    private sealed class Tag { internal SemanticTagValue Value; }
    private sealed class Owner { internal Dictionary<string, Tag> Map = new(); }
    private sealed class Root
    {
        internal Owner[] Owners;
        internal object MapAlias, StorageAlias, ViewAlias, Other;
        internal Dictionary<string, Tag>.Enumerator Iterator;
    }
    private static Owner NewOwner()
    {
        var owner = new Owner();
        owner.Map.Add("disabled", new Tag { Value = new(false, null, 0, 0, 0, 0, false, "id", "name") });
        owner.Map.Add("null", null);
        return owner;
    }
    private static object Field(object value, string name)
        => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
    private static StateGraph Graph(Root root, params object[] extraRoots)
    {
        var access = new ManagedStateAccess(gameAssembly: typeof(Root).Assembly.GetName().Name);
        var ids = new[] { (object)root }.Concat(extraRoots).Select(ManagedObjects.Id).ToArray();
        foreach (var id in ids) access.AddRoot(id);
        return StateGraph.Capture(access, ids);
    }
    private static SemanticTagSnapshot<Owner, Tag> Snapshot(Root root)
        => SemanticTagSnapshot<Owner, Tag>.Capture(root.Owners, owner => owner.Map, tag => tag.Value);
    private static SemanticTagOwnershipAudit Audit(StateGraph graph, SemanticTagSnapshot<Owner, Tag> snapshot)
        => SemanticTagOwnershipAudit.Capture(graph, snapshot, typeof(Owner).FullName + ".Map");
    private static void Throws<T>(Action action, Action<bool> check) where T : Exception
    {
        bool failed = false;
        try { action(); } catch (T) { failed = true; }
        check(failed);
    }

    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("tag ownership audit counts captured map storage and state slots without approving pruning", () =>
        {
            var root = new Root { Owners = new[] { NewOwner() } };
            check(Field(root.Owners[0].Map, "_keys") == null && Field(root.Owners[0].Map, "_values") == null);
            using var graph = Graph(root);
            using var snapshot = Snapshot(root);
            var audit = Audit(graph, snapshot);
            check(audit.Maps == 1 && audit.SharedMaps == 0 && audit.StorageObjects == 2);
            check(audit.MapExternalEdges == 0 && audit.StorageExternalEdges == 0
                && audit.ViewExternalEdges == 0 && audit.RootAliases == 0 && audit.StandaloneStates == 0);
            check(audit.MapSlots > 0 && audit.StorageSlots > 0 && audit.StateSlots == 9 && audit.UnknownMapSlots == 0);
            check(audit.Summary.Contains("scope=captured-graph-only pruningAllowed=false"));
            check(Field(root.Owners[0].Map, "_keys") == null && Field(root.Owners[0].Map, "_values") == null);
            check(graph.Differences().Count == 0 && snapshot.Differences().Count == 0);
        });
        test("tag ownership audit deduplicates shared maps states and backing storage", () =>
        {
            var one = NewOwner();
            var two = new Owner { Map = one.Map };
            var three = NewOwner(); three.Map["disabled"] = one.Map["disabled"];
            var root = new Root { Owners = new[] { one, two, three, one } };
            using var graph = Graph(root);
            using var snapshot = Snapshot(root);
            var audit = Audit(graph, snapshot);
            check(audit.Maps == 2 && audit.SharedMaps == 1 && audit.StorageObjects == 4 && audit.StateSlots == 9);
            check(audit.MapExternalEdges == 0 && audit.StorageExternalEdges == 0 && audit.StateExternalEdges == 0);
        });
        test("tag ownership audit detects direct dictionary aliases and embedded active enumerators", () =>
        {
            var owner = NewOwner();
            var root = new Root { Owners = new[] { owner }, MapAlias = owner.Map, Iterator = owner.Map.GetEnumerator() };
            root.Iterator.MoveNext();
            using var graph = Graph(root);
            using var snapshot = Snapshot(root);
            var audit = Audit(graph, snapshot);
            check(audit.MapExternalEdges == 2 && audit.StateExternalEdges == 1);
            check(audit.Samples.Any(sample => sample.Contains("_dictionary")));
            owner.Map.Add("later", new Tag());
            graph.Restore();
            check(graph.Differences().Count == 0 && snapshot.Differences().Count == 0);
            check(root.Iterator.MoveNext() && root.Iterator.Current.Key == "null");
        });
        test("tag ownership audit detects entry and bucket array aliases without reading live storage", () =>
        {
            foreach (string field in new[] { "_entries", "_buckets" })
            {
                var owner = NewOwner();
                var root = new Root { Owners = new[] { owner }, StorageAlias = Field(owner.Map, field) };
                using var graph = Graph(root);
                using var snapshot = Snapshot(root);
                // Replacement live arrays must not alter inspection of the captured topology.
                for (int i = 0; i < 100; i++) owner.Map["grow" + i] = new Tag();
                var audit = Audit(graph, snapshot);
                check(audit.StorageExternalEdges == 1 && audit.StorageObjects == 2);
                graph.Restore();
                check(ReferenceEquals(root.StorageAlias, Field(owner.Map, field)) && graph.Differences().Count == 0);
            }
        });
        test("tag ownership audit detects cached key and value view aliases and their back references", () =>
        {
            var owner = NewOwner();
            var root = new Root { Owners = new[] { owner }, ViewAlias = owner.Map.Keys, Other = owner.Map.Values };
            using var graph = Graph(root);
            using var snapshot = Snapshot(root);
            var audit = Audit(graph, snapshot);
            check(audit.CachedViews == 2 && audit.ViewExternalEdges == 2 && audit.MapExternalEdges == 2);
            check(graph.Differences().Count == 0);
        });
        test("tag ownership audit distinguishes standalone tags from direct aliases to linked tags", () =>
        {
            var owner = NewOwner();
            var root = new Root { Owners = new[] { owner }, Other = new object[] { new Tag(), owner.Map["disabled"] } };
            using var graph = Graph(root);
            using var snapshot = Snapshot(root);
            var audit = Audit(graph, snapshot);
            check(audit.StandaloneStates == 1 && audit.StateExternalEdges == 1 && audit.StateSlots == 9);
        });
        test("tag ownership audit detects explicit dictionary and storage roots with no incoming alias edge", () =>
        {
            var owner = NewOwner();
            var root = new Root { Owners = new[] { owner } };
            using var graph = Graph(root, owner.Map, Field(owner.Map, "_entries"));
            using var snapshot = Snapshot(root);
            var audit = Audit(graph, snapshot);
            check(audit.RootAliases == 2 && audit.MapExternalEdges == 0 && audit.StorageExternalEdges == 0);
        });
        test("tag ownership diagnostics stay bounded while every late alias is counted", () =>
        {
            var owner = NewOwner();
            var root = new Root { Owners = new[] { owner }, Other = Enumerable.Repeat((object)owner.Map, 200).ToArray() };
            using var graph = Graph(root);
            using var snapshot = Snapshot(root);
            var audit = Audit(graph, snapshot);
            check(audit.MapExternalEdges == 200 && audit.Samples.Count == 6);
            check(audit.Samples.All(sample => sample.Length < 310));
            check(graph.Differences().Count == 0);
        });
        test("captured topology inspection reads no live fields and rejects census and disposed graphs", () =>
        {
            var owner = NewOwner();
            var access = new CountAccess();
            var id = ManagedObjects.Id(owner); access.Inner.AddRoot(id);
            using var graph = StateGraph.Capture(access, new[] { id });
            int reads = access.Reads, slots = 0;
            owner.Map.Clear();
            graph.VisitCapturedSlots((_, _, _) => slots++);
            check(slots == graph.SlotCount && access.Reads == reads && graph.IsCapturedRoot(id));
            Throws<InvalidOperationException>(() => graph.VisitCapturedSlots((_, _, _) => throw new InvalidOperationException()), check);
            graph.Restore(); check(graph.Differences().Count == 0);
            var censusAccess = new CountAccess(); censusAccess.Inner.AddRoot(id);
            using var census = StateGraph.Census(censusAccess, new[] { id });
            Throws<InvalidOperationException>(() => census.VisitCapturedSlots((_, _, _) => { }), check);
            Throws<InvalidOperationException>(() => census.IsCapturedRoot(id), check);
            graph.Dispose();
            Throws<ObjectDisposedException>(() => graph.VisitCapturedSlots((_, _, _) => { }), check);
            Throws<ObjectDisposedException>(() => graph.IsCapturedRoot(id), check);
        });
        test("tag ownership audit never certifies an unfamiliar dictionary layout", () =>
        {
            var root = new Root { Owners = new[] { NewOwner() } };
            var access = new CountAccess { RenameVersion = true };
            var id = ManagedObjects.Id(root); access.Inner.AddRoot(id);
            using var graph = StateGraph.Capture(access, new[] { id });
            using var snapshot = Snapshot(root);
            var audit = Audit(graph, snapshot);
            check(audit.UnknownMapSlots == 1 && audit.Summary.Contains("pruningAllowed=false"));
            check(graph.Differences().Count == 0);
            snapshot.Dispose();
            Throws<ObjectDisposedException>(() => Audit(graph, snapshot), check);
        });
        test("logical dictionary reconstruction can match semantic values while losing captured storage and version", () =>
        {
            var owner = NewOwner();
            var root = new Root { Owners = new[] { owner }, StorageAlias = Field(owner.Map, "_entries"),
                ViewAlias = owner.Map.Keys, Iterator = owner.Map.GetEnumerator() };
            root.Iterator.MoveNext();
            var original = owner.Map.ToArray();
            using var graph = Graph(root);
            using var snapshot = Snapshot(root);
            for (int i = 0; i < 100; i++) owner.Map["growth" + i] = new Tag();
            // Deliberately naive logical restore; it is a test counterexample, never a production writer.
            owner.Map.Clear();
            foreach (var pair in original) owner.Map.Add(pair.Key, pair.Value);
            check(snapshot.Differences().Count == 0);
            check(graph.Differences().Count != 0 && !ReferenceEquals(root.StorageAlias, Field(owner.Map, "_entries")));
            var iterator = root.Iterator;
            Throws<InvalidOperationException>(() => iterator.MoveNext(), check);
            graph.Restore();
            check(graph.Differences().Count == 0 && snapshot.Differences().Count == 0
                && ReferenceEquals(root.StorageAlias, Field(owner.Map, "_entries")));
            check(root.Iterator.MoveNext() && root.Iterator.Current.Key == "null");
        });
        test("logical dictionary reconstruction cannot preserve retained past and future storage by values alone", () =>
        {
            var owner = NewOwner();
            var root = new Root { Owners = new[] { owner } };
            using var past = Graph(root);
            using var semanticPast = Snapshot(root);
            var original = owner.Map.ToArray();
            var originalArray = Field(owner.Map, "_entries");
            for (int i = 0; i < 100; i++) owner.Map["future" + i] = new Tag();
            var futureArray = Field(owner.Map, "_entries");
            using var future = Graph(root);
            using var semanticFuture = Snapshot(root);
            owner.Map.Clear();
            foreach (var pair in original) owner.Map.Add(pair.Key, pair.Value);
            check(semanticPast.Differences().Count == 0 && past.Differences().Count != 0);
            for (int i = 0; i < 3; i++)
            {
                future.Restore();
                check(ReferenceEquals(Field(owner.Map, "_entries"), futureArray)
                    && future.Differences().Count == 0 && semanticFuture.Differences().Count == 0);
                past.Restore();
                check(ReferenceEquals(Field(owner.Map, "_entries"), originalArray)
                    && past.Differences().Count == 0 && semanticPast.Differences().Count == 0);
            }
        });
        test("unknown comparer mutable state is not covered by semantic matching or the legacy opaque policy", () =>
        {
            var comparer = new MutableComparer();
            var owner = NewOwner();
            owner.Map = new Dictionary<string, Tag>(owner.Map, comparer);
            var root = new Root { Owners = new[] { owner } };
            using var graph = Graph(root);
            using var snapshot = Snapshot(root);
            var audit = Audit(graph, snapshot);
            check(audit.StoredComparers == 1 && audit.Summary.Contains("comparerState=unproven"));
            comparer.IgnoreCase = true;
            check(snapshot.Differences().Count == 0 && graph.Differences().Count == 0);
            // Both captures keep comparer identity, but neither covers its hidden mutable state.
            check(owner.Map.ContainsKey("DISABLED"));
        });
    }

    private sealed class MutableComparer : IEqualityComparer<string>
    {
        internal bool IgnoreCase;
        public bool Equals(string x, string y) => string.Equals(x, y,
            IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        public int GetHashCode(string value) => StringComparer.OrdinalIgnoreCase.GetHashCode(value);
    }

    private sealed class CountAccess : IStateAccess, IStateValueComparer
    {
        internal readonly ManagedStateAccess Inner = new(gameAssembly: typeof(Root).Assembly.GetName().Name);
        internal int Reads;
        internal bool RenameVersion;
        public void Retain(IntPtr id) => Inner.Retain(id);
        public IReadOnlyList<StateSlot> Describe(IntPtr id) => Inner.Describe(id)
            .Select(slot => RenameVersion && slot.Name.EndsWith("._version", StringComparison.Ordinal)
                ? slot with { Name = "Dictionary.futureMetadata" } : slot).ToArray();
        public byte[] ReadValue(IntPtr id, StateSlot slot) { Reads++; return Inner.ReadValue(id, slot); }
        public IntPtr ReadReference(IntPtr id, StateSlot slot) { Reads++; return Inner.ReadReference(id, slot); }
        public bool ValueEquals(IntPtr id, StateSlot slot, byte[] value) { Reads++; return Inner.ValueEquals(id, slot, value); }
        public void WriteValue(IntPtr id, StateSlot slot, byte[] value) => Inner.WriteValue(id, slot, value);
        public void WriteReference(IntPtr id, StateSlot slot, IntPtr value) => Inner.WriteReference(id, slot, value);
        public void Validate() => Inner.Validate();
        public void Dispose() => Inner.Dispose();
    }
}
