using ProbablyStolenTools;

internal static class SemanticEntityBoundaryTests
{
    private sealed class Memory : IStateAccess, IStateBlockAccess
    {
        internal readonly FakeMemory Data = new();
        internal bool ForbidReads, LongNames, FailBlock;
        internal int BlockOwner, Reads;
        private static readonly StateSlot[] Slots =
        {
            new(0, 4, false, "value"), new(8, 8, true, "first"), new(16, 8, true, "second")
        };
        private void Read() { Reads++; if (ForbidReads) throw new Exception("Audit reread live storage."); }
        public void Retain(IntPtr id) { Read(); Data.Retain(id); }
        public IReadOnlyList<StateSlot> Describe(IntPtr id)
        {
            Read();
            return LongNames ? Slots.Select(s => s with { Name = new string('x', 1000) + "\r\n\t" + s.Name }).ToArray() : Slots;
        }
        public byte[] ReadValue(IntPtr id, StateSlot slot) { Read(); return Data.ReadValue(id, slot); }
        public IntPtr ReadReference(IntPtr id, StateSlot slot) { Read(); return Data.ReadReference(id, slot); }
        public void WriteValue(IntPtr id, StateSlot slot, byte[] value) => Data.WriteValue(id, slot, value);
        public void WriteReference(IntPtr id, StateSlot slot, IntPtr value) => Data.WriteReference(id, slot, value);
        public void Validate() { Read(); Data.Validate(); }
        public void Dispose() => Data.Dispose();
        public IStateBlock CaptureBlock(IntPtr id, IReadOnlyList<StateSlot> slots)
            => id.ToInt64() == BlockOwner ? new Block(this, id) : null;
        private sealed class Block : IStateBlock
        {
            private readonly Memory _memory;
            private readonly IntPtr _id, _first, _second;
            private readonly byte[] _number;
            internal Block(Memory memory, IntPtr id)
            {
                _memory = memory; _id = id;
                _number = memory.Data.ReadValue(id, Slots[0]);
                _first = memory.Data.ReadReference(id, Slots[1]); _second = memory.Data.ReadReference(id, Slots[2]);
            }
            public IntPtr ReferenceAt(StateSlot slot)
            {
                if (_memory.FailBlock) throw new InvalidOperationException("Captured block unavailable.");
                return slot.Offset == 8 ? _first : _second;
            }
            public void RestoreValues() => _memory.Data.WriteValue(_id, Slots[0], _number);
            public void RestoreReferences()
            {
                _memory.Data.WriteReference(_id, Slots[1], _first);
                _memory.Data.WriteReference(_id, Slots[2], _second);
            }
            public int CollectDifferences(List<string> result, int max)
            {
                int changed = 0;
                foreach (var slot in Slots)
                {
                    bool same = slot.IsReference ? _memory.ReadReference(_id, slot) == ReferenceAt(slot)
                        : _memory.ReadValue(_id, slot).SequenceEqual(_number);
                    if (same) continue;
                    changed++;
                    if (result.Count < max) result.Add(slot.Name);
                }
                return changed;
            }
        }
    }
    private static SemanticEntityObject Entry(int id, SemanticEntityRole role, bool native = true)
        => new((IntPtr)id, role, native);
    private static readonly SemanticEntityObject[] Index =
    {
        Entry(2, SemanticEntityRole.Item), Entry(3, SemanticEntityRole.GridInventory),
        Entry(4, SemanticEntityRole.Callback), Entry(5, SemanticEntityRole.Array),
        Entry(6, SemanticEntityRole.SlotInventory, false)
    };
    private static Memory Fixture()
    {
        var memory = new Memory();
        memory.Data.Node(1, 1, 2, 3);
        memory.Data.Node(2, 2, 3, 4);
        memory.Data.Node(3, 3, 5, 6);
        memory.Data.Node(4, 4, 2, 3);
        memory.Data.Node(5, 5, 2, 6);
        memory.Data.Node(6, 6, 2, 0);
        return memory;
    }
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("entity boundary audit partitions direct entity runtime callback and array links without pruning approval", () =>
        {
            var memory = Fixture();
            using var graph = StateGraph.Capture(memory, new[] { (IntPtr)1 });
            var audit = SemanticEntityBoundaryAudit.Capture(graph, Index);
            check(audit.Items == 1 && audit.GridInventories == 1 && audit.SlotInventories == 1
                && audit.OtherInventories == 0 && audit.ExternalEntities == 1 && audit.EntityCount == 3
                && audit.ScannedSlots == graph.SlotCount && audit.EntitySlots == 9 && audit.EntityLinks == 5
                && audit.InternalLinks == 3 && audit.OutgoingRuntimeLinks == 2 && audit.CallbackOutgoingLinks == 1
                && audit.IncomingRuntimeLinks == 6 && audit.CallbackIncomingLinks == 2 && audit.ArrayIncomingLinks == 2
                && audit.SharedEntities == 3 && audit.RootEntities == 0 && audit.UnreferencedEntities == 0);
            check(audit.Summary.Contains("pruningAllowed=false restoreAllowed=false")
                && audit.Summary.Contains("opaqueAndStaticReferences=unproven") && memory.Data.Writes == 0);
        });
        test("entity audit reads captured topology not changed live storage or identity validation", () =>
        {
            var memory = Fixture();
            using var graph = StateGraph.Capture(memory, new[] { (IntPtr)1 });
            memory.Data.Link(4, 8, 0);
            memory.Data.Link(5, 16, 0);
            int reads = memory.Reads;
            memory.ForbidReads = true;
            var audit = SemanticEntityBoundaryAudit.Capture(graph, Index);
            check(audit.CallbackIncomingLinks == 2 && audit.ArrayIncomingLinks == 2
                && memory.Reads == reads && memory.Data.Writes == 0);
            memory.ForbidReads = false;
            check(graph.Differences().Count == 2);
            graph.Restore();
            check(graph.Differences().Count == 0);
        });
        test("entity audit includes compact block references without rereading live fields", () =>
        {
            var memory = Fixture(); memory.BlockOwner = 2;
            using var graph = StateGraph.Capture(memory, new[] { (IntPtr)1 });
            int reads = memory.Reads;
            memory.ForbidReads = true;
            var audit = SemanticEntityBoundaryAudit.Capture(graph, Index);
            check(graph.BlockCount == 1 && audit.ScannedSlots == 18 && audit.EntitySlots == 9
                && audit.CallbackOutgoingLinks == 1 && audit.InternalLinks == 3 && memory.Reads == reads);
        });
        test("entity audit deduplicates stable identities retains explicit roots and handles empty entity sets", () =>
        {
            var memory = Fixture();
            using var graph = StateGraph.Capture(memory, new[] { (IntPtr)1, (IntPtr)2 });
            var audit = SemanticEntityBoundaryAudit.Capture(graph, Index.Concat(Index));
            check(audit.EntityCount == 3 && audit.RootEntities == 1 && audit.IncomingRuntimeLinks == 6);
            var empty = SemanticEntityBoundaryAudit.Capture(graph, Array.Empty<SemanticEntityObject>(), 0);
            check(empty.EntityCount == 0 && empty.ScannedSlots == graph.SlotCount && empty.IncomingRuntimeLinks == 0);
        });
        test("entity audit distinguishes unreferenced root and unknown inventory without certifying ownership", () =>
        {
            var memory = new Memory(); memory.Data.Node(1, 7, 0, 0);
            using var graph = StateGraph.Capture(memory, new[] { (IntPtr)1 });
            var audit = SemanticEntityBoundaryAudit.Capture(graph, new[] { Entry(1, SemanticEntityRole.OtherInventory) });
            check(audit.OtherInventories == 1 && audit.RootEntities == 1 && audit.UnreferencedEntities == 1
                && audit.EntityLinks == 0 && audit.SharedEntities == 0
                && audit.Summary.Contains("restoreAllowed=false"));
        });
        test("entity audit bounds samples and sanitizes long paths while counting every late callback alias", () =>
        {
            var memory = new Memory { LongNames = true };
            memory.Data.Node(1, 1, 2, 3); memory.Data.Node(2, 2, 0, 0);
            for (int i = 3; i <= 82; i++) memory.Data.Node(i, i, 2, i == 82 ? 0 : i + 1);
            using var graph = StateGraph.Capture(memory, new[] { (IntPtr)1 });
            var entries = new[] { Entry(2, SemanticEntityRole.Item) }
                .Concat(Enumerable.Range(3, 80).Select(i => Entry(i, SemanticEntityRole.Callback)));
            var audit = SemanticEntityBoundaryAudit.Capture(graph, entries);
            check(audit.CallbackIncomingLinks == 80 && audit.IncomingRuntimeLinks == 81
                && audit.ScannedSlots == graph.SlotCount && audit.CallbackSamples.Count == 3
                && audit.BoundarySamples.Count <= 2 && audit.OutgoingCallbackSamples.Count <= 1
                && audit.CallbackSamples.All(s => s.Length < 420)
                && !audit.Summary.Contains('\r') && !audit.Summary.Contains('\n') && !audit.Summary.Contains('\t'));
        });
        test("entity audit rejects invalid conflicting and over-budget metadata without invalidating checkpoints", () =>
        {
            var memory = Fixture();
            using var graph = StateGraph.Capture(memory, new[] { (IntPtr)1 });
            var badSets = new[]
            {
                new[] { Entry(0, SemanticEntityRole.Item) }, new[] { Entry(99, SemanticEntityRole.Item) },
                new[] { Entry(2, SemanticEntityRole.None) }, new[] { Entry(2, (SemanticEntityRole)99) },
                new[] { Entry(2, SemanticEntityRole.Item), Entry(2, SemanticEntityRole.GridInventory) },
                new[] { Entry(2, SemanticEntityRole.Item), Entry(2, SemanticEntityRole.Item, false) }
            };
            foreach (var bad in badSets)
            {
                bool thrown = false;
                try { SemanticEntityBoundaryAudit.Capture(graph, bad); } catch (InvalidOperationException) { thrown = true; }
                check(thrown && graph.Differences().Count == 0);
            }
            foreach (int limit in new[] { -1, 0, 4 })
            {
                bool thrown = false;
                try { SemanticEntityBoundaryAudit.Capture(graph, Index, limit); }
                catch (InvalidOperationException) { thrown = true; }
                catch (ArgumentOutOfRangeException) { thrown = true; }
                check(thrown && memory.Data.Writes == 0);
            }
            check(SemanticEntityBoundaryAudit.Capture(graph, Index, 5).EntityCount == 3);
        });
        test("entity audit rejects census and disposed graphs and propagates late captured block failures", () =>
        {
            var censusMemory = Fixture();
            using (var census = StateGraph.Census(censusMemory, new[] { (IntPtr)1 }))
            {
                bool thrown = false;
                try { SemanticEntityBoundaryAudit.Capture(census, Array.Empty<SemanticEntityObject>()); }
                catch (InvalidOperationException) { thrown = true; }
                check(thrown && censusMemory.Data.Writes == 0);
            }
            var memory = Fixture(); memory.BlockOwner = 2;
            var graph = StateGraph.Capture(memory, new[] { (IntPtr)1 });
            memory.FailBlock = true;
            bool failed = false;
            try { SemanticEntityBoundaryAudit.Capture(graph, Index); } catch (InvalidOperationException) { failed = true; }
            check(failed && memory.Data.Writes == 0);
            memory.FailBlock = false;
            check(graph.Differences().Count == 0);
            graph.Dispose();
            failed = false;
            try { SemanticEntityBoundaryAudit.Capture(graph, Array.Empty<SemanticEntityObject>()); }
            catch (ObjectDisposedException) { failed = true; }
            check(failed);
        });
    }
}
