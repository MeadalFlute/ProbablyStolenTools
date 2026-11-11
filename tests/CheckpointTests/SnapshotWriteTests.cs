using System.Reflection;
using ProbablyStolenTools;

internal static class SnapshotWriteTests
{
    private enum Mode : short { Zero, Other = 7 }
    private struct Cell
    {
        internal int Number;
        internal (long Amount, object Link) Inner;
    }
    private sealed class Data
    {
        internal bool Flag = true;
        internal byte Byte = 231;
        internal char Character = 'Q';
        internal int Number = -19;
        internal long Long = long.MinValue + 2;
        internal decimal Money = 123.456m;
        internal float Single = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
        internal double Double = BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000000017));
        internal Mode Mode = Mode.Other;
        internal int? Empty, Optional = 7;
        internal object Link;
        internal Cell Cell;
        internal readonly int ReadonlyNumber = 41;
        internal readonly string ReadonlyText = new string("original".ToCharArray());
        private int _privateNumber = 17;
        internal int PrivateNumber => _privateNumber;
        internal void ChangePrivate() => _privateNumber = -1;
    }
    private sealed class EqualObject
    {
        public override bool Equals(object other) => other is EqualObject;
        public override int GetHashCode() => 0;
    }
    private static ManagedStateAccess Access(bool direct)
        => new(null, typeof(Data).Assembly.GetName().Name, true, true, direct);
    private static StateGraph Capture(object root, bool direct, params string[] fields)
    {
        var access = Access(direct);
        var id = ManagedObjects.Id(root); access.AddRoot(id, fields);
        return StateGraph.Capture(access, new[] { id });
    }
    private static void Mutate(Data data)
    {
        data.Flag = false; data.Byte = 0; data.Character = '\0'; data.Number = 0; data.Long = 1;
        data.Money = -1; data.Single = 0; data.Double = 0; data.Mode = Mode.Zero;
        data.Empty = 1; data.Optional = null; data.Link = new EqualObject();
        data.Cell = new Cell { Number = -1, Inner = (-1, new object()) };
        data.ChangePrivate();
        typeof(Data).GetField(nameof(Data.ReadonlyNumber), BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(data, -1);
    }
    private static void AssertData(Data data, object link, Action<bool> check)
    {
        check(data.Flag && data.Byte == 231 && data.Character == 'Q' && data.Number == -19
            && data.Long == long.MinValue + 2 && data.Money == 123.456m && data.Mode == Mode.Other
            && data.Empty == null && data.Optional == 7 && data.ReadonlyNumber == 41
            && ReferenceEquals(data.Link, link) && data.PrivateNumber == 17);
        check(BitConverter.SingleToInt32Bits(data.Single) == unchecked((int)0x80000000)
            && BitConverter.DoubleToInt64Bits(data.Double) == unchecked((long)0x7ff8000000000017));
    }

    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("direct field writes match recursive writes for scalars private readonly and nullable storage", () =>
        {
            var link = new EqualObject();
            var data = new Data { Link = link };
            using var recursive = Capture(data, false);
            using var direct = Capture(data, true);
            check(recursive.ObjectCount == direct.ObjectCount && recursive.SlotCount == direct.SlotCount
                && recursive.ByteCount == direct.ByteCount);
            foreach (var graph in new[] { direct, recursive, direct })
            {
                Mutate(data); graph.Restore();
                AssertData(data, link, check);
                check(direct.Differences().Count == 0 && recursive.Differences().Count == 0);
            }
        });
        test("direct writes preserve exact reference identity including equal objects and readonly strings", () =>
        {
            var data = new Data { Link = new EqualObject() };
            object original = data.Link; string text = data.ReadonlyText;
            var readonlyField = typeof(Data).GetField(nameof(Data.ReadonlyText), BindingFlags.Instance | BindingFlags.NonPublic);
            using var recursive = Capture(data, false);
            using var direct = Capture(data, true);
            foreach (var graph in new[] { direct, recursive })
            {
                data.Link = new EqualObject();
                readonlyField.SetValue(data, new string(text.ToCharArray()));
                check(graph.Differences().Count == 2);
                graph.Restore();
                check(ReferenceEquals(data.Link, original) && ReferenceEquals(data.ReadonlyText, text)
                    && recursive.Differences().Count == 0 && direct.Differences().Count == 0);
                graph.Restore();
            }
        });
        test("direct scalar and reference arrays restore distinct indices nulls and floating point bits", () =>
        {
            var integers = new[] { 7, -11, 991 };
            var optional = new int?[] { null, 23, null };
            var singles = new[] { BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)),
                BitConverter.Int32BitsToSingle(unchecked((int)0x7fc00023)), float.PositiveInfinity };
            var doubles = new[] { -0d, BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000000071)), double.NegativeInfinity };
            var bytes = new byte[] { 7, 19, 255 }; var flags = new[] { false, true, false };
            var money = new[] { 1.25m, -19.5m, decimal.MaxValue };
            var modes = new[] { Mode.Other, Mode.Zero, Mode.Other };
            var links = new object[] { new EqualObject(), null, new EqualObject() };
            var expectedLinks = (object[])links.Clone();
            var texts = new[] { new string("one".ToCharArray()), null, "" };
            var expectedTexts = (string[])texts.Clone();
            var root = new object[] { integers, optional, singles, doubles, bytes, flags, money, modes, links, texts };
            var bits = singles.Select(BitConverter.SingleToInt32Bits).ToArray();
            var doubleBits = doubles.Select(BitConverter.DoubleToInt64Bits).ToArray();
            using var recursive = Capture(root, false);
            using var direct = Capture(root, true);
            foreach (var graph in new[] { direct, recursive })
            {
                foreach (Array array in root) Array.Clear(array, 0, array.Length);
                graph.Restore();
                check(integers.SequenceEqual(new[] { 7, -11, 991 }) && optional[0] == null && optional[1] == 23
                    && singles.Select(BitConverter.SingleToInt32Bits).SequenceEqual(bits)
                    && doubles.Select(BitConverter.DoubleToInt64Bits).SequenceEqual(doubleBits)
                    && bytes[2] == 255 && flags[1] && money[2] == decimal.MaxValue && modes[0] == Mode.Other);
                for (int i = 0; i < links.Length; i++)
                    check(ReferenceEquals(links[i], expectedLinks[i]) && ReferenceEquals(texts[i], expectedTexts[i]));
                check(direct.Differences().Count == 0 && recursive.Differences().Count == 0);
            }
        });
        test("nested struct field and struct-array writeback remain recursive and preserve aliases", () =>
        {
            object link = new object();
            var data = new Data { Cell = new Cell { Number = 7, Inner = (91, link) } };
            var cells = new[] { data.Cell, new Cell { Number = 19, Inner = (-8, link) } };
            var root = new object[] { data, cells };
            using var recursive = Capture(root, false);
            using var direct = Capture(root, true);
            foreach (var graph in new[] { direct, recursive })
            {
                data.Cell = default; Array.Clear(cells, 0, cells.Length);
                graph.Restore();
                check(data.Cell.Number == 7 && data.Cell.Inner.Amount == 91 && cells[1].Number == 19
                    && cells[1].Inner.Amount == -8 && ReferenceEquals(data.Cell.Inner.Link, link)
                    && ReferenceEquals(cells[1].Inner.Link, link)
                    && direct.Differences().Count == 0 && recursive.Differences().Count == 0);
            }
        });
        test("direct writes mutate retained boxed struct identities without replacing array entries", () =>
        {
            object boxed = new Cell { Number = 31, Inner = (73, new object()) };
            var root = new[] { boxed, boxed };
            using var recursive = Capture(root, false);
            using var direct = Capture(root, true);
            var field = typeof(Cell).GetField(nameof(Cell.Number), BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var graph in new[] { direct, recursive })
            {
                field.SetValue(boxed, -9); root[0] = new object();
                graph.Restore();
                check(((Cell)boxed).Number == 31 && ReferenceEquals(root[0], boxed)
                    && ReferenceEquals(root[1], boxed) && recursive.Differences().Count == 0);
            }
        });
        test("direct selected roots never restore excluded fields", () =>
        {
            var data = new Data();
            using var graph = Capture(data, true, nameof(Data.Number), nameof(Data.Optional));
            data.Number = 8; data.Optional = 91; data.Flag = false; data.Empty = 23;
            graph.Restore();
            check(data.Number == -19 && data.Optional == 7 && !data.Flag && data.Empty == 23 && graph.SlotCount == 2);
        });
        test("direct writes retain immutable delegate anchors and never invoke callbacks", () =>
        {
            var data = new Data { Number = 7 };
            Action callback = () => data.Number++;
            var root = new object[] { data, callback };
            using var recursive = Capture(root, false);
            using var direct = Capture(root, true);
            data.Number = 21; root[1] = null;
            direct.Restore();
            check(data.Number == 7 && ReferenceEquals(root[1], callback) && recursive.Differences().Count == 0);
            callback(); check(data.Number == 8);
            recursive.Restore(); check(data.Number == 7 && direct.Differences().Count == 0);
        });
        test("direct and recursive retained checkpoints alternate across collection growth and compacting GC", () =>
        {
            var data = new Data { Number = 7 };
            var list = new List<Data> { data };
            using var past = Capture(list, true);
            data.Number = 81; list.Add(new Data { Number = 82 });
            using var future = Capture(list, false);
            for (int i = 0; i < 4; i++)
            {
                list.Add(new Data()); GC.Collect(2, GCCollectionMode.Forced, true, true);
                past.Restore(); check(list.Count == 1 && data.Number == 7 && past.Differences().Count == 0);
                future.Restore(); check(list.Count == 2 && data.Number == 81 && list[1].Number == 82
                    && future.Differences().Count == 0);
            }
        });
        test("reader compilation timings count successful compilation once without forcing early compilation", () =>
        {
            SnapshotFieldReader.ClearCache();
            var field = typeof(Data).GetField(nameof(Data.Number), BindingFlags.Instance | BindingFlags.NonPublic);
            var reader = SnapshotFieldReader.Get(typeof(Data), "timed", new[] { field }, false);
            var data = new Data();
            for (int i = 0; i < 31; i++) check(!reader.TryRead(data, -1, out _));
            check(SnapshotFieldReader.CompileAttempts == 0 && SnapshotFieldReader.CompileTicks == 0);
            check(reader.TryRead(data, -1, out var actual) && (int)actual == -19);
            long ticks = SnapshotFieldReader.CompileTicks;
            check(reader.Compiled && SnapshotFieldReader.CompileAttempts == 1 && ticks > 0);
            for (int i = 0; i < 40; i++) reader.TryRead(data, -1, out _);
            check(SnapshotFieldReader.CompileAttempts == 1 && SnapshotFieldReader.CompileTicks == ticks
                && SnapshotFieldReader.Summary.Contains("compileAttemptsTotal=1,compileMsTotal="));
            SnapshotFieldReader.ClearCache();
            check(SnapshotFieldReader.CompileAttempts == 0 && SnapshotFieldReader.CompileTicks == 0);
            check(reader.TryRead(data, -1, out actual) && (int)actual == -19);
        });
        test("reader compilation timings include unavailable compilation and preserve reflection fallback", () =>
        {
            SnapshotFieldReader.ClearCache();
            var reader = SnapshotFieldReader.Get(typeof(string), "invalid-array", Array.Empty<FieldInfo>(), true);
            for (int i = 0; i < 80; i++) check(!reader.TryRead("text", 0, out _));
            check(!reader.Compiled && SnapshotFieldReader.CompileAttempts == 1 && SnapshotFieldReader.CompileTicks > 0
                && SnapshotFieldReader.Summary.Contains("fallback=1"));
            SnapshotFieldReader.ClearCache();
        });
    }
}
