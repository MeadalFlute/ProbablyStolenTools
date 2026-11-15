using System.Globalization;
using System.Text.RegularExpressions;

// Only the game's API surface is faked. File parsing, planning, guards and
// deletion are the production implementation operating on a temporary directory.
public sealed class PlayerStore
{
    public int saveSlotId = 2, storeState = 2;
    public string runID = "run";
    public void SaveGame() { }
}
public static class StoreStation
{
    public static int Day = 95;
    public static int GetDayCounter() => Day;
}
public sealed class SaveHistoryEntry
{
    public string file;
    public int dayCount;
    public DateTime saveDateTime;
}
public static class SaveFiles
{
    public static string Root;
    public static List<SaveHistoryEntry> History = new();
    public static int IndexWrites;
    public static bool FailIndex;
    public static string Slot(int slot) => $"save_{slot}.es3";
    public static string AbsolutePath(string path) => Path.Combine(Root, path);
    public static List<SaveHistoryEntry> LoadHistory(int slot) =>
        History.Where(e => e != null && File.Exists(AbsolutePath(e.file))).OrderByDescending(e => e.saveDateTime).ToList();
    public static void IndexSave(string key, List<SaveHistoryEntry> history)
    {
        if (FailIndex) throw new IOException("fixture index failure");
        History = history.ToList(); IndexWrites++;
    }
    public static void IndexDeleteKey(string key) => IndexSave(key, new List<SaveHistoryEntry>());
    public static bool TryParseHistoryName(string stem, out int slot, out int day, out DateTime stamp)
    {
        var match = Regex.Match(stem, @"^save_(\d+)_day(\d+)_(\d{8}-\d{6})$");
        slot = -1; day = 0; stamp = DateTime.MinValue;
        return match.Success && int.TryParse(match.Groups[1].Value, out slot)
            && int.TryParse(match.Groups[2].Value, out day)
            && DateTime.TryParseExact(match.Groups[3].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out stamp);
    }
}
public static class ModHelper
{
    public static readonly ModConfig Config = new();
    public static ModConfig GetConfig(string id) => Config;
}
public sealed class ModConfig
{
    public readonly Dictionary<string, ConfigEntry<int>> Entries = new();
    public ConfigEntry<int> Define(string key, int defaultValue, string section, Func<string> label,
        int? min = null, int? max = null, Func<string> tooltip = null)
    {
        var entry = new ConfigEntry<int>(defaultValue, min.Value, max.Value);
        Entries[key] = entry;
        return entry;
    }
}
public sealed class ConfigEntry<T>
{
    public T Value { get; private set; }
    public event Action<T> OnChanged;
    private readonly int _min, _max;
    public ConfigEntry(T initial, int min, int max) { Value = initial; _min = min; _max = max; }
    public void Set(T value)
    {
        if (value is int number) value = (T)(object)Math.Clamp(number, _min, _max);
        if (EqualityComparer<T>.Default.Equals(Value, value)) return;
        Value = value;
        OnChanged?.Invoke(value);
    }
}
public sealed class ModLog
{
    public void Log(string s) { }
    public void Warning(string s) { }
    public void Error(string s) { }
}
namespace UnityEngine
{
    public static class Application { public static string persistentDataPath => SaveFiles.Root; }
    public static class Debug
    {
        public static void Log(string s) { }
        public static void LogWarning(string s) { }
        public static void LogError(string s) { }
    }
}
namespace ProbablyStolenTools
{
    internal sealed class Setting { public bool Value = true; }
    internal static class Plugin
    {
        internal const string Guid = "fixture";
        internal static Setting CfgRewind = new();
        internal static ConfigEntry<int> CfgMaxObjects, CfgMaxSlots, CfgMaxBytesMb;
    }
    internal static class InspectionLocale { internal static string Text(string zh, string en) => en; }
    internal static class EventLog
    {
        internal static List<string> Lines = new();
        internal static void Write(string line) => Lines.Add(line);
    }
    internal static class NativeLoadDiagnostics { internal static void RecordHistory(long started) { } }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string name) { } }
    public sealed class HarmonyPrefix : Attribute { }
    public sealed class HarmonyPostfix : Attribute { }
    public sealed class HarmonyFinalizer : Attribute { }
}
