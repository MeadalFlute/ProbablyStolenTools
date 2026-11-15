using ProbablyStolenTools;

internal static class DayArchiveTests
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("day archives deduplicate by run slot day only, not cash or filename timestamp", () =>
        {
            var a = new SaveMetadata(95, 100, 2, 2, "a");
            var rows = new[] { a, a with { Cash = 200 }, a with { Day = 94 },
                a with { RunId = "b" }, a with { Slot = 1 }, a with { State = 0 },
                a with { State = 1 }, a with { RunId = null }, null, a with { Day = -1 } };
            check(DayArchivePolicy.Duplicates(rows).SequenceEqual(new[] { 1 }));
            check(DayArchivePolicy.Duplicates(Array.Empty<SaveMetadata>()).Count == 0);
        });
        test("replaying the same day repeatedly does not consume native historical day slots", () =>
        {
            var history = new List<SaveMetadata> { new(94, 100, 2, 2, "a"), new(93, 100, 2, 2, "a") };
            for (int replay = 0; replay < 20; replay++)
            {
                history.Insert(0, new SaveMetadata(95, replay, 2, 2, "a"));
                foreach (int index in DayArchivePolicy.Duplicates(history).OrderByDescending(i => i))
                    history.RemoveAt(index);
                history = history.Take(2).ToList();
                check(history.Select(m => m.Day).SequenceEqual(new[] { 95, 94 }));
                check(history[0].Cash == replay);
            }
        });
        test("catalog removes same-day copies before LRU can evict older dates", () => Temp(root =>
        {
            string yesterday = Add(root, 1, 94), older = Add(root, 2, 95), newer = Add(root, 3, 95);
            var catalog = new CheckpointFileCatalog();
            check(catalog.Deduplicate(root, null) == 1);
            var usage = CheckpointStorage.Prune(root, 2, 1024 * 1024);
            check(usage.Deleted == 0 && usage.Files == 2 && File.Exists(yesterday) && File.Exists(newer) && !File.Exists(older));
            check(catalog.Find(new SaveMetadata(95, 999, 2, 2, "a")).Path == newer);
            check(catalog.Deduplicate(root, null) == 0);
        }));
        test("pending file restore wins duplicate selection; multiple protected copies survive", () => Temp(root =>
        {
            string old = Add(root, 1, 95), latest = Add(root, 2, 95);
            var catalog = new CheckpointFileCatalog();
            check(catalog.Deduplicate(root, new[] { old, latest }) == 0);
            check(catalog.Deduplicate(root, new[] { old }) == 1 && File.Exists(old) && !File.Exists(latest));
        }));
        test("catalog preserves foreign runs slots phases and unknown ownership", () => Temp(root =>
        {
            Add(root, 1, 95);
            Add(root, 2, 95, run: "b");
            Add(root, 3, 95, slot: 1);
            Add(root, 4, 95, state: 0);
            string corrupt = Add(root, 5, 95);
            File.WriteAllText(corrupt, "bad");
            string external = Path.Combine(root, "save_2.es3");
            File.Copy(Path.Combine(root, "20261101_120000_0000000_day.es3"), external);
            string nested = Path.Combine(root, "nested");
            Directory.CreateDirectory(nested);
            Add(nested, 6, 95);
            var catalog = new CheckpointFileCatalog();
            check(catalog.Deduplicate(root, null) == 0 && catalog.Entries.Count() == 5);
            check(File.Exists(external) && File.Exists(corrupt) && Directory.GetFiles(nested).Length == 1);
        }));
        test("catalog refresh notices overwritten and deleted day files", () => Temp(root =>
        {
            string path = Add(root, 1, 94);
            var catalog = new CheckpointFileCatalog();
            catalog.Refresh(root);
            var old = catalog.Find(new SaveMetadata(94, 0, 2, 2, "a"));
            check(old != null);
            catalog.Refresh(root);
            check(ReferenceEquals(old, catalog.Find(old.Metadata)));
            Add(root, 1, 95, cash: 12345);
            catalog.Refresh(root);
            check(catalog.Find(old.Metadata) == null && catalog.Find(old.Metadata with { Day = 95 }).Metadata.Cash == 12345);
            File.Delete(path);
            catalog.Refresh(root);
            check(!catalog.Entries.Any());
        }));
        test("locked duplicate deletion preserves all days and can be retried", () => Temp(root =>
        {
            string old = Add(root, 1, 95), newest = Add(root, 2, 95);
            var catalog = new CheckpointFileCatalog();
            using (var locked = new FileStream(old, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                check(catalog.Deduplicate(root, null) == 0);
                check(File.Exists(old) && File.Exists(newest));
            }
            check(catalog.Deduplicate(root, null) == 1 && !File.Exists(old));
        }));
    }

    private static string Add(string root, int stamp, int day, string run = "a", int slot = 2, int state = 2, int cash = 100)
    {
        string path = Path.Combine(root, $"202611{stamp:D2}_120000_0000000_day.es3");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new
        {
            playerStore = new { value = new { saveSlotId = slot, runID = run, playerCash = cash, storeState = state } },
            storeStation = new { value = new { dayCounter = day } }
        }));
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 11, stamp, 12, 0, 0, DateTimeKind.Utc));
        return path;
    }
    private static void Temp(Action<string> action)
    {
        string prefix = Path.Combine(AppContext.BaseDirectory, "pstools-day-archive-");
        string root = prefix + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(root);
        try { action(root); }
        finally
        {
            if (!Path.GetFullPath(root).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new Exception("Invalid test root.");
            Directory.Delete(root, true);
        }
    }
}
