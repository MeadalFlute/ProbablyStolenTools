using ProbablyStolenTools;

internal static class SnapshotDiscoveryTests
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("object discovery rejects broad graphs before expanding an unbounded queue", () =>
        {
            var access = new BroadAccess(100000);
            SnapshotLimitException failure = null;
            try { using var graph = StateGraph.Capture(access, new[] { (IntPtr)1 }, maxObjects: 10); }
            catch (SnapshotLimitException e) { failure = e; }
            check(failure != null && failure.Message.Contains("discovered=11"));
            check(failure.Message.Contains("objects=1/10") && failure.Message.Contains("pending=9"));
            check(failure.Message.Contains("source=root -> child[9]"));
            check(access.Reads == 10 && access.Retains == 1 && access.Disposed && access.Writes == 0);
        });
        test("root enumeration fails early at the discovery budget and releases the accessor", () =>
        {
            var access = new BroadAccess(0);
            int enumerated = 0;
            IEnumerable<IntPtr> Roots()
            {
                for (int i = 1; i < 100000; i++) { enumerated++; yield return (IntPtr)i; }
            }
            try { using var graph = StateGraph.Capture(access, Roots(), maxObjects: 2); }
            catch (SnapshotLimitException e)
            {
                check(e.Message.Contains("objects=0/2") && e.Message.Contains("discovered=3"));
            }
            check(enumerated == 3 && access.Disposed && access.Retains == 0 && access.Reads == 0);
        });
        test("exact discovery budget accepts null duplicate shared cyclic and explicit roots", () =>
        {
            var memory = new FakeMemory();
            memory.Node(1, 10, 2, 2); memory.Node(2, 20, 1, 0);
            using var graph = StateGraph.Capture(memory,
                new[] { IntPtr.Zero, (IntPtr)1, (IntPtr)1, (IntPtr)2 }, maxObjects: 2);
            check(graph.ObjectCount == 2 && graph.SlotCount == 6 && graph.SourceOf((IntPtr)2) == "root");
            memory.Number(2, 99);
            graph.Restore();
            check(memory.Number(2) == 20 && graph.Differences().Count == 0);
        });
    }

    private sealed class BroadAccess : IStateAccess
    {
        private readonly int _children;
        internal int Reads, Retains, Writes;
        internal bool Disposed;
        internal BroadAccess(int children) { _children = children; }
        public void Retain(IntPtr id) { Retains++; }
        public IReadOnlyList<StateSlot> Describe(IntPtr id)
            => id == (IntPtr)1 ? Enumerable.Range(0, _children)
                .Select(i => new StateSlot(i, IntPtr.Size, true, $"child[{i}]")).ToArray()
                : Array.Empty<StateSlot>();
        public IntPtr ReadReference(IntPtr id, StateSlot slot) { Reads++; return (IntPtr)(slot.Offset + 2); }
        public byte[] ReadValue(IntPtr id, StateSlot slot) => throw new NotSupportedException();
        public void WriteValue(IntPtr id, StateSlot slot, byte[] bytes) { Writes++; }
        public void WriteReference(IntPtr id, StateSlot slot, IntPtr value) { Writes++; }
        public void Validate() { }
        public void Dispose() { Disposed = true; }
    }
}
