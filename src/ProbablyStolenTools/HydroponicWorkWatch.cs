#if MONO
using System.Collections.Generic;
using HarmonyLib;

namespace ProbablyStolenTools
{
    internal static class HydroponicWorkWatch
    {
        private static readonly Dictionary<int, int> ShownRates = new();
        internal static void Reset() => ShownRates.Clear();
        internal static void Shown(GameItem machine)
        {
            if (ShownRates.Count >= 256 && !ShownRates.ContainsKey(machine.uniqueId)) ShownRates.Clear();
            ShownRates[machine.uniqueId] = ChemistryData.Int(machine, "CURRENT_PROCESSING_SPEED_TAG");
        }
        internal static void Completed(GameItem machine, int progressBefore, int speed)
        {
            if (Plugin.CfgChemistry?.Value != true || Plugin.CfgFacilityPreview?.Value != true
                || machine.identifier != "hydroponic") return;
            int delta = ChemistryData.Int(machine, "MACHINE_PROGRESS_CURRENT_TAG") - progressBefore;
            bool shown = ShownRates.TryGetValue(machine.uniqueId, out int shownSpeed);
            ShownRates.Remove(machine.uniqueId);
            if (delta != speed || shown && shownSpeed != speed)
                EventLog.Write($"[Chemistry] HYDRO WORK item={machine.uniqueId} shownSpeed={(shown ? shownSpeed.ToString() : "none")} " +
                    $"cycleSpeed={speed} actualDelta={delta} base={ChemistryData.Int(machine, "BASE_PROCESSING_SPEED_TAG")} " +
                    $"performanceBonus={ChemistryData.Int(machine, "TOTAL_PERCENTAGE_PERFORMANCE_BONUS_INT")}; native integer work tags, no fractional progress");
        }
    }

    [HarmonyPatch(typeof(MachineProgressHelper), nameof(MachineProgressHelper.ContinueProgressTypeMachine))]
    internal static class HydroponicProgressWatchPatch
    {
        [HarmonyPrefix] private static void Prefix(GameItem machine, out (int Progress, int Speed) __state)
            => __state = (ChemistryData.Int(machine, "MACHINE_PROGRESS_CURRENT_TAG"), ChemistryData.Int(machine, "CURRENT_PROCESSING_SPEED_TAG"));
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(GameItem machine, (int Progress, int Speed) __state)
            => HydroponicWorkWatch.Completed(machine, __state.Progress, __state.Speed);
    }
}
#endif
