using ProbablyStolenTools;

int passed = 0;
void Test(string name, Action action)
{
    action(); passed++; Console.WriteLine("PASS " + name);
}
void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }

ManagedSnapshotTests.Run(Test, Check);

Test("cycle and shared object keep original identity", () =>
{
    var memory = new FakeMemory();
    memory.Node(1, 10, 2, 2); memory.Node(2, 20, 1, 0);
    using var graph = StateGraph.Capture(memory, new[] { (IntPtr)1 });
    memory.Number(2, 99); memory.Link(1, 8, 0);
    Check(graph.Differences().Count == 2);
    graph.Restore();
    Check(memory.Number(2) == 20 && memory.Link(1, 8) == 2 && memory.Link(1, 16) == 2);
    Check(memory.Link(2, 8) == 1 && graph.ObjectCount == 2);
    Check(graph.Differences().Count == 0);
});
Test("collection backing-array replacement, deletion and new objects", () =>
{
    var memory = new FakeMemory();
    memory.Node(1, 2, 2, 0); // list count + backing array
    memory.Node(2, 0, 3, 4); memory.Node(3, 30, 0, 0); memory.Node(4, 40, 0, 0);
    using var graph = StateGraph.Capture(memory, new[] { (IntPtr)1 });
    memory.Node(5, 0, 6, 0); memory.Node(6, 60, 0, 0);
    memory.Number(1, 1); memory.Link(1, 8, 5); memory.Link(2, 8, 0); memory.Number(3, 300);
    graph.Restore();
    Check(memory.Number(1) == 2 && memory.Link(1, 8) == 2);
    Check(memory.Link(2, 8) == 3 && memory.Link(2, 16) == 4 && memory.Number(3) == 30);
    Check(!graph.Contains((IntPtr)5));
    Check(graph.Differences().Count == 0);
});
Test("null fields restored and snapshots reusable", () =>
{
    var memory = new FakeMemory(); memory.Node(1, 10, 0, 0);
    using var graph = StateGraph.Capture(memory, new[] { (IntPtr)1 });
    for (int i = 0; i < 5; i++)
    {
        memory.Node(2, 20, 0, 0); memory.Link(1, 8, 2); memory.Number(1, i);
        graph.Restore();
        Check(memory.Number(1) == 10 && memory.Link(1, 8) == 0 && graph.Differences().Count == 0);
    }
});
Test("retained handles disposed and limits fail closed without writing", () =>
{
    var memory = new FakeMemory(); memory.Node(1, 10, 2, 0); memory.Node(2, 20, 0, 0);
    try { StateGraph.Capture(memory, new[] { (IntPtr)1 }, maxObjects: 1); throw new Exception("Expected limit"); }
    catch (InvalidOperationException) { }
    Check(memory.Disposed && memory.Writes == 0 && memory.Number(1) == 10);
});
Test("capture read failure releases resources and never returns partial snapshot", () =>
{
    var memory = new FakeMemory(); memory.Node(1, 10, 2, 0);
    try { StateGraph.Capture(memory, new[] { (IntPtr)1 }); throw new Exception("Expected missing node"); }
    catch (KeyNotFoundException) { }
    Check(memory.Disposed && memory.Writes == 0);
});
Test("preflight failure prevents every write", () =>
{
    var memory = new FakeMemory(); memory.Node(1, 10, 0, 0);
    using var graph = StateGraph.Capture(memory, new[] { (IntPtr)1 });
    memory.Invalid = true;
    try { graph.Restore(); throw new Exception("Expected stale handle"); }
    catch (InvalidOperationException) { }
    Check(memory.Writes == 0);
});
Test("arrival dedup, scene/day/customer/store invalidation", () =>
{
    var boundary = new CustomerBoundary();
    Check(boundary.Observe((IntPtr)1, (IntPtr)2, 3, 4));
    boundary.MarkReady();
    Check(!boundary.Observe((IntPtr)1, (IntPtr)2, 3, 4) && boundary.Ready);
    Check(boundary.Observe((IntPtr)1, (IntPtr)5, 3, 4) && !boundary.Ready && !boundary.Attempted);
    boundary.MarkAttempt(); Check(boundary.Attempted && !boundary.Ready);
    Check(boundary.Observe((IntPtr)1, (IntPtr)5, 6, 4));
    boundary.MarkReady(); Check(boundary.Observe((IntPtr)1, (IntPtr)5, 6, 7) && !boundary.Ready);
    Check(boundary.Observe((IntPtr)8, (IntPtr)5, 6, 7));
    boundary.Invalidate(); Check(!boundary.Ready && boundary.Attempted);
    Check(boundary.Observe((IntPtr)8, IntPtr.Zero, 6, 7));
});
Test("object source paths follow first references without cycling", () =>
{
    var memory = new FakeMemory();
    memory.Node(1, 10, 2, 2); memory.Node(2, 20, 1, 3); memory.Node(3, 30, 0, 0);
    using var graph = StateGraph.Capture(memory, new[] { (IntPtr)1 });
    Check(graph.SourceOf((IntPtr)1) == "root");
    Check(graph.SourceOf((IntPtr)2) == "root -> first");
    Check(graph.SourceOf((IntPtr)3) == "root -> first -> second");
    Check(graph.SourceOf((IntPtr)99) == "");
});
Test("explicit roots keep their root identity when also referenced", () =>
{
    var memory = new FakeMemory(); memory.Node(1, 10, 2, 0); memory.Node(2, 20, 1, 0);
    using var graph = StateGraph.Capture(memory, new[] { (IntPtr)1, (IntPtr)2 });
    Check(graph.SourceOf((IntPtr)2) == "root" && graph.ObjectCount == 2);
});
Test("history keeps 10 entries and releases evicted snapshots once", () =>
{
    var history = new CheckpointHistory<TestSnapshot>(10);
    var entries = Enumerable.Range(0, 12).Select(_ => new TestSnapshot()).ToArray();
    foreach (var entry in entries) history.Add(entry);
    Check(history.Items.Count == 10 && history.Items[0] == entries[2]);
    Check(entries[0].Disposed == 1 && entries[1].Disposed == 1 && entries[2].Disposed == 0);
    history.Clear();
    Check(entries.All(e => e.Disposed == 1));
});
Test("explicit branch truncation preserves selected and older entries", () =>
{
    var history = new CheckpointHistory<TestSnapshot>(10);
    var entries = Enumerable.Range(0, 5).Select(_ => new TestSnapshot()).ToArray();
    foreach (var entry in entries) history.Add(entry);
    history.TruncateAfter(entries[1]);
    Check(history.Items.Count == 2 && entries.Take(2).All(e => e.Disposed == 0));
    Check(entries.Skip(2).All(e => e.Disposed == 1));
    history.TruncateAfter(entries[1]);
    var branch = new TestSnapshot(); history.Add(branch);
    Check(history.Items.Count == 3 && history.Items[2] == branch);
    try { history.TruncateAfter(entries[4]); throw new Exception("Expected expired node"); }
    catch (InvalidOperationException) { }
    history.Clear();
});
Test("rewind keeps all nodes until the next customer commits a new branch", () =>
{
    var history = new CheckpointHistory<TestSnapshot>(10);
    var nodes = Enumerable.Range(0, 5).Select(_ => new TestSnapshot()).ToArray();
    foreach (var node in nodes) history.Add(node);
    int revision = history.Revision;
    history.Select(nodes[1]);
    Check(history.Items.Count == 5 && nodes.All(n => n.Disposed == 0));
    Check(history.Revision == revision && !history.IsFuture(nodes[1]) && history.IsFuture(nodes[2]));
    history.Select(nodes[3]);
    Check(history.Items.Count == 5 && !history.IsFuture(nodes[2]) && history.IsFuture(nodes[4]));
    Check(history.CommitBranch() == 1 && history.Items.Count == 4 && nodes[4].Disposed == 1);
    Check(history.CommitBranch() == 0 && nodes.Take(4).All(n => n.Disposed == 0));
    history.Clear();
});
Test("adding a customer is a fallback branch commit and preserves restored node", () =>
{
    var history = new CheckpointHistory<TestSnapshot>(10);
    var a = new TestSnapshot(); var b = new TestSnapshot(); var c = new TestSnapshot();
    history.Add(a); history.Add(b); history.Select(a); history.Add(c);
    Check(history.Items.SequenceEqual(new[] { a, c }) && a.Disposed == 0 && b.Disposed == 1);
    history.Clear();
});
Test("closing appends a stage without removing the ten retained customers", () =>
{
    var history = new CheckpointHistory<TestSnapshot>(10, n => !n.Closed);
    var customers = Enumerable.Range(0, 10).Select(_ => new TestSnapshot()).ToArray();
    foreach (var node in customers) history.Add(node);
    var closed = new TestSnapshot { Closed = true };
    history.Add(closed, commitBranch: false);
    Check(history.Items.Count == 11 && customers.All(n => n.Disposed == 0));
    history.Select(customers[3]);
    Check(history.Items.Count == 11 && closed.Disposed == 0 && history.IsFuture(closed));
    history.Select(closed);
    Check(history.CommitBranch() == 0 && history.Items.Count == 11);
    history.Select(customers[3]);
    Check(history.CommitBranch() == 7 && history.Items.Count == 4 && closed.Disposed == 1);
    Check(customers.Take(4).All(n => n.Disposed == 0) && customers.Skip(4).All(n => n.Disposed == 1));
    history.Clear();
});
Test("capturing a close while browsing history does not commit the customer branch", () =>
{
    var history = new CheckpointHistory<TestSnapshot>(10, n => !n.Closed);
    var a = new TestSnapshot(); var b = new TestSnapshot();
    history.Add(a); history.Add(b); history.Select(a);
    var closed = new TestSnapshot { Closed = true };
    history.Add(closed, commitBranch: false);
    Check(history.Items.Count == 3 && b.Disposed == 0 && history.IsFuture(b) && history.IsFuture(closed));
    var c = new TestSnapshot(); history.Add(c);
    Check(history.Items.SequenceEqual(new[] { a, c }) && b.Disposed == 1 && closed.Disposed == 1);
    history.Clear();
});
Test("stage retention does not reduce customer capacity and old stages are released once", () =>
{
    var history = new CheckpointHistory<TestSnapshot>(10, n => !n.Closed);
    var closed = new TestSnapshot { Closed = true }; history.Add(closed, commitBranch: false);
    var customers = Enumerable.Range(0, 12).Select(_ => new TestSnapshot()).ToArray();
    foreach (var node in customers) history.Add(node);
    Check(history.Items.Count == 11 && closed.Disposed == 0);
    Check(customers.Take(2).All(n => n.Disposed == 1) && customers.Skip(2).All(n => n.Disposed == 0));
    history.Remove(closed); history.Remove(closed);
    Check(closed.Disposed == 1 && history.Items.Count == 10);
    history.Select(customers[3]); history.Clear();
    Check(history.CommitBranch() == 0 && customers.All(n => n.Disposed == 1));
});
Test("replacing a closed stage preserves customers and the selected customer branch", () =>
{
    var history = new CheckpointHistory<TestSnapshot>(10, n => !n.Closed);
    var a = new TestSnapshot(); var b = new TestSnapshot();
    var firstClose = new TestSnapshot { Closed = true };
    var secondClose = new TestSnapshot { Closed = true };
    history.Add(a); history.Add(b); history.Add(firstClose, commitBranch: false);
    history.Select(a);
    history.Remove(firstClose); history.Add(secondClose, commitBranch: false);
    Check(history.Items.SequenceEqual(new[] { a, b, secondClose }));
    Check(firstClose.Disposed == 1 && b.Disposed == 0 && history.IsFuture(b));
    history.Select(secondClose);
    history.Remove(secondClose);
    var thirdClose = new TestSnapshot { Closed = true };
    history.Add(thirdClose, commitBranch: false);
    Check(history.CommitBranch() == 0 && history.Items.SequenceEqual(new[] { a, b, thirdClose }));
    history.Clear();
    Check(new[] { a, b, firstClose, secondClose, thirdClose }.All(n => n.Disposed == 1));
});
Test("closed and open business graphs restore both ways without deleting customer history", () =>
{
    var history = new CheckpointHistory<StateGraph>(10);
    var memory = new FakeMemory();
    memory.Node(1, 1, 2, 3); // OPEN, current customer, remaining queue
    memory.Node(2, 50, 0, 0); memory.Node(3, 2, 0, 0);
    var open = StateGraph.Capture(memory, new[] { (IntPtr)1 });
    history.Add(open);
    memory.Number(1, 0); memory.Link(1, 8, 0); memory.Number(3, 0);
    var closed = StateGraph.Capture(memory, new[] { (IntPtr)1 });
    history.Add(closed, commitBranch: false);
    for (int i = 0; i < 3; i++)
    {
        open.Restore(); history.Select(open);
        Check(memory.Number(1) == 1 && memory.Link(1, 8) == 2 && memory.Number(3) == 2);
        closed.Restore(); history.Select(closed);
        Check(memory.Number(1) == 0 && memory.Link(1, 8) == 0 && memory.Number(3) == 0);
        Check(history.Items.Count == 2);
    }
    history.Clear();
});
Test("retained future business graphs remain restorable after browsing backwards", () =>
{
    var history = new CheckpointHistory<StateGraph>(10);
    var memory = new FakeMemory(); memory.Node(1, 10, 0, 0);
    var first = StateGraph.Capture(memory, new[] { (IntPtr)1 }); history.Add(first);
    memory.Number(1, 20);
    var second = StateGraph.Capture(memory, new[] { (IntPtr)1 }); history.Add(second);
    first.Restore(); history.Select(first);
    Check(memory.Number(1) == 10 && history.Items.Count == 2);
    second.Restore(); history.Select(second);
    Check(memory.Number(1) == 20 && history.Items.Count == 2);
    first.Restore(); history.Select(first); history.CommitBranch();
    Check(memory.Number(1) == 10 && history.Items.Count == 1);
    history.Clear();
});
Test("clearing history at day or scene boundary releases every retained snapshot", () =>
{
    var history = new CheckpointHistory<TestSnapshot>(10);
    var snapshot = new TestSnapshot(); history.Add(snapshot);
    history.Clear(); history.Clear();
    Check(history.Items.Count == 0 && snapshot.Disposed == 1);
});
Test("double click requires the same node and resets after restore or refresh", () =>
{
    var clicks = new NodeDoubleClick(); var a = new object(); var b = new object();
    Check(!clicks.Click(a, 1) && !clicks.Click(b, 1.1f) && clicks.Click(b, 1.2f));
    Check(!clicks.Click(b, 1.3f) && !clicks.Click(b, 2));
    clicks.Reset();
    Check(!clicks.Click(b, 2.1f));
});
Test("panel bounds fit small displays, clamp offscreen geometry and reject nonfinite config", () =>
{
    var bounds = new PanelBounds(5000, -40, 2000, 20).Constrain(1920, 1080);
    Check(bounds.Width == 900 && bounds.Height == 180 && bounds.X == 1020 && bounds.Y == 0);
    var small = new PanelBounds(90, 90, 500, 500).Constrain(320, 160);
    Check(small.Width == 320 && small.Height == 160 && small.X == 0 && small.Y == 0);
    var invalid = new PanelBounds(float.NaN, float.PositiveInfinity, float.NaN, float.NegativeInfinity).Constrain(1920, 1080);
    Check(invalid == new PanelBounds(16, 16, 480, 400));
});
Test("header buttons have fixed nonoverlapping bounds outside the drag region", () =>
{
    foreach (float width in new[] { 320f, 380f, 480f, 900f })
    {
        float settings = PanelHeader.SettingsX(width), fold = PanelHeader.FoldX(width);
        Check(settings > 0 && settings + PanelHeader.ButtonWidth < fold
            && fold + PanelHeader.ButtonWidth <= width);
        Check(PanelHeader.CanDrag(10, 10, width));
        Check(!PanelHeader.CanDrag(settings - 1, 10, width));
        Check(!PanelHeader.CanDrag(settings + 10, 10, width));
        Check(!PanelHeader.CanDrag(fold + 10, 10, width));
        Check(!PanelHeader.CanDrag(10, PanelHeader.Height + 1, width));
    }
});
Test("inspection summary keeps its old default margin and moves independently of text scrolling", () =>
{
    var position = new InspectionWindowPosition(1, 1);
    var bounds = position.Resolve(1920, 1080);
    Check(bounds == new PanelBounds(1596, 816, 300, 240));
    var moved = position.Drag(bounds, -200, -300, 1920, 1080).Resolve(1920, 1080);
    Check(Math.Abs(moved.X - 1396) < 0.01f && Math.Abs(moved.Y - 516) < 0.01f);
    Check(moved.Width == 300 && moved.Height == 240);
});
Test("inspection summary clamps drags on all four edges", () =>
{
    var position = new InspectionWindowPosition(0.5f, 0.5f);
    var bounds = position.Resolve(1920, 1080);
    Check(position.Drag(bounds, -10000, -10000, 1920, 1080).Resolve(1920, 1080)
        == new PanelBounds(24, 24, 300, 240));
    Check(position.Drag(bounds, 10000, 10000, 1920, 1080).Resolve(1920, 1080)
        == new PanelBounds(1596, 816, 300, 240));
});
Test("inspection summary keeps relative position after resize and temporary tiny layouts", () =>
{
    var position = new InspectionWindowPosition(0.25f, 0.75f);
    foreach (var size in new[] { (1920f, 1080f), (1280f, 720f), (3440f, 1440f), (320f, 250f), (200f, 100f) })
    {
        var bounds = position.Resolve(size.Item1, size.Item2);
        Check(bounds.X >= 0 && bounds.Y >= 0
            && bounds.X + bounds.Width <= size.Item1 && bounds.Y + bounds.Height <= size.Item2);
        var unchanged = position.Drag(bounds, 0, 0, size.Item1, size.Item2);
        Check(Math.Abs(unchanged.X - position.X) < 0.00001f && Math.Abs(unchanged.Y - position.Y) < 0.00001f);
    }
    var restored = position.Resolve(1920, 1080);
    Check(restored == new PanelBounds(417, 618, 300, 240));
});
Test("inspection summary rejects corrupt positions and zero or nonfinite display sizes", () =>
{
    foreach (var position in new[] {
        new InspectionWindowPosition(float.NaN, float.PositiveInfinity),
        new InspectionWindowPosition(-100f, 100f)
    })
    foreach (var size in new[] { (1920f, 1080f), (0f, 0f), (float.NaN, float.PositiveInfinity) })
    {
        var bounds = position.Resolve(size.Item1, size.Item2);
        Check(float.IsFinite(bounds.X) && float.IsFinite(bounds.Y)
            && float.IsFinite(bounds.Width) && float.IsFinite(bounds.Height)
            && bounds.X >= 0 && bounds.Y >= 0 && bounds.Width > 0 && bounds.Height > 0);
    }
});
Test("MelonLoader dotted category regression is reproduced without the game host", () =>
{
    var file = new MelonPreferenceFixture();
    file.Put("dsh.probablystolen.tools.Inspection", "ShowQuickInspection", false);
    var loaded = new MelonPreferenceFixture(file.Serialize());
    Check(loaded.Read("dsh.probablystolen.tools.Inspection", "ShowQuickInspection", true));
});
Test("quick inspection toggle and position survive the real MelonLoader TOML round trip", () =>
{
    string category = ModConfig.CategoryIdentifier("Inspection");
    var file = new MelonPreferenceFixture();
    file.Put(category, "ShowQuickInspection", false);
    file.Put(category, "SummaryPositionX", 0.25f);
    file.Put(category, "SummaryPositionY", 0.75f);
    string path = Path.Combine(AppContext.BaseDirectory, "preferences-test-" + Guid.NewGuid().ToString("N") + ".cfg");
    try
    {
        File.WriteAllText(path, file.Serialize());
        var loaded = new MelonPreferenceFixture(File.ReadAllText(path));
        Check(!loaded.Read(category, "ShowQuickInspection", true));
        var position = new InspectionWindowPosition(loaded.Read(category, "SummaryPositionX", 1f),
            loaded.Read(category, "SummaryPositionY", 1f));
        Check(position.Resolve(1920, 1080) == new PanelBounds(417, 618, 300, 240));
        loaded.Put(category, "ShowQuickInspection", true);
        Check(new MelonPreferenceFixture(loaded.Serialize()).Read(category, "ShowQuickInspection", false));
    }
    finally { File.Delete(path); }
});
Test("all mod sections persist while old empty tables and third-party preferences stay intact", () =>
{
    string source = "[dsh]\n[dsh.probablystolen]\n[dsh.probablystolen.tools]\nInspection = { }\n"
        + "[ProbablyStolenInspector]\nwindow_pos_x = 106.5\nEnableAuthenticity = true\n";
    var file = new MelonPreferenceFixture(source);
    string[] sections = { "AutoAppraisal", "Dialogue", "Inspection", "Rewind", "Debug", "History", "CustomerCheckpoint", "Panel" };
    for (int i = 0; i < sections.Length; i++) file.Put(ModConfig.CategoryIdentifier(sections[i]), "Probe", i + 1);
    var loaded = new MelonPreferenceFixture(file.Serialize());
    for (int i = 0; i < sections.Length; i++) Check(loaded.Read<int>(ModConfig.CategoryIdentifier(sections[i]), "Probe") == i + 1);
    Check(loaded.Read<float>("ProbablyStolenInspector", "window_pos_x") == 106.5f);
    Check(loaded.Read<bool>("ProbablyStolenInspector", "EnableAuthenticity"));
});
Test("ES3 metadata comes from exact paths, not nested decoys or strings", () =>
{
    string source = "{\"playerStore\":{\"value\":{\"decoy\":{\"playerCash\":999,\"storeState\":1},"
        + "\"text\":\"\\\"dayCounter\\\":999\", \"playerCash\":42,\"saveSlotId\":0,\"storeState\":2}},"
        + "\"storeStation\":{\"value\":{\"dayCounter\":24}}}";
    Check(SaveMetadata.Read(source) == new SaveMetadata(24, 42, 2, 0));
});
Test("ES3 supports unquoted numeric dictionaries and escaped typographic quotes", () =>
{
    string source = "{\"playerStore\":{\"value\":{\"positions\":{33:[1,2],-102:[0]},"
        + "\"text\":\"escaped \\\u201cquote\\\u201d\",\"playerCash\":-4,\"saveSlotId\":1,\"storeState\":0}},"
        + "\"storeStation\":{\"value\":{\"dayCounter\":25}}}";
    Check(SaveMetadata.Read(source) == new SaveMetadata(25, -4, 0, 1));
});
Test("ES3 metadata reads day at end of large files", () =>
{
    string source = "{\"playerStore\":{\"value\":{\"payload\":\"" + new string('x', 350000)
        + "\",\"playerCash\":57,\"saveSlotId\":0,\"storeState\":2}},"
        + "\"storeStation\":{\"value\":{\"dayCounter\":24}}}";
    Check(SaveMetadata.Read(source).Day == 24);
});
Test("ES3 reads run identity independently of nested decoys", () =>
{
    string source = "{\"playerStore\":{\"value\":{\"decoy\":{\"runID\":\"wrong\"},\"runID\":\"run-24\","
        + "\"playerCash\":57,\"saveSlotId\":0,\"storeState\":2}},"
        + "\"storeStation\":{\"value\":{\"dayCounter\":24}}}";
    Check(SaveMetadata.Read(source).RunId == "run-24");
});
Test("missing, duplicate, truncated and nonnumeric metadata fail closed", () =>
{
    foreach (string source in new[] {
        "{}", "{\"playerStore\":{\"value\":{\"playerCash\":1,\"playerCash\":2}}}",
        "{\"playerStore\":{\"value\":{\"playerCash\":\"not a number\"}}}", "{\"playerStore\":",
        "{\"ignored\":[}", "{\"ignored\":\"unterminated\\"
    })
    {
        try { SaveMetadata.Read(source); throw new Exception("Expected invalid metadata"); }
        catch (FormatException) { }
    }
});
Test("authenticity uses actual native conditions even when a counterfeit claim matches", () =>
{
    Check(InspectionChecks.AuthenticityPass("CATEGORY_GENUINE_CIGARETTE", "genuineCigarette") == true);
    Check(InspectionChecks.AuthenticityPass("CATEGORY_GENUINE_CIGARETTE", "counterfeitCigarette") == false);
    Check(InspectionChecks.AuthenticityPass("CATEGORY_GENUINE_STAMP", "genuineStamp") == true);
    foreach (string condition in new[] { "counterfeitInjector", "expiredInjector", "unusableInjector" })
        Check(InspectionChecks.AuthenticityPass("CATEGORY_GENUINE_INJECTOR", condition) == false);
    Check(InspectionChecks.AuthenticityPass("CATEGORY_GENUINE_INJECTOR", "genuineInjector") == true);
    Check(InspectionChecks.AuthenticityPass("CATEGORY_GENUINE_INJECTOR", null) == null);
    Check(InspectionChecks.AuthenticityPass("UNKNOWN", "genuineInjector") == null);
});
Test("inspection checks follow the current item and do not retain previous issue results", () =>
{
    var issues = new HashSet<string> { "ISSUE_SERIAL", "CIGARETTE_BOX_ISSUE" };
    var drugs = InspectionChecks.For("INS_INJECTOR_TAG");
    Check(drugs.Count == 5);
    Check(drugs.Count(c => issues.Contains(c.IssueTag)) == 1);
    Check(InspectionChecks.For("FOOD_STAMP_TAG").All(c => !issues.Contains(c.IssueTag)));
    Check(InspectionChecks.For("LIQUID_CONTAINER_TAG").Count == 0);
    Check(InspectionChecks.For("UNKNOWN").Count == 0);
    Check(drugs.All(c => !string.IsNullOrWhiteSpace(c.Chinese)
        && !string.IsNullOrWhiteSpace(c.English) && !string.IsNullOrWhiteSpace(c.LocalizationKey)));
});
if (args.Length > 0)
{
    Test("real ES3 save metadata (read only)", () =>
    {
        var metadata = SaveMetadata.Read(File.ReadAllText(args[0]).TrimStart('\uFEFF'));
        Check(metadata.Day >= 0 && metadata.Slot >= 0 && metadata.State >= 0 && metadata.State <= 2
            && !string.IsNullOrEmpty(metadata.RunId));
        Console.WriteLine($"ES3 day={metadata.Day} cash={metadata.Cash} state={metadata.State} slot={metadata.Slot}");
    });
}
Console.WriteLine($"{passed} tests passed. Loader integration and game UI require in-game verification.");

sealed class TestSnapshot : IDisposable
{
    internal int Disposed;
    internal bool Closed;
    public void Dispose() { Disposed++; }
}

sealed class FakeMemory : IStateAccess
{
    private readonly Dictionary<long, Dictionary<int, byte[]>> _nodes = new();
    private static readonly StateSlot[] Slots =
    {
        new(0, 4, false, "value"), new(8, 8, true, "first"), new(16, 8, true, "second")
    };
    internal bool Disposed, Invalid;
    internal int Writes;
    internal void Node(long id, int number, long first, long second)
        => _nodes[id] = new() { [0] = BitConverter.GetBytes(number),
            [8] = BitConverter.GetBytes(first), [16] = BitConverter.GetBytes(second) };
    internal void Number(long id, int value) => _nodes[id][0] = BitConverter.GetBytes(value);
    internal int Number(long id) => BitConverter.ToInt32(_nodes[id][0]);
    internal void Link(long id, int offset, long value) => _nodes[id][offset] = BitConverter.GetBytes(value);
    internal long Link(long id, int offset) => BitConverter.ToInt64(_nodes[id][offset]);
    public void Retain(IntPtr id) { if (!_nodes.ContainsKey(id.ToInt64())) throw new KeyNotFoundException(); }
    public IReadOnlyList<StateSlot> Describe(IntPtr id) => Slots;
    public byte[] ReadValue(IntPtr id, StateSlot slot) => (byte[])_nodes[id.ToInt64()][slot.Offset].Clone();
    public IntPtr ReadReference(IntPtr id, StateSlot slot) => (IntPtr)Link(id.ToInt64(), slot.Offset);
    public void WriteValue(IntPtr id, StateSlot slot, byte[] value)
    { Writes++; _nodes[id.ToInt64()][slot.Offset] = (byte[])value.Clone(); }
    public void WriteReference(IntPtr id, StateSlot slot, IntPtr value)
    { Writes++; Link(id.ToInt64(), slot.Offset, value.ToInt64()); }
    public void Validate() { if (Invalid) throw new InvalidOperationException("stale handle"); }
    public void Dispose() { Disposed = true; }
}
