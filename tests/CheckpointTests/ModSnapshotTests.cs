using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using SnapshotFixtures;
using ProbablyStolenTools;

public class FixtureGameItem
{
    public int NativeNumber;
}

internal sealed class FixtureNativePointer
{
    internal IntPtr Handle = (IntPtr)1;
}

internal static class ModSnapshotTests
{
    private sealed class GameRoot { internal object Payload; internal Action Callback; }
    private static (StateGraph Graph, SnapshotCoverage Coverage) Capture(object value)
    {
        var root = new GameRoot { Payload = value };
        var access = new ManagedStateAccess(gameAssembly: typeof(GameRoot).Assembly.GetName().Name);
        var id = ManagedObjects.Id(root);
        access.AddRoot(id);
        return (StateGraph.Capture(access, new[] { id }), access.Coverage);
    }

    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("foreign data cycles, collection edits and readonly children restore without mod equality", () =>
        {
            var child = new ModData { Number = 3 };
            var data = new ModData(child) { Number = 1, Link = child };
            child.Link = data;
            data.Items.Add(child);
            data.Map["old"] = child;
            var snapshot = Capture(data);
            using var graph = snapshot.Graph;
            data.Number = 90; child.Number = 30;
            data.Link = new ModData(); data.Items.Clear(); data.Map.Clear();
            for (int i = 0; i < 50; i++)
            {
                data.Items.Add(new ModData());
                data.Map[i.ToString()] = new ModData();
            }
            graph.Restore();
            check(data.Number == 1 && child.Number == 3 && ReferenceEquals(data.Link, child));
            check(data.Items.Count == 1 && ReferenceEquals(data.Items[0], child));
            check(data.Map.Count == 1 && ReferenceEquals(data.Map["old"], child));
            check(ReferenceEquals(child.Link, data) && ReferenceEquals(data.ReadonlyChild, child));
            check(snapshot.Coverage.ModTypes > 0 && graph.Differences().Count == 0);
        });
        test("foreign multicast closure one-shot flags reset without replaying callbacks", () =>
        {
            var one = new ModData { Number = 1 };
            var two = new ModData { Number = 2 };
            Action original = ClosureFactory.Once(one);
            original += ClosureFactory.Once(two);
            var root = new GameRoot { Callback = original };
            var access = new ManagedStateAccess(gameAssembly: typeof(GameRoot).Assembly.GetName().Name);
            var id = ManagedObjects.Id(root);
            access.AddRoot(id);
            using var graph = StateGraph.Capture(access, new[] { id });
            original(); original(); root.Callback = () => { };
            graph.Restore();
            check(one.Number == 1 && two.Number == 2 && ReferenceEquals(root.Callback, original));
            root.Callback(); root.Callback();
            check(one.Number == 2 && two.Number == 3);
        });
        test("foreign structs restore values and references while unsupported nullable fields remain live", () =>
        {
            var child = new ModData { Number = 2 };
            var data = new ModData { Pair = new RefValue { Number = 3, Link = child }, Count = 4 };
            var snapshot = Capture(data);
            using var graph = snapshot.Graph;
            data.Pair = new RefValue { Number = 30, Link = new ModData() };
            data.Count = null; data.Optional = new RefValue { Number = 7 }; child.Number = 20;
            graph.Restore();
            check(data.Pair.Number == 3 && ReferenceEquals(data.Pair.Link, child) && child.Number == 2);
            check(data.Count == 4 && data.Optional.Value.Number == 7 && snapshot.Coverage.Skipped > 0);
        });
        test("foreign native handles, tasks, streams and coroutine state stay outside snapshots", () =>
        {
            var firstTask = Task.FromResult(1);
            var nextTask = Task.FromResult(2);
            using var firstStream = new MemoryStream();
            using var nextStream = new MemoryStream();
            var data = new ModData { Number = 1, Handle = (IntPtr)5, OptionalHandle = (IntPtr)6,
                Pending = firstTask, Stream = firstStream, Payload = firstTask };
            var iterator = ClosureFactory.Coroutine(data).GetEnumerator();
            data.Iterator = iterator;
            var snapshot = Capture(data);
            using var graph = snapshot.Graph;
            data.Number = 8; data.Handle = (IntPtr)50; data.OptionalHandle = (IntPtr)60;
            data.Pending = nextTask; data.Payload = nextTask; data.Stream = nextStream;
            iterator.MoveNext();
            graph.Restore();
            check(data.Number == 1 && data.Handle == (IntPtr)50 && data.OptionalHandle == (IntPtr)60);
            check(ReferenceEquals(data.Pending, nextTask) && ReferenceEquals(data.Payload, nextTask));
            check(ReferenceEquals(data.Stream, nextStream) && iterator.Current == 9);
            check(!graph.Contains(ManagedObjects.Id(firstTask)) && !graph.Contains(ManagedObjects.Id(iterator)));
            check(snapshot.Coverage.Skipped >= 6 && graph.Differences().Count == 0);
        });
        test("foreign unsafe pointer fields degrade without aborting other field capture", () =>
        {
            var data = new PointerData { Number = 2 };
            var snapshot = Capture(data);
            using var graph = snapshot.Graph;
            data.Number = 9; graph.Restore();
            check(data.Number == 2 && snapshot.Coverage.Skipped == 1);
        });
        test("foreign data is not skipped just because its name contains Comparer", () =>
        {
            var data = new BusinessComparerData { Number = 1 };
            using var graph = Capture(data).Graph;
            data.Number = 8; graph.Restore();
            check(data.Number == 1);
        });
        test("capture and restore never invoke foreign constructors, static initializers or getters", () =>
        {
            var data = (NoConstruction)FormatterServices.GetUninitializedObject(typeof(NoConstruction));
            typeof(NoConstruction).GetField("Number").SetValue(data, 7);
            int initializations = ConstructionEvidence.Initializations;
            using var graph = Capture(data).Graph;
            typeof(NoConstruction).GetField("Number").SetValue(data, 9);
            graph.Restore();
            check((int)typeof(NoConstruction).GetField("Number").GetValue(data) == 7
                && ConstructionEvidence.Initializations == initializations);
        });
        test("foreign subclasses keep both native base fields and their own data", () =>
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("UnlistedItemMod"), AssemblyBuilderAccess.Run);
            var type = assembly.DefineDynamicModule("items").DefineType("UnlistedItem", TypeAttributes.Public, typeof(FixtureGameItem));
            type.DefineField("Extra", typeof(ModData), FieldAttributes.Public);
            var derived = type.CreateType();
            var item = (FixtureGameItem)Activator.CreateInstance(derived);
            var extra = new ModData { Number = 4 };
            item.NativeNumber = 3; derived.GetField("Extra").SetValue(item, extra);
            using var graph = Capture(item).Graph;
            item.NativeNumber = 30; extra.Number = 40; graph.Restore();
            check(item.NativeNumber == 3 && extra.Number == 4);
        });
        test("foreign graphs support reusable checkpoints in both directions without capturing mod statics", () =>
        {
            var data = new ModData { Number = 1 };
            ModData.StaticCurrent = data;
            using var first = Capture(data).Graph;
            data.Number = 2;
            using var second = Capture(data).Graph;
            var replacement = new ModData { Number = 9 };
            ModData.StaticCurrent = replacement;
            first.Restore(); check(data.Number == 1);
            second.Restore(); check(data.Number == 2);
            first.Restore(); check(data.Number == 1 && ReferenceEquals(ModData.StaticCurrent, replacement));
            ModData.StaticCurrent = null;
        });
        test("unsupported framework descendants in mod data are diagnosed without breaking the capture", () =>
        {
            var data = new ModData { Number = 1, Payload = new Version(1, 2, 3) };
            var snapshot = Capture(data);
            using var graph = snapshot.Graph;
            data.Number = 7; graph.Restore();
            check(data.Number == 1 && snapshot.Coverage.Details.Any(d => d.Contains("System.Version")));
        });
        test("coverage diagnostics are bounded even with many skipped fields", () =>
        {
            var data = Enumerable.Range(0, 100).Select(i => new PointerData { Number = i }).ToArray();
            var snapshot = Capture(data);
            using var graph = snapshot.Graph;
            check(snapshot.Coverage.Skipped == 100 && snapshot.Coverage.Details.Count == 32);
            data[90].Number = -1; graph.Restore(); check(data[90].Number == 90);
        });
        test("mixed object arrays retain slot indexes after filtering runtime references", () =>
        {
            var first = new ModData { Number = 1 };
            var last = new ModData { Number = 3 };
            object[] array = { first, Task.FromResult(2), last };
            var snapshot = Capture(array);
            using var graph = snapshot.Graph;
            var liveTask = Task.FromResult(9);
            array[0] = new ModData(); array[1] = liveTask; array[2] = new ModData();
            first.Number = 10; last.Number = 30; graph.Restore();
            check(ReferenceEquals(array[0], first) && ReferenceEquals(array[2], last));
            check(first.Number == 1 && last.Number == 3 && ReferenceEquals(array[1], liveTask));
            check(snapshot.Coverage.Details.Any(d => d.Contains("[1]: runtime reference"))
                && graph.Differences().Count == 0);
        });
        test("unsupported native pointer fields still reject capture rather than silently degrading", () =>
        {
            var data = new FixtureNativePointer();
            bool failed = false;
            try { using var graph = Capture(data).Graph; }
            catch (InvalidOperationException) { failed = true; }
            check(failed && data.Handle == (IntPtr)1);
        });
    }
}
