using System;
using HarmonyLib;

namespace ProbablyStolenTools
{
    internal static class WaterQualityPatch
    {
        internal const string Category = "CATEGORY_WATER_PURITY";
        private static bool _loggedFailure;

        private sealed record Quality(string Declared, string Actual, bool Matches);

        private static Quality Read(GameItem item)
        {
            if (item == null || !item.IsTag("LIQUID_CONTAINER_TAG") || item.IsTag("WINE_CONTAINER_TAG")
                || WaterHelper.GetTotalVolume(item) <= 0) return null;
            // GetWaterFeature creates a default feature if absent. Read the existing
            // category directly so rendering descriptions never mutates game state.
            var feature = item.FindItemFeatureByCategory(Category);
            if (feature == null || feature.isDisabled || !feature.useCondition)
                return null;
            // Exposing/matching a label switches the native description to the
            // real condition, even though the old fake condition still exists.
            var visible = feature.GetCurrentItemCondition();
            if (visible == null) return null;
            int claimed = visible.customValue;
            int actual = WaterHelper.GetWaterPurity(item);
            return new Quality(WaterFeatureHelper.GetWaterDisplayName(claimed),
                WaterFeatureHelper.GetWaterDisplayName(actual),
                WaterFeatureHelper.GetPurityArrayIndex(claimed) == WaterFeatureHelper.GetPurityArrayIndex(actual));
        }

        /// <summary>水质一栏的文字：空瓶 / 酒精 / 水（保留原水质）/ 最高成分（占比 | 纯度 | 评级）。</summary>
        private static string Describe(LiquidComposition.Analysis analysis, Quality quality, bool water, bool chemistry)
        {
            if (analysis == null) return null;
            if (analysis.Empty) return null;
            if (analysis.WaterTop)
                return !water || quality == null ? null
                    : " -" + InspectionText.Water(quality.Declared, quality.Actual, quality.Matches);
            if (!chemistry) return null;
            if (analysis.AlcoholTop)
                return " -" + LiquidComposition.AlcoholLabel(analysis)
                    + "（" + InspectionText.Number(analysis.AlcoholShare) + "%）";
            if (analysis.TopName == null) return null;
            return " -" + InspectionText.Component(analysis.TopName, analysis.TopPercent,
                analysis.Purity, analysis.Grade, analysis.GradeColor);
        }
        internal static void UpdateFeatures(NegociationUIManager ui, GameItem item)
        {
            try
            {
#if MONO
                if (ui == null || ui.uiObject == null) return;
                var slots = ui.uiObject.GetComponentsInChildren<ItemFeatureUI>(true);
#else
                var slots = ui?.itemFeatureUIs;
#endif
                if (slots == null) return;
                bool water = Plugin.CfgWaterQuality?.Value ?? true;
                bool chemistry = Plugin.CfgChemistry?.Value ?? true;
                if (!water && !chemistry) return;
                var analysis = LiquidComposition.Analyze(item);
                if (analysis == null) return;
                var quality = water ? Read(item) : null;
                string text = Describe(analysis, quality, water, chemistry);
                if (text == null) return;
                foreach (var slot in slots)
                {
                    if (slot == null || !slot.gameObject.activeInHierarchy || slot.TMP == null
                        || slot.itemFeature?.category != Category) continue;
                    string modifier = slot.itemFeature.GetFormattedFakeModifier();
                    string label = text + modifier;
                    if (slot.TMP.text != label) slot.TMP.text = label;
                }
            }
            catch (Exception e) { Warn(e); }
        }

        internal static void AddTooltip(RichTextBuilder builder, GameItem item)
        {
            try
            {
                if (builder == null) return;
                bool water = Plugin.CfgWaterQuality?.Value ?? true;
                bool chemistry = Plugin.CfgChemistry?.Value ?? true;
                if (!water && !chemistry) return;
                var analysis = LiquidComposition.Analyze(item);
                if (analysis == null) return;
                // 空瓶不显示水质：尺寸改由悬浮说明的标题行体现。
                if (analysis.Empty) return;
                var quality = water ? Read(item) : null;
                if (analysis.AlcoholTop)
                {
                    if (!chemistry) return;
                    builder.Add("水质：");
                    builder.Add(LiquidComposition.AlcoholLabel(analysis)
                        + "（" + InspectionText.Number(analysis.AlcoholShare) + "%）", RenderHandler.ColorPalette.LightBlue);
                    builder.NextLine();
                    return;
                }
                if (!analysis.WaterTop)
                {
                    if (!chemistry || analysis.TopName == null) return;
                    builder.Add("水质：");
                    builder.Add(InspectionText.Plain(analysis.TopName), RenderHandler.ColorPalette.Orange);
                    builder.Add("（" + InspectionText.Number(analysis.TopPercent) + "% | 纯度 " + InspectionText.Number(analysis.Purity) + "% | ", RenderHandler.ColorPalette.White);
                    builder.Add("<color=" + analysis.GradeColor + ">" + InspectionText.Plain(analysis.Grade) + "</color>",
                        RenderHandler.ColorPalette.None);
                    builder.Add("）", RenderHandler.ColorPalette.White);
                    builder.NextLine();
                    return;
                }
                if (!water || quality == null) return;
                builder.Add("水质：");
                builder.Add(InspectionText.Plain(quality.Declared), quality.Matches
                    ? RenderHandler.ColorPalette.Green : RenderHandler.ColorPalette.Red);
                if (!quality.Matches)
                {
                    builder.Add(" (");
                    builder.Add(InspectionText.Plain(quality.Actual), RenderHandler.ColorPalette.LightBlue);
                    builder.Add(")");
                }
                builder.NextLine();
            }
            catch (Exception e) { Warn(e); }
        }

        /// <summary>开关变化后重建当前交易/鉴定行。</summary>
        internal static void SettingsChanged()
        {
            try { NegociationUIManager.Instance?.RefreshUI(); } catch { }
        }

        private static void Warn(Exception e)
        {
            if (_loggedFailure) return;
            _loggedFailure = true;
            Plugin.LogSource.LogWarning("[WaterQuality] description failed: " + e);
        }
    }

#if MONO
    [HarmonyPatch(typeof(NegociationUIManager), "BuildExpandedRow")]
#else
    [HarmonyPatch(typeof(NegociationUIManager), "UpdateItemFeatureGroup")]
#endif
    internal static class WaterQualityFeaturesPatch
    {
        [HarmonyPostfix]
        private static void Postfix(NegociationUIManager __instance, GameItem __0)
            => WaterQualityPatch.UpdateFeatures(__instance, __0);
    }


}
