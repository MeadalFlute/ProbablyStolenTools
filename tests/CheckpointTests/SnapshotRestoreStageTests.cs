using ProbablyStolenTools;

internal static class SnapshotRestoreStageTests
{
    private sealed class StableAccess : IStateAccess, IStateStableStorageAccess, IStateValueProbe,
        IStateCensusValueProbe, IStateBlockAccess, IStateOwnerRepairAccess
    {
        internal readonly Dictionary<IntPtr, (long Value, IntPtr Link)> Nodes = new()
        { [(IntPtr)1] = (17, (IntPtr)2), [(IntPtr)2] = (29, (IntPtr)1) };
        internal int ScalarReads, ReferenceReads, Probes, ValueProbes, Writes, Disposals;
        internal bool Invalid, UntrustedBlock, FailBlock;
        public void Retain(IntPtr id) { if (!Nodes.ContainsKey(id)) throw new InvalidOperationException("missing"); }
        public IReadOnlyList<StateSlot> Describe(IntPtr id) => new[]
        {
            new StateSlot(0, 8, false, "value"), new StateSlot(1, 8, true, "link")
        };
        public byte[] ReadValue(IntPtr id, StateSlot slot)
        { ScalarReads++; return BitConverter.GetBytes(Nodes[id].Value); }
        public IntPtr ReadReference(IntPtr id, StateSlot slot)
        { ReferenceReads++; return Nodes[id].Link; }
        public int ProbeValue(IntPtr id, StateSlot slot) { Probes++; return 8; }
        public bool TryProbeValues(IntPtr id, IReadOnlyList<StateSlot> slots) { ValueProbes++; return false; }
        public void WriteValue(IntPtr id, StateSlot slot, byte[] value)
        { Writes++; Nodes[id] = (BitConverter.ToInt64(value), Nodes[id].Link); }
        public void WriteReference(IntPtr id, StateSlot slot, IntPtr value)
        { Writes++; Nodes[id] = (Nodes[id].Value, value); }
        public void Validate() { if (Invalid) throw new InvalidOperationException("invalid identity"); }
        public void Dispose() { Disposals++; }
        public IStateBlock CaptureBlock(IntPtr id, IReadOnlyList<StateSlot> slots)
            => UntrustedBlock && id == (IntPtr)1 ? new CustomBlock(this) : null;
    }

    private sealed class CustomBlock : IStateBlock
    {
        private readonly StableAccess _access;
        private readonly long _value;
        private readonly IntPtr _link;
        internal CustomBlock(StableAccess access)
        { _access = access; (_value, _link) = access.Nodes[(IntPtr)1]; }
        public IntPtr ReferenceAt(StateSlot slot) => _link;
        public void RestoreValues() => _access.WriteValue((IntPtr)1, default, BitConverter.GetBytes(_value));
        public void RestoreReferences() => _access.WriteReference((IntPtr)1, default, _link);
        public int CollectDifferences(List<string> result, int max)
        {
            if (_access.FailBlock) throw new InvalidOperationException("custom block read");
            return 0;
        }
    }

    private sealed class UnapprovedAccess : IStateAccess
    {
        internal readonly StableAccess Inner = new();
        internal bool FailScalar;
        public void Retain(IntPtr id) => Inner.Retain(id);
        public IReadOnlyList<StateSlot> Describe(IntPtr id) => Inner.Describe(id);
        public byte[] ReadValue(IntPtr id, StateSlot slot)
            => FailScalar ? throw new InvalidOperationException("custom read") : Inner.ReadValue(id, slot);
        public IntPtr ReadReference(IntPtr id, StateSlot slot) => Inner.ReadReference(id, slot);
        public void WriteValue(IntPtr id, StateSlot slot, byte[] value) => Inner.WriteValue(id, slot, value);
        public void WriteReference(IntPtr id, StateSlot slot, IntPtr value) => Inner.WriteReference(id, slot, value);
        public void Validate() => Inner.Validate();
        public void Dispose() => Inner.Dispose();
    }

    private static void Throws<T>(Action action, Action<bool> check) where T : Exception
    {
        bool failed = false;
        try { action(); } catch (T) { failed = true; }
        check(failed);
    }

    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("stable restore preflight validates retention without reading expected live differences", () =>
        {
            var access = new StableAccess();
            using var graph = StateGraph.Capture(access, new[] { (IntPtr)1 });
            access.Nodes[(IntPtr)1] = (91, IntPtr.Zero);
            access.ScalarReads = access.ReferenceReads = 0;
            check(graph.ValidateRestore() == "retained-storage" && access.ScalarReads == 0
                && access.ReferenceReads == 0 && access.Writes == 0);
            graph.Restore();
            check(access.Nodes[(IntPtr)1] == (17L, (IntPtr)2) && graph.Differences().Count == 0);
        });
        test("stable preflight rejects invalid retention and invalidates an earlier repair plan", () =>
        {
            var access = new StableAccess();
            using var graph = StateGraph.Capture(access, new[] { (IntPtr)1 });
            var plan = graph.PrepareReconciliation();
            graph.ValidateRestore();
            Throws<InvalidOperationException>(() => plan.Apply(), check);
            access.Invalid = true;
            Throws<InvalidOperationException>(() => graph.ValidateRestore(), check);
            check(access.Writes == 0);
            graph.Dispose();
            Throws<ObjectDisposedException>(() => graph.ValidateRestore(), check);
        });
        test("restore topology census keeps all identities paths slots and bytes but reads only references", () =>
        {
            var fullAccess = new StableAccess();
            var topologyAccess = new StableAccess();
            using var full = StateGraph.Census(fullAccess, new[] { (IntPtr)1 });
            using var topology = StateGraph.CensusForRestore(topologyAccess, new[] { (IntPtr)1 }, 2, 4, 32);
            check(topology.CensusMode == "topology" && topology.ObjectCount == full.ObjectCount
                && topology.SlotCount == full.SlotCount && topology.ByteCount == full.ByteCount
                && full.Objects.ToHashSet().SetEquals(topology.Objects));
            foreach (var id in full.Objects) check(full.SourceOf(id) == topology.SourceOf(id));
            check(topologyAccess.ReferenceReads == 2 && topologyAccess.ScalarReads == 0
                && topologyAccess.Probes == 0 && topologyAccess.ValueProbes == 0 && topologyAccess.Writes == 0
                && fullAccess.Probes == 2);
            Throws<InvalidOperationException>(() => topology.ValidateRestore(), check);
            Throws<InvalidOperationException>(() => topology.Restore(), check);
        });
        test("restore topology census retains object slot byte limits and validation failure cleanup", () =>
        {
            foreach (int budget in new[] { 0, 1, 2 })
            {
                string[] failures = new string[2];
                for (int mode = 0; mode < 2; mode++)
                {
                    var access = new StableAccess();
                    try
                    {
                        using var graph = mode == 0
                            ? StateGraph.Census(access, new[] { (IntPtr)1 }, budget == 0 ? 1 : 2,
                                budget == 1 ? 3 : 4, budget == 2 ? 31 : 32)
                            : StateGraph.CensusForRestore(access, new[] { (IntPtr)1 }, budget == 0 ? 1 : 2,
                                budget == 1 ? 3 : 4, budget == 2 ? 31 : 32);
                    }
                    catch (SnapshotLimitException e) { failures[mode] = e.Message; }
                    check(access.Disposals == 1 && access.Writes == 0);
                }
                check(failures[0] != null && failures[0] == failures[1]);
            }
            var invalid = new StableAccess { Invalid = true };
            Throws<InvalidOperationException>(() => StateGraph.CensusForRestore(invalid, new[] { (IntPtr)1 }), check);
            check(invalid.Disposals == 1 && invalid.Writes == 0);
        });
        test("unapproved restore access retains full scalar preflight and census read failures", () =>
        {
            var access = new UnapprovedAccess();
            using var graph = StateGraph.Capture(access, new[] { (IntPtr)1 });
            access.Inner.ScalarReads = 0;
            check(graph.ValidateRestore() == "full-read" && access.Inner.ScalarReads == 2);
            using (var census = StateGraph.CensusForRestore(new UnapprovedAccess(), new[] { (IntPtr)1 }))
                check(census.CensusMode == "full-read" && census.ByteCount == 32);
            access.FailScalar = true;
            Throws<InvalidOperationException>(() => graph.ValidateRestore(), check);
            var failed = new UnapprovedAccess { FailScalar = true };
            Throws<InvalidOperationException>(() => StateGraph.CensusForRestore(failed, new[] { (IntPtr)1 }), check);
            check(failed.Inner.Disposals == 1 && access.Inner.Writes == 0);
        });
        test("an unapproved block prevents read-free preflight even with approved ordinary storage", () =>
        {
            var access = new StableAccess { UntrustedBlock = true };
            using var graph = StateGraph.Capture(access, new[] { (IntPtr)1 });
            check(graph.ValidateRestore() == "full-read");
            access.FailBlock = true;
            Throws<InvalidOperationException>(() => graph.ValidateRestore(), check);
            check(access.Writes == 0);
        });
        test("a separately disposed managed access cannot pass an empty retention validation", () =>
        {
            var root = new[] { 17, 29 };
            var access = new ManagedStateAccess();
            var id = ManagedObjects.Id(root);
            access.AddRoot(id);
            using var graph = StateGraph.Capture(access, new[] { id });
            access.Dispose();
            Throws<ObjectDisposedException>(() => graph.ValidateRestore(), check);
            Throws<ObjectDisposedException>(() => graph.Restore(), check);
            check(root.SequenceEqual(new[] { 17, 29 }));
        });
    }
}
