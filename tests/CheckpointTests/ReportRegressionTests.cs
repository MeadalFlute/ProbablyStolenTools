using ProbablyStolenTools;

internal static class ReportRegressionTests
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("snapshot limits report observed sizes, configured budgets and source without retaining partial state", () =>
        {
            foreach (var kind in new[] { "object", "slot", "byte" })
            {
                var memory = new FakeMemory();
                memory.Node(1, 10, 2, 0); memory.Node(2, 20, 0, 0);
                SnapshotLimitException failure = null;
                try
                {
                    using var graph = StateGraph.Capture(memory, new[] { (IntPtr)1 },
                        maxObjects: kind == "object" ? 1 : 10,
                        maxSlots: kind == "slot" ? 1 : 100,
                        maxBytes: kind == "byte" ? 1 : 1000);
                }
                catch (SnapshotLimitException e) { failure = e; }
                check(failure != null && failure.Message.Contains("Snapshot " + kind + " limit exceeded"));
                foreach (string label in new[] { "objects=", "slots=", "bytes=", "pending=", "source=" })
                    check(failure.Message.Contains(label));
                check(memory.Disposed && memory.Writes == 0);
            }
        });

        test("failed later capture does not dispose or invalidate an existing checkpoint", () =>
        {
            var first = new FakeMemory(); first.Node(1, 10, 0, 0);
            using var retained = StateGraph.Capture(first, new[] { (IntPtr)1 });
            var second = new FakeMemory(); second.Node(2, 20, 3, 0); second.Node(3, 30, 0, 0);
            try { using var skipped = StateGraph.Capture(second, new[] { (IntPtr)2 }, maxObjects: 1); }
            catch (SnapshotLimitException) { }
            first.Number(1, 99);
            retained.Restore();
            check(first.Number(1) == 10 && !first.Disposed && second.Disposed);
        });

        test("settle diagnostics log transitions and heartbeat while counting repeated states", () =>
        {
            var gate = new DiagnosticGate();
            check(gate.ShouldWrite("dialogue", 0, out int n) && n == 0);
            for (int i = 1; i < 60; i++) check(!gate.ShouldWrite("dialogue", i, out _));
            check(gate.ShouldWrite("dialogue", 60, out n) && n == 59);
            check(gate.ShouldWrite("moving", 61, out n) && n == 0);
            check(!gate.ShouldWrite("moving", 62, out _));
            gate.Reset();
            check(gate.ShouldWrite("moving", 62, out n) && n == 0);
        });

        test("log rotation preserves recent records and resumes after oversized legacy logs", () =>
        {
            InTemp(root =>
            {
                string log = Path.Combine(root, "PSTools.log");
                File.WriteAllText(log, new string('x', 1024));
                RollingLog.Append(log, "first error", 64, 2);
                check(File.ReadAllText(log + ".1").Length == 1024);
                for (int i = 0; i < 20; i++) RollingLog.Append(log, "record " + i.ToString("D3"), 64, 2);
                check(File.ReadAllText(log).Contains("record 019"));
                check(File.Exists(log + ".1") && File.Exists(log + ".2") && !File.Exists(log + ".3"));
                check(Directory.GetFiles(root).All(file => new FileInfo(file).Length <= 64));
                string errors = Path.Combine(root, "PSTools_errors.log");
                RollingLog.Append(errors, "original failure");
                for (int i = 0; i < 30; i++) RollingLog.Append(log, "noise " + i, 64, 2);
                check(File.ReadAllText(errors).Contains("original failure"));
            });
        });

        test("disk cleanup applies across sessions and never removes unrelated saves or nested files", () =>
        {
            InTemp(root =>
            {
                string checkpoints = Path.Combine(root, "PSTools_Checkpoints");
                Directory.CreateDirectory(checkpoints);
                var paths = Enumerable.Range(1, 4).Select(i => AddCheckpoint(checkpoints, i, 10)).ToArray();
                File.WriteAllText(Path.Combine(root, "save_0.es3"), "user save");
                File.WriteAllText(Path.Combine(checkpoints, "external.es3"), "external");
                string nested = Path.Combine(checkpoints, "unknown");
                Directory.CreateDirectory(nested);
                File.WriteAllText(Path.Combine(nested, "save_0.es3"), "nested");
                var result = CheckpointStorage.Prune(checkpoints, 2, 100);
                check(result.Deleted == 2 && result.Files == 3 && result.Bytes == 28 && result.Error == null);
                check(!File.Exists(paths[0]) && !File.Exists(paths[1]) && File.Exists(paths[3]));
                check(File.ReadAllText(Path.Combine(root, "save_0.es3")) == "user save");
                check(File.ReadAllText(Path.Combine(checkpoints, "external.es3")) == "external");
                check(File.ReadAllText(Path.Combine(nested, "save_0.es3")) == "nested");
                var again = CheckpointStorage.Prune(checkpoints, 2, 100);
                check(again.Deleted == 0 && again.Bytes == 28);
            });
        });

        test("disk byte budget, protected restore and explicit LRU touches are honored", () =>
        {
            InTemp(root =>
            {
                string first = AddCheckpoint(root, 1, 10);
                string second = AddCheckpoint(root, 2, 10);
                string third = AddCheckpoint(root, 3, 10);
                CheckpointStorage.Touch(root, first);
                var result = CheckpointStorage.Prune(root, 2, 20, new[] { third });
                check(result.Deleted == 1 && result.Bytes == 20 && result.Error == null);
                check(File.Exists(first) && !File.Exists(second) && File.Exists(third));
                result = CheckpointStorage.Prune(root, 1, 1, new[] { first });
                check(result.Deleted == 1 && result.Bytes == 10 && result.Error != null && File.Exists(first));
            });
        });

        test("checkpoint filename ownership rejects native slots, staging, malformed dates and backups", () =>
        {
            check(CheckpointStorage.IsOwnedName("20261110_123456_1234567_day-start.es3"));
            foreach (string name in new[] { "save_0.es3", "saves_index.es3", "pstools_restore.es3",
                "20261310_123456_1234567_day.es3", "20261110_123456_1234567_day.es3.bak", "backup.es3" })
                check(!CheckpointStorage.IsOwnedName(name));
        });

        test("cleanup reports an unmet budget and preserves locked checkpoints and unknown files", () =>
        {
            InTemp(root =>
            {
                string checkpoint = AddCheckpoint(root, 1, 10);
                string unknown = Path.Combine(root, "external.es3");
                File.WriteAllBytes(unknown, new byte[100]);
                if (OperatingSystem.IsWindows())
                {
                    using var held = new FileStream(checkpoint, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var locked = CheckpointStorage.Prune(root, 1, 1);
                    check(locked.Error != null && locked.Deleted == 0 && locked.Bytes == 110);
                    check(File.Exists(checkpoint) && File.Exists(unknown));
                }
                var result = CheckpointStorage.Prune(root, 1, 1);
                check(result.Deleted == 1 && result.Error != null && result.Bytes == 100);
                check(File.ReadAllBytes(unknown).Length == 100);
            });
        });

        test("Chinese English and unknown locale policies are explicit and do not treat arbitrary zh substrings as Chinese", () =>
        {
            foreach (string code in new[] { "zh", "zh-CN", "zh-Hans", "ZH_hant" })
            {
                check(LocalePolicy.Text(code, "cn", "en") == "cn");
                check(LocalePolicy.SupportsAnnotations(code));
            }
            foreach (string code in new[] { "en", "en-US", "en_GB" })
                check(LocalePolicy.Text(code, "cn", "en") == "en" && LocalePolicy.SupportsAnnotations(code));
            foreach (string code in new[] { null, "", "ru", "de-DE", "fr", "ja", "unknown-zh-text" })
                check(LocalePolicy.Text(code, "cn", "en") == "en" && !LocalePolicy.SupportsAnnotations(code));
        });
    }

    private static string AddCheckpoint(string root, int day, int bytes)
    {
        string path = Path.Combine(root, $"202610{day:D2}_120000_0000000_day.es3");
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 10, day, 12, 0, 0, DateTimeKind.Utc));
        return path;
    }

    private static void InTemp(Action<string> action)
    {
        string prefix = Path.Combine(AppContext.BaseDirectory, "PSTools-report-tests-");
        string root = prefix + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(root);
        try { action(root); }
        finally
        {
            if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(prefix), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected temporary test directory.");
            Directory.Delete(root, true);
        }
    }
}
