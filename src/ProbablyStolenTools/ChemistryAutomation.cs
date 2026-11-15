#if MONO
using System;
using System.Collections.Generic;
using HarmonyLib;

namespace ProbablyStolenTools
{
    internal static class ChemistryAutomation
    {
        private static readonly HashSet<GameItem> Pending = new();
        private static readonly BlendBatchPlan<GameItem> GroupPlan = new();
        private static int _placements, _groups;
        private static bool _running, _extracting;
        private static readonly HashSet<string> Reported = new();
        private static bool Available => CustomerCheckpoint.PatchesReady && Plugin.CfgChemistry?.Value == true
            && !CustomerCheckpoint.Restoring && !Rewind.Restoring
            && !SaveManager.IsDecoding && !CurrentConverterHelper.isNightCycle && Plugin.Store() != null;
        private static bool AutoBlend => Available && Plugin.CfgAutoBlend?.Value == true;
        private static bool PlayerMoving => _placements > 0
            || ItemMouseDragHandler.current?.IsDraggingItem == true
            || ItemMultiSelectHandler.current?.IsGroupDragging == true;

        internal static void BeginPlacement(bool group)
        {
            _placements++;
            if (group) { if (_groups == 0) GroupPlan.Clear(); _groups++; }
        }
        internal static void EndPlacement(bool group, bool success)
        {
            _placements = Math.Max(0, _placements - 1);
            if (group) _groups = Math.Max(0, _groups - 1);
            if (!success) Pending.Clear();
            else if (_placements == 0) Drain();
            if (_groups == 0) GroupPlan.Clear();
        }
        internal static void Reset() { Pending.Clear(); GroupPlan.Clear(); HydroponicWorkWatch.Reset(); }

        private static GameItem Blender(GameGridInventory grid)
        {
            var machine = grid?.GetParentItem();
            return machine?.identifier == "blender" && ReferenceEquals(MachineHelper.GetBodyStorage(machine, 1), grid)
                ? machine : null;
        }
        private static GameItem Bottle(GameItem machine) =>
            (MachineHelper.GetBodyStorage(machine, 2) as GameSlotInventory)?.childItem;

        internal static bool SequentialGroup(GameGridInventory grid)
        {
            if (!AutoBlend || _running || (_groups == 0 && ItemMultiSelectHandler.current?.IsGroupDragging != true)) return false;
            try
            {
                var machine = Blender(grid);
                return machine != null && Bottle(machine) != null;
            }
            catch (Exception e) { Warn("blend-group", e); return false; }
        }

        private static bool Compatible(GameItem bottle, GameItem ingredient, string[] ids)
        {
            if (bottle == null || ingredient == null || !ingredient.IsTag("BLENDABLE_TAG")) return false;
            string input = ChemistryRules.Signature(ids, ChemistryData.Parts(ingredient));
            var parts = ChemistryData.Parts(bottle);
            return ChemistryData.Total(parts) == 0
                || input.Length > 0 && input == ChemistryRules.Signature(ids, parts);
        }

        internal static bool Allow(GameGridInventory grid, GameItem ingredient)
        {
            if (!AutoBlend || !PlayerMoving || _running) return true;
            try
            {
                var machine = Blender(grid);
                if (machine == null) return true;
                var bottle = Bottle(machine);
                if (bottle == null) return true;
                var ids = ChemistryData.Ids();
                bool compatible = Compatible(bottle, ingredient, ids);
                // A single incompatible placement remains available for manual recipes.
                if (!compatible) return _groups == 0 && ItemMultiSelectHandler.current?.IsGroupDragging != true;
                string signature = ChemistryRules.Signature(ids, ChemistryData.Parts(ingredient));
                long reserved = 0;
                foreach (var item in grid.childItems)
                {
                    if (ReferenceEquals(item, ingredient) || !item.IsTag("BLENDABLE_TAG")) continue;
                    if (ChemistryRules.Signature(ids, ChemistryData.Parts(item)) != signature)
                    {
                        if (ChemistryData.Total(ChemistryData.Parts(bottle)) == 0) return false;
                        continue;
                    }
                    reserved += ChemistryRules.BlendVolume(ChemistryData.Parts(item), MachineBlender.GetLoss(machine));
                }
                long output = ChemistryRules.BlendVolume(ChemistryData.Parts(ingredient), MachineBlender.GetLoss(machine));
                if (_groups > 0)
                    return GroupPlan.Reserve(machine, ingredient, signature, output, ChemistryData.Capacity(bottle),
                        ChemistryData.Total(ChemistryData.Parts(bottle)), reserved, item => grid.childItems.Contains(item));
                return output > 0 && output + reserved + ChemistryData.Total(ChemistryData.Parts(bottle))
                    <= ChemistryData.Capacity(bottle);
            }
            catch (Exception e) { Warn("blend-plan", e); return false; }
        }

        internal static void Added(GameGridInventory grid, bool accepted)
        {
            if (!accepted || !AutoBlend || _placements == 0 || _running) return;
            try
            {
                var machine = Blender(grid);
                if (machine != null) Pending.Add(machine);
            }
            catch (Exception e) { Warn("blend-queue", e); }
        }
        internal static void BatchBoundary()
        {
            if (_groups > 0 && !_running) { Drain(); GroupPlan.BatchBoundary(); }
        }
        private static void Drain()
        {
            if (_running || Pending.Count == 0) return;
            var machines = new List<GameItem>(Pending);
            Pending.Clear();
            if (!AutoBlend) return;
            _running = true;
            try
            {
                var ids = ChemistryData.Ids();
                foreach (var machine in machines)
                {
                    var grid = MachineHelper.GetBodyStorage(machine, 1) as GameGridInventory;
                    var bottle = Bottle(machine);
                    if (grid == null || bottle == null || !GeneralHelper.IsItemOwned(machine)
                        || !GeneralHelper.IsItemOwned(bottle)) continue;
                    string signature = ChemistryRules.Signature(ids, ChemistryData.Parts(bottle));
                    bool played = false;
                    foreach (var item in new List<GameItem>(grid.childItems))
                    {
                        if (!ReferenceEquals(item.parentInventory, grid) || !GeneralHelper.IsItemOwned(item)
                            || !Compatible(bottle, item, ids)) continue;
                        string product = ChemistryRules.Signature(ids, ChemistryData.Parts(item));
                        if (signature.Length > 0 && signature != product) continue;
                        long output = ChemistryRules.BlendVolume(ChemistryData.Parts(item), MachineBlender.GetLoss(machine));
                        if (output <= 0 || output + ChemistryData.Total(ChemistryData.Parts(bottle))
                            > ChemistryData.Capacity(bottle)) continue;
                        signature = product;
                        if (!played) { AudioManager.Instance?.Play2("blender"); played = true; }
                        ChemicalHelper.BlendInto(item, bottle, MachineBlender.GetLoss(machine));
                        EventLog.Debug("[Chemistry] AUTO BLEND machine=" + machine.uniqueId + " product=" + product + " ul=" + output);
                    }
                }
            }
            catch (Exception e) { Warn("blend", e); }
            finally { _running = false; }
        }

        internal static void AlcoholAdded(GameItem container, int before)
        {
            if (!Available || Plugin.CfgAutoExtraction?.Value != true || _running || _extracting
                || container?.parentInventory == null || !container.IsTag("LIQUID_CONTAINER_TAG")
                || container.IsTag("WINE_CONTAINER_TAG") || !GeneralHelper.IsItemOwned(container)) return;
            try
            {
                var ids = ChemistryData.Ids();
                int alcohol = Array.IndexOf(ids, "alcohol");
                var parts = ChemistryData.Parts(container);
                if (alcohol < 0 || parts[alcohol] <= before) return;
                var result = ChemistryRules.Extract(ids, parts);
                if (result == null) return;
                _extracting = true;
                var originals = new Dictionary<string, TagState>();
                try
                {
                    for (int i = 0; i < result.Length; i++)
                    {
                        if (result[i] == parts[i]) continue;
                        string key = Liquid.Liquids[i].currentPart;
                        originals[key] = container.state.dict.TryGetValue(key, out var old) ? old.Clone() : null;
                        int value = result[i];
                        container.ModifyTag(key, state => state.Enable().SetInt(value), skipValidate: true);
                    }
                    container.ValidateShapeState();
                }
                catch
                {
                    foreach (var pair in originals)
                        if (pair.Value == null) container.state.dict.Remove(pair.Key);
                        else container.state.dict[pair.Key].Set(pair.Value).SetEnabled(pair.Value.valueEnabled);
                    container.ValidateShapeState();
                    throw;
                }
                WaterFeatureHelper.UpdateWaterFeature(container, isExpose: false);
                SpriteHelper.UpdateWaterContainerSprite(container);
                EventLog.Debug("[Chemistry] AUTO EXTRACT item=" + container.uniqueId + " ul="
                    + ChemistryData.Total(parts) + "->" + ChemistryData.Total(result) + " overflowAllowed=true");
            }
            catch (Exception e) { Warn("extract", e); }
            finally { _extracting = false; }
        }
        internal static bool CanReact(GameItem container)
            => Available && Plugin.CfgAutoExtraction?.Value == true && !_extracting
                && container?.parentInventory != null && container.IsTag("LIQUID_CONTAINER_TAG")
                && !container.IsTag("WINE_CONTAINER_TAG") && GeneralHelper.IsItemOwned(container);
        internal static int Alcohol(GameItem container)
        {
            if (!Available || Plugin.CfgAutoExtraction?.Value != true) return 0;
            var liquid = Liquid.GetLiquidFromId("alcohol");
            return liquid == null ? 0 : ChemistryData.Int(container, liquid.currentPart);
        }
        private static void Warn(string phase, Exception e)
        {
            if (Reported.Add(phase + ":" + e.GetType().Name + ":" + e.Message))
                EventLog.Write("[Chemistry] " + phase + " failed: " + e);
        }
    }

    [HarmonyPatch(typeof(GameGridInventory), nameof(GameGridInventory.MayHaveValidInventorySlot))]
    internal static class ChemistryBlendAdmissionPatch
    {
        [HarmonyPostfix] private static void Postfix(GameGridInventory __instance, GameItem item, ref bool __result)
        { if (__result) __result = ChemistryAutomation.Allow(__instance, item); }
    }
    [HarmonyPatch(typeof(GameGridInventory), nameof(GameGridInventory.UncheckedAccept))]
    internal static class ChemistryBlendAddedPatch
    {
        [HarmonyPostfix] private static void Postfix(GameGridInventory __instance, bool __result)
            => ChemistryAutomation.Added(__instance, __result);
    }
    [HarmonyPatch(typeof(GameGridInventory), "get_SupportsGroupPlacement")]
    internal static class ChemistryBlendGroupModePatch
    {
        // Native sequential placement can leave rejected members at home and free the
        // blender between items; precise formation placement is all-or-nothing.
        [HarmonyPostfix] private static void Postfix(GameGridInventory __instance, ref bool __result)
        { if (__result && ChemistryAutomation.SequentialGroup(__instance)) __result = false; }
    }
    [HarmonyPatch(typeof(GraphUtils), nameof(GraphUtils.TryAcceptAll))]
    internal static class ChemistryBlendBatchPatch
    {
        [HarmonyPostfix] private static void Postfix() => ChemistryAutomation.BatchBoundary();
    }
    [HarmonyPatch(typeof(ItemMouseDragHandler), "EndDrag")]
    internal static class ChemistrySinglePlacementPatch
    {
        [HarmonyPrefix] private static void Prefix() => ChemistryAutomation.BeginPlacement(false);
        [HarmonyFinalizer] private static void Finalizer(Exception __exception)
            => ChemistryAutomation.EndPlacement(false, __exception == null);
    }
    [HarmonyPatch(typeof(ItemMultiSelectHandler), "EndGroupDrag")]
    internal static class ChemistryGroupPlacementPatch
    {
        [HarmonyPrefix] private static void Prefix() => ChemistryAutomation.BeginPlacement(true);
        [HarmonyFinalizer] private static void Finalizer(Exception __exception)
            => ChemistryAutomation.EndPlacement(true, __exception == null);
    }
    [HarmonyPatch(typeof(WaterHelper), nameof(WaterHelper.TransferLiquid))]
    internal static class ChemistryPourExtractionPatch
    {
        [HarmonyPrefix] private static void Prefix(GameItem targetContainer, out int __state)
            => __state = ChemistryAutomation.Alcohol(targetContainer);
        [HarmonyPostfix] private static void Postfix(GameItem targetContainer, int __state)
            => ChemistryAutomation.AlcoholAdded(targetContainer, __state);
    }
    [HarmonyPatch(typeof(WaterHelper), nameof(WaterHelper.AddLiquid))]
    internal static class ChemistryAddExtractionPatch
    {
        [HarmonyPrefix] private static void Prefix(GameItem waterContainer, string liquidId, out int __state)
            => __state = liquidId == "alcohol" ? ChemistryAutomation.Alcohol(waterContainer) : -1;
        [HarmonyPostfix] private static void Postfix(GameItem waterContainer, int __state)
        { if (__state >= 0) ChemistryAutomation.AlcoholAdded(waterContainer, __state); }
    }
}
#endif
