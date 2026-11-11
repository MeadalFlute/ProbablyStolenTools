#nullable enable
using System.Reflection;
using ProbablyStolenTools;

internal static class SemanticEntityRecordTests
{
    private class Item
    {
        public int Count = 4;
        public long Value = 123;
        public bool Enabled = true;
        public float Float = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
        public double Double = BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000000001));
        public object? Parent;
        public string Text = "same";
        public byte[] Shape = new byte[] { 1, 0, 2, 3 };
        public List<object?> Members = new();
        public virtual string Name => throw new Exception("Gameplay getter must not run");
        public override bool Equals(object? obj) => throw new Exception("Business Equals must not run");
        public override int GetHashCode() => throw new Exception("Business hash must not run");
    }
    private sealed class Derived : Item { public int Extra = 12; }
    private static FieldInfo[] Fields => SemanticEntitySnapshot.Schema(typeof(Item), true,
        ("Count", typeof(int)), ("Value", typeof(long)), ("Enabled", typeof(bool)),
        ("Float", typeof(float)), ("Double", typeof(double)), ("Parent", typeof(object)),
        ("Text", typeof(string)), ("Shape", typeof(byte[])), ("Members", typeof(List<object?>)));
    private static SemanticEntitySnapshot Capture(Item item, int objects = 200000, int slots = 3000000, long bytes = 268435456)
        => SemanticEntitySnapshot.Capture(new[] { new SemanticEntityProjection(item, Fields) },
            ManagedObjects.Id, _ => true, objects, slots, bytes);
    private static void Throws(Action action, Action<bool> check)
    {
        try { action(); throw new Exception("Expected failure"); }
        catch (InvalidOperationException) { check(true); }
    }
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("entity records preserve identity cycles scalar values and full shape bytes without gameplay APIs", () =>
        {
            var item = new Item();
            item.Parent = item;
            item.Members.AddRange(new object?[] { item, null, item });
            using var snapshot = Capture(item);
            check(snapshot.RecordCount == 1 && snapshot.SequenceCount == 2 && snapshot.LogicalSlots == 16
                && snapshot.LogicalBytes == 9 * 8 + 7 * IntPtr.Size && snapshot.Differences(out _) == 0);
            item.Count++; item.Value++; item.Enabled = false;
            item.Shape[2] = 7;
            check(snapshot.Differences(out _) == 4);
        });
        test("entity records compare same-content strings and member aliases by identity", () =>
        {
            var item = new Item();
            item.Parent = new Item();
            item.Members.AddRange(new object?[] { item, item.Parent, null });
            using var snapshot = Capture(item);
            item.Text = new string(item.Text.ToCharArray());
            (item.Members[0], item.Members[1]) = (item.Members[1], item.Members[0]);
            check(snapshot.Differences(out _) == 3);
        });
        test("entity records compare floating signed zero and NaN payload bits", () =>
        {
            var item = new Item();
            using var snapshot = Capture(item);
            item.Float = 0;
            item.Double = BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000000002));
            check(snapshot.Differences(out _) == 2);
        });
        test("entity record difference samples do not stop scanning late shape mutations", () =>
        {
            var item = new Item { Shape = Enumerable.Repeat((byte)1, 90).ToArray() };
            using var snapshot = Capture(item);
            item.Count++;
            Array.Fill(item.Shape, (byte)2);
            check(snapshot.Differences(out var sample, 1) == 91 && sample.Count == 1);
            check(snapshot.Differences(out sample, 0) == 91 && sample.Count == 0);
        });
        test("entity records retain shared sequences once but compare every reference field", () =>
        {
            var first = new Item();
            var second = new Item { Shape = first.Shape, Members = first.Members };
            using var snapshot = SemanticEntitySnapshot.Capture(new[]
            {
                new SemanticEntityProjection(first, Fields), new SemanticEntityProjection(second, Fields)
            }, ManagedObjects.Id, _ => true);
            check(snapshot.RecordCount == 2 && snapshot.SequenceCount == 2);
            first.Shape = (byte[])first.Shape.Clone();
            check(snapshot.Differences(out _) == 1);
            second.Shape[3] = 8;
            check(snapshot.Differences(out _) == 2);
        });
        test("entity records preserve list ordering nulls and length without rebuilding", () =>
        {
            var item = new Item();
            item.Members.AddRange(new object?[] { item, null });
            using var snapshot = Capture(item);
            item.Members.Clear();
            check(snapshot.Differences(out _) == 1);
            item.Members.AddRange(new object?[] { item, null });
            check(snapshot.Differences(out _) == 0);
        });
        test("entity records enforce object slot and token byte budgets before sequence payload", () =>
        {
            var item = new Item();
            using var baseline = Capture(item);
            Throws(() => Capture(item, slots: baseline.LogicalSlots - 1), check);
            Throws(() => Capture(item, bytes: baseline.LogicalBytes - 1), check);
            Throws(() => Capture(item, objects: 1), check);
            using var exact = Capture(item, slots: baseline.LogicalSlots, bytes: baseline.LogicalBytes);
            check(exact.Differences(out _) == 0);
        });
        test("entity record schemas reject added missing duplicate and wrong-typed fields", () =>
        {
            Throws(() => SemanticEntitySnapshot.Schema(typeof(Item), true, ("Count", typeof(int))), check);
            Throws(() => SemanticEntitySnapshot.Schema(typeof(Item), false, ("Missing", typeof(int))), check);
            Throws(() => SemanticEntitySnapshot.Schema(typeof(Item), false, ("Count", typeof(long))), check);
            Throws(() => SemanticEntitySnapshot.Schema(typeof(Item), false, ("Count", typeof(int)), ("Count", typeof(int))), check);
        });
        test("entity records reject missing legacy coverage and duplicate record identities", () =>
        {
            var item = new Item();
            Throws(() => SemanticEntitySnapshot.Capture(new[] { new SemanticEntityProjection(item, Fields) },
                ManagedObjects.Id, id => id != ManagedObjects.Id(item.Shape)), check);
            Throws(() => SemanticEntitySnapshot.Capture(new[]
            {
                new SemanticEntityProjection(item, Fields), new SemanticEntityProjection(item, Fields)
            }, ManagedObjects.Id, _ => true), check);
        });
        test("entity records isolate base projection schema and lifetime across independent nodes", () =>
        {
            var item = new Derived();
            var first = Capture(item);
            item.Count++;
            using var second = Capture(item);
            item.Extra++;
            check(first.Differences(out _) == 1 && second.Differences(out _) == 0);
            first.Dispose(); first.Dispose();
            try { first.Differences(out _); throw new Exception("Expected disposed failure"); }
            catch (ObjectDisposedException) { check(second.Differences(out _) == 0); }
        });
        test("entity records have no restore or write entry points", () =>
        {
            check(!typeof(SemanticEntitySnapshot).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(m => m.Name.Contains("Restore") || m.Name.Contains("Write")));
        });
    }
}
