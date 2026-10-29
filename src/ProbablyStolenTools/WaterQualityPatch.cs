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
                var quality = Read(item);
                if (quality == null) return;
                string text = " -" + InspectionText.Water(quality.Declared, quality.Actual, quality.Matches);
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
                var quality = Read(item);
                if (quality == null) return;
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

    [HarmonyPatch(typeof(WaterFeatureHelper), nameof(WaterFeatureHelper.CreateWaterFeatureTooltip))]
    internal static class WaterQualityTooltipPatch
    {
        [HarmonyPostfix]
        private static void Postfix(RichTextBuilder __0, GameItem __1)
            => WaterQualityPatch.AddTooltip(__0, __1);
    }
}
