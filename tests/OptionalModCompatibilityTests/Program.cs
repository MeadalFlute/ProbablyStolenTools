using ProbablyStolenTools;

int passed = 0;
void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
void Test(string name, Action body)
{
    ModLoader.Registered.Clear();
    Plugin.Instance = new();
    UnityEngine.Time.unscaledTime += 10f;
    Plugin.CfgDisableMemoryCheckpoints.Value = false;
    Plugin.CfgHandledMemoryConflicts.Value = "";
    OptionalModCompatibility.Refresh();
    Plugin.StoreValue = null;
    Plugin.CfgWineAppraisal.Value = true;
    InspectionLocale.Chinese = false;
    WaterHelper.Calls = WineHelper.Calls = 0;
    body();
    passed++;
    Console.WriteLine("PASS " + name);
}
LoadedMod Add(string id, bool enabled = true)
{
    var mod = new LoadedMod { Manifest = new() { ID = id }, IsEnabled = enabled };
    ModLoader.Registered.Add(mod);
    OptionalModCompatibility.Refresh();
    return mod;
}
GameItem Bottle() => new() { identifier = "wine_bottle", Tags = new() { ["WINE_CONTAINER_TAG"] = 1 } };

Test("vanilla has no optional dependencies and retains memory checkpoints", () =>
{
    Check(OptionalModCompatibility.MemoryCheckpointsAllowed);
    Check(!OptionalModCompatibility.BrewingOwns(null));
    Check(WineAppraisal.IsUnfermentedBottle(Bottle()));
    Check(WineAppraisal.Describe(Bottle()).Contains("Expected:"));
});
Test("installed but disabled mods do not affect vanilla behavior", () =>
{
    Add(OptionalModCompatibility.ClaimsId, false);
    Add(OptionalModCompatibility.BrewingId, false);
    var bottle = Bottle(); bottle.Contents.Add(new() { identifier = "nutrifruit" });
    Check(OptionalModCompatibility.MemoryCheckpointsAllowed);
    Check(!OptionalModCompatibility.BrewingOwns(bottle));
    Check(WineAppraisal.IsUnfermentedBottle(bottle));
});
Test("all optional combinations select the conservative checkpoint policy", () =>
{
    foreach (bool brewing in new[] { false, true })
    foreach (bool claims in new[] { false, true })
    {
        ModLoader.Registered.Clear();
        Plugin.CfgDisableMemoryCheckpoints.Value = false;
        Plugin.CfgHandledMemoryConflicts.Value = "";
        Add(OptionalModCompatibility.BrewingId, brewing);
        Add(OptionalModCompatibility.ClaimsId, claims);
        Check(OptionalModCompatibility.MemoryCheckpointsAllowed == !claims);
    }
});
Test("brewing alone leaves vanilla berry recipes and checkpoints available", () =>
{
    Add(OptionalModCompatibility.BrewingId);
    var bottle = Bottle(); bottle.Contents.Add(new() { identifier = "bloomberry" });
    Check(OptionalModCompatibility.MemoryCheckpointsAllowed);
    Check(WineAppraisal.IsUnfermentedBottle(bottle));
    Check(WineAppraisal.Lines(bottle)?.Count > 0);
});
Test("unmarked expansion ingredients suppress every vanilla wine scoring entry", () =>
{
    Add(OptionalModCompatibility.BrewingId);
    foreach (string id in new[] { "nutrifruit", "dream_cap" })
    {
        var bottle = Bottle(); bottle.Contents.Add(new() { identifier = id });
        Check(OptionalModCompatibility.BrewingOwns(bottle));
        Check(!WineAppraisal.IsUnfermentedBottle(bottle));
        Check(WineAppraisal.Describe(bottle) == null && WineAppraisal.Lines(bottle) == null);
        Check(WineAppraisal.WaterWarning(bottle) == null && !WineAppraisal.IsIllegalBottle(bottle));
        var builder = new RichTextBuilder(); WineAppraisal.AddTooltip(builder, bottle);
        Check(builder.Lines.Count == 0);
    }
    Check(WineHelper.Calls == 0 && WaterHelper.Calls == 0);
});
Test("VBE tag presence protects state zero, fermenting and finished products even without expansion", () =>
{
    foreach (int state in new[] { 0, 1, 2 })
    {
        var bottle = Bottle(); bottle.Tags["VBE_STATE"] = state;
        Check(OptionalModCompatibility.BrewingOwns(bottle));
        Check(WineAppraisal.Describe(bottle) == null && WineAppraisal.Lines(bottle) == null);
    }
});
Test("recipe buckets are protected but normal water containers are not", () =>
{
    Add(OptionalModCompatibility.BrewingId);
    var jug = new GameItem { identifier = "water_jug" };
    Check(!OptionalModCompatibility.BrewingOwns(jug));
    jug.Contents.Add(new() { identifier = "dream_cap" });
    Check(OptionalModCompatibility.BrewingOwns(jug));
    jug.identifier = "water_pitcher";
    Check(!OptionalModCompatibility.BrewingOwns(jug));
    jug.Tags["VBE_STATE"] = 1;
    Check(OptionalModCompatibility.BrewingOwns(jug));
});
Test("a mixed invalid recipe does not receive a misleading vanilla prediction", () =>
{
    Add(OptionalModCompatibility.BrewingId);
    var bottle = Bottle(); bottle.Contents.AddRange(new[] { new GameItem { identifier = "bloomberry" },
        new GameItem { identifier = "nutrifruit" } });
    Check(WineAppraisal.Describe(bottle) == null);
});
Test("first conflict disables memory once and manual override survives refresh and re-enable", () =>
{
    var claims = Add(OptionalModCompatibility.ClaimsId);
    Check(!OptionalModCompatibility.MemoryCheckpointsAllowed);
    Check(Plugin.CfgHandledMemoryConflicts.Value == OptionalModCompatibility.ClaimsId);
    Plugin.CfgDisableMemoryCheckpoints.Value = false;
    OptionalModCompatibility.Refresh();
    Check(OptionalModCompatibility.MemoryCheckpointsAllowed && OptionalModCompatibility.HasMemoryConflict);
    claims.IsEnabled = false;
    OptionalModCompatibility.Refresh();
    Check(OptionalModCompatibility.MemoryCheckpointsAllowed);
    claims.IsEnabled = true;
    OptionalModCompatibility.Refresh();
    Check(OptionalModCompatibility.MemoryCheckpointsAllowed);
});
Test("manual memory disable works without optional mods and is not undone when conflict disappears", () =>
{
    Plugin.CfgDisableMemoryCheckpoints.Value = true;
    OptionalModCompatibility.Refresh();
    Check(!OptionalModCompatibility.MemoryCheckpointsAllowed && !OptionalModCompatibility.HasMemoryConflict);
    var claims = Add(OptionalModCompatibility.ClaimsId);
    claims.IsEnabled = false;
    OptionalModCompatibility.Refresh();
    Check(!OptionalModCompatibility.MemoryCheckpointsAllowed);
});
Test("persisted acknowledgement preserves explicit override on a fresh detection", () =>
{
    Plugin.CfgHandledMemoryConflicts.Value = "some.other.mod," + OptionalModCompatibility.ClaimsId;
    Add(OptionalModCompatibility.ClaimsId);
    Check(OptionalModCompatibility.MemoryCheckpointsAllowed);
});
Test("whitespace and duplicate handled IDs preserve a manual override without rewriting config", () =>
{
    string ids = " other.mod, " + OptionalModCompatibility.ClaimsId + " ," + OptionalModCompatibility.ClaimsId;
    Plugin.CfgHandledMemoryConflicts.Value = ids;
    Add(OptionalModCompatibility.ClaimsId);
    Check(OptionalModCompatibility.MemoryCheckpointsAllowed);
    Check(Plugin.CfgHandledMemoryConflicts.Value == ids && Plugin.Instance.Config.Saves == 0);
});
Test("failed conflict save stays disabled for this session, retries slowly and preserves a later override", () =>
{
    var config = Plugin.Instance.Config;
    config.SaveFailure = new IOException("configuration locked");
    EventLog.Lines.Clear();
    Add(OptionalModCompatibility.ClaimsId);
    Check(!OptionalModCompatibility.MemoryCheckpointsAllowed && config.Saves == 1 && config.SaveOnConfigSet);
    Check(Plugin.CfgHandledMemoryConflicts.Value == OptionalModCompatibility.ClaimsId);
    int errors = EventLog.Lines.Count(l => l.Contains("save failed"));
    Plugin.CfgDisableMemoryCheckpoints.Value = false;
    for (int i = 0; i < 100; i++) OptionalModCompatibility.Tick();
    Check(OptionalModCompatibility.MemoryCheckpointsAllowed && config.Saves == 1);
    Check(EventLog.Lines.Count(l => l.Contains("save failed")) == errors);
    typeof(OptionalModCompatibility).GetField("_retryAfterUtcTicks", System.Reflection.BindingFlags.Static
        | System.Reflection.BindingFlags.NonPublic).SetValue(null, 0L);
    OptionalModCompatibility.Refresh();
    Check(config.Saves == 2 && EventLog.Lines.Count(l => l.Contains("save failed")) == errors);
    config.SaveFailure = null;
    typeof(OptionalModCompatibility).GetField("_retryAfterUtcTicks", System.Reflection.BindingFlags.Static
        | System.Reflection.BindingFlags.NonPublic).SetValue(null, 0L);
    OptionalModCompatibility.Refresh();
    Check(!config.SavedDisable && config.SavedHandled == OptionalModCompatibility.ClaimsId && config.Saves == 3);
    Check(EventLog.Lines.Any(l => l.Contains("save recovered")));
});
Test("compatibility logs change only on transitions and notice waits for gameplay", () =>
{
    OptionalModCompatibility.Tick();
    EventLog.Lines.Clear(); PanelNotice.Lines.Clear();
    var claims = Add(OptionalModCompatibility.ClaimsId);
    EventLog.Lines.Clear();
    OptionalModCompatibility.Tick(); OptionalModCompatibility.Tick();
    Check(EventLog.Lines.Count == 1 && PanelNotice.Lines.Count == 0);
    Plugin.StoreValue = new object();
    for (int i = 0; i < 100; i++) OptionalModCompatibility.Tick();
    Check(EventLog.Lines.Count == 1 && PanelNotice.Lines.Count == 1);
    claims.IsEnabled = false; OptionalModCompatibility.Tick();
    claims.IsEnabled = true; OptionalModCompatibility.Tick();
    Check(EventLog.Lines.Count == 3 && PanelNotice.Lines.Count == 2);
});
Test("warnings have Chinese and English variants", () =>
{
    Add(OptionalModCompatibility.ClaimsId);
    Check(OptionalModCompatibility.MemoryCheckpointWarning.Contains("unsupported"));
    InspectionLocale.Chinese = true;
    Check(OptionalModCompatibility.MemoryCheckpointWarning == "存在mod冲突，已禁用内存节点。\n可在设置中强行启用，但不受理因此导致的BUG。");
});
Test("read-only loader snapshots are shared within a tick and refreshed at action boundaries", () =>
{
    OptionalModCompatibility.Tick();
    ModLoader.Reads = 0;
    for (int i = 0; i < 100; i++) Check(OptionalModCompatibility.MemoryCheckpointsAllowed);
    Check(ModLoader.Reads == 0);
    ModLoader.Registered.Add(new() { Manifest = new() { ID = OptionalModCompatibility.ClaimsId }, IsEnabled = true });
    OptionalModCompatibility.Refresh();
    Check(!OptionalModCompatibility.MemoryCheckpointsAllowed && ModLoader.Reads == 1);
});
Test("per-frame ticks reuse loader snapshot, polling and action boundaries still discover additions", () =>
{
    OptionalModCompatibility.Tick();
    ModLoader.Reads = 0;
    for (int i = 0; i < 100; i++) OptionalModCompatibility.Tick();
    Check(ModLoader.Reads == 0);
    ModLoader.Registered.Add(new() { Manifest = new() { ID = OptionalModCompatibility.ClaimsId }, IsEnabled = true });
    UnityEngine.Time.unscaledTime += 1f;
    OptionalModCompatibility.Tick();
    Check(ModLoader.Reads == 1 && !OptionalModCompatibility.MemoryCheckpointsAllowed);
    Plugin.CfgDisableMemoryCheckpoints.Value = false;
    OptionalModCompatibility.Tick();
    Check(OptionalModCompatibility.MemoryCheckpointsAllowed && ModLoader.Reads == 1);
    Check(OptionalModCompatibility.Status.Contains("forced on"));
});
Console.WriteLine($"Passed {passed} optional-mod compatibility tests.");
