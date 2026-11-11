using System.Reflection;
using ProbablyStolenTools;

internal static class SnapshotScalarWriteTests
{
    private enum Kind : byte { First, Last = 255 }
    private struct Leaf
    {
        internal decimal Money;
        internal (long Number, object Link) Inner;
    }
    private sealed class Data
    {
        internal int Number = 17;
        internal long Long = long.MinValue;
        internal bool Flag = true;
        internal decimal Money = decimal.MaxValue;
        internal Kind Kind = Kind.Last;
        internal int? Empty, Optional = 13;
        internal float Single = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
        internal double Double = BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000000017));
        internal Leaf Leaf;
    }
    private struct Custom
    {
        internal int Number;
        internal static bool RejectEquals;
        public override bool Equals(object other)
        {
            if (RejectEquals) throw new InvalidOperationException("Custom Equals was called during a write.");
            return other is Custom custom && Number == custom.Number;
        }
        public override int GetHashCode() => Number;
    }
    private sealed class CustomData { internal Custom? Value; }

    private static (ManagedStateAccess Access, StateGraph Graph) Capture(object root, bool guard, params string[] fields)
    {
        var access = new ManagedStateAccess(null, typeof(Data).Assembly.GetName().Name)
            { SkipUnchangedScalars = guard };
        var id = ManagedObjects.Id(root);
        access.AddRoot(id, fields);
        return (access, StateGraph.Capture(access, new[] { id }));
    }

    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("scalar write guard counts unchanged framework values and writes every changed value", () =>
        {
            var data = new Data();
            var (access, graph) = Capture(data, true, nameof(Data.Number), nameof(Data.Long),
                nameof(Data.Flag), nameof(Data.Money), nameof(Data.Kind), nameof(Data.Empty), nameof(Data.Optional));
            using (graph)
            {
                graph.Restore();
                check(graph.ScalarWrites == (7L, 7L));
                data.Number = 0; data.Empty = 1; data.Optional = null;
                graph.Restore();
                check(graph.ScalarWrites == (14L, 11L) && data.Number == 17 && data.Empty == null
                    && data.Optional == 13 && graph.Differences().Count == 0);
            }
            check(access.ScalarWrites == default);
        });
        test("guarded floating point writes retain signed zeros and exact NaN payloads", () =>
        {
            var values = new object[]
            {
                new[] { BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)),
                    BitConverter.Int32BitsToSingle(unchecked((int)0x7fc00023)), float.PositiveInfinity },
                new[] { 0d, BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000000017)), double.NegativeInfinity }
            };
            var singles = (float[])values[0]; var doubles = (double[])values[1];
            var singleBits = singles.Select(BitConverter.SingleToInt32Bits).ToArray();
            var doubleBits = doubles.Select(BitConverter.DoubleToInt64Bits).ToArray();
            var (_, graph) = Capture(values, true);
            using (graph)
            {
                singles[0] = 0f; singles[1] = BitConverter.Int32BitsToSingle(unchecked((int)0x7fc00024));
                doubles[0] = -0d; doubles[1] = BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000000018));
                graph.Restore();
                check(graph.ScalarWrites == (6L, 2L)
                    && singles.Select(BitConverter.SingleToInt32Bits).SequenceEqual(singleBits)
                    && doubles.Select(BitConverter.DoubleToInt64Bits).SequenceEqual(doubleBits));
                graph.Restore();
                check(graph.ScalarWrites == (12L, 8L) && graph.Differences().Count == 0);
            }
        });
        test("all changed ordinary scalars are checked but never incorrectly skipped", () =>
        {
            var data = new Data();
            var (_, graph) = Capture(data, true, nameof(Data.Number), nameof(Data.Long), nameof(Data.Flag),
                nameof(Data.Money), nameof(Data.Kind), nameof(Data.Empty), nameof(Data.Optional),
                nameof(Data.Single), nameof(Data.Double));
            using (graph)
            {
                data.Number = 0; data.Long = 0; data.Flag = false; data.Money = 0;
                data.Kind = Kind.First; data.Empty = 1; data.Optional = null;
                data.Single = 0; data.Double = 0;
                graph.Restore();
                check(graph.ScalarWrites == (9L, 0L) && graph.Differences().Count == 0);
            }
        });
        test("guarded nested structs preserve parent copies shared references and retained checkpoints", () =>
        {
            object link = new object();
            var cells = new[] { new Leaf { Money = 7, Inner = (19, link) },
                new Leaf { Money = 11, Inner = (-91, link) } };
            var (_, past) = Capture(cells, true);
            using (past)
            {
                cells[1] = new Leaf { Money = -3, Inner = (79, new object()) };
                var (_, future) = Capture(cells, false);
                using (future)
                    for (int i = 0; i < 3; i++)
                    {
                        Array.Clear(cells, 0, cells.Length);
                        GC.Collect(2, GCCollectionMode.Forced, true, true);
                        past.Restore();
                        check(cells[0].Money == 7 && cells[1].Money == 11 && cells[1].Inner.Number == -91
                            && ReferenceEquals(cells[1].Inner.Link, link) && past.Differences().Count == 0);
                        future.Restore();
                        check(cells[0].Money == 7 && cells[1].Money == -3 && cells[1].Inner.Number == 79
                            && future.Differences().Count == 0 && future.ScalarWrites == default);
                    }
            }
        });
        test("nullable custom structs keep unconditional storage writes without new Equals callbacks", () =>
        {
            var data = new CustomData { Value = new Custom { Number = 41 } };
            var (_, graph) = Capture(data, true);
            using (graph)
            {
                data.Value = new Custom { Number = -1 };
                Custom.RejectEquals = true;
                try
                {
                    graph.Restore();
                    check(data.Value.Value.Number == 41 && graph.ScalarWrites == default);
                }
                finally { Custom.RejectEquals = false; }
                check(graph.Differences().Count == 0);
            }
        });
        test("scalar guard read failures propagate without retry or invalidating another checkpoint", () =>
        {
            var data = new Data();
            var (access, broken) = Capture(data, true, nameof(Data.Number));
            var (_, older) = Capture(data, false, nameof(Data.Number));
            using (broken)
            using (older)
            {
                data.Number = -3;
                var storage = (System.Collections.IDictionary)typeof(ManagedStateAccess)
                    .GetField("_storage", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(access);
                storage.Clear();
                bool failed = false;
                try { broken.Restore(); }
                catch (KeyNotFoundException) { failed = true; }
                check(failed && data.Number == -3 && broken.ScalarWrites == (1L, 0L));
                older.Restore();
                check(data.Number == 17 && older.Differences().Count == 0);
            }
        });
        test("disabled scalar guard retains unconditional writes and selected-root exclusions", () =>
        {
            var data = new Data { Leaf = new Leaf { Money = 37, Inner = (91, null) } };
            var (_, graph) = Capture(data, false, nameof(Data.Number), nameof(Data.Leaf));
            using (graph)
            {
                data.Flag = false; data.Number = 0; data.Leaf = default;
                graph.Restore();
                check(!data.Flag && data.Number == 17 && data.Leaf.Money == 37
                    && data.Leaf.Inner.Number == 91 && graph.ScalarWrites == default
                    && graph.Differences().Count == 0);
            }
        });
    }
}
