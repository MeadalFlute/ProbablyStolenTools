#if MONO
using System;
using HarmonyLib;

namespace ProbablyStolenTools
{
    [HarmonyPatch(typeof(ModConfigWindow), nameof(ModConfigWindow.Open))]
    internal static class NativeQuickSLPanelPatch
    {
        private static string _lastError;
        [HarmonyPostfix]
        private static void Postfix(LoadedMod mod, CustomUIWindow __result)
        {
            if (mod?.Manifest?.ID != Plugin.Guid || __result == null || !__result.IsAlive) return;
            try
            {
                var window = __result;
                var builder = new CustomUIBuilder(CustomUIManager.Instance, window);
                builder.AddButton(InspectionLocale.Text("打开QuickSL功能面板", "Open QuickSL feature panel"), () =>
                {
                    window.Close();
                    CheckpointPanel.OpenOptions();
                }, "quicksl_features");
                _lastError = null;
            }
            catch (Exception e)
            {
                string error = e.GetType().Name + ": " + e.Message;
                if (_lastError != error) EventLog.Write("[Settings] QuickSL panel shortcut unavailable: " + error);
                _lastError = error;
            }
        }
    }
}
#endif
