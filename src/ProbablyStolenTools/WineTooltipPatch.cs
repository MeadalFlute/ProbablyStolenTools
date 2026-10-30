using System;
using HarmonyLib;

namespace ProbablyStolenTools
{
    /// <summary>
    /// 把酿酒鉴定结论写进物品描述（悬停提示），只对荧光莓与未发酵酒瓶生效。
    /// </summary>
    [HarmonyPatch(typeof(GameItemElement), "GetTooltipBasic")]
    internal static class WineTooltipPatch
    {
        [HarmonyPostfix]
        private static void Postfix(GameItemElement __instance, RichTextBuilder __result)
        {
            if (__instance == null || __result == null) return;
            try
            {
                string line = WineAppraisal.Describe(__instance);
                if (string.IsNullOrEmpty(line)) return;
                __result.AddLine(line, true, RenderHandler.ColorPalette.None, false, false, false, false,
                    RenderHandler.ColorPalette.None, RenderHandler.ColorPalette.None, RenderHandler.ColorPalette.None);
            }
            catch (Exception e)
            {
                Plugin.LogSource?.LogWarning("[Wine] tooltip append failed: " + e.Message);
            }
        }
    }
}
