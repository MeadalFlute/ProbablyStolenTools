using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ProbablyStolenTools
{
    [HarmonyPatch(typeof(ItemMouseDoubleClickHandler), "DoubleClickAction")]
    internal static class DoubleClickInspectionPatch
    {
        private static bool _loggedFailure;
        private static int _diagnostics;

        [HarmonyPrefix]
        private static bool Prefix(ItemMouseDoubleClickHandler __instance, GameItem newItem)
            => !TryInspect(__instance, newItem);

        private static bool TryInspect(ItemMouseDoubleClickHandler handler, GameItem item)
        {
            bool consumed = false;
            var why = new StringBuilder();
            try
            {
                bool enabled = Plugin.CfgDoubleClickInspection?.Value == true;
                if (!enabled) why.Append("disabled ");

                KeyCode key = KeyCode.None;
                try { key = GameFields.Get<KeyCode>(handler, "lastKey"); }
                catch (Exception e) { why.Append("keyErr:").Append(e.GetType().Name).Append(' '); }
                if (key != KeyCode.Mouse0) why.Append("key=").Append(key).Append(' ');

                if (Rewind.Restoring || CustomerCheckpoint.Restoring) why.Append("restoring ");
                if (item == null) why.Append("nullItem ");
                else
                {
                    bool inspectable = false;
                    try { inspectable = item.IsTag("INSPECTABLE_TAG"); } catch { }
                    if (!inspectable)
                    {
                        string id = "?";
                        try { id = item.identifier; } catch { }
                        bool hasState = false, hasModified = false;
                        bool stateTag = false, modifiedTag = false;
                        try { hasState = item.state != null; stateTag = hasState && item.state.IsTag("INSPECTABLE_TAG"); } catch { }
                        try { hasModified = item.modifiedState != null; modifiedTag = hasModified && item.modifiedState.IsTag("INSPECTABLE_TAG"); } catch { }
                        int unitCount = -1;
                        try { unitCount = item.unitCount; } catch { }
                        why.Append($"notInspectable(id={id} state={hasState}/{stateTag} modified={hasModified}/{modifiedTag} units={unitCount}) ");
                        try
                        {
                            var e2 = EmporiumEntry.Instance;
                            if (e2 != null)
                                why.Append("cand(").Append(Describe(e2.GetInvItems(), item)).Append("|")
                                   .Append(Describe(e2.GetCountersItems(), item)).Append(") ");
                        }
                        catch (Exception e2x) { why.Append("candErr:").Append(e2x.GetType().Name).Append(' '); }
                    }
                }

                var store = Plugin.Store();
                var ui = InspectionUIManager.Instance;
                var entry = EmporiumEntry.Instance;
                if (store == null) why.Append("noStore ");
                if (entry == null) why.Append("noEntry ");
                if (ui == null) why.Append("noUI ");
                else if (ui.panel == null) why.Append("noPanel ");
                else if (ui.panel.activeInHierarchy) why.Append("panelOpen ");
                if (store != null)
                {
                    if (store.lockAllFlag > 0) why.Append("lockAll ");
                    if (store.lockAllInventoryFlag > 0) why.Append("lockInv ");
                    if (store.isHandlingShootout) why.Append("shootout ");
                    if (store.isClientBeingArrested) why.Append("arrest ");
                    if (store.isGeneScannerScanning) why.Append("geneScan ");
                }
                if (ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem) why.Append("dragging ");
                if (ItemSelectHandler.current != null && ItemSelectHandler.current.IsEquipped) why.Append("equipped ");

                if (why.Length != 0 || !enabled || item == null) return Finish(why, false);

                var candidates = Candidates(entry);
                var magnifier = FindMagnifier(candidates, item);
                if (magnifier == null)
                {
                    why.Append("noMagnifier(").Append(Describe(candidates, item)).Append(") ");
                    return Finish(why, false);
                }

                // Use the real tool so native targeting, sounds and item hooks
                // follow the same path as manually applying the magnifier.
                consumed = true;
                magnifier.Target(item);
                return Finish(why, true);
            }
            catch (Exception e)
            {
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Plugin.LogSource.LogWarning("[DoubleClickInspection] failed: " + e);
                }
                return consumed;
            }
        }

        private static bool Finish(StringBuilder why, bool consumed)
        {
            if (_diagnostics < 12)
            {
                ++_diagnostics;
                Plugin.LogSource.LogInfo($"[DoubleClick] consumed={consumed} {(why.Length == 0 ? "ok" : why.ToString().Trim())}");
            }
            return consumed;
        }

        /// <summary>只用于诊断：列出候选工具及其被拒原因。</summary>
        private static string Describe(List<GameItem> items, GameItem target)
        {
            if (items == null) return "null";
            var sb = new StringBuilder();
            for (int i = 0; i < items.Count && i < 40; i++)
            {
                var tool = items[i];
                if (tool == null) continue;
                string id;
                try { id = tool.identifier; } catch { continue; }
                bool tag = false, owned = false, may = false, can = false, disabled = false;
                try { tag = tool.IsTag("MAGNIFYING_GLASS"); } catch { }
                try { owned = GeneralHelper.IsItemOwned(tool); } catch { }
                try { may = tool.MayTarget(target); } catch { }
                try { can = tool.CanTarget(target); } catch { }
                try { disabled = tool.forceDisableUse; } catch { }
                if (tag || may || can)
                    sb.Append(id).Append("[tag=").Append(tag ? 1 : 0)
                      .Append(",own=").Append(owned ? 1 : 0)
                      .Append(",may=").Append(may ? 1 : 0)
                      .Append(",can=").Append(can ? 1 : 0)
                      .Append(",off=").Append(disabled ? 1 : 0).Append("] ");
            }
            return sb.Length == 0 ? "none" : sb.ToString().Trim();
        }

        /// <summary>放大镜可能在背包、柜台，也可能挂在快捷栏/装备位，这里把它们合起来找。</summary>
        private static List<GameItem> Candidates(EmporiumEntry entry)
        {
            var result = new List<GameItem>();
            void Add(List<GameItem> items)
            {
                if (items == null) return;
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    if (it != null && !result.Contains(it)) result.Add(it);
                }
            }
            try { Add(entry.GetInvItems()); } catch { }
            try { Add(entry.GetCountersItems()); } catch { }
            try { Add(entry.GetAllOwnedItems(true, true)); }
            catch { try { Add(entry.GetAllItems()); } catch { } }

            // 放大镜也可能收在档案箱之类的容器里：把容器内容也展开（最多两层）。
            try
            {
                var nested = new List<GameItem>(result);
                for (int i = 0; i < nested.Count; i++)
                {
                    var children = nested[i].FindAllChildItems();
                    if (children != null) Add(children);
                }
            }
            catch { }
            return result;
        }

        private static GameItem FindMagnifier(List<GameItem> items, GameItem target)
        {
            if (items == null) return null;
            for (int i = 0; i < items.Count; i++)
            {
                var tool = items[i];
                if (tool == null || Id(tool) == Id(target) || !tool.IsTag("MAGNIFYING_GLASS")
                    || !GeneralHelper.IsItemOwned(tool) || tool.forceDisableUse) continue;
                if (tool.MayTarget(target) && tool.CanTarget(target)) return tool;
            }
            return null;
        }
    }
}
