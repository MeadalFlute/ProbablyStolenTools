using System;
using System.IO;
using UnityEngine;

namespace ProbablyStolenTools
{
    internal static class GameSaveActions
    {
        private static float _nextReroll;

        internal static void RequestReload()
        {
            if (Ready("reload")) Rewind.RequestQuickReload();
        }

        internal static void Reload()
        {
            if (!Ready("reload")) return;
            try
            {
                var store = Plugin.Store();
                // Never use ActiveSlotPath's latest-file fallback for this explicit action.
                string path = Path.Combine(Application.persistentDataPath, "save_" + store.saveSlotId + ".es3");
                if (!File.Exists(path))
                    throw new InvalidOperationException(InspectionLocale.Text("当前槽位没有存档文件", "The active slot has no save file"));
                var metadata = SaveMetadata.Read(File.ReadAllText(path).TrimStart('\uFEFF'));
                if (metadata.Slot != store.saveSlotId || metadata.RunId != store.runID)
                    throw new InvalidOperationException(InspectionLocale.Text("存档槽位或周目与当前游戏不符",
                        "The save slot or run does not match the current game"));
                if (metadata.State != 0 && metadata.State != 2)
                    throw new InvalidOperationException(InspectionLocale.Text("该文件不是可安全载入的开业前或闭店存档",
                        "This file is not a safely loadable morning or closed-shop save"));
                var point = new Rewind.Point
                {
                    Label = InspectionLocale.Text("快速重载存档", "Quick reload save"),
                    Path = path, Cash = metadata.Cash, SlotId = metadata.Slot, Day = metadata.Day,
                    State = metadata.State, RunId = metadata.RunId, At = File.GetLastWriteTime(path),
                    IsDayStart = metadata.State == 2, IsQuickReload = true
                };
                CheckpointPicker.Cancel();
                Rewind.Restore(point, allowDayChange: true, forceMenu: true);
            }
            catch (Exception e) { Failed("reload", e); }
        }

        internal static void Reroll()
        {
            if (!Ready("reroll") || Time.unscaledTime < _nextReroll) return;
            try
            {
                int seed = Guid.NewGuid().GetHashCode();
                var previous = RNG.random;
                UnityRandomState.Capture(out var unity);
                try
                {
#if MONO
                    RNG.random = new System.Random(seed);
#else
                    RNG.random = new Il2CppSystem.Random(seed);
#endif
                    UnityEngine.Random.InitState(seed);
                }
                catch
                {
                    RNG.random = previous;
                    UnityRandomState.Restore(unity);
                    throw;
                }
                _nextReroll = Time.unscaledTime + 0.5f;
                EventLog.Write("[SaveActions] REROLL OK streams=game,unity existing-content=unchanged");
                PanelNotice.Show(InspectionLocale.Text("已重掷随机数，后续随机结果将使用新序列",
                    "Random streams reseeded for future rolls"));
            }
            catch (Exception e) { Failed("reroll", e); }
        }

        private static bool Ready(string action)
        {
            try
            {
                string reason = CustomerCheckpoint.SaveActionBlocker();
                if (reason == null) return true;
                EventLog.Write("[SaveActions] " + action + " refused: " + reason);
                PanelNotice.Show(reason, true);
            }
            catch (Exception e) { Failed(action, e); }
            return false;
        }

        private static void Failed(string action, Exception error)
        {
            EventLog.Write("[SaveActions] " + action + " FAILED: " + error);
            PanelNotice.Show(InspectionLocale.Text("操作失败：", "Action failed: ")
                + error.GetType().Name + ": " + error.Message, true);
        }
    }
}
