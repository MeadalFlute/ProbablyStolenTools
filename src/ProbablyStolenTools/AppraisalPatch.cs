using System;
using HarmonyLib;
#if MONO
using TMPro;
#else
using Il2CppTMPro;
#endif

namespace ProbablyStolenTools
{
    /// <summary>
    /// 鉴定任务自动报价：把报价输入框填成游戏内部的“正确估值”。
    /// 钩子挂在 InspectionUIManager.UpdateAppraisalPanel（打开/刷新鉴定面板时调用）。
    /// </summary>
    [HarmonyPatch(typeof(InspectionUIManager), nameof(InspectionUIManager.UpdateAppraisalPanel))]
    internal static class AppraisalPatch
    {
        private static int _lastItemUniqueId = int.MinValue;
        private static int _lastValue = -1;
        private static bool _busy;
        internal static void Reset() { _lastItemUniqueId = int.MinValue; _lastValue = -1; }

        [HarmonyPostfix]
        public static void Postfix(InspectionUIManager __instance)
        {
            if (_busy) return;
            if (!Plugin.CfgAutoAppraisal.Value) return;

            _busy = true;
            try
            {
                Fill(__instance);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Appraisal] fill failed: {e.GetType().Name}: {e.Message}");
            }
            finally
            {
                _busy = false;
            }
        }

        private static void Fill(InspectionUIManager ui)
        {
            if (ui == null) return;

            var panel = ui.appraisalPanel;
            if (panel != null && !panel.activeInHierarchy) return;

            var field = ui.apprisalServiceInputField;
            if (field == null) return;

            var item = ui.currentInspectingItem;
            if (item == null) return;

            int correct;
            try
            {
                correct = AppraisalHelper.GetCorrectBaseValue(item);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Appraisal] GetCorrectBaseValue threw: {e.Message}");
                return;
            }

            if (correct <= 0) return;

            int itemId = 0;
            try { itemId = item.uniqueId; } catch { }

            bool sameItem = itemId == _lastItemUniqueId && correct == _lastValue;
            if (sameItem && !Plugin.CfgAlwaysOverwrite.Value)
                return;

            _lastItemUniqueId = itemId;
            _lastValue = correct;

            string text = correct.ToString();
            try
            {
                // SetTextWithoutNotify 不触发 onValueChanged，避免与 OnValueChanged 互相递归
                if (field.text != text)
                    field.SetTextWithoutNotify(text);
                field.ForceLabelUpdate();
            }
            catch
            {
                field.text = text;
                try { field.ForceLabelUpdate(); } catch { }
            }

            Plugin.LogSource.LogInfo($"[Appraisal] item={itemId} correct={correct} -> field filled");

            if (Plugin.CfgAutoSubmit.Value)
            {
                try
                {
                    ui.OnAppraisalServiceEstimated();
                    // The native handler may rebuild the label immediately after
                    // submission; keep the visible value synchronized for this frame.
                    int currentId = 0;
                    try { currentId = ui.currentInspectingItem?.uniqueId ?? 0; } catch { }
                    if (ui.apprisalServiceInputField != null && currentId == itemId)
                    {
                        ui.apprisalServiceInputField.SetTextWithoutNotify(text);
                        ui.apprisalServiceInputField.ForceLabelUpdate();
                    }
                    Plugin.LogSource.LogInfo("[Appraisal] auto-submitted.");
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Appraisal] auto-submit failed: {e.Message}");
                }
            }
        }
    }
}
