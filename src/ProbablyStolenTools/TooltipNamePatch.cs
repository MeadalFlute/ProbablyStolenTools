using System;
using HarmonyLib;

namespace ProbablyStolenTools
{
    /// <summary>
    /// 只在“物品悬浮说明”构建期间，把液体容器的显示名换成主要成分名。
    /// 背包、交易列表、任务文本等其它界面保持原名。
    /// </summary>
    internal static class TooltipNameScope
    {
        internal static bool Active;

        internal static void Reset() => Active = false;
    }

    [HarmonyPatch(typeof(GameItemElement), "GetTooltipBasic")]
    internal static class TooltipNameScopePatch
    {
        [HarmonyPrefix]
        private static void Prefix() => TooltipNameScope.Active = true;

        [HarmonyPostfix]
        private static void Postfix() => TooltipNameScope.Active = false;
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.GetDisplayName))]
    internal static class TooltipComponentNamePatch
    {
        [HarmonyPostfix]
        private static void Postfix(GameItem __instance, ref string __result)
        {
            if (!TooltipNameScope.Active || __instance == null) return;
            try
            {
                if (LiquidComposition.NoRename(__instance)) return;
                var analysis = LiquidComposition.Analyze(__instance);
                if (analysis == null) return;
                // 空瓶：把尺寸写进名字（水质行不再重复）。
                if (analysis.Empty) { __result = "空瓶（" + analysis.SizeName + "）"; return; }
                // 水/酒精最高时保留原物品名；其它成分最高时用成分名替换。
                if (!analysis.WaterTop && !analysis.AlcoholTop && !string.IsNullOrEmpty(analysis.TopName))
                    __result = analysis.TopName;
            }
            catch { }
        }
    }
}
