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
        internal static bool ExtractionEnabled => Plugin.CfgChemistry?.Value == true && Plugin.CfgAutoExtraction?.Value == true;
        internal static bool ReactionsEnabled => Plugin.CfgChemistry?.Value == true && Plugin.CfgRapidReactions?.Value == true;
        private static bool Available => CustomerCheckpoint.PatchesReady && Plugin.CfgChemistry?.Value == true
            && !CustomerCheckpoint.Restoring && !Rewind.Restoring
            && !NativeLoadDiagnostics.Active && !SaveManager.IsDecoding
            && !CurrentConverterHelper.isNightCycle && Plugin.Store() != null;
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
        internal static bool BlendBottleAvailable(GameItem bottle) => bottle != null && !OptionalModCompatibility.BrewingOwns(bottle);

        internal static bool IsExtrusionSlot(GameItem machine, GameSlotInventory slot)
            // Unlike the blender, the manual finisher has no battery panel: module=0, container=1, output=2.
            => machine?.identifier == "chem_finisher" && slot != null
                && ReferenceEquals(MachineHelper.GetBodyStorage(machine, 1), slot);

        internal static void ExtrusionAdded(GameSlotInventory slot, GameItem item, bool accepted)
        {
            if (!accepted || !Available || Plugin.CfgSmartExtrusion?.Value != true || item == null) return;
            try
            {
                var machine = slot?.GetParentItem();
                if (!IsExtrusionSlot(machine, slot)
                    || !ReferenceEquals(slot.childItem, item) || !ReferenceEquals(item.parentInventory, slot)
                    || !item.IsTag("LIQUID_CONTAINER_TAG") || !GeneralHelper.IsItemOwned(machine)
                    || !GeneralHelper.IsItemOwned(item) || OptionalModCompatibility.BrewingOwns(item)) return;
                string ingredient = ChemistryRules.ExtrusionIngredient(ChemistryData.Ids(), ChemistryData.Parts(item));
                if (ingredient == null) return;
                string product = null;
                foreach (var recipe in ChemFinisherRecipe.chemFinisherRecipes)
                {
                    if (recipe == null || recipe.activeIngredientId != ingredient || string.IsNullOrEmpty(recipe.identifier)) continue;
                    if (product != null) return; // Ambiguous extension recipes remain a manual choice.
                    product = recipe.identifier;
                }
                if (product == null || ChemistryData.Tag(machine, "SELECTED_CHEM_ID_TAG")?.GetString() == product) return;
                machine.ModifyTag("SELECTED_CHEM_ID_TAG", state => state.Enable().SetString(product));
                EventLog.Debug("[Chemistry] SMART EXTRUSION machine=" + machine.uniqueId + " product=" + product);
            }
            catch (Exception e) { Warn("extrusion", e); }
        }

        internal static bool SequentialGroup(GameGridInventory grid)
        {
            if (!AutoBlend || _running || (_groups == 0 && ItemMultiSelectHandler.current?.IsGroupDragging != true)) return false;
            try
            {
                var machine = Blender(grid);
                var bottle = machine == null ? null : Bottle(machine);
                return bottle != null && !OptionalModCompatibility.BrewingOwns(bottle);
            }
            catch (Exception e) { Warn("blend-group", e); return false; }
        }

        internal static bool Allow(GameGridInventory grid, GameItem ingredient)
        {
            if (ingredient == null || !AutoBlend || !PlayerMoving || _running) return true;
            try
            {
                var machine = Blender(grid);
                if (machine == null) return true;
                var bottle = Bottle(machine);
                if (bottle == null) return true;
                if (!BlendBottleAvailable(bottle)) return true;
                var ids = ChemistryData.Ids();
                var contents = ChemistryData.Parts(bottle);
                var input = ChemistryData.Parts(ingredient);
                long total = ChemistryData.Total(contents);
                string signature = ChemistryRules.Signature(ids, input);
                bool compatible = ingredient != null && ingredient.IsTag("BLENDABLE_TAG")
                    && (total == 0 || signature.Length > 0 && signature == ChemistryRules.Signature(ids, contents));
                // A single incompatible placement remains available for manual recipes.
                if (!compatible) return _groups == 0 && ItemMultiSelectHandler.current?.IsGroupDragging != true;
                int loss = MachineBlender.GetLoss(machine);
                long reserved = 0;
                var residents = grid.childItems;
                foreach (var item in residents)
                {
                    if (ReferenceEquals(item, ingredient) || !item.IsTag("BLENDABLE_TAG")) continue;
                    var parts = ChemistryData.Parts(item);
                    if (ChemistryRules.Signature(ids, parts) != signature)
                    {
                        if (total == 0) return false;
                        continue;
                    }
                    reserved += ChemistryRules.BlendVolume(parts, loss);
                }
                long output = ChemistryRules.BlendVolume(input, loss);
                if (_groups > 0)
                    return GroupPlan.Reserve(machine, ingredient, signature, output, ChemistryData.Capacity(bottle),
                        total, reserved, item => residents.Contains(item));
                return output > 0 && output + reserved + total
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
            var timing = System.Diagnostics.Stopwatch.StartNew();
            int blended = 0, combined = 0;
            try
            {
                var ids = ChemistryData.Ids();
                foreach (var machine in machines)
                {
                    var grid = MachineHelper.GetBodyStorage(machine, 1) as GameGridInventory;
                    var bottle = Bottle(machine);
                    if (grid == null || bottle == null || !GeneralHelper.IsItemOwned(machine)
                        || !GeneralHelper.IsItemOwned(bottle) || !BlendBottleAvailable(bottle)) continue;
                    string signature = ChemistryRules.Signature(ids, ChemistryData.Parts(bottle));
                    bool played = false;
                    int loss = MachineBlender.GetLoss(machine);
                    foreach (var item in grid.childItems)
                    {
                        if (!ReferenceEquals(item.parentInventory, grid) || !GeneralHelper.IsItemOwned(item)
                            || !item.IsTag("BLENDABLE_TAG")) continue;
                        var input = ChemistryData.Parts(item);
                        var contents = ChemistryData.Parts(bottle);
                        string product = ChemistryRules.Signature(ids, input);
                        long total = ChemistryData.Total(contents);
                        if (total > 0 && (product.Length == 0 || product != ChemistryRules.Signature(ids, contents))) continue;
                        if (signature.Length > 0 && signature != product) continue;
                        long output = ChemistryRules.BlendVolume(input, loss);
                        if (output <= 0 || output + total
                            > ChemistryData.Capacity(bottle)) continue;
                        signature = product;
                        if (!played) { AudioManager.Instance?.Play2("blender"); played = true; }
                        if (Blend(item, bottle, ids, input, contents, loss)) combined++;
                        blended++;
                        EventLog.Debug("[Chemistry] AUTO BLEND machine=" + machine.uniqueId + " product=" + product + " ul=" + output);
                    }
                }
            }
            catch (Exception e) { Warn("blend", e); }
            finally
            {
                _running = false;
                if (timing.ElapsedMilliseconds >= 100)
                    EventLog.Write("[Chemistry] AUTO BLEND batch items=" + blended + " combined=" + combined
                        + " ms=" + timing.ElapsedMilliseconds);
            }
        }

        private static bool Blend(GameItem item, GameItem bottle, string[] ids, int[] input, int[] contents, int loss)
        {
            if (!ChemistryRules.CanCombineColdCoffee(ids, contents, input,
                ChemistryData.Tag(bottle, "CONTAINER_TEMP_TAG")?.GetDouble() ?? 0, bottle.IsTag("HEATING_TAG")))
            {
                ChemicalHelper.BlendInto(item, bottle, loss);
                return false;
            }
            double multiplier = 1.0 - Math.Max(0, Math.Min(100, loss)) / 100.0;
            // Keep native temperature, validation, feature/sprite, overflow and destruction callbacks.
            for (int i = 0; i < input.Length; i++)
            {
                int amount = (int)Math.Round(input[i] * multiplier, MidpointRounding.AwayFromZero);
                if (amount > 0) WaterHelper.AddLiquid(bottle, ids[i], amount);
            }
            WaterHelper.RemoveExcessLiquid(bottle);
            item.Destroy();
            return true;
        }

        internal static void AlcoholAdded(GameItem container, int before)
        {
            if (!Available || !ExtractionEnabled || _running || _extracting
                || container?.parentInventory == null || !container.IsTag("LIQUID_CONTAINER_TAG")
                || container.IsTag("WINE_CONTAINER_TAG") || OptionalModCompatibility.BrewingOwns(container)
                || !GeneralHelper.IsItemOwned(container)) return;
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
            => Available && ReactionsEnabled && !_extracting
                && container?.parentInventory != null && container.IsTag("LIQUID_CONTAINER_TAG")
                && !container.IsTag("WINE_CONTAINER_TAG") && !OptionalModCompatibility.BrewingOwns(container)
                && GeneralHelper.IsItemOwned(container);
        internal static int Alcohol(GameItem container)
        {
            if (!Available || !ExtractionEnabled) return 0;
            var liquid = Liquid.GetLiquidFromId("alcohol");
            return liquid == null ? 0 : ChemistryData.Int(container, liquid.currentPart);
        }
        private static void Warn(string phase, Exception e)
        {
            if (Reported.Add(phase + ":" + e.GetType().Name + ":" + e.Message))
                EventLog.Write("[Chemistry] " + phase + " failed: " + e);
        }
    }

    // Native EndDrag tries Target after inserting into a container. VBE recipe
    // ingredients must remain solid instead of immediately dissolving in that vessel.
    [HarmonyPatch(typeof(GameItem), nameof(GameItem.CanTarget))]
    internal static class BrewingIngredientTargetPatch
    {
        [HarmonyPostfix] private static void Postfix(GameItem __instance, GameItem targetItem, ref bool __result)
        { if (__result && OptionalModCompatibility.BrewingRecipeTarget(__instance, targetItem)) __result = false; }
    }

    [HarmonyPatch(typeof(ChemicalHelper), nameof(ChemicalHelper.DissolveInto))]
    internal static class BrewingIngredientDissolvePatch
    {
        [HarmonyPrefix] private static bool Prefix(GameItem chemicalItem, GameItem liquidContainer)
            => !OptionalModCompatibility.BrewingRecipeTarget(chemicalItem, liquidContainer);
    }

    [HarmonyPatch(typeof(GameGridInventory), nameof(GameGridInventory.MayHaveValidInventorySlot))]
    internal static class ChemistryBlendAdmissionPatch
    {
        [HarmonyPostfix] private static void Postfix(GameGridInventory __instance, GameItem item, ref bool __result)
        { if (__result) __result = ChemistryAutomation.Allow(__instance, item); }
    }
    [HarmonyPatch(typeof(GameSlotInventory), nameof(GameSlotInventory.UncheckedAccept))]
    internal static class ChemistryExtrusionAddedPatch
    {
        [HarmonyPostfix] private static void Postfix(GameSlotInventory __instance, GameItem item, bool __result)
            => ChemistryAutomation.ExtrusionAdded(__instance, item, __result);
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
