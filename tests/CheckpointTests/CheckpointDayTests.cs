using ProbablyStolenTools;

internal static class CheckpointDayTests
{
    private sealed class Node : IDisposable
    {
        internal int Day, Disposals;
        internal bool Closed, Manual;
        internal bool Morning;
        public void Dispose() => Disposals++;
    }

    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        CheckpointHistory<Node> History() => new(20, capacityGroup: n => n.Day);
        test("morning anchors do not consume either day's twenty customer/manual slots", () =>
        {
            var h = new CheckpointHistory<Node>(20, n => !n.Morning, n => n.Day);
            var anchors = new[] { new Node { Day = 1, Morning = true }, new Node { Day = 2, Morning = true } };
            h.Add(anchors[0], false);
            for (int day = 1; day <= 2; day++)
            {
                if (day == 2) h.Add(anchors[1], false);
                for (int i = 0; i < 25; i++) h.Add(new Node { Day = day });
            }
            check(h.Items.Count == 42 && anchors.All(n => n.Disposals == 0));
            h.Select(anchors[0]);
            check(h.Items.Count == 42 && h.IsFuture(anchors[1]));
            h.Select(anchors[1]);
            check(h.Items.Count == 42 && anchors.All(n => n.Disposals == 0));
            h.Clear();
            check(anchors.All(n => n.Disposals == 1));
        });
        test("discard callback archives only committed branches, before releasing graph resources", () =>
        {
            var archived = new List<(int Day, int Disposals)>();
            var h = new CheckpointHistory<Node>(2, abandon: node => archived.Add((node.Day, node.Disposals)));
            var a = new Node { Day = 1 }; var b = new Node { Day = 2 }; var c = new Node { Day = 3 };
            h.Add(a); h.Add(b); h.Add(c);
            check(a.Disposals == 1 && archived.Count == 0);
            h.Select(b);
            check(archived.Count == 0 && c.Disposals == 0);
            check(h.CommitBranch() == 1 && archived.Single() == (3, 0) && c.Disposals == 1);
            h.Clear();
            check(archived.Count == 1 && b.Disposals == 1);
        });
        test("timeline orders today's morning before all yesterday's nodes, regardless of file timestamp", () =>
        {
            var old = DateTime.UtcNow.AddYears(-1);
            var entries = new List<CheckpointTimelineEntry>
            {
                new() { Day = 8, DayStart = false, At = DateTime.UtcNow },
                new() { Day = 9, DayStart = true, At = old },
                new() { Day = 8, DayStart = true, At = DateTime.UtcNow.AddDays(2) },
                new() { Day = 9, DayStart = false, At = old.AddSeconds(1) },
                new() { Day = 9, DayStart = false, At = old.AddSeconds(2) }
            };
            CheckpointTimelineEntry.Sort(entries);
            check(entries.Select(e => e.Day).SequenceEqual(new[] { 9, 9, 9, 8, 8 }));
            check(!entries[0].DayStart && entries[0].At > entries[1].At && entries[2].DayStart && entries[4].DayStart);
        });
        test("F6 selects one older row including manual nodes, arrows move and clamp, cancel releases selection", () =>
        {
            object newest = new(), manual = new(), morning = new(), yesterday = new();
            object[] rows = { newest, manual, morning, yesterday };
            var selection = new CheckpointSelection();
            check(selection.Begin(rows, newest) && selection.Selected == manual);
            selection.Move(-1); check(selection.Selected == newest);
            selection.Move(-1); check(selection.Selected == newest);
            selection.Move(2); check(selection.Selected == morning);
            selection.Move(100); check(selection.Selected == yesterday);
            selection.Cancel();
            check(!selection.Active && selection.Selected == null);
            check(selection.Begin(rows, null) && selection.Selected == newest);
            check(selection.Begin(rows, yesterday) && selection.Selected == yesterday);
            check(!selection.Begin(Array.Empty<object>(), null) && selection.Selected == null);
        });
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
        test("arrows can initialize selection in either direction without F6", () =>
        {
            object a = new(), b = new(), c = new();
            var selection = new CheckpointSelection();
            check(selection.Begin(new[] { a, b, c }, b, -1) && selection.Selected == a);
            check(selection.Begin(new[] { a, b, c }, b, 1) && selection.Selected == c);
            check(selection.Begin(new[] { a, b, c }, a, -1) && selection.Selected == a);
            check(selection.Begin(new[] { a, b, c }, null, -1) && selection.Selected == a);
        });
        test("return checkpoint waits for stable state, ignores initial arrival and duplicate return hooks", () =>
        {
            var gate = new CheckpointReturnGate();
            gate.Return();
            check(!gate.Pending && !gate.Poll(true, 1));
            gate.Leave(); gate.Leave();
            check(gate.Outside && !gate.Poll(true, 2));
            gate.Return();
            check(gate.Pending && !gate.Outside && !gate.Poll(true, 3));
            gate.Return();
            check(!gate.Poll(false, 3.5f) && !gate.Poll(true, 4) && !gate.Poll(true, 4.4f));
            check(gate.Poll(true, 4.5f) && !gate.Poll(true, 5));
            gate.Leave(); gate.Return(); gate.CancelPending();
            check(!gate.Poll(true, 6));
            gate.Leave(); gate.Reset(); gate.Return();
            check(!gate.Pending && !gate.Outside);
        });
        test("closed return allows persistent native shutter lock but opening transitions remain blocked", () =>
        {
            check(CheckpointReturnGate.ShutterReady(0, 1));
            check(CheckpointReturnGate.ShutterReady(0, 2));
            check(!CheckpointReturnGate.ShutterReady(1, 1));
            check(!CheckpointReturnGate.ShutterReady(2, 1));
            check(CheckpointReturnGate.ShutterReady(1, 0));
            check(CheckpointReturnGate.ShutterReady(2, 0));
            check(!CheckpointReturnGate.ShutterReady(-1, 0));
            check(!CheckpointReturnGate.ShutterReady(3, 0));
        });
        test("chemistry dividers preserve row separation and hotkey scroll reachability", () =>
        {
            foreach (float width in new[] { 380f, 480f, 900f })
            foreach (int toggleCount in new[] { 18, 19, 20 })
            foreach (int keyCount in new[] { 5, 10, 12 })
            foreach (int actionCount in new[] { 0, 2 })
            {
                var layout = new PanelSettingsLayout(width, toggleCount, keyCount, 7, 14, actionCount);
                check(layout.Toggle(7).Y - layout.Toggle(6).Y == PanelSettingsLayout.RowHeight + 16);
                check(layout.Toggle(14).Y - layout.Toggle(13).Y == PanelSettingsLayout.RowHeight + 16);
                check(layout.DividerY(7) > layout.Toggle(6).Y + layout.Toggle(6).Height);
                check(layout.DividerY(14) > layout.Toggle(13).Y + layout.Toggle(13).Height);
                check(layout.HotkeySectionY > layout.Toggle(toggleCount - 1).Y + layout.Toggle(toggleCount - 1).Height);
                for (int i = 0; i < actionCount; i++)
                {
                    var action = layout.Action(i);
                    check(action.X >= 0 && action.X + action.Width <= width);
                    check(action.Y > layout.Toggle(toggleCount - 1).Y + layout.Toggle(toggleCount - 1).Height);
                    check(action.Y + action.Height < layout.HotkeySectionY);
                    if (i > 0) check(action.Y > layout.Action(i - 1).Y + layout.Action(i - 1).Height);
                }
                check(layout.ResetY > layout.Key(keyCount - 1).Y + layout.Key(keyCount - 1).Height);
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
