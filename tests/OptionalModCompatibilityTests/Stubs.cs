using System.Collections.Generic;

public sealed class LoadedMod
{
    public ModManifest Manifest = new();
    public bool IsEnabled;
}
public sealed class ModManifest { public string ID; }
public static class ModLoader
{
    public static List<LoadedMod> Registered = new();
    public static int Reads;
    public static List<LoadedMod> Mods { get { Reads++; return new(Registered); } }
}
public sealed class GameItem
{
    public string identifier;
    public Dictionary<string, int> Tags = new();
    public List<GameItem> Contents = new();
    public bool IsTag(string key) => Tags.ContainsKey(key);
    public TagState GetTagReadonly(string key) => new() { Value = Tags.TryGetValue(key, out int value) ? value : 0 };
    public List<GameItem> FindAllChildItems() => Contents;
}
public sealed class TagState { public int Value; public int GetInt() => Value; }
public static class WaterHelper
{
    public static int Calls;
    public static int GetCurrentCapacityML(GameItem item) { Calls++; return 750; }
    public static int GetWaterPurity(GameItem item) => 10000;
}
public static class WineHelper
{
    public static int Calls;
    public static int GetWaterQualityScore(GameItem item) { Calls++; return 2; }
}
public static class RenderHandler
{
    public enum ColorPalette { None, LightRed, Black }
}
public sealed class RichTextBuilder
{
    public readonly List<string> Lines = new();
    public void AddLine(string text, bool a, RenderHandler.ColorPalette b, bool c, bool d, bool e, bool f,
        RenderHandler.ColorPalette g, RenderHandler.ColorPalette h, RenderHandler.ColorPalette i) => Lines.Add(text);
}
namespace UnityEngine { internal static class Time { internal static float unscaledTime; } }
namespace ProbablyStolenTools
{
    internal sealed class BoolConfig { internal bool Value = true; }
    internal sealed class StringConfig { internal string Value = ""; }
    internal sealed class Logger { internal void LogWarning(string text) => throw new Exception(text); }
    internal sealed class ModConfig
    {
        internal bool SaveOnConfigSet = true;
        internal Exception SaveFailure;
        internal int Saves;
        internal bool SavedDisable;
        internal string SavedHandled;
        internal void Save()
        {
            Saves++;
            if (SaveFailure != null) throw SaveFailure;
            SavedDisable = Plugin.CfgDisableMemoryCheckpoints.Value;
            SavedHandled = Plugin.CfgHandledMemoryConflicts.Value;
        }
    }
    internal sealed class ConfigHost { internal ModConfig Config = new(); }
    internal static class Plugin
    {
        internal static ConfigHost Instance = new();
        internal static BoolConfig CfgWineAppraisal = new();
        internal static BoolConfig CfgDisableMemoryCheckpoints = new() { Value = false };
        internal static StringConfig CfgHandledMemoryConflicts = new();
        internal static Logger LogSource = new();
        internal static object StoreValue;
        internal static object Store() => StoreValue;
    }
    internal static class EventLog
    {
        internal static bool Verbose => false;
        internal static readonly List<string> Lines = new();
        internal static void Write(string text) => Lines.Add(text);
    }
    internal static class PanelNotice
    {
        internal static readonly List<string> Lines = new();
        internal static void Show(string text, bool error = false) => Lines.Add(text);
    }
    internal static class InspectionLocale
    {
        internal static bool AnnotationsReady = true;
        internal static bool Chinese;
        internal static string Text(string cn, string en) => Chinese ? cn : en;
        internal static string Label(string key) => key;
    }
    internal static class AnnotationLabels
    {
        internal const string Contraband = "Contraband", NotEnoughWater = "Not enough water", GloomWine = "Gloom wine";
        internal static string WineGrade(int score) => "Grade " + score;
        internal static string Water(int score) => "Water " + score;
    }
}
