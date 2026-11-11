using System.Diagnostics;
using System.Reflection;
using ProbablyStolenTools;

internal static class SemanticTagSnapshotTests
{
    private sealed class State
    {
        internal SemanticTagValue Value;
    }
    private sealed class SystemState
    {
        internal Dictionary<string, State> Map = new();
    }
    private static SemanticTagValue Value(int i = 0) => new(false, null, i, 0, i, 0, false, "id" + i, "name" + i);
    private static SemanticTagSnapshot<SystemState, State> Capture(params SystemState[] systems)
        => SemanticTagSnapshot<SystemState, State>.Capture(systems, system => system.Map, tag => tag.Value);
    private static StateGraph Legacy(object root)
    {
        var access = new ManagedStateAccess(gameAssembly: typeof(State).Assembly.GetName().Name);
        var id = ManagedObjects.Id(root); access.AddRoot(id);
        return StateGraph.Capture(access, new[] { id });
    }
    private static void Throws<T>(Action action, Action<bool> check) where T : Exception
    {
        bool failed = false;
        try { action(); } catch (T) { failed = true; }
        check(failed);
    }

    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("semantic tags preserve disabled zero and null entries without any restore API", () =>
        {
            var state = new State { Value = Value() };
            var system = new SystemState();
            system.Map["disabled"] = state; system.Map["alias"] = state; system.Map["null"] = null;
            using var snapshot = Capture(system);
            check(snapshot.SystemCount == 1 && snapshot.MapCount == 1
                && snapshot.EntryCount == 3 && snapshot.TagCount == 1);
            check(snapshot.LogicalSlots == 17 && snapshot.LogicalBytes == 11 * IntPtr.Size + 48);
            check(snapshot.Differences().Count == 0 && system.Map.Count == 3 && state.Value == Value());
            check(!snapshot.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(method => method.Name.Contains("Restore") || method.Name.Contains("Write")));
            state.Value = state.Value with { Text = "" };
            check(snapshot.Differences().Count == 1);
        });
        test("semantic tags detect every field and preserve signed zero and NaN payloads", () =>
        {
            var value = Value();
            var state = new State { Value = value };
            var system = new SystemState(); system.Map["key"] = state;
            using var snapshot = Capture(system);
            foreach (var changed in new[] {
                value with { Enabled = true }, value with { Text = "" }, value with { Integer = -1 },
                value with { SingleBits = unchecked((int)0x80000000) }, value with { Long = -1 },
                value with { DoubleBits = unchecked((long)0x8000000000000000) }, value with { Boolean = true },
                value with { Identifier = "new" }, value with { DisplayName = "new" } })
            {
                state.Value = changed; check(snapshot.Differences().Count == 1);
            }
            state.Value = value with { SingleBits = 0x7fc00001, DoubleBits = 0x7ff8000000000001 };
            using var nan = Capture(system);
            state.Value = state.Value with { SingleBits = 0x7fc00002 };
            check(nan.Differences().Count == 1);
            state.Value = state.Value with { SingleBits = 0x7fc00001, DoubleBits = 0x7ff8000000000002 };
            check(nan.Differences().Count == 1);
            state.Value = value;
            check(snapshot.Differences().Count == 0);
        });
        test("semantic tags deduplicate shared maps states and repeated roots by identity", () =>
        {
            var shared = new State { Value = Value(1) };
            var one = new SystemState(); one.Map["one"] = one.Map["two"] = shared;
            var two = new SystemState { Map = one.Map };
            var three = new SystemState(); three.Map["third"] = shared;
            var empty = new SystemState { Map = null };
            using var snapshot = Capture(one, two, three, one, empty, null);
            check(snapshot.SystemCount == 4 && snapshot.MapCount == 2
                && snapshot.TagCount == 1 && snapshot.EntryCount == 3);
            check(snapshot.Differences().Count == 0);
            two.Map = new Dictionary<string, State>(one.Map);
            check(snapshot.Differences().Count == 1);
            two.Map = one.Map;
            shared.Value = Value(2);
            check(snapshot.Differences().Count == 1);
        });
        test("semantic tags detect key spelling order count and value identity independently of comparers", () =>
        {
            var first = new State { Value = Value(1) };
            var second = new State { Value = Value(1) };
            var system = new SystemState { Map = new Dictionary<string, State>(StringComparer.OrdinalIgnoreCase) };
            system.Map.Add("A", first); system.Map.Add("B", second);
            using var snapshot = Capture(system);
            system.Map["A"] = second; check(snapshot.Differences().Count > 0);
            system.Map["A"] = first; check(snapshot.Differences().Count == 0);
            system.Map.Clear(); system.Map.Add("a", first); system.Map.Add("B", second);
            check(snapshot.Differences().Count > 0);
            system.Map.Clear(); system.Map.Add("B", second); system.Map.Add("A", first);
            check(snapshot.Differences().Count > 0);
            system.Map.Remove("B"); check(snapshot.Differences().Count > 0);
        });
        test("semantic verification reads every state after the message cap and propagates late failures", () =>
        {
            var system = new SystemState();
            for (int i = 0; i < 50; i++) system.Map.Add("k" + i, new State { Value = Value(i) });
            int reads = 0, failAt = 0;
            SemanticTagValue Read(State state)
            {
                if (++reads == failAt) throw new InvalidOperationException("late tag read");
                return state.Value;
            }
            using var snapshot = SemanticTagSnapshot<SystemState, State>.Capture(new[] { system }, s => s.Map, Read);
            foreach (var state in system.Map.Values) state.Value = Value(-1);
            reads = 0;
            check(snapshot.Differences(1).Count == 1 && reads == 50);
            reads = 0; failAt = 50;
            Throws<InvalidOperationException>(() => snapshot.Differences(1), check);
            check(reads == 50);
        });
        test("semantic tags apply independent object slot and byte budgets before retaining oversized maps", () =>
        {
            var system = new SystemState(); system.Map.Add("k", new State { Value = Value(1) });
            using var exact = Capture(system);
            foreach (int mode in new[] { 0, 1, 2 })
                Throws<InvalidOperationException>(() =>
                {
                    using var rejected = SemanticTagSnapshot<SystemState, State>.Capture(new[] { system },
                        s => s.Map, t => t.Value, mode == 0 ? 2 : 3,
                        (int)exact.LogicalSlots - (mode == 1 ? 1 : 0),
                        exact.LogicalBytes - (mode == 2 ? 1 : 0));
                }, check);
            using var accepted = SemanticTagSnapshot<SystemState, State>.Capture(new[] { system },
                s => s.Map, t => t.Value, 3, (int)exact.LogicalSlots, exact.LogicalBytes);
            check(accepted.Differences().Count == 0);
        });
        test("semantic capture read failures leave existing legacy and semantic checkpoints usable", () =>
        {
            var system = new SystemState(); system.Map.Add("k", new State { Value = Value(1) });
            using var legacy = Legacy(system);
            using var shadow = Capture(system);
            Throws<InvalidOperationException>(() =>
            {
                using var failed = SemanticTagSnapshot<SystemState, State>.Capture(new[] { system },
                    s => s.Map, _ => throw new InvalidOperationException("read failure"));
            }, check);
            system.Map["k"].Value = Value(99);
            legacy.Restore();
            check(shadow.Differences().Count == 0 && legacy.Differences().Count == 0);
            shadow.Dispose(); shadow.Dispose();
            Throws<ObjectDisposedException>(() => shadow.Differences(), check);
            Throws<ObjectDisposedException>(() => shadow.ValidateCoverage(_ => true), check);
        });
        test("semantic coverage independently matches legacy objects and survives repeated full restores", () =>
        {
            var a = new State { Value = Value(1) };
            var b = new State { Value = Value(2) };
            var system = new SystemState(); system.Map.Add("a", a); system.Map.Add("b", b);
            using var legacy = Legacy(system);
            using var shadow = Capture(system);
            shadow.ValidateCoverage(value => legacy.Contains(ManagedObjects.Id(value)));
            Throws<InvalidOperationException>(() =>
                shadow.ValidateCoverage(value => !ReferenceEquals(value, b)), check);
            using var newer = Capture(system);
            for (int i = 0; i < 3; i++)
            {
                a.Value = Value(90); b.Value = Value(91);
                system.Map.Clear(); system.Map.Add("replacement", new State { Value = Value(92) });
                system.Map = new Dictionary<string, State>();
                legacy.Restore();
                check(shadow.Differences().Count == 0 && newer.Differences().Count == 0);
                check(ReferenceEquals(system.Map["a"], a) && ReferenceEquals(system.Map["b"], b));
            }
        });
        test("semantic tags reject equal-text replacement identities for all three string fields", () =>
        {
            var value = Value(1) with { Text = new string(new[] { 't', 'e', 'x', 't' }) };
            var state = new State { Value = value };
            var system = new SystemState(); system.Map.Add("a", state);
            using var legacy = Legacy(system);
            using var shadow = Capture(system);
            string Copy(string text) => new(text.ToCharArray());
            foreach (var replaced in new[] { value with { Text = Copy(value.Text) },
                value with { Identifier = Copy(value.Identifier) }, value with { DisplayName = Copy(value.DisplayName) } })
            {
                check(replaced == value && !replaced.SameState(value));
                state.Value = replaced;
                check(shadow.Differences().Count == 1 && legacy.Differences().Count == 1);
                legacy.Restore();
                check(shadow.Differences().Count == 0 && state.Value.SameState(value));
            }
        });
        test("semantic tag keys preserve reference identity even when equal spelling and enumeration order match", () =>
        {
            string key = new(new[] { 'k', 'e', 'y' });
            var state = new State { Value = Value(1) };
            var system = new SystemState(); system.Map.Add(key, state);
            using var legacy = Legacy(system);
            using var shadow = Capture(system);
            system.Map.Clear(); system.Map.Add(new string(key.ToCharArray()), state);
            check(shadow.Differences().Count == 1 && legacy.Differences().Count != 0);
            legacy.Restore();
            check(shadow.Differences().Count == 0 && ReferenceEquals(system.Map.First().Key, key));
        });
    }

    internal static void Benchmark()
    {
        var systems = Enumerable.Range(0, 1500).Select(i =>
        {
            var system = new SystemState();
            for (int j = 0; j < 24; j++) system.Map.Add("tag" + j, new State { Value = Value(i * 24 + j) });
            for (int j = 0; j < 12; j++) system.Map.Remove("tag" + j);
            return system;
        }).ToArray();
        var times = new[] { new List<double>(), new List<double>() };
        var allocations = new[] { new List<long>(), new List<long>() };
        for (int run = 0; run < 7; run++)
            foreach (int mode in run % 2 == 0 ? new[] { 0, 1 } : new[] { 1, 0 })
            {
                GC.Collect(); GC.WaitForPendingFinalizers();
                long before = GC.GetAllocatedBytesForCurrentThread();
                var clock = Stopwatch.StartNew();
                using var legacy = mode == 0 ? Legacy(systems) : null;
                using var shadow = mode == 1 ? Capture(systems) : null;
                if ((mode == 0 ? legacy.Differences() : shadow.Differences()).Count != 0) throw new Exception("Benchmark differs.");
                clock.Stop();
                if (run > 0)
                {
                    times[mode].Add(clock.Elapsed.TotalMilliseconds);
                    allocations[mode].Add(GC.GetAllocatedBytesForCurrentThread() - before);
                }
                if (run == 6) Console.WriteLine(mode == 0
                    ? $"TAG_COVERAGE legacy objects={legacy.ObjectCount} slots={legacy.SlotCount} bytes={legacy.ByteCount}"
                    : $"TAG_COVERAGE semantic systems={shadow.SystemCount} maps={shadow.MapCount} entries={shadow.EntryCount} " +
                        $"tags={shadow.TagCount} slots={shadow.LogicalSlots} bytes={shadow.LogicalBytes}");
            }
        for (int mode = 0; mode < 2; mode++)
        {
            times[mode].Sort(); allocations[mode].Sort();
            Console.WriteLine($"TAG_BENCH {(mode == 0 ? "legacy" : "semantic-read-only")} " +
                $"captureVerifyMedianMs={(times[mode][2] + times[mode][3]) / 2:0.00} " +
                $"allocatedMedianBytes={(allocations[mode][2] + allocations[mode][3]) / 2} " +
                "(NET6 modeled tag subset; semantic mode has no restore/runtime graph; not Unity Mono)");
        }
    }
}
