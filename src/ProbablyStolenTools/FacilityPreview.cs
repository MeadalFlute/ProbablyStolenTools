#if MONO
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ProbablyStolenTools
{
    internal static class FacilityPreview
    {
        private static readonly HashSet<string> Reported = new();
        private static string T(string zh, string en) => InspectionLocale.Text(zh, en);
        private static GameItem Slot(GameItem machine, int index) =>
            (MachineHelper.GetBodyStorage(machine, index) as GameSlotInventory)?.childItem;
        private static int Performance(GameItem machine) => ChemistryData.Int(machine, "TOTAL_PERCENTAGE_PERFORMANCE_BONUS_INT");
        private static int Quality(GameItem machine) => ChemistryData.Int(machine, "TOTAL_PERCENTAGE_QUALITY_BONUS_INT");

        internal static void AddTooltip(RichTextBuilder builder, GameItem item)
        {
            if (Plugin.CfgChemistry?.Value != true || Plugin.CfgFacilityPreview?.Value != true
                || !InspectionLocale.AnnotationsReady || CustomerCheckpoint.Restoring || Rewind.Restoring
                || item.IsTag("BROKEN_MACHINE_TAG")) return;
            try
            {
                switch (item.identifier)
                {
                    case "water_purifier": Purifier(builder, item); break;
                    case "evaporator": Evaporator(builder, item); break;
                    case "hydroponic": Hydroponic(builder, item); break;
                    default:
                        if (item.IsTag("RAS_TAG")) Fish(builder, item);
                        break;
                }
            }
            catch (Exception e)
            {
                if (Reported.Add(item.identifier + ":" + e.GetType().Name))
                    EventLog.Write("[Chemistry] preview unavailable for " + item.identifier + ": " + e);
            }
        }

        private static void Header(RichTextBuilder b) => b.AddLine(
            T("预计隔夜（按当前内容）", "Next cycle (current contents)"), true, RenderHandler.ColorPalette.LightCyan);
        private static void Output(RichTextBuilder b, string text) => b.AddLine(
            T("产物：", "Output: ") + text, true, RenderHandler.ColorPalette.LightGreen);
        private static void Warning(RichTextBuilder b, string text) => b.AddLine(
            T("产物：", "Output: ") + text, true, RenderHandler.ColorPalette.Red);
        private static string Amount(long ul) => (ul / 1000d).ToString("0.###", CultureInfo.InvariantCulture) + " mL";
        private static string LiquidName(int i) => InspectionLocale.Label(
            AnnotationLabels.Liquid(Liquid.Liquids[i].identifier, Liquid.Liquids[i].displayName));
        private static string ItemName(string id)
        {
            if (string.IsNullOrEmpty(id)) return T("未知产物", "Unknown product");
            return InspectionLocale.Label(new NativeLabel("Item", "item_" + id + "_name", id, id));
        }
        private static void Removed(RichTextBuilder b, int[] amounts)
        {
            var values = new List<string>();
            for (int i = 0; i < amounts.Length; i++)
                if (amounts[i] > 0) values.Add(LiquidName(i) + " " + Amount(amounts[i]));
            b.AddLine(T("去除：", "Remove: ") + (values.Count == 0 ? T("无", "None") : string.Join(", ", values)),
                true, RenderHandler.ColorPalette.LightCyan);
        }
        private static int[] Difference(int[] before, int[] after)
        {
            var removed = new int[before.Length];
            for (int i = 0; i < removed.Length; i++) removed[i] = Math.Max(0, before[i] - after[i]);
            return removed;
        }
        private static int Index(string id) => Array.IndexOf(ChemistryData.Ids(), id);

        private static bool Powered(GameItem machine)
        {
            var battery = Slot(machine, 0);
            if (battery == null) return false;
            int usage = ChemistryData.Int(machine, "machinery_drawn_power");
            if (battery.IsTag("CURRENT_CONVERTER_TAG"))
            {
                var service = StoreService.GetCurrentConverterService();
                // Forecast the selected overnight service, not daytime CanPay().
                return ChemistryData.Tag(battery, "CURRENT_CONVERTER_ON_BOOL")?.GetBool() == true
                    && !CurrentConverterHelper.IsBlackedOut()
                    && service != null && service.unlocked && service.isUsing && !service.IsOnCooldown();
            }
            if (battery.IsTag("HYPERTECH_BATTERY_TAG"))
                return ChemistryData.Int(battery, "HYPERTECH_BATTERY_OUTPUT_INT") >= usage;
            return battery.IsTag("power_source_item") && ChemistryData.Int(battery, "power_source_item_energy") >= usage;
        }
        private static bool CheckPower(RichTextBuilder b, GameItem machine)
        {
            if (Powered(machine)) return true;
            Warning(b, T("电力不足／夜间供电服务未开启", "Insufficient power / overnight service disabled"));
            return false;
        }
        private static int Modules(GameItem machine, string id)
        {
            int count = 0;
            var grid = MachineHelper.GetBodyStorage(machine, 1) as GameInventory;
            if (grid != null) foreach (var module in grid.childItems) if (module.identifier == id) count++;
            return count;
        }

        private static void Purifier(RichTextBuilder b, GameItem machine)
        {
            var bottle = Slot(machine, 2);
            if (bottle == null) return;
            Header(b);
            if (!CheckPower(b, machine)) return;
            var parts = ChemistryData.Parts(bottle);
            long total = FacilityProjection.Total(parts);
            int water = Index("water");
            if (total <= 0 || water < 0) { Output(b, T("无", "None")); return; }
            int purity = (int)(parts[water] * 10000L / total);
            double multiplier = Math.Max(0, MachinePurifier.GetDirtBonus(purity) + Performance(machine) / 100d) * 1.25;
            long budget = (long)((Math.Min(total, 10000000) * 3 / 1000 + 25000) * multiplier);
            var priorities = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                var liquid = Liquid.Liquids[i];
                priorities[i] = !liquid.canBeRemovedByPurifier ? int.MaxValue
                    : (liquid.identifier == "mineral" || liquid.identifier == "chemical_contaminant"
                        || liquid.identifier == "heavy_metal" ? 1000000 : 0) - liquid.particleSize;
            }
            var result = FacilityProjection.Purify(parts, water, MachinePurifier.GetMaxPurityCap(Quality(machine)), budget, priorities);
            Removed(b, Difference(parts, result));
            Output(b, LiquidName(water) + " " + (result[water] * 100d / total).ToString("0.##", CultureInfo.InvariantCulture) + "%");
        }

        private static void Evaporator(RichTextBuilder b, GameItem machine)
        {
            var input = Slot(machine, 2);
            if (input == null) return;
            Header(b);
            var receiver = Slot(machine, 3);
            if (receiver == null) Warning(b, T("缺少回收液容器（蒸发液将丢失）", "Missing recovery container (distillate lost)"));
            if (!CheckPower(b, machine)) return;
            var flags = new bool[Liquid.Liquids.Count];
            for (int i = 0; i < flags.Length; i++) flags[i] = Liquid.Liquids[i].isEvaporative;
            int budget = checked((int)Math.Round(300000f * Math.Max(0f, 1f + Performance(machine) / 100f)));
            var removed = FacilityProjection.Evaporate(ChemistryData.Parts(input), flags, budget);
            Removed(b, removed);
            if (receiver == null) return;
            var values = new List<string>();
            bool alcoholOnly = Modules(machine, "evaporator_module_solvent_recovery") > 0;
            var existing = ChemistryData.Parts(receiver);
            for (int i = 0; i < removed.Length; i++)
            {
                if (alcoholOnly && Liquid.Liquids[i].identifier != "alcohol") continue;
                int recovered = (int)Math.Round(removed[i] * Math.Max(0, Math.Min(100, Quality(machine))) / 100d);
                // Native AddLiquid caps each component individually, not their sum.
                recovered = Math.Min(recovered, Math.Max(0, ChemistryData.Capacity(receiver) - existing[i]));
                if (recovered > 0) values.Add(LiquidName(i) + " " + Amount(recovered));
            }
            Output(b, values.Count == 0 ? T("无回收液", "No distillate") : string.Join(", ", values));
        }

        private static void Fish(RichTextBuilder b, GameItem machine)
        {
            var grid = MachineRAS.GetTankGrid(machine);
            var bottle = MachineRAS.GetWaterSlot(machine)?.childItem;
            if (grid == null || bottle == null) return;
            float removalMl = 0;
            bool tilapia = false;
            foreach (var fish in grid.childItems)
            {
                if (!fish.IsTag("FISH_ITEM_TAG") || fish.IsTag("FISH_IS_DEAD_TAG")) continue;
                string id = ChemistryData.String(fish, "FISH_SPECIES_TAG");
                if (!FishSpecies.All.TryGetValue(id, out var species)) continue;
                if (id == "gutter_tilapia") tilapia = true;
                float kcal = Math.Max(0f, ChemistryData.Float(fish, "FISH_KCAL_GROWN_TAG")
                    - ChemistryData.Float(fish, "FISH_EGG_GRAMS_TAG") * species.eggKcalPerGram);
                removalMl += AquacultureHelper.GetWeightForBodyKcal(species, kcal) / 100f * species.remediationMlPer100gPerNight;
            }
            if (!tilapia) return;
            Header(b);
            var parts = ChemistryData.Parts(bottle);
            var result = (int[])parts.Clone();
            int water = Index("water");
            int converted = 0;
            foreach (string id in AquacultureHelper.REMEDIATED_LIQUIDS)
            {
                int i = Index(id);
                if (i < 0) continue;
                int take = FacilityProjection.Remove(result, i, water, Mathf.RoundToInt(removalMl * 1000f),
                    Liquid.Liquids[i].GetMaxPurityFromMachine(1f));
                converted += take;
            }
            if (water >= 0) result[water] = checked(result[water] + converted);
            Removed(b, Difference(parts, result));
            b.AddLine(T("按当前鱼重估算；不含生长、死亡及滤芯效果", "Current fish weights; excludes growth, mortality and filters"),
                true, RenderHandler.ColorPalette.Gray);
        }

        private static void Hydroponic(RichTextBuilder b, GameItem machine)
        {
            var seed = Slot(machine, 3);
            if (seed == null) return;
            Header(b);
            var bottle = Slot(machine, 2);
            int use = HydroponicHelper.GetWaterUsePerCycle(seed);
            var initial = ChemistryData.Parts(bottle);
            if (bottle == null || FacilityProjection.Total(initial) / 1000 < use)
            {
                Warning(b, T("水量不足，需要 ", "Insufficient water; requires ") + use + " mL");
                return;
            }
            if (!CheckPower(b, machine)) return;
            string state = ChemistryData.String(machine, "MACHINE_STATE_TAG");
            if (state != "STATE_READY" && state != "STATE_WORKING") { Output(b, T("当前未工作", "Not processing")); return; }
            bool growing = state == "STATE_WORKING";
            int progress = ChemistryData.Int(machine, "CURRENT_PROCESSING_SPEED_TAG");
            int required = growing ? ChemistryData.Int(seed, "REQUIRED_PROGRESS") : HydroponicHelper.GetRequiredProgress(seed);
            int current = growing ? ChemistryData.Int(seed, "GROWING_PROGRESS") : 0;
            int health = growing ? ChemistryData.Int(seed, "PLANT_HEALTH_INT") : HydroponicHelper.GetStartingHealthPercent(seed) * 100;
            var parts = (int[])initial.Clone();
            int water = Index("water"), mineral = Index("mineral");
            var damages = new[] { "heavy_metal", "chemical_contaminant", "microbe", "organic_waste", "acid", "base" };
            // Match the work ticks, then consumption, then the purifying trait.
            if (progress < 0 || progress > 10000) throw new InvalidOperationException("Invalid hydroponic work rate: " + progress);
            for (int tick = 0; tick < progress; tick++)
            {
                int nutrient = Percentage(parts, mineral, water);
                int level = HydroponicHelper.GetNutrimentLevel(seed), range = HydroponicHelper.GetNutrimentRange(seed);
                if (nutrient < level - range) health -= 100;
                else if (nutrient > level + range) health -= 50;
                FacilityProjection.Remove(parts, mineral, water, HydroponicHelper.GetMineralUseRate(seed), 0);
                foreach (string id in damages)
                    health -= FacilityProjection.HealthDamage(Percentage(parts, Index(id), water),
                        HydroponicHelper.GetContaminantSensitivity(seed), HydroponicHelper.GetContaminantEffect(seed));
            }
            parts = FacilityProjection.Consume(parts, use);
            var preFilter = (int[])parts.Clone();
            int rank = PlantGeneticsHelper.GetTraitRank(seed, "purifying");
            if (rank > 0)
            {
                int converted = 0;
                for (int i = 0; i < parts.Length; i++)
                {
                    var liquid = Liquid.Liquids[i];
                    if (liquid.identifier == "mineral" || !liquid.canBeRemovedByPurifier || liquid.basePurifierRemoveAmount <= 0) continue;
                    converted += FacilityProjection.Remove(parts, i, water,
                        Math.Max(1000, Mathf.RoundToInt(parts[i] * PlantWaterQuality.PurifyingShare(rank))),
                        PlantWaterQuality.PurifyingFloor(liquid, rank));
                }
                if (water >= 0) parts[water] = checked(parts[water] + converted);
                Removed(b, Difference(preFilter, parts));
            }
            int next = Math.Min(required, current + progress);
            int machineTarget = growing ? ChemistryData.Int(machine, "MACHINE_PROGRESS_TARGET_TAG") : required;
            int machineProgress = growing ? ChemistryData.Int(machine, "MACHINE_PROGRESS_CURRENT_TAG") : 0;
            if (machineProgress + progress < machineTarget) { Output(b, T("继续生长 ", "Growing ") + next + "/" + required); return; }
            Harvest(b, machine, seed, health / 100);
        }

        private static int Percentage(int[] parts, int i, int water)
            => i < 0 || parts[i] <= 0 ? 0 : water < 0 || parts[water] <= 0 ? 1000000
                : (int)Math.Min(1000000L, parts[i] * 10000L / parts[water]);

        private static void Harvest(RichTextBuilder b, GameItem machine, GameItem seed, int health)
        {
            string plant = ChemistryData.String(seed, "PLANT_ID_STRING");
            int ratio = ChemistryData.Int(machine, "MACHINE_QUALITY_RATIO_INT");
            int quality = ratio <= 0 ? 0 : Quality(machine) / ratio;
            int count = (int)Math.Round(HydroponicHelper.GetBaseHarvestQuantity(seed) * (1 + quality / 100d), MidpointRounding.AwayFromZero);
            string id = HydroponicHelper.GetHarvestItemId(seed);
            double mult = 1;
            switch (plant)
            {
                case "nutrifruit_seed": count += Modules(machine, "farm_module_nutrifruit"); break;
                case "dream_cap_seed":
                    count += 2 * Modules(machine, "farm_module_dream_cap");
                    if (Modules(machine, "farm_module_nightmare_cap") > 0 && Modules(machine, "farm_module_dream_cap") == 0) id = "nightmare_cap";
                    mult = health <= 15 ? .5 : health <= 30 ? .65 : health <= 50 ? .8 : health <= 70 ? .9 : 1;
                    break;
                case "kotton_seed":
                    if (Modules(machine, "farm_module_kotton_loom") > 0) id = "kotton_fabric";
                    mult = health <= 15 ? .5 : health <= 30 ? .65 : health <= 50 ? .8 : health <= 70 ? .9 : 1;
                    break;
                case "coffee_bean": mult = health <= 15 ? .5 : health <= 30 ? .6 : health <= 50 ? .7 : health <= 70 ? .8 : 1; break;
                case "fillerweed_seed":
                    if (Modules(machine, "farm_module_fillerweed_production") > 0) count += 4;
                    count = Math.Max(1, count - 1) + 1;
                    break;
                case "hydroreed_seed":
                    string seeds = ItemName("hydroreed_seed") + " 1-2";
                    int filters = Modules(machine, "farm_module_hydroreed_filter");
                    Output(b, seeds + (filters > 0 ? ", " + ItemName("hydroreed_filter") + " " + filters : ""));
                    return;
            }
            int minimum = plant == "coffee_bean" || plant == "bloomberry_seed" || plant == "fillerweed_seed" ? 1 : 0;
            int low = Math.Max(minimum, (int)Math.Floor(count * mult)), high = Math.Max(minimum, (int)Math.Ceiling(count * mult));
            Output(b, ItemName(id) + " " + (low == high ? low.ToString() : low + "-" + high));
        }
    }
}
#endif
