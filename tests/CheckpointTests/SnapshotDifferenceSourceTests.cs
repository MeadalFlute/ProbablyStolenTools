using ProbablyStolenTools;

internal static class SnapshotDifferenceSourceTests
{
    private sealed class Node
    {
        internal int Number;
        internal Node Link;
        internal string Secret;
        public int Computed => throw new Exception("Business getter called.");
        public override string ToString() => throw new Exception("Business ToString called.");
    }

    private sealed class CountingAccess : IStateAccess, IStateValueComparer, IStateObjectDescription
    {
        internal readonly ManagedStateAccess Inner = new(gameAssembly: typeof(Node).Assembly.GetName().Name);
        internal int Reads, Writes, Descriptions, FailAt;
        internal string TypeName;
        public void Retain(IntPtr id) => Inner.Retain(id);
        public IReadOnlyList<StateSlot> Describe(IntPtr id) => Inner.Describe(id);
        private void Read()
        {
            if (++Reads == FailAt) throw new InvalidOperationException("late read");
        }
        public byte[] ReadValue(IntPtr id, StateSlot slot) { Read(); return Inner.ReadValue(id, slot); }
        public IntPtr ReadReference(IntPtr id, StateSlot slot) { Read(); return Inner.ReadReference(id, slot); }
        public bool ValueEquals(IntPtr id, StateSlot slot, byte[] value)
        { Read(); return Inner.ValueEquals(id, slot, value); }
        public void WriteValue(IntPtr id, StateSlot slot, byte[] value)
        { Writes++; Inner.WriteValue(id, slot, value); }
        public void WriteReference(IntPtr id, StateSlot slot, IntPtr value)
        { Writes++; Inner.WriteReference(id, slot, value); }
        public string ObjectTypeName(IntPtr id)
        {
            Descriptions++;
            return TypeName ?? ((IStateObjectDescription)Inner).ObjectTypeName(id);
        }
        public void Validate() => Inner.Validate();
        public void Dispose() => Inner.Dispose();
    }

    private static StateGraph Capture(object root, CountingAccess access)
    {
        var id = ManagedObjects.Id(root); access.Inner.AddRoot(id);
        return StateGraph.Capture(access, new[] { id });
    }

    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("difference sources distinguish aliased cyclic owners without business getters or values", () =>
        {
            var a = new Node { Number = 1, Secret = "private-original" };
            var b = new Node { Number = 2, Link = a }; a.Link = b;
            var access = new CountingAccess();
            using var graph = Capture(a, access);
            a.Number = 7; b.Number = 8; a.Secret = "private-changed";
            var plain = graph.Differences();
            check(plain.Count == 3 && access.Descriptions == 0);
            access.Reads = 0;
            var detail = graph.DifferencesWithSources();
            check(detail.Count == plain.Count && access.Reads == graph.SlotCount && access.Writes == 0);
            check(detail.Any(s => s.Contains($"owner=0x{ManagedObjects.Id(a).ToInt64():X} ")
                && s.EndsWith("source=root")));
            check(detail.Any(s => s.Contains($"owner=0x{ManagedObjects.Id(b).ToInt64():X} ")
                && s.EndsWith("source=root -> " + typeof(Node).FullName + ".Link")));
            check(detail.All(s => s.Contains("type=" + typeof(Node).FullName) && s.Contains("storage=field")
                && !s.Contains("private-original") && !s.Contains("private-changed")));
            graph.Restore();
            check(graph.DifferencesWithSources().Count == 0 && a.Number == 1 && b.Number == 2);
        });
        test("difference source cap bounds metadata work but still reads all slots and late errors", () =>
        {
            var root = Enumerable.Range(0, 100).ToArray();
            var access = new CountingAccess();
            using var graph = Capture(root, access);
            Array.Fill(root, -1); access.Reads = 0;
            check(graph.DifferencesWithSources(1).Count == 1 && access.Reads == 100
                && access.Descriptions == 1 && access.Writes == 0);
            access.Reads = 0; access.FailAt = 100;
            bool failed = false;
            try { graph.DifferencesWithSources(1); }
            catch (InvalidOperationException e) when (e.Message == "late read") { failed = true; }
            check(failed && access.Reads == 100 && access.Writes == 0);
        });
        test("zero difference source cap still scans every field without metadata formatting", () =>
        {
            var root = new[] { 1, 2, 3 };
            var access = new CountingAccess();
            using var graph = Capture(root, access);
            Array.Fill(root, -1); access.Reads = 0;
            check(graph.DifferencesWithSources(0).Count == 0 && access.Reads == 3
                && access.Descriptions == 0 && access.Writes == 0);
        });
        test("difference sources work with legacy byte access lacking type descriptions", () =>
        {
            var memory = new FakeMemory();
            memory.Node(1, 1, 2, 0); memory.Node(2, 2, 1, 0);
            using var graph = StateGraph.Capture(memory, new[] { (IntPtr)1 });
            memory.Number(2, 9); memory.Link(1, 8, 0);
            var detail = graph.DifferencesWithSources();
            check(detail.Count == 2 && detail.All(s => s.Contains("type=unknown")));
            check(detail.Any(s => s.Contains("owner=0x2 ") && s.EndsWith("source=root -> first")));
            graph.Restore(); check(graph.Differences().Count == 0);
        });
        test("difference metadata is length bounded and single line", () =>
        {
            var root = new Node();
            var access = new CountingAccess { TypeName = "\r\n\t" + new string('x', 10000) };
            var id = ManagedObjects.Id(root);
            access.Inner.AddRoot(id);
            using var graph = StateGraph.Capture(access, new[] { id });
            root.Number = 1;
            string detail = graph.DifferencesWithSources().Single();
            check(detail.Length < 600 && detail.Contains("...")
                && !detail.Any(c => c == '\r' || c == '\n' || c == '\t'));
        });
        test("difference source diagnostics reject census and disposed checkpoints", () =>
        {
            var root = new Node();
            var access = new CountingAccess();
            var id = ManagedObjects.Id(root); access.Inner.AddRoot(id);
            using var census = StateGraph.Census(access, new[] { id });
            bool failed = false;
            try { census.DifferencesWithSources(); } catch (InvalidOperationException) { failed = true; }
            check(failed);
            var graph = Capture(root, new CountingAccess()); graph.Dispose();
            failed = false;
            try { graph.DifferencesWithSources(); } catch (ObjectDisposedException) { failed = true; }
            check(failed);
        });
    }
}
