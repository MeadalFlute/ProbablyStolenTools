using System.Collections;
using System.Reflection;
using ProbablyStolenTools;

internal static class SnapshotTypedComparisonTests
{
    private enum Mode : short { First = -7, Last = 91 }
    private struct Nested { internal float? Number; internal (decimal Money, object Link) Inner; }
    private sealed class Data
    {
        internal int Number;
        internal Nested Nested;
        internal readonly long Readonly = long.MinValue;
        private bool _flag = true;
        internal void Change() { Number = -1; Nested = default; _flag = false; }
        internal bool Flag => _flag;
    }
    private struct Custom
    {
        internal int Number;
        public override bool Equals(object value) => value is Custom other && Number == other.Number;
        public override int GetHashCode() => Number;
    }
    private sealed class CustomData { internal Custom? Value; }
    private static (ManagedStateAccess Access, StateGraph Graph) Capture(object root, bool typed, bool fast = true)
    {
        var access = new ManagedStateAccess(null, typeof(Data).Assembly.GetName().Name, fast)
            { TypedScalarComparisons = typed };
        var id = ManagedObjects.Id(root); access.AddRoot(id);
        return (access, StateGraph.Capture(access, new[] { id }));
    }
    private static (int, int, long) Tokens(ManagedStateAccess access)
    {
        object Field(string name) => typeof(ManagedStateAccess).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(access);
        return (((IDictionary)Field("_values")).Count, ((IDictionary)Field("_savedValues")).Count, (long)Field("_nextValue"));
    }

    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("typed comparer waits for 32 comparisons and keeps reader compile counters independent", () =>
        {
            SnapshotFieldReader.ClearCache();
            var reader = SnapshotFieldReader.Get(typeof(int[]), "typed", Array.Empty<FieldInfo>(), true);
            var values = new[] { 17, -8 };
            for (int i = 0; i < 31; i++) check(!reader.TryEquals(values, 0, 17, out _));
            check(!reader.ComparerCompiled && SnapshotFieldReader.CompareCompileAttempts == 0);
            check(reader.TryEquals(values, 0, 17, out bool same) && same);
            check(reader.ComparerCompiled && !reader.Compiled && SnapshotFieldReader.CompileAttempts == 0
                && SnapshotFieldReader.CompareCompileAttempts == 1 && SnapshotFieldReader.CompareCompileTicks > 0);
            long ticks = SnapshotFieldReader.CompareCompileTicks;
            check(reader.TryEquals(values, 1, 17, out same) && !same);
            check(SnapshotFieldReader.CompareCompileTicks == ticks);
            SnapshotFieldReader.ClearCache();
            check(SnapshotFieldReader.CompareCompileAttempts == 0 && SnapshotFieldReader.CompareCompileTicks == 0);
            check(reader.TryEquals(values, 1, -8, out same) && same);
        });
        test("typed and boxed verification agree for primitive enum decimal nullable and distinct array indices", () =>
        {
            var arrays = new Array[]
            {
                new[] { true, false }, new byte[] { 1, 255 }, new sbyte[] { -128, 127 },
                new short[] { -71, 23 }, new ushort[] { 0, 65535 }, new[] { -1, 71 },
                new[] { uint.MaxValue, 3u }, new[] { long.MinValue, 7L },
                new[] { ulong.MaxValue, 9UL }, new[] { 'Q', '\0' },
                new[] { Mode.First, Mode.Last }, new[] { 1.00m, decimal.MaxValue },
                new int?[] { null, 23 }, new Mode?[] { Mode.Last, null },
                new decimal?[] { null, -1.25m }
            };
            var expected = arrays.Select(a => (Array)a.Clone()).ToArray();
            var (typedAccess, typed) = Capture(arrays, true);
            var (_, boxed) = Capture(arrays, false);
            using (typed) using (boxed)
            {
                var tokens = Tokens(typedAccess);
                for (int i = 0; i < 40; i++) check(typed.Differences().Count == 0);
                check(typed.TypedComparisonCount > 0);
                foreach (var array in arrays) Array.Clear(array, 0, array.Length);
                check(typed.Differences(100).SequenceEqual(boxed.Differences(100)));
                typed.Restore();
                for (int i = 0; i < arrays.Length; i++)
                    for (int j = 0; j < arrays[i].Length; j++) check(Equals(arrays[i].GetValue(j), expected[i].GetValue(j)));
                check(boxed.Differences().Count == 0 && Tokens(typedAccess) == tokens);
            }
        });
        test("typed nullable float and double comparisons preserve null signed zero and NaN bits", () =>
        {
            var singles = new float?[] { null, -0f, BitConverter.Int32BitsToSingle(unchecked((int)0x7fc00013)), float.NegativeInfinity };
            var doubles = new double?[] { null, 0d, BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000000017)), double.Epsilon };
            var root = new object[] { singles, doubles };
            var (_, typed) = Capture(root, true);
            var (_, boxed) = Capture(root, false);
            using (typed) using (boxed)
            {
                for (int i = 0; i < 40; i++) check(typed.Differences().Count == 0);
                singles[0] = 7; singles[1] = 0f; singles[2] = BitConverter.Int32BitsToSingle(unchecked((int)0x7fc00014));
                doubles[0] = 9; doubles[1] = -0d; doubles[2] = BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000000018));
                check(typed.Differences(100).Count == 6 && typed.Differences(100).SequenceEqual(boxed.Differences(100)));
                singles[3] = null; doubles[3] = null;
                check(typed.Differences(100).Count == 8);
                typed.Restore();
                check(singles[0] == null && BitConverter.SingleToInt32Bits(singles[1].Value) == unchecked((int)0x80000000)
                    && BitConverter.DoubleToInt64Bits(doubles[2].Value) == unchecked((long)0x7ff8000000000017)
                    && boxed.Differences().Count == 0);
            }
        });
        test("typed private readonly nested and boxed-struct readers never bind checkpoint instances or expected values", () =>
        {
            object link = new object();
            var data = Enumerable.Range(0, 40).Select(i => new Data { Number = i,
                Nested = new Nested { Number = i, Inner = (i + .25m, link) } }).ToArray();
            object boxedValue = new Nested { Number = -0f, Inner = (71, link) };
            var root = new object[] { data, boxedValue };
            var (_, past) = Capture(root, true);
            using (past)
            {
                for (int i = 0; i < 4; i++) check(past.Differences().Count == 0);
                foreach (var item in data) item.Change();
                var (_, future) = Capture(root, true);
                using (future)
                    for (int i = 0; i < 3; i++)
                    {
                        GC.Collect(2, GCCollectionMode.Forced, true, true);
                        past.Restore();
                        check(data[39].Number == 39 && data[39].Nested.Inner.Money == 39.25m && data[39].Flag
                            && data[39].Readonly == long.MinValue && ReferenceEquals(data[39].Nested.Inner.Link, link)
                            && past.Differences().Count == 0 && past.TypedComparisonCount > 0);
                        future.Restore();
                        check(data[39].Number == -1 && !data[39].Flag && future.Differences().Count == 0);
                    }
            }
        });
        test("custom nullable scalar comparison falls back without compiling overloaded equality", () =>
        {
            var root = Enumerable.Range(0, 40).Select(i => new CustomData { Value = new Custom { Number = i } }).ToArray();
            var (_, typed) = Capture(root, true);
            var (_, boxed) = Capture(root, false);
            using (typed) using (boxed)
            {
                for (int i = 0; i < 3; i++) check(typed.Differences().Count == 0);
                check(typed.TypedComparisonCount == 0);
                root[39].Value = null; root[0].Value = new Custom { Number = 91 };
                check(typed.Differences().Count == 2 && typed.Differences().SequenceEqual(boxed.Differences()));
                typed.Restore(); check(boxed.Differences().Count == 0);
            }
        });
        test("typed read exceptions are not swallowed or counted as matching values", () =>
        {
            var reader = SnapshotFieldReader.Get(typeof(int[]), "throwing-typed", Array.Empty<FieldInfo>(), true);
            var values = new[] { 7 };
            for (int i = 0; i < 32; i++) reader.TryEquals(values, 0, 7, out _);
            bool bounds = false, owner = false;
            try { reader.TryEquals(values, 2, 7, out _); } catch (IndexOutOfRangeException) { bounds = true; }
            try { reader.TryEquals(null, 0, 7, out _); } catch (NullReferenceException) { owner = true; }
            check(bounds && owner && reader.ComparerCompiled);
            check(reader.TryEquals(values, 0, 8, out bool same) && !same);
            var unsupported = SnapshotFieldReader.Get(typeof(string), "invalid-typed-array", Array.Empty<FieldInfo>(), true);
            for (int i = 0; i < 50; i++) check(!unsupported.TryEquals("text", 0, 7, out _));
        });
        test("unavailable typed delegate falls back once without retrying compilation", () =>
        {
            SnapshotFieldReader.ClearCache();
            var values = Enumerable.Range(0, 80).ToArray();
            var (_, graph) = Capture(values, true);
            using (graph)
            {
                graph.Differences();
                var reader = SnapshotFieldReader.Get(typeof(int[]), "", Array.Empty<FieldInfo>(), true);
                check(reader.ComparerCompiled);
                typeof(SnapshotFieldReader).GetField("_equals", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(reader, (Func<object, int, object, bool>)((_, _, _) => throw new NotSupportedException("Unavailable.")));
                long attempts = SnapshotFieldReader.CompareCompileAttempts;
                values[79] = -1;
                check(graph.Differences().Count == 1 && !reader.ComparerCompiled);
                graph.Restore();
                check(values[79] == 79 && graph.Differences().Count == 0
                    && SnapshotFieldReader.CompareCompileAttempts == attempts);
            }
        });
        test("disabled or reflection-only access keeps boxed comparisons and cannot compile typed readers", () =>
        {
            foreach (bool fast in new[] { false, true })
            {
                SnapshotFieldReader.ClearCache();
                var values = Enumerable.Range(0, 80).ToArray();
                var (_, graph) = Capture(values, !fast, fast);
                using (graph)
                {
                    for (int i = 0; i < 40; i++) check(graph.Differences().Count == 0);
                    Array.Fill(values, -1);
                    check(graph.Differences(100).Count == 80);
                    graph.Restore();
                    check(graph.TypedComparisonCount == 0 && SnapshotFieldReader.CompareCompileAttempts == 0
                        && values[79] == 79);
                }
            }
        });
        test("typed full scan continues beyond diagnostic limit and cache remains bounded without losing old readers", () =>
        {
            SnapshotFieldReader.ClearCache();
            var values = Enumerable.Range(0, 100).ToArray();
            var (_, graph) = Capture(values, true);
            using (graph)
            {
                for (int i = 0; i < 3; i++) check(graph.Differences().Count == 0);
                long before = graph.TypedComparisonCount;
                Array.Fill(values, -1);
                check(graph.Differences(1).Count == 1 && graph.TypedComparisonCount - before == 100);
                for (int i = 0; i < 4096; i++)
                    SnapshotFieldReader.Get(typeof(int[]), "bounded-" + i, Array.Empty<FieldInfo>(), true);
                check(SnapshotFieldReader.Get(typeof(int[]), "past-limit", Array.Empty<FieldInfo>(), true) == null);
                SnapshotFieldReader.ClearCache();
                graph.Restore();
                check(graph.Differences().Count == 0 && values[99] == 99);
            }
            SnapshotFieldReader.ClearCache();
        });
    }
}
