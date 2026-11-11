using ProbablyStolenTools;

internal static class SemanticEntityBoundaryPlanTests
{
    private sealed class Item
    {
        public int Value = 0;
        public Item Next = null;
        public Runtime Runtime = null;
        public object Payload = null;
        public override bool Equals(object other) => throw new Exception("Business Equals must not run.");
        public override int GetHashCode() => throw new Exception("Business hash must not run.");
    }
    private sealed class Runtime
    {
        public Item Link = null;
        public int Cache = 0;
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly Item First = new(), Second = new();
        internal readonly Runtime Runtime = new();
        internal readonly StateGraph Graph;
        internal readonly SemanticSlotSchema Schema;
        internal Fixture()
        {
            First.Next = Second; Second.Next = First;
            Runtime.Link = First; First.Runtime = Runtime;
            First.Payload = new object[] { new Runtime { Link = Second }, new byte[] { 1, 2, 3 }, new[] { 1, 2, 3, 4 } };
            var capturedItem = First;
            Func<Item> callback = () => capturedItem;
            var root = new object[] { First, Second, Runtime, callback };
            var access = new ManagedStateAccess(null, typeof(Item).Assembly.GetName().Name, true, true, true, true);
            access.AddRoot(ManagedObjects.Id(root));
            Graph = StateGraph.Capture(access, new[] { ManagedObjects.Id(root), ManagedObjects.Id(First) });
            Schema = new SemanticSlotSchema(new[]
            {
                (Name("Value"), false, SemanticFieldRole.Entity),
                (Name("Next"), true, SemanticFieldRole.Entity),
                (Name("Runtime"), true, SemanticFieldRole.Runtime),
                (Name("Payload"), true, SemanticFieldRole.Entity)
            });
        }
        internal static string Name(string field) => typeof(Item).FullName + "." + field;
        internal (IntPtr, SemanticSlotSchema)[] Entities => new[]
        {
            (ManagedObjects.Id(First), Schema), (ManagedObjects.Id(Second), Schema)
        };
        internal SemanticEntityBoundaryPlan Capture(int maxObjects = 200000, int maxSlots = 3000000)
            => SemanticEntityBoundaryPlan.Capture(Graph, Entities, ManagedObjects.Get, ManagedObjects.Id, maxObjects, maxSlots);
        public void Dispose() => Graph.Dispose();
    }
    private static void Throws(Action action, Action<bool> check)
    {
        try { action(); throw new Exception("Expected boundary rejection."); }
        catch (InvalidOperationException) { check(true); }
    }
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("independent runtime payload covers all remaining slots objects and scoped entity bindings", () =>
        {
            using var f = new Fixture();
            using var plan = f.Capture();
            using var runtime = SemanticRuntimeSnapshot.Capture(f.Graph, plan, 200000, 3000000, 256L * 1024 * 1024);
            check(runtime.SlotCount == plan.RuntimeSlots && runtime.ObjectCount == f.Graph.ObjectCount
                && runtime.BoundEntities == 2 && runtime.ByteCount == 8L * runtime.SlotCount
                && runtime.BlockCount == 1 && runtime.Differences().Count == 0);
            f.First.Value = 21; f.First.Next = null;
            check(runtime.Differences().Count == 0 && f.Graph.Differences().Count == 2);
            f.Graph.Restore();
        });
        test("runtime payload retains callback fields array capacity bytes and complete int blocks", () =>
        {
            using var f = new Fixture();
            using var plan = f.Capture();
            using var runtime = SemanticRuntimeSnapshot.Capture(f.Graph, plan, 200000, 3000000, 256L * 1024 * 1024);
            f.Runtime.Cache = 41; f.Runtime.Link = f.Second; f.First.Runtime = null;
            var data = (object[])f.First.Payload;
            ((byte[])data[1])[2] = 19; ((int[])data[2])[3] = 100;
            check(runtime.Differences(20).Count == 5 && f.Graph.Differences(20).Count == 5);
            for (int i = 0; i < 6; i++)
            {
                f.Graph.Restore();
                check(runtime.Differences().Count == 0 && f.Graph.Differences().Count == 0);
                f.Runtime.Cache = i + 1;
            }
            f.Graph.Restore();
        });
        test("runtime payload owns value tokens and retention after the old graph is disposed", () =>
        {
            using var f = new Fixture();
            using var plan = f.Capture();
            using var runtime = SemanticRuntimeSnapshot.Capture(f.Graph, plan, 200000, 3000000, 256L * 1024 * 1024);
            f.Graph.Dispose();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            check(runtime.Differences().Count == 0);
            f.Runtime.Cache = 99;
            check(runtime.Differences().Count == 1);
            f.Runtime.Cache = 0;
            check(runtime.Differences().Count == 0);
        });
        test("runtime snapshots isolate simultaneous past and future values and disposal", () =>
        {
            using var f = new Fixture();
            using var plan = f.Capture();
            var first = SemanticRuntimeSnapshot.Capture(f.Graph, plan, 200000, 3000000, 256L * 1024 * 1024);
            using var second = SemanticRuntimeSnapshot.Capture(f.Graph, plan, 200000, 3000000, 256L * 1024 * 1024);
            f.Runtime.Cache = 31;
            using var futureGraph = StateGraph.Capture(f.Graph.CreateIndependentAccess(),
                f.Graph.Objects.Where(f.Graph.IsCapturedRoot).ToArray());
            using var futureBoundary = SemanticEntityBoundaryPlan.Capture(futureGraph, f.Entities,
                ManagedObjects.Get, ManagedObjects.Id, 200000, 3000000);
            using var future = SemanticRuntimeSnapshot.Capture(futureGraph, futureBoundary, 200000, 3000000, 256L * 1024 * 1024);
            first.Dispose();
            check(second.Differences().Count == 1 && f.Graph.Differences().Count == 1);
            check(future.Differences().Count == 0);
            f.Graph.Restore();
            check(second.Differences().Count == 0 && future.Differences().Count == 1);
            futureGraph.Restore();
            check(future.Differences().Count == 0 && second.Differences().Count == 1);
            f.Graph.Restore();
        });
        test("runtime capture rejects out of checkpoint references and changed scalar values", () =>
        {
            using var f = new Fixture();
            using var plan = f.Capture();
            f.Runtime.Cache = 21;
            Throws(() => SemanticRuntimeSnapshot.Capture(f.Graph, plan, 200000, 3000000, 256L * 1024 * 1024), check);
            f.Graph.Restore();
            f.Runtime.Link = new Item();
            Throws(() => SemanticRuntimeSnapshot.Capture(f.Graph, plan, 200000, 3000000, 256L * 1024 * 1024), check);
            f.Graph.Restore();
            using var runtime = SemanticRuntimeSnapshot.Capture(f.Graph, plan, 200000, 3000000, 256L * 1024 * 1024);
            check(runtime.Differences().Count == 0);
        });
        test("runtime capture object slot byte budgets fail without damaging retained captures", () =>
        {
            using var f = new Fixture();
            using var plan = f.Capture();
            using var old = SemanticRuntimeSnapshot.Capture(f.Graph, plan, 200000, 3000000, 256L * 1024 * 1024);
            Throws(() => SemanticRuntimeSnapshot.Capture(f.Graph, plan, f.Graph.ObjectCount - 1, 3000000, 256L * 1024 * 1024), check);
            Throws(() => SemanticRuntimeSnapshot.Capture(f.Graph, plan, 200000, old.SlotCount - 1, old.ByteCount), check);
            Throws(() => SemanticRuntimeSnapshot.Capture(f.Graph, plan, 200000, old.SlotCount, old.ByteCount - 1), check);
            using var exact = SemanticRuntimeSnapshot.Capture(f.Graph, plan, f.Graph.ObjectCount, old.SlotCount, old.ByteCount);
            check(old.Differences().Count == 0 && exact.Differences().Count == 0 && f.Graph.Differences().Count == 0);
        });
        test("runtime payload forbids ordinary and block restoration even by internal graph access", () =>
        {
            using var f = new Fixture();
            using var plan = f.Capture();
            using var runtime = SemanticRuntimeSnapshot.Capture(f.Graph, plan, 200000, 3000000, 256L * 1024 * 1024);
            var graph = (StateGraph)typeof(SemanticRuntimeSnapshot).GetField("_graph",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(runtime);
            f.Runtime.Cache = 83;
            Throws(graph.Restore, check);
            check(f.Runtime.Cache == 83);
            var blocks = (System.Collections.IEnumerable)typeof(StateGraph).GetField("_blocks",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(graph);
            foreach (var value in blocks)
            {
                var block = (IStateBlock)value.GetType().GetField("Item3").GetValue(value);
                Throws(block.RestoreValues, check);
                Throws(block.RestoreReferences, check);
            }
            f.Graph.Restore();
            check(runtime.Differences().Count == 0);
        });
        test("independent runtime access clones selected root surfaces without widening them", () =>
        {
            var owner = new Runtime();
            var access = new ManagedStateAccess(null, typeof(Runtime).Assembly.GetName().Name);
            access.AddRoot(ManagedObjects.Id(owner), "Cache");
            using var graph = StateGraph.Capture(access, new[] { ManagedObjects.Id(owner) });
            using var boundary = SemanticEntityBoundaryPlan.Capture(graph, Array.Empty<(IntPtr, SemanticSlotSchema)>(),
                ManagedObjects.Get, ManagedObjects.Id, 200000, 3000000);
            using var runtime = SemanticRuntimeSnapshot.Capture(graph, boundary, 1, 1, 8);
            owner.Link = new Item();
            check(runtime.ObjectCount == 1 && runtime.SlotCount == 1 && runtime.Differences().Count == 0);
            owner.Cache = 1;
            check(runtime.Differences().Count == 1);
        });
        test("runtime payload preserves dictionary storage cached views and active enumerator version", () =>
        {
            var map = new Dictionary<string, Item> { ["a"] = new Item(), ["b"] = new Item() };
            var enumerator = map.GetEnumerator();
            check(enumerator.MoveNext());
            var roots = new object[] { map, map.Keys, map.Values, enumerator };
            var access = new ManagedStateAccess(null, typeof(Item).Assembly.GetName().Name);
            access.AddRoot(ManagedObjects.Id(roots));
            using var graph = StateGraph.Capture(access, new[] { ManagedObjects.Id(roots) });
            using var boundary = SemanticEntityBoundaryPlan.Capture(graph, Array.Empty<(IntPtr, SemanticSlotSchema)>(),
                ManagedObjects.Get, ManagedObjects.Id, 200000, 3000000);
            using var runtime = SemanticRuntimeSnapshot.Capture(graph, boundary, 200000, 3000000, 256L * 1024 * 1024);
            var value = map["a"];
            map.Remove("a"); map.Add("a", value);
            check(runtime.Differences().Count != 0 && graph.Differences().Count != 0);
            graph.Restore();
            check(runtime.Differences().Count == 0 && graph.Differences().Count == 0
                && ReferenceEquals(((Dictionary<string, Item>.KeyCollection)roots[1]), map.Keys));
            check(enumerator.MoveNext());
        });
        test("runtime payload compares floating point bits and nullable array values independently", () =>
        {
            var singles = new[] { BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)), BitConverter.Int32BitsToSingle(0x7fc00001) };
            var doubles = new double?[] { null, BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000000)) };
            var root = new object[] { singles, doubles };
            var access = new ManagedStateAccess(null, typeof(Item).Assembly.GetName().Name);
            access.AddRoot(ManagedObjects.Id(root));
            using var graph = StateGraph.Capture(access, new[] { ManagedObjects.Id(root) });
            using var boundary = SemanticEntityBoundaryPlan.Capture(graph, Array.Empty<(IntPtr, SemanticSlotSchema)>(),
                ManagedObjects.Get, ManagedObjects.Id, 200000, 3000000);
            using var runtime = SemanticRuntimeSnapshot.Capture(graph, boundary, 200000, 3000000, 256L * 1024 * 1024);
            singles[0] = 0; singles[1] = BitConverter.Int32BitsToSingle(0x7fc00002); doubles[1] = 0;
            check(runtime.Differences().Count == 3);
            graph.Restore();
            check(runtime.Differences().Count == 0 && BitConverter.SingleToInt32Bits(singles[0]) == int.MinValue);
        });
        test("entity handles preserve original identities cycles closure links and complete runtime partition", () =>
        {
            using var f = new Fixture();
            using var plan = f.Capture();
            var first = plan.GetHandle(ManagedObjects.Id(f.First));
            var second = plan.GetHandle(ManagedObjects.Id(f.Second));
            check(ReferenceEquals(plan.Resolve(first, typeof(Item)), f.First)
                && ReferenceEquals(plan.Resolve(second, typeof(Item)), f.Second)
                && plan.EntityCount == 2 && plan.RootEntities == 1
                && plan.EntitySlots == 6 && plan.RuntimeSlots == f.Graph.SlotCount - 6
                && plan.TotalSlots == f.Graph.SlotCount && plan.RuntimeHandleLinks >= 3 && plan.EntityHandleLinks == 2
                && plan.RuntimeReachableObjects == f.Graph.ObjectCount);
            check(plan.RoleOf(first, Fixture.Name("Runtime")) == SemanticFieldRole.Runtime
                && plan.RoleOf(first, Fixture.Name("Next")) == SemanticFieldRole.Entity);
            plan.Validate(ManagedObjects.Get);
        });
        test("entity boundary seeds include original roots and semantic outbound references with deduplication", () =>
        {
            using var f = new Fixture();
            using var plan = f.Capture();
            var seeds = plan.RuntimeSeeds();
            check(seeds.Length == plan.RuntimeSeedCount && seeds.Distinct().Count() == seeds.Length
                && seeds.Contains(ManagedObjects.Id(f.First)) && seeds.Contains(ManagedObjects.Id(f.Second))
                && seeds.Contains(ManagedObjects.Id(f.First.Payload))
                && seeds.Count(id => f.Graph.IsCapturedRoot(id)) == 2);
            seeds[0] = IntPtr.Zero;
            check(!plan.RuntimeSeeds().Contains(IntPtr.Zero));
        });
        test("entity handles reject cross-node default wrong-type and unknown identities", () =>
        {
            using var f = new Fixture();
            using var first = f.Capture();
            using var second = f.Capture();
            var handle = first.GetHandle(ManagedObjects.Id(f.First));
            Throws(() => second.Resolve(handle, typeof(Item)), check);
            Throws(() => first.Resolve(handle, typeof(object)), check);
            Throws(() => first.Resolve(default, typeof(Item)), check);
            Throws(() => first.GetHandle(ManagedObjects.Id(f.Runtime)), check);
            Throws(() => second.RoleOf(handle, Fixture.Name("Value")), check);
        });
        test("entity boundary reads only captured topology and validates identities without rereading values", () =>
        {
            using var f = new Fixture();
            using var first = f.Capture();
            f.First.Next = null; f.Runtime.Link = f.Second; f.First.Value = 42;
            using var changed = f.Capture();
            check(changed.EntitySlots == first.EntitySlots && changed.RuntimeHandleLinks == first.RuntimeHandleLinks
                && changed.EntityHandleLinks == first.EntityHandleLinks && changed.RuntimeSeedCount == first.RuntimeSeedCount);
            changed.Validate(ManagedObjects.Get);
            Throws(() => changed.Validate(id => id == ManagedObjects.Id(f.First) ? f.Second : ManagedObjects.Get(id)), check);
            f.Graph.Restore();
            check(f.Graph.Differences().Count == 0);
        });
        test("entity boundary fails closed for missing extra visual and wrong-kind schemas", () =>
        {
            using var f = new Fixture();
            void Reject(params (string, bool, SemanticFieldRole)[] fields)
            {
                var schema = new SemanticSlotSchema(fields);
                Throws(() => SemanticEntityBoundaryPlan.Capture(f.Graph,
                    new[] { (ManagedObjects.Id(f.First), schema) }, ManagedObjects.Get, ManagedObjects.Id, 200000, 3000000), check);
            }
            Reject((Fixture.Name("Value"), false, SemanticFieldRole.Entity));
            Reject((Fixture.Name("Value"), false, SemanticFieldRole.Entity),
                (Fixture.Name("Next"), true, SemanticFieldRole.Entity), (Fixture.Name("Runtime"), true, SemanticFieldRole.Runtime),
                ("missing", false, SemanticFieldRole.Runtime));
            Reject((Fixture.Name("Value"), true, SemanticFieldRole.Entity),
                (Fixture.Name("Next"), true, SemanticFieldRole.Entity), (Fixture.Name("Runtime"), true, SemanticFieldRole.Runtime));
            Reject((Fixture.Name("Value"), false, SemanticFieldRole.Visual),
                (Fixture.Name("Next"), true, SemanticFieldRole.Entity), (Fixture.Name("Runtime"), true, SemanticFieldRole.Runtime));
        });
        test("entity boundary rejects invalid duplicate and mismatched identities", () =>
        {
            using var f = new Fixture();
            void Reject(IEnumerable<(IntPtr, SemanticSlotSchema)> entities, Func<object, IntPtr> identity)
                => Throws(() => SemanticEntityBoundaryPlan.Capture(f.Graph, entities, ManagedObjects.Get, identity, 200000, 3000000), check);
            Reject(f.Entities.Concat(f.Entities), ManagedObjects.Id);
            Reject(new[] { (IntPtr.Zero, f.Schema) }, ManagedObjects.Id);
            Reject(new[] { (new IntPtr(long.MaxValue), f.Schema) }, ManagedObjects.Id);
            Reject(new[] { (ManagedObjects.Id(f.First), (SemanticSlotSchema)null) }, ManagedObjects.Id);
            Reject(f.Entities, _ => IntPtr.Zero);
        });
        test("entity boundary rejects object schema and full scan limits without affecting old records", () =>
        {
            using var f = new Fixture();
            using var old = f.Capture();
            Throws(() => f.Capture(maxObjects: 1), check);
            Throws(() => f.Capture(maxSlots: 5), check);
            Throws(() => f.Capture(maxSlots: f.Graph.SlotCount - 1), check);
            old.Validate(ManagedObjects.Get);
            check(f.Graph.Differences().Count == 0 && ReferenceEquals(old.Resolve(
                old.GetHandle(ManagedObjects.Id(f.First)), typeof(Item)), f.First));
        });
        test("disposed entity handles cannot be reused and another node remains valid", () =>
        {
            using var f = new Fixture();
            var first = f.Capture();
            using var second = f.Capture();
            var handle = first.GetHandle(ManagedObjects.Id(f.First));
            first.Dispose(); first.Dispose();
            try { first.Resolve(handle, typeof(Item)); throw new Exception("Expected disposed rejection."); }
            catch (ObjectDisposedException) { check(true); }
            Throws(() => second.Resolve(handle, typeof(Item)), check);
            second.Validate(ManagedObjects.Get);
            check(first.EntityCount == 0 && first.RuntimeSeedCount == 0);
        });
    }
}
