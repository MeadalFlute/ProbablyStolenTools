using System.Collections;
using System.Diagnostics;
using System.Reflection;
using ProbablyStolenTools;

internal static class SnapshotValueComparisonTests
{
    // Wrapping only IStateAccess preserves the previous byte-comparison path as a control.
    private class ByteComparisonAccess : IStateAccess
    {
        protected readonly ManagedStateAccess Inner;
        internal int Reads;
        internal ByteComparisonAccess(bool fastReads = true)
        {
            Inner = new ManagedStateAccess(null, typeof(SnapshotValueComparisonTests).Assembly.GetName().Name, fastReads);
        }
        internal void AddRoot(IntPtr id) => Inner.AddRoot(id);
        internal (int Values, int Saved, long Next) ValueCounts => (
            ((IDictionary)Field("_values")).Count,
            ((IDictionary)Field("_savedValues")).Count,
            (long)Field("_nextValue"));
        private object Field(string name) => typeof(ManagedStateAccess)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Inner);
        public void Retain(IntPtr id) => Inner.Retain(id);
        public IReadOnlyList<StateSlot> Describe(IntPtr id) => Inner.Describe(id);
        public byte[] ReadValue(IntPtr id, StateSlot slot) { Reads++; return Inner.ReadValue(id, slot); }
        public IntPtr ReadReference(IntPtr id, StateSlot slot) => Inner.ReadReference(id, slot);
        public void WriteValue(IntPtr id, StateSlot slot, byte[] value) => Inner.WriteValue(id, slot, value);
        public void WriteReference(IntPtr id, StateSlot slot, IntPtr value) => Inner.WriteReference(id, slot, value);
        public void Validate() => Inner.Validate();
        public void Dispose() => Inner.Dispose();
    }

    private sealed class ComparisonAccess : ByteComparisonAccess, IStateValueComparer
    {
        internal int Comparisons;
        internal int FailAt;
        internal ComparisonAccess(bool fastReads = true) : base(fastReads) { }
        public bool ValueEquals(IntPtr id, StateSlot slot, byte[] captured)
        {
            if (++Comparisons == FailAt) throw new InvalidOperationException("Modeled unreadable late field.");
            return Inner.ValueEquals(id, slot, captured);
        }
    }

    private static StateGraph Capture(object root, ByteComparisonAccess access)
    {
        var id = ManagedObjects.Id(root);
        access.AddRoot(id);
        return StateGraph.Capture(access, new[] { id });
    }

    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("verification never interns changed values with reflection or compiled reads", () =>
        {
            foreach (bool fast in new[] { false, true })
            {
                var root = new[] { 12345 };
                var access = new ComparisonAccess(fast);
                using var graph = Capture(root, access);
                var before = access.ValueCounts;
                check(before.Values == 1 && before.Saved == 1);
                access.Reads = 0;
                for (int i = 0; i < 1000; i++)
                {
                    root[0] = 20000 + i;
                    check(graph.Differences().Count == 1);
                }
                check(access.Reads == 0 && access.Comparisons == 1000 && access.ValueCounts == before);
                check(graph.ByteCount == 8);
                graph.Restore();
                check(root[0] == 12345 && graph.Differences().Count == 0 && access.ValueCounts == before);
            }
        });
        test("read-only comparison matches byte tokens for signed zero NaN infinities and nullable floats", () =>
        {
            var singles = new[] { 0f, BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)),
                BitConverter.Int32BitsToSingle(0x7fc00001), BitConverter.Int32BitsToSingle(0x7fc00002),
                float.PositiveInfinity, float.NegativeInfinity, float.Epsilon };
            var doubles = new[] { 0d, BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000000)),
                BitConverter.Int64BitsToDouble(0x7ff8000000000001), BitConverter.Int64BitsToDouble(0x7ff8000000000002),
                double.PositiveInfinity, double.NegativeInfinity, double.Epsilon };
            var optional = new float?[] { null, singles[1], singles[2] };
            var root = new object[] { singles, doubles, optional };
            var access = new ComparisonAccess();
            using var current = Capture(root, access);
            using var bytes = Capture(root, new ByteComparisonAccess());
            var before = access.ValueCounts;
            void Compare(int expected)
            {
                check(current.Differences(100).Count == expected);
                check(current.Differences(100).SequenceEqual(bytes.Differences(100)));
                check(access.ValueCounts == before);
            }
            Compare(0);
            singles[0] = singles[1]; singles[1] = 0f; singles[2] = singles[3];
            doubles[0] = doubles[1]; doubles[1] = 0d; doubles[2] = doubles[3];
            optional[0] = 1; optional[1] = null; optional[2] = singles[3];
            Compare(9);
            singles[4] = float.MaxValue; singles[5] = float.PositiveInfinity; singles[6] = 0;
            doubles[4] = double.MaxValue; doubles[5] = double.PositiveInfinity; doubles[6] = 0;
            Compare(15);
            current.Restore();
            Compare(0);
            check(BitConverter.SingleToInt32Bits(singles[1]) == unchecked((int)0x80000000));
            check(BitConverter.DoubleToInt64Bits(doubles[2]) == 0x7ff8000000000001);
        });
        test("read-only comparison preserves enum decimal nullable and multi-checkpoint semantics", () =>
        {
            var integers = new int?[] { null, 3 };
            var money = new[] { 1m, 1.25m };
            var modes = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday };
            var root = new object[] { integers, money, modes };
            var firstAccess = new ComparisonAccess();
            using var first = Capture(root, firstAccess);
            using var firstBytes = Capture(root, new ByteComparisonAccess());
            var firstCount = firstAccess.ValueCounts;
            // Existing token equality treats equivalent decimal scales as the same value.
            money[0] = 1.00m;
            check(first.Differences().Count == 0);
            integers[0] = 7; integers[1] = null; money[1] = -8; modes[0] = DayOfWeek.Friday;
            var secondAccess = new ComparisonAccess();
            using var second = Capture(root, secondAccess);
            var secondCount = secondAccess.ValueCounts;
            check(first.Differences().Count == 4
                && first.Differences().SequenceEqual(firstBytes.Differences()));
            for (int i = 0; i < 10; i++)
            {
                integers[0] = 9000 + i; money[1] = i;
                check(first.Differences().Count > 0 && second.Differences().Count > 0);
                first.Restore();
                check(integers[0] == null && integers[1] == 3 && money[1] == 1.25m
                    && modes[0] == DayOfWeek.Monday && first.Differences().Count == 0);
                second.Restore();
                check(integers[0] == 7 && integers[1] == null && money[1] == -8
                    && modes[0] == DayOfWeek.Friday && second.Differences().Count == 0);
            }
            check(firstAccess.ValueCounts == firstCount && secondAccess.ValueCounts == secondCount);
        });
        test("comparison scans every scalar after reaching the diagnostic message limit", () =>
        {
            var root = Enumerable.Range(0, 100).ToArray();
            var access = new ComparisonAccess();
            using var graph = Capture(root, access);
            Array.Fill(root, -1);
            check(graph.Differences(1).Count == 1 && access.Comparisons == root.Length);
            access.Comparisons = 0;
            access.FailAt = 100;
            bool failed = false;
            try { graph.Differences(1); }
            catch (InvalidOperationException e) when (e.Message == "Modeled unreadable late field.") { failed = true; }
            check(failed && access.Comparisons == 100);
        });
    }

    internal static void Benchmark()
    {
        var root = Enumerable.Range(0, 100000).Select(i => (double)i / 7).ToArray();
        var times = new[] { new List<double>(), new List<double>() };
        var allocations = new[] { new List<long>(), new List<long>() };
        for (int run = 0; run < 7; run++)
        {
            foreach (int mode in run % 2 == 0 ? new[] { 0, 1 } : new[] { 1, 0 })
            {
                ByteComparisonAccess access = mode == 0 ? new ByteComparisonAccess() : new ComparisonAccess();
                using var graph = Capture(root, access);
                if (graph.Differences().Count != 0) throw new Exception("Benchmark warmup differs.");
                long before = GC.GetAllocatedBytesForCurrentThread();
                var clock = Stopwatch.StartNew();
                for (int i = 0; i < 5; i++)
                    if (graph.Differences().Count != 0) throw new Exception("Benchmark differs.");
                clock.Stop();
                if (run > 0)
                {
                    times[mode].Add(clock.Elapsed.TotalMilliseconds / 5);
                    allocations[mode].Add((GC.GetAllocatedBytesForCurrentThread() - before) / 5);
                }
            }
        }
        for (int mode = 0; mode < 2; mode++)
        {
            times[mode].Sort(); allocations[mode].Sort();
            Console.WriteLine($"VALUE_BENCH {(mode == 0 ? "previous-byte-path" : "read-only-compare")} slots={root.Length} " +
                $"verifyMedianMs={(times[mode][2] + times[mode][3]) / 2:0.00} " +
                $"allocatedMedianBytes={(allocations[mode][2] + allocations[mode][3]) / 2} " +
                "(NET6 synthetic double array; full scan; capture excluded; not Unity Mono)");
        }
    }
}
