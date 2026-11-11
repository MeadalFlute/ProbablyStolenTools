using ProbablyStolenTools;
using System.Reflection;

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

    private static string Format(string text, int max = 128, bool tail = true)
        => (string)typeof(StateGraph).GetMethod("DiagnosticText", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { text, max, tail });

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
        test("generic dictionary diagnostic names retain field and array element suffixes", () =>
        {
            string map = typeof(Dictionary<string, Node>).FullName;
            string entries = typeof(Dictionary<string, Node>).GetField("_entries",
                BindingFlags.NonPublic | BindingFlags.Instance).FieldType.FullName;
            check(Format(map + "._version") == "System.Collections.Generic.Dictionary`2<...>._version");
            string formatted = Format(entries + "[12].value.Link");
            check(formatted.Contains("<...>[][12].value.Link") && !formatted.Contains("Version="));
            check(Format("System.Int32[][2]") == "System.Int32[][2]");
            check(Format("TagState.<identifier>k__BackingField") == "TagState.<identifier>k__BackingField");
        });
        test("nested generic arguments and multiple source edges compact without losing path segments", () =>
        {
            string map = typeof(Dictionary<string, List<Dictionary<int, Node[]>>>).FullName;
            string path = "root -> " + map + "._entries -> " + map + "[][3].value";
            string formatted = Format(path, 240);
            check(formatted == "root -> System.Collections.Generic.Dictionary`2<...>._entries -> " +
                "System.Collections.Generic.Dictionary`2<...>[][3].value");
            check(!formatted.Contains("Version="));
        });
        test("long diagnostic field and source text preserve their distinguishing tails", () =>
        {
            string suffix = "[][27].value.<identifier>k__BackingField";
            string field = Format(new string('x', 1000) + suffix);
            check(field.Length == 128 && field.StartsWith("...") && field.EndsWith(suffix));
            string path = Format(new string('x', 1000) + " -> cassettePlayer -> modifiedState -> dict", 240);
            check(path.Length == 240 && path.EndsWith(" -> cassettePlayer -> modifiedState -> dict"));
            check(Format("broken[[metadata") == "broken[[metadata");
        });
        test("live dictionary differences keep raw names unchanged and diagnostic suffixes readable", () =>
        {
            var map = new Dictionary<string, Node> { ["a"] = new Node { Number = 1 } };
            var access = new CountingAccess();
            using var graph = Capture(map, access);
            map.Add("b", new Node { Number = 2 });
            var raw = graph.Differences();
            access.Reads = 0;
            var detail = graph.DifferencesWithSources();
            check(raw.Count > 0 && raw.Count == detail.Count && raw.Any(s => s.Contains("Version=")));
            check(detail.All(s => !s.Contains("Version=")) && access.Reads == graph.SlotCount);
            for (int i = 0; i < raw.Count; i++)
                check(detail[i].Contains("field=" + Format(raw[i]) + " source="));
            graph.Restore(); check(map.Count == 1 && graph.Differences().Count == 0);
        });
        test("all twelve bounded diagnostic samples remain available while later fields are scanned", () =>
        {
            var root = Enumerable.Range(0, 30).ToArray();
            var access = new CountingAccess();
            using var graph = Capture(root, access);
            Array.Fill(root, -1); access.Reads = 0;
            var detail = graph.DifferencesWithSources();
            check(detail.Count == 12 && detail[6].Contains("field=System.Int32[][6] ")
                && detail[11].Contains("field=System.Int32[][11] ") && access.Reads == 30);
            access.Reads = 0; access.FailAt = 30;
            bool failed = false;
            try { graph.DifferencesWithSources(); }
            catch (InvalidOperationException e) when (e.Message == "late read") { failed = true; }
            check(failed && access.Reads == 30 && access.Writes == 0);
        });
    }
}
