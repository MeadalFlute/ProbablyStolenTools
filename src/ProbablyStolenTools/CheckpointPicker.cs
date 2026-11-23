using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ProbablyStolenTools
{
    internal static class CheckpointPicker
    {
        private static readonly CheckpointSelection Selection = new();
        private static int _historyRevision, _fileRevision, _day;
        private static PlayerStore _store;
        private static string _hint = "";
        internal static int Revision => Selection.Revision;
        internal static object Selected => Selection.Selected;
        internal static bool Active => Selection.Active;
        internal static bool RevealPanel;

        internal static List<CheckpointTimelineEntry> Timeline()
        {
            var entries = new List<CheckpointTimelineEntry>();
            var mornings = new HashSet<int>();
            foreach (var node in CustomerCheckpoint.Nodes)
            {
                if (node.Graph == null) continue;
                if (node.IsDayStart) mornings.Add(node.Day);
                entries.Add(new CheckpointTimelineEntry
                {
                    Node = node, Day = node.Day, Cash = node.Cash, At = node.At, DayStart = node.IsDayStart,
                    Label = CustomerCheckpoint.Name(node),
                    State = node.Id == CustomerCheckpoint.ActiveId ? InspectionLocale.Text("当前", "Current")
                        : CustomerCheckpoint.IsFuture(node) ? InspectionLocale.Text("后续", "Later")
                        : node.IsDayStart ? InspectionLocale.Text("内存", "Memory") : ""
                });
            }
            var store = Plugin.Store();
            int day = store == null ? -1 : StoreStation.GetDayCounter();
            foreach (var point in Rewind.Points.OrderByDescending(p => p.At))
            {
                if (!point.IsDayStart || point.State != 2 || store == null || point.SlotId != store.saveSlotId
                    || point.RunId != store.runID || point.Day != day
                        && !(Plugin.CfgKeepPreviousDay?.Value == true && point.Day == day - 1)
                    || !mornings.Add(point.Day)) continue;
                entries.Add(new CheckpointTimelineEntry
                {
                    Node = point, Day = point.Day, Cash = point.Cash, At = point.At, DayStart = true,
                    Label = InspectionLocale.Text("开业前", "Day start"), State = InspectionLocale.Text("存档", "File")
                });
            }
            CheckpointTimelineEntry.Sort(entries);
            return entries;
        }

        internal static string Hint => _hint;
        private static string BuildHint() => InspectionLocale.Text(
            $"使用“{Keys(Plugin.CfgKeyNewer, Plugin.CfgKeyNewerAlt)}”、“{Keys(Plugin.CfgKeyOlder, Plugin.CfgKeyOlderAlt)}”切换节点；“{Plugin.KeyName(Plugin.CfgKeyRewindConfirm.Value)}”确认回溯。",
            $"Use {Keys(Plugin.CfgKeyNewer, Plugin.CfgKeyNewerAlt)} / {Keys(Plugin.CfgKeyOlder, Plugin.CfgKeyOlderAlt)} to select; {Plugin.KeyName(Plugin.CfgKeyRewindConfirm.Value)} to rewind.");
        private static string Keys(ConfigEntry<KeyCode> primary, ConfigEntry<KeyCode> alternate)
        {
            string a = primary.Value == KeyCode.None ? "" : Plugin.KeyName(primary.Value);
            string b = alternate.Value == KeyCode.None ? "" : Plugin.KeyName(alternate.Value);
            return a.Length == 0 ? b : b.Length == 0 ? a : a + "/" + b;
        }

        internal static void Begin(int direction = 1)
        {
            if (Rewind.Restoring || Rewind.Settling || CustomerCheckpoint.Restoring
                || Rewind.PendingWholeDay != Rewind.WholeDayAction.None || Plugin.Store() == null
                || StoreUIManager.IsAnyTyping()) return;
            var entries = Timeline();
            object current = CustomerCheckpoint.Nodes.FirstOrDefault(node => node.Id == CustomerCheckpoint.ActiveId);
            if (!Selection.Begin(entries.Select(entry => entry.Node), current, direction))
            {
                PanelNotice.Show(OptionalModCompatibility.MemoryCheckpointsAllowed
                    ? InspectionLocale.Text("暂无可用节点", "No checkpoints available")
                    : OptionalModCompatibility.MemoryCheckpointWarning, true);
                return;
            }
            _historyRevision = CustomerCheckpoint.Revision;
            _fileRevision = Rewind.PointRevision;
            _store = Plugin.Store();
            _day = StoreStation.GetDayCounter();
            _hint = BuildHint();
            RevealPanel = true;
            PanelNotice.Show(Hint);
        }

        internal static void Cancel()
        {
            Selection.Cancel();
            _store = null;
        }

        internal static bool TickInput()
        {
            if (!Active)
            {
                // Primary navigation bindings can open the picker; W/S remain modal
                // so normal gameplay does not unexpectedly consume movement keys.
                bool newer = Plugin.Down(Plugin.CfgKeyNewer);
                bool older = Plugin.Down(Plugin.CfgKeyOlder);
                if (!newer && !older || StoreUIManager.IsAnyTyping()) return false;
                Begin(newer ? -1 : 1);
                return Active;
            }
            if (Rewind.Restoring || Rewind.Settling || CustomerCheckpoint.Restoring
                || Rewind.PendingWholeDay != Rewind.WholeDayAction.None
                || CustomerCheckpoint.Revision != _historyRevision || Rewind.PointRevision != _fileRevision
                || Plugin.Store() != _store || StoreStation.GetDayCounter() != _day
                || StoreUIManager.IsAnyTyping())
            {
                Cancel();
                return true;
            }
            if (Input.GetKeyDown(KeyCode.Escape)) { Cancel(); return true; }
            if (Plugin.Down(Plugin.CfgKeyRewindConfirm))
            {
                object node = Selected;
                Cancel();
                Restore(node);
            }
            else if (Plugin.Down(Plugin.CfgKeyNewer) || Plugin.Down(Plugin.CfgKeyNewerAlt))
            {
                Selection.Move(-1);
                PanelNotice.Show(Hint);
            }
            else if (Plugin.Down(Plugin.CfgKeyOlder) || Plugin.Down(Plugin.CfgKeyOlderAlt) || Plugin.Down(Plugin.CfgKeyPrev))
            {
                Selection.Move(1);
                PanelNotice.Show(Hint);
            }
            return true;
        }

        internal static void Restore(object node)
        {
            Cancel();
            if (node is CustomerCheckpoint.Snapshot snapshot) CustomerCheckpoint.Restore(snapshot);
            else if (node is Rewind.Point point)
                Rewind.Restore(point, allowDayChange: point.Day != StoreStation.GetDayCounter());
        }
    }
}
