using ProbablyStolenTools;

internal static class CheckpointDayTests
{
    private sealed class Node : IDisposable
    {
        internal int Day, Disposals;
        internal bool Closed, Manual;
        public void Dispose() => Disposals++;
    }

    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        CheckpointHistory<Node> History() => new(20, capacityGroup: n => n.Day);
        test("file restore waits for native decode and exact day slot run identity", () =>
        {
            var gate = new CheckpointLoadGate();
            gate.Begin(new SaveMetadata(93, 0, 2, 2, "run"));
            check(!gate.Completed);
            check(!gate.Loaded(94, 2, "run") && !gate.Loaded(93, 1, "run") && !gate.Loaded(93, 2, "other"));
            check(gate.Loaded(93, 2, "run") && gate.Completed);
            gate.Reset();
            check(!gate.Completed && !gate.Loaded(93, 2, "run"));
        });
        test("chemistry dividers preserve row separation and hotkey scroll reachability", () =>
        {
            foreach (float width in new[] { 380f, 480f, 900f })
            {
                var layout = new PanelSettingsLayout(width, 18, 5, 7, 13);
                check(layout.Toggle(7).Y - layout.Toggle(6).Y == PanelSettingsLayout.RowHeight + 16);
                check(layout.Toggle(13).Y - layout.Toggle(12).Y == PanelSettingsLayout.RowHeight + 16);
                check(layout.DividerY(7) > layout.Toggle(6).Y + layout.Toggle(6).Height);
                check(layout.DividerY(13) > layout.Toggle(12).Y + layout.Toggle(12).Height);
                check(layout.HotkeySectionY > layout.Toggle(17).Y + layout.Toggle(17).Height);
                check(layout.ScrollOffset(float.MaxValue, 180) + 180 == layout.ContentHeight);
            }
        });
        test("two days each retain twenty nodes without cross-day eviction", () =>
        {
            var h = History();
            var yesterday = Enumerable.Range(0, 20).Select(_ => new Node { Day = 10 }).ToArray();
            var today = Enumerable.Range(0, 25).Select(_ => new Node { Day = 11 }).ToArray();
            foreach (var n in yesterday.Concat(today)) h.Add(n);
            check(h.Items.Count == 40 && yesterday.All(n => n.Disposals == 0));
            check(today.Take(5).All(n => n.Disposals == 1) && today.Skip(5).All(n => n.Disposals == 0));
            h.Clear();
            check(yesterday.Concat(today).All(n => n.Disposals == 1));
        });
        test("third-day pruning releases only expired day's nodes and stages", () =>
        {
            var h = History();
            var nodes = new[] { new Node { Day = 1 }, new Node { Day = 1, Closed = true },
                new Node { Day = 2 }, new Node { Day = 2, Closed = true }, new Node { Day = 3 } };
            foreach (var n in nodes) h.Add(n, false);
            h.RemoveWhere(n => n.Day < 2 || n.Day > 3);
            check(h.Items.SequenceEqual(nodes.Skip(2)) && nodes.Take(2).All(n => n.Disposals == 1));
            h.Clear();
            check(nodes.All(n => n.Disposals == 1));
        });
        test("cross-day browsing remains reversible until new customer commits", () =>
        {
            var h = History();
            var yesterday = new Node { Day = 1 };
            var close = new Node { Day = 1, Closed = true };
            var today = new Node { Day = 2 };
            h.Add(yesterday); h.Add(close, false); h.Add(today);
            h.Select(yesterday);
            check(h.Items.Count == 3 && h.IsFuture(today) && today.Disposals == 0);
            h.Select(today);
            check(!h.IsFuture(close) && yesterday.Disposals == 0);
            h.Select(yesterday);
            var branch = new Node { Day = 1 };
            h.Add(branch);
            check(h.Items.SequenceEqual(new[] { yesterday, branch }) && close.Disposals == 1 && today.Disposals == 1);
            h.Clear();
        });
        test("manual and automatic closed nodes count toward the twenty-node daily limit", () =>
        {
            var h = History();
            var nodes = Enumerable.Range(0, 21).Select(_ => new Node { Day = 1, Closed = true, Manual = true }).ToArray();
            foreach (var n in nodes) h.Add(n);
            var stage = new Node { Day = 1, Closed = true };
            h.Add(stage, false);
            check(h.Items.Count == 20 && nodes[0].Disposals == 1 && nodes[1].Disposals == 1 && stage.Disposals == 0);
            h.Clear();
        });
        test("disabling retention prunes other day and resets an expired selection", () =>
        {
            var h = History();
            var yesterday = new Node { Day = 1 }; var today = new Node { Day = 2 };
            h.Add(yesterday); h.Add(today); h.Select(yesterday);
            h.RemoveWhere(n => n.Day != 2);
            check(h.Items.Single() == today && h.CommitBranch() == 0 && yesterday.Disposals == 1);
            h.Clear();
        });
        test("sleeping after rewind abandons future then retains adjacent days", () =>
        {
            var h = History();
            var a = new Node { Day = 8 }; var b = new Node { Day = 8 }; var c = new Node { Day = 9 };
            h.Add(a); h.Add(b); h.Add(c); h.Select(a);
            check(h.CommitBranch() == 2);
            h.RemoveWhere(n => n.Day < 8 || n.Day > 9);
            var replacement = new Node { Day = 9 }; h.Add(replacement);
            check(h.Items.SequenceEqual(new[] { a, replacement }) && b.Disposals == 1 && c.Disposals == 1);
            h.Clear();
        });
    }
}
