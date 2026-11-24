using System;
using System.Collections.Generic;
using HarmonyLib;
#if MONO
using TMPro;
#else
using Il2CppTMPro;
#endif
using UnityEngine;

namespace ProbablyStolenTools
{
    internal static class DetailedCompositionTooltip
    {
        internal static bool ShiftEnabled => Plugin.CfgShiftComposition?.Value == true;
        internal static bool ShiftHeld => ItemDetailsInput.Held;
        private static RichText _lastFaucetResult;
        private static bool _loggedFailure;

        internal static List<CompositionDetails.Row> ContainerRows(GameItem item)
        {
            if (item == null || item.identifier == "faucet" || !item.IsTag("LIQUID_CONTAINER_TAG")) return null;
            var analysis = LiquidComposition.Analyze(item);
            if (analysis == null) return null;
            return CompositionDetails.Rows(ChemistryData.Ids(), ChemistryData.Parts(item), analysis?.TopIdentifier);
        }

        internal static bool AddReagent(RichTextBuilder builder, GameItem item, string main)
        {
            if (!CompositionDetails.IsReagent(main)) return false;
            AddRows(builder, CompositionDetails.Rows(ChemistryData.Ids(), ChemistryData.Parts(item), main), false);
            return true;
        }

        internal static void AddRows(RichTextBuilder builder, List<CompositionDetails.Row> rows, bool pipe)
        {
            if (rows == null || rows.Count == 0)
            {
                builder.AddLine(InspectionLocale.Text("空", "Empty"), color: RenderHandler.ColorPalette.White);
                return;
            }
            var names = new string[rows.Count + 1];
            var amounts = pipe ? null : new string[names.Length];
            var percentages = new string[names.Length];
            names[0] = InspectionLocale.Text("名称", "Name");
            if (amounts != null) amounts[0] = InspectionLocale.Text("含量", "Amount");
            percentages[0] = InspectionLocale.Text("占比", "Share");
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var liquid = Liquid.GetLiquidFromId(row.Id);
                names[i + 1] = InspectionText.Plain(InspectionLocale.Label(AnnotationLabels.Liquid(row.Id, liquid?.displayName)));
                if (amounts != null) amounts[i + 1] = CompositionDetails.Amount(row.Amount);
                percentages[i + 1] = CompositionDetails.Percent(row.Percent);
            }
            // TMP tab stops depend on each glyph's width. Absolute column stops
            // measured with the actual tooltip font align both CJK and Latin names.
            var renderer = OverlayHandler.current?.GetTooltipWindow()?.handler?.GetComponentInChildren<TextMeshProUGUI>();
            double Measure(string value) => renderer != null ? renderer.GetPreferredValues(value).x : value.Length * 16.0;
            double gap = renderer != null ? Math.Max(8, renderer.fontSize * 0.75) : 16;
            string[] lines;
            string originalText = renderer != null ? renderer.text : null;
            try { lines = CompositionDetails.Table(names, amounts, percentages, Measure, gap); }
            finally
            {
                // TMP's measurement overload rewrites its processing buffers.
                // Restore them even when OverlayHandler reuses an unchanged tooltip.
                if (renderer != null) renderer.SetText(originalText);
            }
            builder.maxCharWidth = Math.Max(builder.maxCharWidth, 100);
            for (int i = 0; i < lines.Length; i++)
                builder.AddLine(lines[i], color: i > 0 && rows[i - 1].Main
                    ? RenderHandler.ColorPalette.Orange : RenderHandler.ColorPalette.White);
        }

#if MONO
        internal static bool TryPipePurity(GameItem item, out int purity)
        {
            purity = 0;
            if (item?.identifier != "faucet" || !PlayerStore.HasInstance || StoreStation.Instance == null) return false;
            var upgrades = PlayerStore.Instance.networkUpgrade;
            if (upgrades == null) return false;
            if (upgrades.TryGetValue("RUSTWATER_TAP", out var rust) && rust != null && rust.IsUnlocked())
                purity = StoreStation.Instance.todayRustwaterPurity;
            else if (upgrades.TryGetValue("GUTTERFLOW_TAP", out var gutter) && gutter != null && gutter.IsUnlocked())
                purity = StoreStation.Instance.todayGutterflowPurity;
            else return false;
            return true;
        }

        internal static List<CompositionDetails.Row> PipeRows(int purity)
        {
            // Native faucets call AddWater(grade: 1) for BOTH upgrades. Do not use
            // the similarly named gutterflow/rust distribution or consume RNG.
            var rows = new List<CompositionDetails.Row>();
            double water = Math.Clamp(purity, 0, 10000) / 100.0;
            rows.Add(new CompositionDetails.Row("water", 0, water, true));
            foreach (var liquid in Liquid.Liquids)
            {
                if (liquid.identifier == "water") continue;
                if (WaterPremadeHelper.waterContaminantDistribution.TryGetValue(liquid.currentPart, out float share) && share > 0 && water < 100)
                    rows.Add(new CompositionDetails.Row(liquid.identifier, 0, (100 - water) * share, false));
            }
            return CompositionDetails.Sort(rows);
        }

        internal static bool PipeDetailsEnabled(bool shiftHeld)
            => Plugin.CfgPipeComposition?.Value == true && shiftHeld;

        internal static bool PipeTooltipVisible(GameItem item, bool nativeVisible, bool shiftHeld)
            => nativeVisible || PipeDetailsEnabled(shiftHeld) && TryPipePurity(item, out _);

        internal static RichText Append(RichText original, RichText extra)
        {
            if (original == null) return extra;
            string Join(string first, string second) => first + "\n" + second;
            return new RichText(Join(original.nakedText, extra.nakedText),
                Join(original.richTextStrikethrough, extra.richTextStrikethrough),
                Join(original.richTextUnderline, extra.richTextUnderline),
                Join(original.richTextForeground, extra.richTextForeground),
                Join(original.richTextBackground, extra.richTextBackground),
                Math.Max(original.recommendedWidth, extra.recommendedWidth),
                original.recommendedHeight + extra.recommendedHeight, original.isTruncated || extra.isTruncated);
        }

        internal static void FinalizeTooltip(GameItem item, ref RichText result)
        {
            try
            {
                if (item?.identifier == "faucet")
                {
                    if (PipeDetailsEnabled(ShiftHeld) && InspectionLocale.AnnotationsReady && TryPipePurity(item, out int purity))
                    {
                        if (ReferenceEquals(result, _lastFaucetResult)) return;
                        var builder = new RichTextBuilder(100);
                        builder.AddLine(InspectionLocale.Label(AnnotationLabels.WaterHeading) + ": "
                            + InspectionLocale.Label(AnnotationLabels.Water(purity)), color: RenderHandler.ColorPalette.Green);
                        AddRows(builder, PipeRows(purity), true);
                        // Preserve every native layer, including the connection description.
                        result = Append(result, builder.Build());
                        _lastFaucetResult = result;
                    }
                    return;
                }
                ReplaceIfShift(item, ref result);
            }
            catch (Exception e) { Warn(e); }
        }

        internal static void ReplaceIfShift(GameItem item, ref RichText result)
        {
            if (item == null || !InspectionLocale.AnnotationsReady
                || !ShiftHeld) return;
            try
            {
                var rows = ShiftEnabled ? ContainerRows(item) : null;
                var builder = new RichTextBuilder(72);
                if (rows != null) AddRows(builder, rows, false);
                WineAppraisal.AddTooltip(builder, item);
                FacilityPreview.AddTooltip(builder, item);
                var details = builder.Build();
                if (!string.IsNullOrWhiteSpace(details.nakedText)) result = details;
            }
            catch (Exception e) { Warn(e); }
        }
#endif

        internal static void Warn(Exception e)
        {
            if (_loggedFailure) return;
            _loggedFailure = true;
            Plugin.LogSource?.LogWarning("[Composition] tooltip failed: " + e);
        }
    }

#if MONO
    [HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.GetTooltip))]
    internal static class DetailedCompositionTooltipPatch
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(GameItemElement __instance, ref RichText __result)
            => DetailedCompositionTooltip.FinalizeTooltip(__instance, ref __result);
    }

    [HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.GetTooltipAdvanced))]
    internal static class DetailedCompositionAdvancedTooltipPatch
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(GameItemElement __instance, ref RichText __result)
            => DetailedCompositionTooltip.FinalizeTooltip(__instance, ref __result);
    }

    [HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.HasTooltip))]
    internal static class FaucetTooltipVisibilityPatch
    {
        [HarmonyPostfix]
        private static void Postfix(GameItemElement __instance, ref bool __result)
        {
            if (__result || Plugin.CfgPipeComposition?.Value != true) return;
            try { __result = DetailedCompositionTooltip.PipeTooltipVisible(__instance, __result, DetailedCompositionTooltip.ShiftHeld); }
            catch (Exception e) { DetailedCompositionTooltip.Warn(e); }
        }
    }
#endif
}
