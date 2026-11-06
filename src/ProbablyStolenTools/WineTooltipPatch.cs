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
                if (!string.IsNullOrEmpty(line))
                    __result.AddLine(line, true, RenderHandler.ColorPalette.None, false, false, false, false,
                        RenderHandler.ColorPalette.None, RenderHandler.ColorPalette.None, RenderHandler.ColorPalette.None);
                // 违禁酵母：用原版违禁品标签的样式单独占一行，不再拼进名字/说明里。
                if (WineAppraisal.IsIllegalBottle(__instance))
                {
                    string tag = LocHelper.GetLocalizedMechanic("mech_contraband_low");
                    if (!string.IsNullOrEmpty(tag))
                        __result.AddLine(tag, true, RenderHandler.ColorPalette.LightRed, true, false, false, false,
                            RenderHandler.ColorPalette.Black, RenderHandler.ColorPalette.Black, RenderHandler.ColorPalette.Black);
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource?.LogWarning("[Wine] tooltip append failed: " + e.Message);
            }
        }
    }
}
