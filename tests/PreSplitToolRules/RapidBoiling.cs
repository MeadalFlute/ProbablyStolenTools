using System;
#if MONO
using System.Collections.Generic;
using HarmonyLib;
#endif

namespace ProbablyStolenTools
{
    internal static class RapidBoilingRules
    {
        internal static bool Remove(string id, double temperature, bool evaporative, double boilingPoint)
        {
            if (double.IsNaN(temperature) || double.IsInfinity(temperature)) return false;
            // Native boiling uses >, whereas thermal disinfection begins at >= 70 C.
            if (id == "microbe") return temperature >= 70.0;
            return (id == "water" || id == "alcohol" || id == "lipobond_agent")
                && evaporative && !double.IsNaN(boilingPoint) && !double.IsInfinity(boilingPoint)
                && temperature > boilingPoint;
        }
    }

#if MONO
    internal static class RapidBoiling
    {
        private static bool _running, _reported;

        internal static bool Enabled => Plugin.CfgChemistry?.Value == true && Plugin.CfgRapidBoiling?.Value == true;

        internal static bool IsHeatingContainer(GameSlotInventory slot, int heatingState, GameItem machine)
        {
            var container = slot?.childItem;
            return (heatingState == 1 || heatingState == 2) && machine?.identifier == "heating_plate"
                && container != null && ReferenceEquals(container.parentInventory, slot)
                && ReferenceEquals(MachineHelper.GetBodyStorage(machine, 1), slot)
                && ReferenceEquals(slot.GetParentItem(), machine)
                && container.IsTag("LIQUID_CONTAINER_TAG") && !container.IsTag("WINE_CONTAINER_TAG")
                && !OptionalModCompatibility.BrewingOwns(container) && !OptionalModCompatibility.BrewingOwns(machine)
                && GeneralHelper.IsItemOwned(container);
        }

        internal static void Heated(GameSlotInventory slot, int heatingState, GameItem machine)
        {
            if (_running || !Enabled || !CustomerCheckpoint.PatchesReady
                || CustomerCheckpoint.Restoring || Rewind.Restoring
                || NativeLoadDiagnostics.Active || SaveManager.IsDecoding
                || CurrentConverterHelper.isNightCycle || Plugin.Store() == null) return;
            try
            {
                if (!IsHeatingContainer(slot, heatingState, machine)) return;
                var container = slot.childItem;
                // HandleHeating writes state with skipValidate; modifiedState still has the previous temperature.
                var temperature = container.state?.dict != null
                    && container.state.dict.TryGetValue("CONTAINER_TEMP_TAG", out var heatedTemperature)
                        ? heatedTemperature : null;
                if (temperature == null) return;
                double degrees = temperature.GetDouble();
                var keys = new List<string>(4);
                foreach (var liquid in Liquid.Liquids)
                    if (RapidBoilingRules.Remove(liquid.identifier, degrees, liquid.isEvaporative, liquid.boilingPoint)
                        && ChemistryData.Int(container, liquid.currentPart) > 0 && !keys.Contains(liquid.currentPart))
                        keys.Add(liquid.currentPart);
                if (keys.Count == 0) return;
                _running = true;
                var originals = new Dictionary<string, TagState>();
                try
                {
                    foreach (string key in keys)
                    {
                        originals[key] = container.state.dict.TryGetValue(key, out var old) ? old.Clone() : null;
                        container.ModifyTag(key, state => state.Enable().SetInt(0), skipValidate: true);
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
                // Refresh native value/type, purity, empty-temperature state and container art.
                var feature = WaterFeatureHelper.GetWaterFeature(container);
                WaterFeatureHelper.OnLiquidChanged(container, feature.IsExposed());
                WaterFeatureHelper.UpdateWaterFeature(container, isExpose: false);
                SpriteHelper.UpdateWaterContainerSprite(container);
            }
            catch (Exception e)
            {
                if (!_reported)
                {
                    _reported = true;
                    EventLog.Write("[Chemistry] rapid boiling failed; native heating retained: " + e);
                }
            }
            finally { _running = false; }
        }
    }

    [HarmonyPatch(typeof(MachineHeatingPlate), nameof(MachineHeatingPlate.HandleHeating))]
    internal static class HotplateRapidBoilingPatch
    {
        [HarmonyPostfix]
        private static void Postfix(GameSlotInventory containerSlot, int heatingState, GameItem machine)
            => RapidBoiling.Heated(containerSlot, heatingState, machine);
    }
#endif
}
