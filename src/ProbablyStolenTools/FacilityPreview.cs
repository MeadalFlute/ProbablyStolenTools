#if MONO
using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace ProbablyStolenTools
{
    internal static class FacilityPreview
    {
        private sealed class Preview
        {
            internal readonly RichTextBuilder Builder;
            internal readonly bool WarningsOnly;
            internal Preview(RichTextBuilder builder, bool warningsOnly) { Builder = builder; WarningsOnly = warningsOnly; }
        }
        private static readonly HashSet<string> Reported = new();
        private static string T(string zh, string en) => InspectionLocale.Text(zh, en);
        private static GameItem Slot(GameItem machine, int index) =>
            (MachineHelper.GetBodyStorage(machine, index) as GameSlotInventory)?.childItem;
        private static int Performance(GameItem machine) => ChemistryData.Int(machine, "TOTAL_PERCENTAGE_PERFORMANCE_BONUS_INT");
        private static int Quality(GameItem machine) => ChemistryData.Int(machine, "TOTAL_PERCENTAGE_QUALITY_BONUS_INT");

        internal static bool HasDetails(GameItem item)
        {
            if (item == null || Plugin.CfgChemistry?.Value != true || Plugin.CfgFacilityPreview?.Value != true
                || Plugin.Store() == null || item.IsTag("BROKEN_MACHINE_TAG")) return false;
            if (item.identifier == "hydroponic") return Slot(item, 3) != null;
            if (item.identifier == "water_purifier" || item.identifier == "evaporator") return Slot(item, 2) != null;
            if (!item.IsTag("RAS_TAG")) return false;
            var tank = MachineRAS.GetTankGrid(item);
            if (tank == null || MachineRAS.GetWaterSlot(item)?.childItem == null) return false;
            foreach (var fish in tank.childItems)
                if (fish.IsTag("FISH_ITEM_TAG") && !fish.IsTag("FISH_IS_DEAD_TAG")
                    && FishSpecies.All.TryGetValue(ChemistryData.String(fish, "FISH_SPECIES_TAG"), out var species)
                    && species.fishIdentifier == "gutter_tilapia") return true;
            return false;
        }

        internal static void AddTooltip(RichTextBuilder builder, GameItem item, bool warningsOnly = false)
        {
            if (item == null || builder == null || Plugin.CfgChemistry?.Value != true || Plugin.CfgFacilityPreview?.Value != true
                || !InspectionLocale.AnnotationsReady || CustomerCheckpoint.Restoring || Rewind.Restoring
                || Plugin.Store() == null || item.IsTag("BROKEN_MACHINE_TAG")) return;
            try
            {
                var preview = new Preview(builder, warningsOnly);
                switch (item.identifier)
                {
                    case "water_purifier": Purifier(preview, item); break;
                    case "evaporator": Evaporator(preview, item); break;
                    case "hydroponic": Hydroponic(preview, item); break;
                    default:
                        if (item.IsTag("RAS_TAG") && !warningsOnly) Fish(preview, item);
                        break;
                }
            }
            catch (Exception e)
            {
                if (Reported.Add(item.identifier + ":" + e.GetType().Name))
                    EventLog.Write("[Chemistry] preview unavailable for " + item.identifier + ": " + e);
            }
        }

        private static void Header(Preview b)
        {
            if (!b.WarningsOnly) b.Builder.AddLine(
                T("预期今夜产出：", "Expected tonight: "), true, RenderHandler.ColorPalette.DarkOrange);
        }
        private static void Values(Preview b, string heading, IEnumerable<string> values,
            RenderHandler.ColorPalette color = RenderHandler.ColorPalette.LightGreen)
        {
            if (b.WarningsOnly) return;
            b.Builder.Add(heading, RenderHandler.ColorPalette.LightBlue);
            bool first = true;
            foreach (string value in values)
            {
                if (!first) b.Builder.Add(T("、", ", "), RenderHandler.ColorPalette.LightBlue);
                b.Builder.Add(value, color);
                first = false;
            }
            if (first) b.Builder.Add(T("无", "None"), color);
            b.Builder.NextLine();
        }
        private static void Output(Preview b, string text) => Outputs(b, new[] { text });
        private static void Outputs(Preview b, IEnumerable<string> values) => Values(b, T("产物：", "Output: "), values);
        private static void Warning(Preview b, string text)
        {
            if (!b.WarningsOnly) b.Builder.Add(T("产物：", "Output: "), RenderHandler.ColorPalette.LightBlue);
            b.Builder.Add(text, RenderHandler.ColorPalette.Red);
            b.Builder.NextLine();
        }
        private static string Amount(long ul) => (ul / 1000d).ToString("0.###", CultureInfo.InvariantCulture) + "mL";
        private static string WithAmount(string name, string amount) => name + T("（", " (") + amount + T("）", ")");
        private static string LiquidName(int i) => InspectionLocale.Label(
            AnnotationLabels.Liquid(Liquid.Liquids[i].identifier, Liquid.Liquids[i].displayName));
        private static string ItemName(string id)
        {
            if (string.IsNullOrEmpty(id)) return T("未知产物", "Unknown product");
            return InspectionLocale.Label(AnnotationLabels.FacilityItem(id));
        }
        private static void Removed(Preview b, int[] amounts)
        {
            var values = new List<string>();
            for (int i = 0; i < amounts.Length; i++)
                if (amounts[i] > 0) values.Add(WithAmount(LiquidName(i), Amount(amounts[i])));
            Values(b, T("去除：", "Remove: "), values);
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
        private static bool CheckPower(Preview b, GameItem machine)
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

        private static void Purifier(Preview b, GameItem machine)
        {
            var bottle = Slot(machine, 2);
            if (bottle == null) return;
            Header(b);
            var parts = ChemistryData.Parts(bottle);
            long total = FacilityProjection.Total(parts);
            int water = Index("water");
            var priorities = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                var liquid = Liquid.Liquids[i];
                priorities[i] = !liquid.canBeRemovedByPurifier ? int.MaxValue
                    : (liquid.identifier == "mineral" || liquid.identifier == "chemical_contaminant"
                        || liquid.identifier == "heavy_metal" ? 1000000 : 0) - liquid.particleSize;
            }
            int cap = MachinePurifier.GetMaxPurityCap(Quality(machine));
            int purity = total > 0 && water >= 0 ? (int)(parts[water] * 10000L / total) : 0;
            double multiplier = Math.Max(0, MachinePurifier.GetDirtBonus(purity) + Performance(machine) / 100d) * 1.25;
            long budget = (long)((Math.Min(total, 10000000) * 3 / 1000 + 25000) * multiplier);
            var result = FacilityProjection.Purify(parts, water, cap, budget, priorities);
            if (!FacilityProjection.HasRemoval(parts, result))
            {
                Warning(b, T("净水完成，需换水", "Purification complete; replace water"));
                return;
            }
            if (!CheckPower(b, machine) || b.WarningsOnly) return;
            Removed(b, Difference(parts, result));
            Output(b, WithAmount(LiquidName(water), (result[water] * 100d / total).ToString("0.##", CultureInfo.InvariantCulture) + "%"));
        }

        private static void Evaporator(Preview b, GameItem machine)
        {
            var input = Slot(machine, 2);
            if (input == null) return;
            Header(b);
            var receiver = Slot(machine, 3);
            if (receiver == null) Warning(b, T("缺少回收液容器（蒸发液将丢失）", "Missing recovery container (distillate lost)"));
            if (!CheckPower(b, machine) || b.WarningsOnly) return;
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
                if (recovered > 0) values.Add(WithAmount(LiquidName(i), Amount(recovered)));
            }
            Outputs(b, values.Count == 0 ? new[] { T("无回收液", "No distillate") } : (IEnumerable<string>)values);
        }

        private static void Fish(Preview b, GameItem machine)
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
                if (species.fishIdentifier == "gutter_tilapia") tilapia = true;
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
            b.Builder.AddLine(T("按当前鱼重估算；不含生长、死亡及滤芯效果", "Current fish weights; excludes growth, mortality and filters"),
                true, RenderHandler.ColorPalette.Gray);
        }

        private static void Hydroponic(Preview b, GameItem machine)
        {
            var seed = Slot(machine, 3);
            if (seed == null) return;
            if (!b.WarningsOnly) HydroponicWorkWatch.Shown(machine);
            Header(b);
            bool growing = ChemistryData.String(machine, "MACHINE_STATE_TAG") == "STATE_WORKING";
            int health = growing ? ChemistryData.Int(seed, "PLANT_HEALTH_INT") : HydroponicHelper.GetStartingHealthPercent(seed) * 100;
            int startingHealth = health;
            var bottle = Slot(machine, 2);
            int use = HydroponicHelper.GetWaterUsePerCycle(seed);
            var initial = ChemistryData.Parts(bottle);
            if (bottle == null || FacilityProjection.Total(initial) / 1000 < use)
            {
                GrowthScore(b, startingHealth);
                Warning(b, WithAmount(T("水量不足", "Insufficient water"),
                    Amount(FacilityProjection.Total(initial)) + "/" + (use * 1L).ToString(CultureInfo.InvariantCulture) + "mL"));
                return;
            }
            if (!CheckPower(b, machine) || b.WarningsOnly) { GrowthScore(b, startingHealth); return; }
            string state = ChemistryData.String(machine, "MACHINE_STATE_TAG");
            if (state != "STATE_READY" && state != "STATE_WORKING")
            { GrowthScore(b, startingHealth); Output(b, T("当前未工作", "Not processing")); return; }
            int progress = ChemistryData.Int(machine, "CURRENT_PROCESSING_SPEED_TAG");
            int required = growing ? ChemistryData.Int(seed, "REQUIRED_PROGRESS") : HydroponicHelper.GetRequiredProgress(seed);
            int current = growing ? ChemistryData.Int(seed, "GROWING_PROGRESS") : 0;
            var parts = (int[])initial.Clone();
            int water = Index("water"), mineral = Index("mineral");
            var damages = FacilityProjection.PlantContaminants;
            int level = HydroponicHelper.GetNutrimentLevel(seed), range = HydroponicHelper.GetNutrimentRange(seed);
            int mineralUse = HydroponicHelper.GetMineralUseRate(seed);
            double sensitivity = HydroponicHelper.GetContaminantSensitivity(seed), effect = HydroponicHelper.GetContaminantEffect(seed);
            var damageIndices = new int[damages.Length];
            for (int i = 0; i < damages.Length; i++) damageIndices[i] = Index(damages[i]);
            // Match the work ticks, then consumption, then the purifying trait.
            if (progress < 0 || progress > 10000) throw new InvalidOperationException("Invalid hydroponic work rate: " + progress);
            for (int tick = 0; tick < progress; tick++)
            {
                int nutrient = Percentage(parts, mineral, water);
                if (nutrient < level - range) health -= 100;
                else if (nutrient > level + range) health -= 50;
                FacilityProjection.Remove(parts, mineral, water, mineralUse, 0);
                foreach (int i in damageIndices)
                    health -= FacilityProjection.HealthDamage(Percentage(parts, i, water), sensitivity, effect);
            }
            int nutrientUsed = mineral < 0 ? 0 : initial[mineral] - parts[mineral];
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
                var removed = Difference(preFilter, parts);
                if (mineral >= 0) removed[mineral] += nutrientUsed;
                Removed(b, removed);
            }
            int polluting = PlantGeneticsHelper.GetTraitRank(seed, "polluting");
            if (polluting > 0 && water >= 0)
            {
                int pollution = Mathf.RoundToInt(parts[water] * PlantWaterQuality.PollutingShare(polluting));
                int moved = 0;
                var pollutionIds = new[] { "organic_waste", "microbe", "physical_contaminant" };
                var shares = new[] { .5f, .35f, .15f };
                for (int i = 0; i < pollutionIds.Length; i++)
                {
                    int index = Index(pollutionIds[i]);
                    int amount = Math.Min((int)(pollution * shares[i]), parts[water] - moved);
                    if (index < 0 || amount <= 0) continue;
                    parts[index] = Math.Min(ChemistryData.Capacity(bottle), checked(parts[index] + amount));
                    moved += amount;
                }
                parts[water] -= moved;
            }
            GrowthScore(b, startingHealth, health);
            b.Builder.AddLine(T("剩余", "Remaining ") + FacilityProjection.RemainingAmount(FacilityProjection.Total(parts))
                + T("水培营养液（", " hydroponic nutrient solution (")
                + FacilityProjection.MineralPercent(parts, water, mineral).ToString("0.##", CultureInfo.InvariantCulture)
                + T("%）", "%)"), color: RenderHandler.ColorPalette.LightGreen);
            int next = Math.Min(required, current + progress);
            int machineTarget = growing ? ChemistryData.Int(machine, "MACHINE_PROGRESS_TARGET_TAG") : required;
            int machineProgress = growing ? ChemistryData.Int(machine, "MACHINE_PROGRESS_CURRENT_TAG") : 0;
            if (machineProgress + progress < machineTarget)
            {
                Output(b, WithAmount(T("继续生长", "Growing"), current + "→" + next + "/" + required));
                return;
            }
            Harvest(b, machine, seed, health / 100);
        }

        private static int Percentage(int[] parts, int i, int water)
            => i < 0 || parts[i] <= 0 ? 0 : water < 0 || parts[water] <= 0 ? 1000000
                : (int)Math.Min(1000000L, parts[i] * 10000L / parts[water]);

        private static void GrowthScore(Preview b, int current, int? projected = null)
        {
            if (b.WarningsOnly) return;
            var font = OverlayHandler.current?.GetTooltipWindow()?.handler?.GetComponentInChildren<TextMeshProUGUI>()?.font;
            bool arrowSupported = font != null && font.HasCharacter('\u2192', true, false);
            Values(b, T("生长评分：", "Growth score: "),
                new[] { FacilityProjection.GrowthScore(current, projected, arrowSupported) });
        }

        private static void Harvest(Preview b, GameItem machine, GameItem seed, int health)
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
                    string seeds = WithAmount(ItemName("hydroreed_seed"), "1-2");
                    int filters = Modules(machine, "farm_module_hydroreed_filter");
                    Outputs(b, filters > 0 ? new[] { seeds, WithAmount(ItemName("hydroreed_filter"), filters.ToString()) } : new[] { seeds });
                    return;
            }
            int minimum = plant == "coffee_bean" || plant == "bloomberry_seed" || plant == "fillerweed_seed" ? 1 : 0;
            int low = Math.Max(minimum, (int)Math.Floor(count * mult)), high = Math.Max(minimum, (int)Math.Ceiling(count * mult));
            var values = new List<string>();
            void Add(string name, int min, int max)
            {
                if (max > 0) values.Add(WithAmount(name, min == max ? min.ToString() : min + "-" + max));
            }
            if (plant == "bloomberry_seed")
            {
                var probabilities = FacilityProjection.BerryProbabilities(health);
                var names = new[] { T("发霉", "Moldy"), T("过熟", "Overripe"), T("正常", "Normal"), T("较小", "Small") };
                for (int i = 0; i < probabilities.Length; i++)
                    if (probabilities[i] > 0) values.Add(WithAmount(names[i] + T("", " ") + ItemName(id),
                        "~" + (Math.Max(1, count) * probabilities[i]).ToString("0.##", CultureInfo.InvariantCulture)));
                Outputs(b, values);
                b.Builder.AddLine(T("随机品质，按期望估算", "Random quality; expected counts"), color: RenderHandler.ColorPalette.Gray);
                return;
            }
            bool sized = plant == "nutrifruit_seed" || plant == "kotton_seed" && id != "kotton_fabric"
                || plant == "dream_cap_seed" && id != "nightmare_cap";
            if (plant == "dream_cap_seed" && Modules(machine, "farm_module_dream_cap") > 0) id = "meal_cap";
            if (sized)
            {
                var lows = FacilityProjection.HarvestSizes(low, health, plant == "nutrifruit_seed", quality);
                var highs = FacilityProjection.HarvestSizes(high, health, plant == "nutrifruit_seed", quality);
                string[] sizes = { "small", "", "large", "huge" };
                for (int i = 0; i < sizes.Length; i++) Add(SizedItemName(id, sizes[i]), lows[i], highs[i]);
            }
            else if (plant == "fillerweed_seed")
            {
                Add(SizedItemName(id, "small"), 1, 1);
                Add(ItemName(id), low - 1, high - 1);
            }
            else Add(ItemName(id), low, high);
            Outputs(b, values);
        }

        private static string SizedItemName(string id, string size)
        {
            string name = ItemName(id);
            if (size.Length == 0) return name;
            string en = size == "small" ? "Small {0}" : size == "large" ? "Large {0}" : "Huge {0}";
            string zh = size == "small" ? "小型{0}" : size == "large" ? "大型{0}" : "巨型{0}";
            var template = InspectionLocale.Label(new NativeLabel("Mechanic", "item_size_" + size + "_of", zh, en));
            return string.Format(CultureInfo.InvariantCulture, template, name);
        }
    }
}
#endif
