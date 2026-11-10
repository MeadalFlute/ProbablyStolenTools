using System;
using HarmonyLib;

namespace ProbablyStolenTools
{
    /// <summary>
    /// 只在“物品悬浮说明 / 拖拽使用提示”构建期间，把液体容器的显示名换成主要成分名。
    /// 背包、交易列表、任务文本等其它界面保持原名。
    /// 用嵌套深度而不是布尔值，避免两个入口同时处于作用域时相互覆盖。
    /// </summary>
    internal static class TooltipNameScope
    {
        private static int _depth;

        internal static bool Active => _depth > 0;

        internal static void Push() { _depth++; }

        internal static void Pop() { if (_depth > 0) _depth--; }

        internal static void Reset() => _depth = 0;
    }

    [HarmonyPatch(typeof(GameItemElement), "GetTooltipBasic")]
    internal static class TooltipNameScopePatch
    {
        [HarmonyPrefix]
        private static void Prefix(out bool __state) { TooltipNameScope.Push(); __state = true; }

        [HarmonyFinalizer]
        private static void Finalizer(bool __state) { if (__state) TooltipNameScope.Pop(); }
    }

    /// <summary>
    /// 拖拽时的“对 X 使用 Y / 对 X 放入 Y”提示由 RichTextGenerator 直接取 GetDisplayName，
    /// 不在 GetTooltipBasic 的作用域里，所以单独包一层。
    /// </summary>
    [HarmonyPatch(typeof(RichTextGenerator), nameof(RichTextGenerator.UseItemOnOtherItem))]
    internal static class TooltipNameScopeUsePatch
    {
        [HarmonyPrefix]
        private static void Prefix(out bool __state) { TooltipNameScope.Push(); __state = true; }

        [HarmonyFinalizer]
        private static void Finalizer(bool __state) { if (__state) TooltipNameScope.Pop(); }
    }

    /// <summary>
    /// 倾倒 / 放入提示（“将 X 放入 Y”）走 InsertItemInOtherItem，同样直接取 GetDisplayName。
    /// </summary>
    [HarmonyPatch(typeof(RichTextGenerator), nameof(RichTextGenerator.InsertItemInOtherItem))]
    internal static class TooltipNameScopeInsertPatch
    {
        [HarmonyPrefix]
        private static void Prefix(out bool __state) { TooltipNameScope.Push(); __state = true; }

        [HarmonyFinalizer]
        private static void Finalizer(bool __state) { if (__state) TooltipNameScope.Pop(); }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.GetDisplayName))]
    internal static class TooltipComponentNamePatch
    {
        [HarmonyPostfix]
        private static void Postfix(GameItem __instance, ref string __result)
        {
            if (!TooltipNameScope.Active || __instance == null || !InspectionLocale.AnnotationsReady) return;
            if (Plugin.CfgChemistry != null && !Plugin.CfgChemistry.Value) return;
            try
            {
                if (LiquidComposition.NoRename(__instance)) return;
                var analysis = LiquidComposition.Analyze(__instance);
                if (analysis == null) return;
                // 空瓶：把尺寸写进名字（水质行不再重复）。
                if (analysis.Empty)
                {
                    __result = InspectionLocale.Text("空瓶（" + analysis.SizeName + "）", "Empty bottle (" + analysis.SizeName + ")");
                    return;
                }
                // 酒精最高：名字显示 酒精 / 医用酒精 / 脱水乙醇。
                if (analysis.AlcoholTop) { __result = LiquidComposition.AlcoholLabel(analysis); return; }
                // 水最高时保留原物品名；其它成分最高时用成分名替换。
                if (!analysis.WaterTop && !string.IsNullOrEmpty(analysis.TopName))
                    __result = analysis.TopName;
            }
            catch { }
        }
    }
}
