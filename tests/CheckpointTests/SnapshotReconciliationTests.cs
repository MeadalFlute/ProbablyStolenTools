using System.Reflection;
using ProbablyStolenTools;

internal static class SnapshotReconciliationTests
{
    private sealed class Node
    {
        internal int Number;
        internal bool Flag;
        internal Node Link;
    }
    private struct Cell
    {
        internal int Number;
        internal (long Amount, Node Link) Inner;
    }
    private sealed class PrivateData
    {
        private int _number = 7;
        internal readonly Node Fixed = new() { Number = 3 };
        internal int Number => _number;
        internal void Mutate() => _number = 9;
    }
    private class ProbeAccess : IStateAccess, IStateValueComparer, IStateBlockAccess
    {
        internal readonly ManagedStateAccess Inner = new(gameAssembly: typeof(Node).Assembly.GetName().Name);
        internal int Reads, FailReadAt, FailWriteAt;
        internal readonly List<(IntPtr Owner, bool Reference)> Writes = new();
        internal Action BeforeReferenceWrite;
        private void Read()
        {
            if (++Reads == FailReadAt) throw new InvalidOperationException("late read");
        }
        private void Write(IntPtr id, bool reference)
        {
            Writes.Add((id, reference));
            if (Writes.Count == FailWriteAt) throw new InvalidOperationException("write failure");
            if (reference) BeforeReferenceWrite?.Invoke();
        }
        public void Retain(IntPtr id) => Inner.Retain(id);
        public IReadOnlyList<StateSlot> Describe(IntPtr id) => Inner.Describe(id);
        public byte[] ReadValue(IntPtr id, StateSlot slot) { Read(); return Inner.ReadValue(id, slot); }
        public IntPtr ReadReference(IntPtr id, StateSlot slot) { Read(); return Inner.ReadReference(id, slot); }
        public bool ValueEquals(IntPtr id, StateSlot slot, byte[] value)
        { Read(); return Inner.ValueEquals(id, slot, value); }
        public void WriteValue(IntPtr id, StateSlot slot, byte[] value)
        { Write(id, false); Inner.WriteValue(id, slot, value); }
        public void WriteReference(IntPtr id, StateSlot slot, IntPtr value)
        { Write(id, true); Inner.WriteReference(id, slot, value); }
        public IStateBlock CaptureBlock(IntPtr id, IReadOnlyList<StateSlot> slots)
            => ((IStateBlockAccess)Inner).CaptureBlock(id, slots);
        public void Validate() => Inner.Validate();
        public void Dispose() => Inner.Dispose();
    }
    private sealed class RepairAccess : ProbeAccess, IStateOwnerRepairAccess { }
    private class Block : IStateBlock
    {
        private readonly Node _target, _link;
        private readonly int _number;
        private readonly bool _flag;
        internal int Reads, Writes;
        internal bool FailRead, FailWrite;
        internal Action BeforeReferenceWrite;
        internal Block(Node target)
        { _target = target; _number = target.Number; _flag = target.Flag; _link = target.Link; }
        public IntPtr ReferenceAt(StateSlot slot) => ManagedObjects.Id(_link);
        public void RestoreValues()
        {
            if (FailWrite) throw new InvalidOperationException("block write");
            Writes++; _target.Number = _number; _target.Flag = _flag;
        }
        public void RestoreReferences()
        { BeforeReferenceWrite?.Invoke(); Writes++; _target.Link = _link; }
        public int CollectDifferences(List<string> result, int max)
        {
            int changed = 0;
            void Compare(bool same, string name)
            {
                Reads++;
                if (FailRead) throw new InvalidOperationException("block read");
                if (!same)
                {
                    changed++;
                    if (result.Count < max) result.Add("Node." + name);
                }
            }
            Compare(_target.Number == _number, "Number");
            Compare(_target.Flag == _flag, "Flag");
            Compare(ReferenceEquals(_target.Link, _link), "Link");
            return changed;
        }
    }
    private sealed class SafeBlock : Block, IStateOwnerRepairBlock
    { internal SafeBlock(Node target) : base(target) { } }

    private static StateGraph Capture(object root, ProbeAccess access, params string[] fields)
    {
        var id = ManagedObjects.Id(root); access.Inner.AddRoot(id, fields);
        return StateGraph.Capture(access, new[] { id });
    }
    private static void Throws<T>(Action action, Action<bool> check) where T : Exception
    {
        bool failed = false;
        try { action(); } catch (T) { failed = true; }
        check(failed);
    }
    private static List<Block> EnableBlocks(RepairAccess access, bool safe = true)
    {
        var blocks = new List<Block>();
        access.Inner.BlockCapture = (target, _) =>
        {
            if (target.GetType() != typeof(Node)) return null;
            Block block = safe ? new SafeBlock((Node)target) : new Block((Node)target);
            blocks.Add(block); return block;
        };
        return blocks;
    }

    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("owner repair scans once and writes complete dirty owners but no clean owners", () =>
        {
            var a = new Node { Number = 1 }; var b = new Node { Number = 2, Link = a };
            var access = new RepairAccess();
            using var graph = Capture(new[] { a, b }, access);
            a.Number = 9; access.Reads = 0;
            var plan = graph.PrepareReconciliation();
            check(plan.Mode == "owners" && plan.ChangedSlots == 1 && plan.TrackedOwners == 1
                && plan.PlannedSlots == 3 && access.Reads == graph.SlotCount && access.Writes.Count == 0);
            plan.Apply();
            check(a.Number == 1 && access.Reads == graph.SlotCount && access.Writes.Count == 3
                && access.Writes.All(w => w.Owner == ManagedObjects.Id(a)));
            check(graph.Differences().Count == 0);
        });
        test("clean reconciliation performs no writes and still scans all fields", () =>
        {
            var access = new RepairAccess();
            using var graph = Capture(new Node(), access);
            access.Reads = 0;
            var plan = graph.PrepareReconciliation();
            check(plan.Mode == "none" && plan.ChangedSlots == 0 && plan.PlannedSlots == 0
                && access.Reads == graph.SlotCount);
            plan.Apply(); check(access.Writes.Count == 0);
        });
        test("owner repair includes every changed owner beyond the diagnostic message cap", () =>
        {
            var nodes = Enumerable.Range(0, 30).Select(i => new Node { Number = i }).ToArray();
            var access = new RepairAccess();
            using var graph = Capture(nodes, access);
            foreach (var node in nodes) node.Number = -1;
            access.Reads = 0;
            var plan = graph.PrepareReconciliation();
            check(plan.Mode == "owners" && plan.ChangedSlots == 30 && plan.Differences.Count == 12
                && plan.TrackedOwners == 30 && plan.PlannedSlots == 90 && access.Reads == graph.SlotCount);
            plan.Apply(); check(graph.Differences().Count == 0 && nodes[29].Number == 29);
        });
        test("late scan failure prevents all owner repair writes after the message cap", () =>
        {
            var root = Enumerable.Range(0, 100).ToArray();
            var access = new RepairAccess();
            using var graph = Capture(root, access);
            Array.Fill(root, -1); access.Reads = 0; access.FailReadAt = 100;
            Throws<InvalidOperationException>(() => graph.PrepareReconciliation(), check);
            check(access.Reads == 100 && access.Writes.Count == 0);
        });
        test("owner limit safely falls back to full restore after a complete scan", () =>
        {
            var nodes = new[] { new Node { Number = 1 }, new Node { Number = 2 } };
            var access = new RepairAccess();
            using var graph = Capture(nodes, access);
            nodes[0].Number = 8; nodes[1].Number = 9; access.Reads = 0;
            var plan = graph.PrepareReconciliation(maxOwners: 1);
            check(plan.Mode == "full" && plan.Reason == "owner-limit" && plan.ChangedSlots == 2
                && plan.PlannedSlots == graph.SlotCount && access.Reads == graph.SlotCount);
            plan.Apply(); check(access.Writes.Count == graph.SlotCount && graph.Differences().Count == 0);
        });
        test("slot budget applies to complete owner surface not just differing leaves", () =>
        {
            var root = Enumerable.Range(0, 100).ToArray();
            var access = new RepairAccess();
            using var graph = Capture(root, access);
            root[50] = -1;
            var plan = graph.PrepareReconciliation(maxSlots: 99);
            check(plan.Mode == "full" && plan.Reason == "slot-limit" && plan.ChangedSlots == 1);
            plan.Apply(); check(root[50] == 50 && access.Writes.Count == 100);
            root[50] = -1;
            plan = graph.PrepareReconciliation(maxSlots: 100);
            check(plan.Mode == "owners" && plan.PlannedSlots == 100);
            plan.Apply(); check(graph.Differences().Count == 0);
        });
        test("disabled and unsupported accessors retain full restore semantics", () =>
        {
            foreach (bool disabled in new[] { false, true })
            {
                ProbeAccess access = disabled ? new RepairAccess() : new ProbeAccess();
                var node = new Node { Number = 1 };
                using var graph = Capture(node, access);
                node.Number = 2;
                var plan = graph.PrepareReconciliation(!disabled);
                check(plan.Mode == "full" && plan.Reason == (disabled ? "disabled" : "access"));
                plan.Apply(); check(node.Number == 1 && access.Writes.Count == graph.SlotCount);
            }
        });
        test("owner repair retains nested struct array copyback and all scalar before reference ordering", () =>
        {
            var a = new Node { Number = 1 }; var b = new Node { Number = 2 };
            var cells = new[] { new Cell { Number = 3, Inner = (4, a) },
                new Cell { Number = 5, Inner = (6, b) } };
            var access = new RepairAccess();
            using var graph = Capture(new object[] { cells, a, b }, access);
            cells[0] = new Cell { Number = 8, Inner = (9, b) };
            cells[1] = new Cell { Number = 10, Inner = (11, null) };
            a.Number = -1; b.Number = -2; a.Link = b;
            var plan = graph.PrepareReconciliation();
            access.BeforeReferenceWrite = () =>
                check(cells[0].Number == 3 && cells[0].Inner.Amount == 4 && cells[1].Number == 5
                    && cells[1].Inner.Amount == 6 && a.Number == 1 && b.Number == 2);
            plan.Apply();
            check(ReferenceEquals(cells[0].Inner.Link, a) && ReferenceEquals(cells[1].Inner.Link, b)
                && a.Link == null && graph.Differences().Count == 0);
            check(!access.Writes.SkipWhile(w => !w.Reference).Any(w => !w.Reference));
        });
        test("owner repair preserves shared dictionary capacity aliases cached views and active enumerators", () =>
        {
            var a = new Node { Number = 1 };
            var map = new Dictionary<string, Node> { ["a"] = a, ["alias"] = a };
            var entriesField = map.GetType().GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance);
            var entries = entriesField.GetValue(map);
            var enumerator = map.GetEnumerator(); enumerator.MoveNext();
            object boxed = enumerator;
            var root = new object[] { map, entries, map.Keys, map.Values, boxed, a };
            var access = new RepairAccess();
            using var graph = Capture(root, access);
            using var oracle = Capture(root, new RepairAccess());
            a.Number = 8; map.Remove("a"); map["alias"] = new Node();
            for (int i = 0; i < 100; i++) map["new" + i] = new Node();
            var plan = graph.PrepareReconciliation(); plan.Apply();
            check(plan.Mode == "owners" && oracle.Differences().Count == 0 && graph.Differences().Count == 0
                && ReferenceEquals(entriesField.GetValue(map), entries) && map.Count == 2
                && ReferenceEquals(map["a"], map["alias"]));
            enumerator.MoveNext();
            check(((Dictionary<string, Node>.Enumerator)boxed).MoveNext());
        });
        test("owner repair restores detached original storage and references with cycles and equal objects", () =>
        {
            var a = new Node { Number = 1 }; var b = new Node { Number = 1, Link = a }; a.Link = b;
            var original = new[] { a, b }; var root = new object[] { original, a };
            var access = new RepairAccess();
            using var graph = Capture(root, access);
            root[0] = new[] { new Node() }; original[1] = a;
            a.Link = new Node(); b.Link = b; b.Number = -1;
            var plan = graph.PrepareReconciliation(); plan.Apply();
            check(ReferenceEquals(root[0], original) && ReferenceEquals(original[1], b)
                && ReferenceEquals(a.Link, b) && ReferenceEquals(b.Link, a)
                && b.Number == 1 && graph.Differences().Count == 0);
        });
        test("owner repair checkpoints remain independent across past future and compacting GC", () =>
        {
            var list = new List<Node> { new Node { Number = 1 } };
            using var past = Capture(list, new RepairAccess());
            list[0].Number = 7; list.Add(new Node { Number = 8 });
            using var future = Capture(list, new RepairAccess());
            for (int i = 0; i < 4; i++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers();
                past.PrepareReconciliation().Apply();
                check(list.Count == 1 && list[0].Number == 1 && past.Differences().Count == 0);
                future.PrepareReconciliation().Apply();
                check(list.Count == 2 && list[0].Number == 7 && list[1].Number == 8
                    && future.Differences().Count == 0);
            }
        });
        test("owner repair preserves float bit patterns nullable private fields and selected root surface", () =>
        {
            var numbers = new[] { BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)),
                BitConverter.Int32BitsToSingle(0x7fc00017) };
            int?[] nullable = { null, 7 };
            var access = new RepairAccess();
            using var graph = Capture(new object[] { numbers, nullable }, access);
            numbers[0] = 0; numbers[1] = float.NaN; nullable[0] = 1; nullable[1] = null;
            graph.PrepareReconciliation().Apply();
            check(BitConverter.SingleToInt32Bits(numbers[0]) == unchecked((int)0x80000000)
                && BitConverter.SingleToInt32Bits(numbers[1]) == 0x7fc00017
                && nullable[0] == null && nullable[1] == 7 && graph.Differences().Count == 0);
            var node = new Node { Number = 1 };
            using var selected = Capture(node, new RepairAccess(), "Number");
            node.Number = 2; node.Flag = true;
            var plan = selected.PrepareReconciliation(); plan.Apply();
            check(plan.PlannedSlots == 1 && node.Number == 1 && node.Flag);
        });
        test("owner repair plans are one shot and invalidated by scans restores and disposal", () =>
        {
            foreach (int action in new[] { 0, 1, 2, 3, 4 })
            {
                var node = new Node(); var access = new RepairAccess();
                using var graph = Capture(node, access); node.Number = 1;
                var plan = graph.PrepareReconciliation();
                if (action == 0) plan.Apply();
                if (action == 1) graph.Differences();
                if (action == 2) graph.Restore();
                if (action == 3) graph.PrepareReconciliation();
                if (action == 4) graph.Dispose();
                int before = access.Writes.Count;
                Throws<InvalidOperationException>(() => plan.Apply(), check);
                check(access.Writes.Count == before);
            }
        });
        test("owner repair rejects census and invalid limits without writes", () =>
        {
            var node = new Node(); var access = new RepairAccess();
            var id = ManagedObjects.Id(node); access.Inner.AddRoot(id);
            using var census = StateGraph.Census(access, new[] { id });
            Throws<InvalidOperationException>(() => census.PrepareReconciliation(), check);
            using var graph = Capture(node, new RepairAccess());
            Throws<ArgumentOutOfRangeException>(() => graph.PrepareReconciliation(maxSlots: -1), check);
            Throws<ArgumentOutOfRangeException>(() => graph.PrepareReconciliation(maxOwners: -1), check);
            check(access.Writes.Count == 0);
        });
        test("ordinary write failure propagates without implicit retry and consumes the plan", () =>
        {
            var node = new Node(); var access = new RepairAccess();
            using var graph = Capture(node, access); node.Number = 1;
            var plan = graph.PrepareReconciliation(); access.FailWriteAt = 1;
            Throws<InvalidOperationException>(() => plan.Apply(), check);
            check(access.Writes.Count == 1);
            Throws<InvalidOperationException>(() => plan.Apply(), check);
            check(access.Writes.Count == 1);
        });
        test("safe compact owner repair writes all block fields and preserves global value reference phases", () =>
        {
            var a = new Node { Number = 1 }; var b = new Node { Number = 2, Link = a };
            var access = new RepairAccess(); var blocks = EnableBlocks(access);
            using var graph = Capture(new[] { a, b }, access);
            a.Number = 8; b.Link = b;
            foreach (var block in blocks) block.BeforeReferenceWrite = () => check(a.Number == 1 && b.Number == 2);
            var plan = graph.PrepareReconciliation();
            check(plan.Mode == "owners" && plan.ChangedSlots == 2 && plan.PlannedSlots == 6
                && blocks.Sum(b => b.Reads) == 6);
            plan.Apply();
            check(blocks.All(b => b.Writes == 2) && graph.Differences().Count == 0);
        });
        test("compact repair detects dirty late blocks after ordinary diagnostics fill the cap", () =>
        {
            var nodes = Enumerable.Range(0, 20).Select(i => new Node()).ToArray();
            var access = new RepairAccess(); var blocks = EnableBlocks(access);
            using var graph = Capture(nodes, access);
            var late = nodes[^1];
            Array.Fill(nodes, null); late.Number = 9;
            var plan = graph.PrepareReconciliation();
            check(plan.ChangedSlots == 21 && plan.Differences.Count == 12 && plan.TrackedOwners == 2
                && blocks.Sum(b => b.Reads) == 60);
            plan.Apply(); check(late.Number == 0 && blocks[^1].Writes == 2 && graph.Differences().Count == 0);
        });
        test("unknown compact blocks and oversized compact owner surfaces force full fallback", () =>
        {
            foreach (bool safe in new[] { false, true })
            {
                var node = new Node { Number = 1 }; var access = new RepairAccess();
                var blocks = EnableBlocks(access, safe);
                using var graph = Capture(node, access); node.Number = 8;
                var plan = graph.PrepareReconciliation(maxSlots: safe ? 2 : 8192);
                check(plan.Mode == "full" && plan.Reason == (safe ? "slot-limit" : "block"));
                plan.Apply(); check(node.Number == 1 && blocks[0].Writes == 2);
            }
        });
        test("late compact read failure and compact write failures never silently retry", () =>
        {
            var nodes = new[] { new Node(), new Node() };
            var access = new RepairAccess(); var blocks = EnableBlocks(access);
            using var graph = Capture(nodes, access); nodes[0].Number = 7; nodes[1].Number = 8;
            blocks[1].FailRead = true;
            Throws<InvalidOperationException>(() => graph.PrepareReconciliation(), check);
            check(blocks.All(b => b.Writes == 0) && access.Writes.Count == 0);
            blocks[1].FailRead = false;
            var plan = graph.PrepareReconciliation(); blocks[0].FailWrite = true;
            Throws<InvalidOperationException>(() => plan.Apply(), check);
            check(blocks.All(b => b.Writes == 0));
        });
        test("final full verification succeeds without extra write or visual retry", () =>
        {
            var node = new Node(); var access = new RepairAccess();
            using var graph = Capture(node, access); node.Number = 1;
            var plan = graph.PrepareReconciliation(); plan.Apply();
            int writes = access.Writes.Count; access.Reads = 0;
            int visuals = 0;
            var diff = graph.VerifyReconciliation(plan, () => visuals++, out bool fallback);
            check(diff.Count == 0 && !fallback && visuals == 0 && access.Writes.Count == writes
                && access.Reads == graph.SlotCount);
        });
        test("final completed mismatch retries full graph before visuals and verifies all fields again", () =>
        {
            var nodes = new[] { new Node { Number = 1 }, new Node { Number = 2 } };
            var access = new RepairAccess(); using var graph = Capture(nodes, access);
            nodes[0].Number = 7;
            var plan = graph.PrepareReconciliation(); plan.Apply();
            nodes[1].Number = 9; access.Reads = 0;
            int writes = access.Writes.Count, visuals = 0;
            var diff = graph.VerifyReconciliation(plan, () =>
            {
                visuals++;
                check(nodes[0].Number == 1 && nodes[1].Number == 2);
            }, out bool fallback);
            check(diff.Count == 0 && fallback && visuals == 1
                && access.Writes.Count == writes + graph.SlotCount && access.Reads == graph.SlotCount * 2);
        });
        test("persistent final mismatch remains a failure after exactly one full retry", () =>
        {
            var node = new Node(); var access = new RepairAccess();
            using var graph = Capture(node, access); node.Number = 1;
            var plan = graph.PrepareReconciliation(); plan.Apply(); node.Number = 2;
            int visuals = 0;
            var diff = graph.VerifyReconciliation(plan, () => { visuals++; node.Number = 3; }, out bool fallback);
            check(diff.Count != 0 && fallback && visuals == 1);
        });
        test("final read exception never triggers a full retry", () =>
        {
            var node = new Node(); var access = new RepairAccess();
            using var graph = Capture(node, access); node.Number = 1;
            var plan = graph.PrepareReconciliation(); plan.Apply();
            int writes = access.Writes.Count; access.Reads = 0; access.FailReadAt = graph.SlotCount;
            Throws<InvalidOperationException>(() => graph.VerifyReconciliation(plan,
                () => throw new Exception("unexpected retry"), out _), check);
            check(access.Writes.Count == writes);
        });
        test("full fallback write and visual exceptions propagate without another retry", () =>
        {
            foreach (bool failWrite in new[] { false, true })
            {
                var node = new Node(); var access = new RepairAccess();
                using var graph = Capture(node, access); node.Number = 1;
                var plan = graph.PrepareReconciliation(); plan.Apply(); node.Number = 2;
                int visuals = 0, writes = access.Writes.Count;
                if (failWrite) access.FailWriteAt = writes + 1;
                Throws<InvalidOperationException>(() => graph.VerifyReconciliation(plan, () =>
                {
                    visuals++;
                    throw new InvalidOperationException("visual failure");
                }, out _), check);
                check(visuals == (failWrite ? 0 : 1)
                    && access.Writes.Count == writes + (failWrite ? 1 : graph.SlotCount));
            }
        });
        test("legacy full and clean modes never add a new retry on final mismatches", () =>
        {
            foreach (bool initialDirty in new[] { false, true })
            {
                var node = new Node(); var access = new RepairAccess();
                using var graph = Capture(node, access);
                if (initialDirty) node.Number = 1;
                var plan = graph.PrepareReconciliation(false); plan.Apply();
                node.Number = 2; int writes = access.Writes.Count;
                var diff = graph.VerifyReconciliation(plan, () => throw new Exception("unexpected"), out bool fallback);
                check(diff.Count != 0 && !fallback && access.Writes.Count == writes);
            }
        });
        test("reconciliation final verification rejects unapplied stale and repeated plans", () =>
        {
            var node = new Node(); var access = new RepairAccess();
            using var graph = Capture(node, access);
            var plan = graph.PrepareReconciliation();
            Throws<InvalidOperationException>(() => graph.VerifyReconciliation(plan, () => { }, out _), check);
            plan.Apply();
            graph.VerifyReconciliation(plan, () => { }, out _);
            Throws<InvalidOperationException>(() => graph.VerifyReconciliation(plan, () => { }, out _), check);
            plan = graph.PrepareReconciliation(); plan.Apply(); graph.Differences();
            Throws<InvalidOperationException>(() => graph.VerifyReconciliation(plan, () => { }, out _), check);
        });
        test("owner repair preserves private readonly and delegate anchors without invoking callbacks", () =>
        {
            var data = new PrivateData(); var original = data.Fixed;
            int calls = 0;
            Action callback = () => { calls++; data.Mutate(); };
            var root = new object[] { data, callback };
            var access = new RepairAccess(); using var graph = Capture(root, access);
            data.Mutate();
            typeof(PrivateData).GetField("Fixed", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(data, new Node());
            root[1] = (Action)(() => calls++);
            var plan = graph.PrepareReconciliation(); plan.Apply();
            check(data.Number == 7 && ReferenceEquals(data.Fixed, original) && ReferenceEquals(root[1], callback)
                && calls == 0 && graph.Differences().Count == 0);
        });
        test("seeded mixed graph repairs remain identical to full checkpoints through repeated growth", () =>
        {
            var nodes = Enumerable.Range(0, 24).Select(i => new Node { Number = i }).ToArray();
            for (int i = 0; i < nodes.Length; i++) nodes[i].Link = nodes[(i + 1) % nodes.Length];
            var map = new Dictionary<string, Node> { ["first"] = nodes[0], ["last"] = nodes[^1] };
            var root = new object[] { nodes, map, map.Keys, map.Values };
            using var graph = Capture(root, new RepairAccess());
            using var oracle = Capture(root, new RepairAccess());
            var random = new Random(72423);
            for (int round = 0; round < 40; round++)
            {
                for (int i = 0; i < 20; i++)
                {
                    var node = nodes[random.Next(nodes.Length)];
                    node.Number = random.Next(); node.Flag = true;
                    node.Link = i % 2 == 0 ? new Node() : nodes[random.Next(nodes.Length)];
                }
                for (int i = 0; i < 80; i++) map["growth-" + i] = nodes[random.Next(nodes.Length)];
                map.Remove("first"); root[0] = new Node[1];
                var plan = graph.PrepareReconciliation();
                check(plan.Mode == "owners");
                plan.Apply();
                graph.VerifyReconciliation(plan, () => throw new Exception("Unexpected fallback"), out bool fallback);
                check(!fallback && oracle.Differences().Count == 0 && ReferenceEquals(root[0], nodes)
                    && map.Count == 2 && ReferenceEquals(map["first"], nodes[0]));
            }
        });
    }
}
