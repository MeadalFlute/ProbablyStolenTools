using ProbablyStolenTools;

internal static class SemanticFieldBlockTests
{
    private enum Mode : long { First = 1 }
    private class Base { public long Price = long.MinValue; public Action Callback; }
    private class Item : Base
    {
        public int Count = 3;
        public byte Outside = 255;
        public bool Flag = true;
        public float Fraction = -0.0f;
        public double Exact = BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000001234));
        public Mode Style = (Mode)long.MinValue;
        public string Name = "original";
        public Item Link;
        public List<Item> Members = new();
        public Dictionary<string, int> Map = new() { ["old"] = 7 };
        public int[] Buckets = new[] { 1, 2, 3 };
        public int GameplayGetter => throw new Exception("Gameplay getter must not run.");
    }
    private sealed class Derived : Item { public int Extra = 7; }
    private sealed class Bad { public decimal Unsupported = 1; }
    private sealed class ReadonlyOwner
    {
        public readonly string Text = "original";
        public readonly int Number = 7;
    }
    private static SemanticEntityFieldPlan[] Plans() => new[]
    {
        new SemanticEntityFieldPlan(typeof(Item),
            new("Count", typeof(int), SemanticFieldRole.Entity), new("Outside", typeof(byte), SemanticFieldRole.Entity),
            new("Flag", typeof(bool), SemanticFieldRole.Entity), new("Fraction", typeof(float), SemanticFieldRole.Entity),
            new("Exact", typeof(double), SemanticFieldRole.Entity), new("Style", typeof(Mode), SemanticFieldRole.Entity),
            new("Name", typeof(string), SemanticFieldRole.Entity), new("Link", typeof(Item), SemanticFieldRole.Entity),
            new("Members", typeof(List<Item>), SemanticFieldRole.Entity), new("Map", typeof(Dictionary<string, int>), SemanticFieldRole.Runtime),
            new("Buckets", typeof(int[]), SemanticFieldRole.Runtime)),
        new SemanticEntityFieldPlan(typeof(Base),
            new("Price", typeof(long), SemanticFieldRole.Entity), new("Callback", typeof(Action), SemanticFieldRole.Runtime))
    };
    private static SemanticFieldBlockFactory Factory()
        => new(new[] { (typeof(Item), Plans()) });
    private static ManagedStateAccess Access(SemanticFieldBlockFactory factory = null)
    {
        var access = new ManagedStateAccess(null, typeof(Item).Assembly.GetName().Name, true, true, true, true);
        if (factory != null) access.BlockCapture = factory.Capture;
        return access;
    }
    private static StateGraph Capture(ManagedStateAccess access, params Item[] items)
    {
        foreach (var item in items) access.AddRoot(ManagedObjects.Id(item));
        return StateGraph.Capture(access, items.Select(ManagedObjects.Id));
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("single-pass exact blocks preserve complete inherited fields cycles callbacks and physical collections", () =>
        {
            var factory = Factory();
            var a = new Item { Callback = () => throw new Exception("Business callback invoked.") };
            var b = new Item { Count = 9, Link = a }; a.Link = b;
            a.Members.Add(a); a.Members.Add(b); a.Members.Add(a);
            var members = a.Members; var map = a.Map; var buckets = a.Buckets; var callback = a.Callback;
            using var graph = Capture(Access(factory), a, b);
            using var oracle = new SemanticCaptureOracle(graph, Capture(Access(), a, b));
            check(factory.BlockCount == 2 && factory.SlotCount == 26 && factory.RuntimeSlotCount == 6);
            for (int i = 0; i < 8; i++)
            {
                a.Count = 91; a.Price = 92; a.Link = a; a.Callback = null;
                members.Clear(); members.Add(b); a.Members = new(); map.Clear(); map["new"] = 99; a.Map = new();
                buckets[1] = 42; a.Buckets = new[] { 10 }; b.Count = 93;
                graph.Restore();
                oracle.VerifyRestore();
                check(graph.Differences().Count == 0 && a.Count == 3 && b.Count == 9 && ReferenceEquals(a.Link, b)
                    && ReferenceEquals(a.Members, members) && members.Count == 3 && ReferenceEquals(members[2], a)
                    && ReferenceEquals(a.Callback, callback) && ReferenceEquals(a.Map, map) && map.Count == 1
                    && map["old"] == 7 && ReferenceEquals(a.Buckets, buckets) && buckets[1] == 2);
            }
        });
        test("single-pass blocks compare bitwise floating point wide enum and reference identity", () =>
        {
            var item = new Item(); var original = item.Name;
            using var graph = Capture(Access(Factory()), item);
            item.Name = new string(original.ToCharArray()); item.Fraction = 0; item.Exact = double.NaN;
            item.Style = Mode.First; item.Outside = 0; item.Flag = false; item.Price = 1;
            check(graph.Differences().Count == 7);
            graph.Restore();
            check(ReferenceEquals(item.Name, original) && BitConverter.SingleToInt32Bits(item.Fraction) == int.MinValue
                && BitConverter.DoubleToInt64Bits(item.Exact) == unchecked((long)0x7ff8000000001234)
                && item.Style == (Mode)long.MinValue && item.Outside == 255 && item.Flag && item.Price == long.MinValue);
        });
        test("single-pass block owner repair and late full fallback retain complete oracle equivalence", () =>
        {
            var a = new Item(); var b = new Item { Count = 8 }; a.Link = b;
            using var graph = Capture(Access(Factory()), a);
            using var oracle = new SemanticCaptureOracle(graph, Capture(Access(), a));
            graph.Restore(); a.Count = 70; a.Name = null;
            var repair = graph.PrepareReconciliation();
            check(repair.Mode == "owners" && repair.ChangedSlots == 2 && repair.PlannedSlots == 13);
            repair.Apply(); b.Count = 77;
            int visualRefresh = 0;
            check(graph.VerifyReconciliation(repair, () => visualRefresh++, out bool fallback).Count == 0
                && fallback && visualRefresh == 1);
            oracle.VerifyRestore();
        });
        test("single-pass block repair falls back to full write at owner slot budget", () =>
        {
            var item = new Item();
            using var graph = Capture(Access(Factory()), item);
            item.Count = 90;
            var repair = graph.PrepareReconciliation(maxSlots: 12);
            check(repair.Mode == "full" && repair.Reason == "slot-limit");
            repair.Apply();
            check(graph.VerifyReconciliation(repair, () => { }, out bool fallback).Count == 0 && !fallback);
        });
        test("single-pass block two nodes retain separate values references and disposal lifetimes", () =>
        {
            var a = new Item(); var b = new Item { Count = 8 };
            a.Members.Add(a);
            var earlier = Capture(Access(Factory()), a);
            using var firstOracle = new SemanticCaptureOracle(earlier, Capture(Access(), a));
            a.Count = 19; a.Members.Clear(); a.Members.Add(b); a.Link = b; a.Map["second"] = 8;
            using var later = Capture(Access(Factory()), a);
            using var secondOracle = new SemanticCaptureOracle(later, Capture(Access(), a));
            for (int i = 0; i < 6; i++)
            {
                earlier.Restore(); firstOracle.VerifyRestore();
                check(a.Count == 3 && a.Members.Count == 1 && ReferenceEquals(a.Members[0], a) && a.Link == null);
                later.Restore(); secondOracle.VerifyRestore();
                check(a.Count == 19 && ReferenceEquals(a.Members[0], b) && ReferenceEquals(a.Link, b) && a.Map["second"] == 8);
            }
            earlier.Dispose(); firstOracle.Dispose(); a.Count = 90;
            later.Restore(); secondOracle.VerifyRestore(); check(a.Count == 19);
            Throws<ObjectDisposedException>(firstOracle.VerifyRestore);
        });
        test("single-pass blocks never apply exact base schema to unknown subclasses", () =>
        {
            var factory = Factory(); var item = new Derived();
            using var graph = Capture(Access(factory), item);
            check(factory.BlockCount == 0);
            item.Extra = 90; item.Count = 91; graph.Restore();
            check(item.Extra == 7 && item.Count == 3 && graph.Differences().Count == 0);
        });
        test("single-pass selected root remains filtered legacy even with matching factory", () =>
        {
            var factory = Factory(); var item = new Item(); var access = Access(factory);
            access.AddRoot(ManagedObjects.Id(item), "Count");
            using var graph = StateGraph.Capture(access, new[] { ManagedObjects.Id(item) });
            item.Count = 90; item.Price = 91; graph.Restore();
            check(factory.BlockCount == 0 && graph.SlotCount == 1 && item.Count == 3 && item.Price == 91);
        });
        test("single-pass schema mismatch missing extra duplicate and kind mismatch never captures partial block", () =>
        {
            var item = new Item(); using var access = Access();
            access.Retain(ManagedObjects.Id(item));
            var slots = access.Describe(ManagedObjects.Id(item));
            var factory = Factory();
            check(factory.Capture(item, slots.Skip(1).ToArray()) == null);
            check(factory.Capture(item, slots.Append(slots[0]).ToArray()) == null);
            var duplicate = slots.ToArray(); duplicate[1] = duplicate[0];
            check(factory.Capture(item, duplicate) == null);
            var wrong = slots.ToArray(); wrong[0] = wrong[0] with { IsReference = !wrong[0].IsReference };
            check(factory.Capture(item, wrong) == null);
            check(factory.FallbackCount == 4 && factory.BlockCount == 0);
            check(factory.Capture(item, slots) != null && factory.BlockCount == 1);
        });
        test("single-pass frozen block layout survives factory descriptor changes", () =>
        {
            var item = new Item(); var factory = Factory(); using var access = Access();
            access.Retain(ManagedObjects.Id(item)); var slots = access.Describe(ManagedObjects.Id(item));
            var block = factory.Capture(item, slots);
            var link = slots.Single(slot => slot.Name.EndsWith(".Link"));
            check(factory.Capture(item, slots.Take(1).ToArray()) == null);
            check(block.ReferenceAt(link) == IntPtr.Zero);
            Throws<InvalidOperationException>(() => block.ReferenceAt(link with { Offset = -1 }));
            item.Count = 90; block.RestoreValues(); check(item.Count == 3);
        });
        test("single-pass block difference cap still counts all changed fields", () =>
        {
            var item = new Item(); using var access = Access();
            access.Retain(ManagedObjects.Id(item));
            var block = Factory().Capture(item, access.Describe(ManagedObjects.Id(item)));
            item.Count = 90; item.Price = 91; item.Name = null;
            var result = new List<string>();
            check(block.CollectDifferences(result, 1) == 3 && result.Count == 1);
            result.Clear(); check(block.CollectDifferences(result, 0) == 3 && result.Count == 0);
        });
        test("single-pass blocks retain identical graph budget boundaries to ordinary storage", () =>
        {
            var item = new Item();
            using var legacy = Capture(Access(), item);
            var roots = new[] { ManagedObjects.Id(item) };
            using var exact = StateGraph.Capture(Access(Factory()), roots, legacy.ObjectCount, legacy.SlotCount, legacy.ByteCount);
            foreach (bool blocks in new[] { false, true })
            {
                ManagedStateAccess A() => Access(blocks ? Factory() : null);
                Throws<SnapshotLimitException>(() => StateGraph.Capture(A(), roots, legacy.ObjectCount - 1));
                Throws<SnapshotLimitException>(() => StateGraph.Capture(A(), roots, maxSlots: legacy.SlotCount - 1));
                Throws<SnapshotLimitException>(() => StateGraph.Capture(A(), roots, maxBytes: legacy.ByteCount - 1));
            }
            check(exact.ByteCount == legacy.ByteCount && exact.SlotCount == legacy.SlotCount);
        });
        test("single-pass independent oracle rejects mismatched capture and detects candidate omissions after restore", () =>
        {
            var item = new Item();
            using var graph = Capture(Access(Factory()), item);
            var different = new Item();
            Throws<InvalidOperationException>(() => new SemanticCaptureOracle(graph, Capture(Access(), different)));
            using var oracle = new SemanticCaptureOracle(graph, Capture(Access(), item));
            item.Callback = () => { };
            Throws<InvalidOperationException>(oracle.VerifyRestore);
            graph.Restore(); oracle.VerifyRestore(); check(item.Callback == null);
        });
        test("single-pass unsupported scalar and invalid manifest chains fail before installing factory", () =>
        {
            Throws<InvalidOperationException>(() => new SemanticFieldBlockFactory(new[]
            {
                (typeof(Bad), new[] { new SemanticEntityFieldPlan(typeof(Bad),
                    new SemanticFieldRule("Unsupported", typeof(decimal), SemanticFieldRole.Entity)) })
            }));
            Throws<InvalidOperationException>(() => new SemanticFieldBlockFactory(new[] { (typeof(Item), Array.Empty<SemanticEntityFieldPlan>()) }));
            check(true);
        });
        test("single-pass census does not construct or retain any field blocks", () =>
        {
            var factory = Factory(); var item = new Item(); using var access = Access(factory);
            using var census = StateGraph.Census(access, new[] { ManagedObjects.Id(item) });
            check(factory.BlockCount == 0 && census.SlotCount > 0);
        });
        test("single-pass reflection and boxed comparison fallbacks match cached reads across warm restores", () =>
        {
            foreach (bool fast in new[] { false, true })
                foreach (bool typed in new[] { false, true })
                {
                    var item = new Item();
                    var factory = new SemanticFieldBlockFactory(new[] { (typeof(Item), Plans()) }, fast, typed);
                    using var graph = Capture(Access(factory), item);
                    using var oracle = new SemanticCaptureOracle(graph, Capture(Access(), item));
                    for (int i = 0; i < 40; i++)
                    {
                        item.Fraction = 0; item.Exact = 0; item.Count = 99; item.Name = null;
                        graph.Restore(); oracle.VerifyRestore(); check(graph.Differences().Count == 0);
                    }
                }
        });
        test("single-pass readonly instance storage retains ordinary reflection write and identity semantics", () =>
        {
            var owner = new ReadonlyOwner();
            var plan = new SemanticEntityFieldPlan(typeof(ReadonlyOwner),
                new("Text", typeof(string), SemanticFieldRole.Entity),
                new("Number", typeof(int), SemanticFieldRole.Entity));
            var factory = new SemanticFieldBlockFactory(new[] { (typeof(ReadonlyOwner), new[] { plan }) });
            using var access = Access(factory);
            using var graph = StateGraph.Capture(access, new[] { ManagedObjects.Id(owner) });
            typeof(ReadonlyOwner).GetField("Text").SetValue(owner, "changed");
            typeof(ReadonlyOwner).GetField("Number").SetValue(owner, 99);
            graph.Restore();
            check(factory.BlockCount == 1 && owner.Number == 7 && owner.Text == "original" && graph.Differences().Count == 0);
        });
    }
}
