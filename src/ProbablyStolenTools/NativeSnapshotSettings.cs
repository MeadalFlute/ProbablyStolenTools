#if MONO
using System;
using System.Collections.Generic;

namespace ProbablyStolenTools
{
    // XML remains authoritative across restarts so manual edits continue to work.
    // The game's gear panel is only an editor of those same limits, not a second
    // independently persisted source that could override the user's XML silently.
    internal static class NativeSnapshotSettings
    {
        private static readonly List<Action> Unsubscribe = new();
        internal static void Register()
        {
            Disable();
            var config = ModHelper.GetConfig(Plugin.Guid);
            Bind(config, "Snapshot.MaxObjects", Plugin.CfgMaxObjects, 200000, 1000, 1000000,
                "快照对象上限", "Snapshot object limit");
            Bind(config, "Snapshot.MaxSlots", Plugin.CfgMaxSlots, 3000000, 10000, 8000000,
                "快照字段槽上限", "Snapshot field-slot limit");
            Bind(config, "Snapshot.MaxBytesMB", Plugin.CfgMaxBytesMb, 256, 16, 2048,
                "单份快照字节上限（MiB）", "Per-snapshot byte limit (MiB)");
        }
        private static void Bind(global::ModConfig config, string key, ConfigEntry<int> xml,
            int defaultValue, int min, int max, string zh, string en)
        {
            var native = config.Define(key, defaultValue, "Snapshot",
                () => InspectionLocale.Text(zh, en), min: min, max: max,
                tooltip: () => InspectionLocale.Text(
                    "应用后立即写入 PSTools.settings.xml。调大上限不增加未超限节点的遍历量，但允许更大内存占用。",
                    "Apply writes PSTools.settings.xml immediately. Raising the cap does not expand below-limit graphs, but permits more memory use."));
            int Clamp(int value) => Math.Max(min, Math.Min(max, value));
            native.Set(Clamp(xml.Value));
            Action<int> apply = value => xml.Value = Clamp(value);
            EventHandler mirror = (_, _) => native.Set(Clamp(xml.Value));
            native.OnChanged += apply;
            xml.SettingChanged += mirror;
            Unsubscribe.Add(() => { native.OnChanged -= apply; xml.SettingChanged -= mirror; });
        }
        internal static void Disable()
        {
            foreach (var action in Unsubscribe) action();
            Unsubscribe.Clear();
        }
    }
}
#endif
