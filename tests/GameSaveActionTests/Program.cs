using ProbablyStolenTools;
using UnityEngine;

string parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "out"));
string root = Path.Combine(parent, "pstools-save-actions-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Application.persistentDataPath = root;
int passed = 0;
void Check(bool value) { if (!value) throw new Exception("Assertion failed."); }
void Test(string name, Action body)
{
    CustomerCheckpoint.Blocker = null;
    Rewind.Last = null; Rewind.Requests = 0;
    PanelNotice.Message = ""; EventLog.Lines.Clear();
    UnityEngine.Random.FailInit = false;
    Plugin.Current = new PlayerStore { saveSlotId = 2, runID = "current" };
    Time.unscaledTime += 1;
    body();
    Console.WriteLine("PASS " + name);
    passed++;
}
string Save(int slot = 2, string run = "current", int state = 2, int day = 94) =>
    """{"storeStation":{"value":{"dayCounter":DAY}},"playerStore":{"value":{"playerCash":123,"storeState":STATE,"saveSlotId":SLOT,"runID":"RUN"}}}"""
        .Replace("DAY", day.ToString()).Replace("STATE", state.ToString())
        .Replace("SLOT", slot.ToString()).Replace("RUN", run);
string active = Path.Combine(root, "save_2.es3");
try
{
    Test("reload request goes through confirmation instead of directly loading", () =>
    {
        GameSaveActions.RequestReload();
        Check(Rewind.Requests == 1 && Rewind.Last == null);
    });
    Test("blocked requests do not open confirmation or mutate RNG", () =>
    {
        CustomerCheckpoint.Blocker = "busy";
        var before = RNG.random; var unity = UnityEngine.Random.state;
        GameSaveActions.RequestReload(); GameSaveActions.Reload(); GameSaveActions.Reroll();
        Check(Rewind.Requests == 0 && Rewind.Last == null && ReferenceEquals(before, RNG.random));
        Check(unity == UnityEngine.Random.state && PanelNotice.Message == "busy");
    });
    Test("missing active slot never falls back to another slot's newer file", () =>
    {
        File.WriteAllText(Path.Combine(root, "save_3.es3"), Save(3));
        GameSaveActions.Reload();
        Check(Rewind.Last == null && PanelNotice.Message.Contains("no save file"));
    });
    Test("morning reload uses exact active file and forces native menu path without writing", () =>
    {
        File.WriteAllText(active, "\uFEFF" + Save());
        byte[] before = File.ReadAllBytes(active);
        var rng = RNG.random;
        GameSaveActions.Reload();
        var point = Rewind.Last;
        Check(point != null && point.Path == active && point.SlotId == 2 && point.Day == 94
            && point.Cash == 123 && point.RunId == "current" && point.IsQuickReload && point.IsDayStart);
        Check(Rewind.ForceMenu && Rewind.AllowDayChange && ReferenceEquals(rng, RNG.random));
        Check(before.SequenceEqual(File.ReadAllBytes(active)));
        Check(!File.Exists(active + ".pre_restore") && Directory.GetFiles(root).Length == 2);
    });
    Test("closed save reload is allowed without customer reconstruction", () =>
    {
        File.WriteAllText(active, Save(state: 0));
        GameSaveActions.Reload();
        Check(Rewind.Last?.IsQuickReload == true && !Rewind.Last.IsDayStart && Rewind.Last.ClientIds == null);
    });
    Test("mismatched slot is rejected without altering save bytes", () =>
    {
        File.WriteAllText(active, Save(3)); byte[] before = File.ReadAllBytes(active);
        GameSaveActions.Reload();
        Check(Rewind.Last == null && before.SequenceEqual(File.ReadAllBytes(active)));
    });
    Test("mismatched run is rejected", () =>
    {
        File.WriteAllText(active, Save(run: "other")); GameSaveActions.Reload();
        Check(Rewind.Last == null && PanelNotice.Message.Contains("does not match"));
    });
    Test("open and unknown-state files are rejected regardless of old allow-open settings", () =>
    {
        foreach (int state in new[] { 1, -1, 3 })
        {
            File.WriteAllText(active, Save(state: state)); GameSaveActions.Reload();
            Check(Rewind.Last == null && PanelNotice.Message.Contains("not a safely loadable"));
        }
    });
    Test("malformed files report the concrete parser exception", () =>
    {
        File.WriteAllText(active, "{}"); GameSaveActions.Reload();
        Check(Rewind.Last == null && PanelNotice.Message.Contains("FormatException")
            && EventLog.Lines.Any(line => line.Contains("FAILED")));
    });
    Test("conditions are rechecked after confirmation", () =>
    {
        File.WriteAllText(active, Save()); GameSaveActions.RequestReload();
        CustomerCheckpoint.Blocker = "now loading"; GameSaveActions.Reload();
        Check(Rewind.Requests == 1 && Rewind.Last == null && PanelNotice.Message == "now loading");
    });
    Test("reroll replaces both streams without a file reload or touching saved bytes", () =>
    {
        byte[] before = File.ReadAllBytes(active);
        var previous = RNG.random; var unity = UnityEngine.Random.state;
        GameSaveActions.Reroll();
        Check(!ReferenceEquals(previous, RNG.random) && UnityEngine.Random.state != unity);
        Check(Rewind.Last == null && Rewind.Requests == 0 && before.SequenceEqual(File.ReadAllBytes(active)));
        Check(EventLog.Lines.Any(line => line.Contains("REROLL OK")));
    });
    Test("reroll throttles a repeated same-frame request", () =>
    {
        GameSaveActions.Reroll(); var first = RNG.random; var unity = UnityEngine.Random.state;
        GameSaveActions.Reroll();
        Check(ReferenceEquals(first, RNG.random) && UnityEngine.Random.state == unity);
    });
    Test("partial RNG initialization failure rolls both streams back", () =>
    {
        var previous = RNG.random; var unity = UnityEngine.Random.state;
        UnityEngine.Random.FailInit = true;
        GameSaveActions.Reroll();
        Check(ReferenceEquals(previous, RNG.random) && UnityEngine.Random.state == unity);
        Check(PanelNotice.Message.Contains("InvalidOperationException"));
    });
    Console.WriteLine($"{passed} production save-action tests passed; scene/UI/native RNG still require game acceptance.");
}
finally
{
    string resolved = Path.GetFullPath(root);
    if (!Path.GetDirectoryName(resolved)!.Equals(parent,
            StringComparison.OrdinalIgnoreCase)
        || !Path.GetFileName(resolved).StartsWith("pstools-save-actions-", StringComparison.Ordinal))
        throw new IOException("Invalid fixture cleanup path.");
    Directory.Delete(resolved, true);
}

public sealed class PlayerStore { public int saveSlotId; public string runID; }
public static class RNG { public static System.Random random = new(123); }
namespace UnityEngine
{
    public static class Application { public static string persistentDataPath; }
    public static class Time { public static float unscaledTime; }
    public static class Random
    {
        public static int state = 123;
        public static bool FailInit;
        public static void InitState(int seed)
        {
            state = seed;
            if (FailInit) throw new InvalidOperationException("native-init-test");
        }
    }
}
namespace ProbablyStolenTools
{
    internal static class Plugin
    {
        internal static PlayerStore Current;
        internal static PlayerStore Store() => Current;
    }
    internal static class CustomerCheckpoint
    {
        internal static string Blocker;
        internal static string SaveActionBlocker() => Blocker;
    }
    internal static class Rewind
    {
        internal sealed class Point
        {
            public string Label, Path, RunId;
            public int Cash, SlotId, Day, State;
            public DateTime At;
            public bool IsDayStart, IsQuickReload;
            public List<string> ClientIds = null;
        }
        internal static Point Last;
        internal static bool AllowDayChange, ForceMenu;
        internal static int Requests;
        internal static void RequestQuickReload() => Requests++;
        internal static void Restore(Point point, bool allowDayChange, bool forceMenu)
        { Last = point; AllowDayChange = allowDayChange; ForceMenu = forceMenu; }
    }
    internal static class CheckpointPicker { internal static void Cancel() { } }
    internal static class PanelNotice
    {
        internal static string Message;
        internal static void Show(string message, bool warning = false) => Message = message;
    }
    internal static class InspectionLocale { internal static string Text(string chinese, string english) => english; }
    internal static class EventLog
    {
        internal static List<string> Lines = new();
        internal static void Write(string text) => Lines.Add(text);
    }
    internal static class UnityRandomState
    {
        internal static void Capture(out int state) => state = UnityEngine.Random.state;
        internal static void Restore(int state) => UnityEngine.Random.state = state;
    }
}
