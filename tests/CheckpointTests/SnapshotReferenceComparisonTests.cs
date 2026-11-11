using System.Reflection;
using ProbablyStolenTools;

internal static class SnapshotReferenceComparisonTests
{
    private sealed class Trap
    {
        public override bool Equals(object value) => throw new Exception("Business Equals must not run.");
        public override int GetHashCode() => throw new Exception("Business hash must not run.");
        public static bool operator ==(Trap left, Trap right) => throw new Exception("Business operator must not run.");
        public static bool operator !=(Trap left, Trap right) => throw new Exception("Business operator must not run.");
        internal void Invoke() { }
    }
    private struct Nested { internal object Link; }
    private sealed class Data
    {
        private object _link;
        internal Nested Nested;
        internal readonly object Readonly;
        internal Data(object link) { _link = link; Readonly = link; Nested.Link = link; }
        internal void Change(object link) { _link = link; Nested.Link = link; }
        internal object Link => _link;
    }

    private static (ManagedStateAccess Access, StateGraph Graph) Capture(object root, bool direct, bool fast = true,
        params string[] selected)
    {
        var access = new ManagedStateAccess(null, typeof(Data).Assembly.GetName().Name, fast)
            { DirectReferenceComparisons = direct };
        var id = ManagedObjects.Id(root); access.AddRoot(id, selected);
        return (access, StateGraph.Capture(access, new[] { id }));
    }

    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("direct references distinguish null same-text strings and equal boxed values without business equality", () =>
        {
            object link = new Trap();
            var text = new string('q', 3);
            object boxed = 987;
            var root = new object[] { link, text, boxed, null, link };
            var (_, direct) = Capture(root, true);
            var (_, ids) = Capture(root, false);
            using (direct) using (ids)
            {
                check(direct.Differences().Count == 0 && direct.ReferenceComparisonCount == 5);
                root[0] = new Trap(); root[1] = new string('q', 3); root[2] = 987; root[3] = link; root[4] = null;
                check(direct.Differences(100).Count == 5
                    && direct.Differences(100).SequenceEqual(ids.Differences(100)));
                direct.Restore();
                check(ReferenceEquals(root[0], link) && ReferenceEquals(root[1], text)
                    && ReferenceEquals(root[2], boxed) && root[3] == null
                    && ReferenceEquals(root[4], link) && ids.Differences().Count == 0
                    && ids.ReferenceComparisonCount == 0);
            }
        });
        test("direct references preserve private readonly nested fields and selected roots in reflection mode", () =>
        {
            foreach (bool fast in new[] { true, false })
            {
                var link = new Trap(); var data = new Data(link);
                var (_, graph) = Capture(data, true, fast, "_link", "Nested");
                using (graph)
                {
                    data.Change(new Trap());
                    check(graph.Differences().Count == 2);
                    graph.Restore();
                    check(ReferenceEquals(data.Link, link) && ReferenceEquals(data.Nested.Link, link)
                        && ReferenceEquals(data.Readonly, link) && graph.Differences().Count == 0
                        && graph.ReferenceComparisonCount > 0);
                }
            }
        });
        test("direct references preserve struct array indices and multicast delegate anchor identities", () =>
        {
            var a = new Trap(); var b = new Trap();
            Action action = a.Invoke; action += b.Invoke;
            var array = new[] { new Nested { Link = a }, new Nested { Link = b } };
            var root = new object[] { array, action };
            var (_, graph) = Capture(root, true);
            var (_, ids) = Capture(root, false);
            using (graph) using (ids)
            {
                array[0].Link = b; array[1].Link = null; root[1] = b;
                check(graph.Differences(100).Count == 3
                    && graph.Differences(100).SequenceEqual(ids.Differences(100)));
                graph.Restore();
                check(ReferenceEquals(array[0].Link, a) && ReferenceEquals(array[1].Link, b)
                    && ReferenceEquals(root[1], action) && ids.Differences().Count == 0);
            }
        });
        test("direct reference scans do not register new live objects in the global identity map", () =>
        {
            var root = new object[] { null };
            var (_, graph) = Capture(root, true);
            using (graph)
            {
                var counter = typeof(ManagedObjects).GetField("_next", BindingFlags.Static | BindingFlags.NonPublic);
                long before = (long)counter.GetValue(null);
                root[0] = new Trap();
                check(graph.Differences().Count == 1 && (long)counter.GetValue(null) == before);
                graph.Restore();
                check(root[0] == null && graph.Differences().Count == 0);
            }
        });
        test("direct reference scans continue beyond the message limit and reset diagnostics on dispose", () =>
        {
            var root = new object[100];
            var (access, graph) = Capture(root, true);
            for (int i = 0; i < root.Length; i++) root[i] = new Trap();
            long before = graph.ReferenceComparisonCount;
            check(graph.Differences(1).Count == 1 && graph.ReferenceComparisonCount - before == 100);
            var plan = graph.PrepareReconciliation();
            check(plan.ChangedSlots == 100 && plan.PlannedSlots == 100 && plan.Mode == "owners");
            plan.Apply();
            check(graph.VerifyReconciliation(plan, () => { }, out bool fallback).Count == 0 && !fallback);
            graph.Dispose();
            check(access.ReferenceComparisonCount == 0);
        });
        test("direct reference owner and full repairs match the ID oracle after collection storage replacement", () =>
        {
            var link = new Trap();
            var map = new Dictionary<string, object> { ["one"] = link, ["alias"] = link };
            var (_, graph) = Capture(map, true);
            var (_, ids) = Capture(map, false);
            using (graph) using (ids)
                foreach (bool enabled in new[] { true, false })
                {
                    map.Remove("one"); map["alias"] = new Trap();
                    for (int i = 0; i < 80; i++) map["new-" + i] = new Trap();
                    check(graph.Differences(1000).SequenceEqual(ids.Differences(1000)));
                    var plan = graph.PrepareReconciliation(enabled);
                    check(plan.Mode == (enabled ? "owners" : "full"));
                    plan.Apply();
                    check(graph.VerifyReconciliation(plan, () => { }, out _).Count == 0
                        && ids.Differences().Count == 0 && map.Count == 2
                        && ReferenceEquals(map["one"], link) && ReferenceEquals(map["alias"], link));
                }
        });
        test("direct reference comparison keeps each checkpoint identity across repeated GC and cache clears", () =>
        {
            var a = new Trap(); var b = new Trap();
            var root = new object[] { a };
            var (_, past) = Capture(root, true);
            root[0] = b;
            var (_, future) = Capture(root, true);
            using (past) using (future)
                for (int i = 0; i < 3; i++)
                {
                    GC.Collect(2, GCCollectionMode.Forced, true, true);
                    SnapshotFieldReader.ClearCache();
                    past.Restore(); check(ReferenceEquals(root[0], a) && past.Differences().Count == 0);
                    future.Restore(); check(ReferenceEquals(root[0], b) && future.Differences().Count == 0);
                }
        });
        test("direct reference reads propagate real errors and never count a failed read as a match", () =>
        {
            SnapshotFieldReader.ClearCache();
            var root = new object[80];
            var (access, graph) = Capture(root, true);
            using (graph)
            {
                var reader = SnapshotFieldReader.Get(typeof(object[]), "", Array.Empty<FieldInfo>(), true);
                check(reader.Compiled);
                typeof(SnapshotFieldReader).GetField("_read", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(reader, (Func<object, int, object>)((_, _) => throw new IndexOutOfRangeException("Read failed.")));
                long before = access.ReferenceComparisonCount;
                bool thrown = false;
                try { graph.Differences(); } catch (IndexOutOfRangeException) { thrown = true; }
                check(thrown && access.ReferenceComparisonCount == before);
            }
            SnapshotFieldReader.ClearCache();
        });
    }
}
