using System.Reflection;
using System.Text.Json;
using ProbablyStolenTools;

int passed = 0;
ModLogger.Log = new ModLog();
void Check(bool value) { if (!value) throw new Exception("Check failed."); }
void Test(string name, Action action)
{
    string parent = Path.Combine(AppContext.BaseDirectory, "native-history-fixtures");
    Directory.CreateDirectory(parent);
    string root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    SaveFiles.Root = root;
    SaveFiles.History.Clear(); SaveFiles.IndexWrites = 0; SaveFiles.FailIndex = false;
    NativeSaveScopePatch.Saving = null; Plugin.CfgRewind.Value = true;
    try { action(); passed++; Console.WriteLine("PASS " + name); }
    finally
    {
        if (Path.GetDirectoryName(Path.GetFullPath(root)) != parent) throw new Exception("Invalid test root.");
        Directory.Delete(root, true);
    }
}
string Add(int day, int second, string run = "run", int slot = 2, int state = 2, bool active = false)
{
    string name = active ? SaveFiles.Slot(slot) : $"save_{slot}_day{day}_20261115-2200{second:D2}.es3";
    string path = SaveFiles.AbsolutePath(name);
    File.WriteAllText(path, JsonSerializer.Serialize(new
    {
        playerStore = new { value = new { saveSlotId = slot, runID = run, storeState = state, playerCash = 100 } },
        storeStation = new { value = new { dayCounter = day } }
    }));
    File.SetLastWriteTimeUtc(path, new DateTime(2026, 11, 15, 22, 0, second, DateTimeKind.Utc));
    if (!active) SaveFiles.History.Add(new SaveHistoryEntry
        { file = name, dayCount = day, saveDateTime = File.GetLastWriteTimeUtc(path) });
    return path;
}
int Trim(int keep)
{
    SaveFiles.History = SaveFiles.LoadHistory(2);
    object[] args = { SaveFiles.History, keep };
    typeof(NativeSaveHistoryPatch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
    return (int)args[1];
}
Test("active day 95 removes historical 95 but keeps 94 and 93; active bytes unchanged", () =>
{
    string active = Add(95, 50, active: true), duplicate = Add(95, 40), previous = Add(94, 30), old = Add(93, 20);
    byte[] bytes = File.ReadAllBytes(active);
    NativeSaveHistoryPatch.ReconcileActive(2);
    Check(!File.Exists(duplicate) && File.Exists(previous) && File.Exists(old));
    Check(File.ReadAllBytes(active).SequenceEqual(bytes) && SaveFiles.History.Count == 2);
    NativeSaveHistoryPatch.ReconcileActive(2);
    Check(SaveFiles.IndexWrites == 1);
});
Test("rotation reserves rollback slot and failed slot write retains the recovery file", () =>
{
    string rotated = Add(95, 40), previous = Add(94, 30), old = Add(93, 20);
    NativeSaveScopePatch.Saving = new PlayerStore();
    int keep = Trim(2);
    Check(keep == 3 && SaveFiles.History.Count == 3);
    Check(File.Exists(rotated) && File.Exists(previous) && File.Exists(old));
    // Failed WriteSlotFile has no successful postfix; native RestoreFromHistory
    // can still promote the rotated file without losing another date.
    File.Move(rotated, SaveFiles.AbsolutePath(SaveFiles.Slot(2)));
    Check(File.Exists(previous) && File.Exists(old));
});
Test("successful write hook alone permits removing the rotated rollback copy", () =>
{
    string rotated = Add(95, 40);
    Add(95, 50, active: true);
    typeof(NativeSlotWrittenPatch).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, new object[] { new PlayerStore() });
    Check(!File.Exists(rotated) && SaveFiles.History.Count == 0);
});
Test("newer history never loses to an older active file", () =>
{
    Add(95, 30, active: true);
    string history = Add(95, 40);
    NativeSaveHistoryPatch.ReconcileActive(2);
    Check(File.Exists(history) && SaveFiles.IndexWrites == 0);
});
Test("foreign run slot phase invalid metadata and nonnative names remain untouched", () =>
{
    Add(95, 50, active: true);
    var paths = new[] { Add(95, 40, run: "other"), Add(95, 39, slot: 1), Add(95, 38, state: 0), Add(94, 37) };
    string corrupt = Add(95, 36); File.WriteAllText(corrupt, "corrupt");
    string unknown = SaveFiles.AbsolutePath("custom.es3");
    File.Copy(paths[0], unknown);
    SaveFiles.History.Add(new SaveHistoryEntry { file = "custom.es3", dayCount = 95 });
    NativeSaveHistoryPatch.ReconcileActive(2);
    Check(paths.All(File.Exists) && File.Exists(corrupt) && File.Exists(unknown) && SaveFiles.IndexWrites == 0);
});
Test("duplicate index references to a single obsolete file are all removed", () =>
{
    Add(95, 50, active: true);
    string path = Add(95, 40);
    SaveFiles.History.Add(SaveFiles.History[0]);
    NativeSaveHistoryPatch.ReconcileActive(2);
    Check(!File.Exists(path) && SaveFiles.History.Count == 0);
});
Test("locked active file and disabled feature never delete history", () =>
{
    string active = Add(95, 50, active: true), history = Add(95, 40);
    using (var stream = new FileStream(active, FileMode.Open, FileAccess.Read, FileShare.None))
        NativeSaveHistoryPatch.ReconcileActive(2);
    Check(File.Exists(history));
    Plugin.CfgRewind.Value = false;
    NativeSaveHistoryPatch.ReconcileActive(2);
    Check(File.Exists(history) && SaveFiles.IndexWrites == 0);
});
Test("closed rotation does not reserve an unremovable extra historical slot", () =>
{
    Add(95, 40, state: 0); Add(94, 30);
    NativeSaveScopePatch.Saving = new PlayerStore();
    Check(Trim(2) == 2);
});
Test("index write failure does not escape into native save recovery or alter active bytes", () =>
{
    string active = Add(95, 50, active: true);
    Add(95, 40);
    byte[] bytes = File.ReadAllBytes(active);
    SaveFiles.FailIndex = true;
    NativeSaveHistoryPatch.ReconcileActive(2);
    Check(File.ReadAllBytes(active).SequenceEqual(bytes));
    Check(EventLog.Lines.Last().Contains("cleanup incomplete"));
    Check(SaveFiles.LoadHistory(2).Count == 0);
});
Test("native gear editor mirrors XML limits and writes applied values to XML immediately", () =>
{
    string path = Path.Combine(SaveFiles.Root, "PSTools.settings.xml");
    var config = new ProbablyStolenTools.ModConfig(path) { SaveOnConfigSet = true };
    Plugin.CfgMaxObjects = config.Bind("Snapshot", "MaxObjects", 350000, "");
    Plugin.CfgMaxSlots = config.Bind("Snapshot", "MaxSlots", 3000000, "");
    Plugin.CfgMaxBytesMb = config.Bind("Snapshot", "MaxBytesMB", 256, "");
    NativeSnapshotSettings.Register();
    Check(ModHelper.Config.Entries["Snapshot.MaxObjects"].Value == 350000);
    ModHelper.Config.Entries["Snapshot.MaxObjects"].Set(500000);
    Check(Plugin.CfgMaxObjects.Value == 500000 && File.ReadAllText(path).Contains("500000"));
    ModHelper.Config.Entries["Snapshot.MaxObjects"].Set(2000000);
    Check(Plugin.CfgMaxObjects.Value == 1000000);
    Plugin.CfgMaxBytesMb.Value = 512;
    Check(ModHelper.Config.Entries["Snapshot.MaxBytesMB"].Value == 512);
    var old = ModHelper.Config.Entries["Snapshot.MaxObjects"];
    NativeSnapshotSettings.Register();
    old.Set(300000);
    Check(Plugin.CfgMaxObjects.Value == 1000000);
    NativeSnapshotSettings.Disable();
    ModHelper.Config.Entries["Snapshot.MaxObjects"].Set(400000);
    Check(Plugin.CfgMaxObjects.Value == 1000000);
});
Console.WriteLine($"{passed} native history/settings IO fixture tests passed; Unity/native ES3 writing requires game verification.");
