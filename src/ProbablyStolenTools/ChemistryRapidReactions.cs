#if MONO
using System;
using HarmonyLib;

namespace ProbablyStolenTools
{
    internal static class ChemistryRapidReactions
    {
        private static bool _running;
        private static bool _reported;

        internal static void Complete(GameItem container, int changed, Func<GameItem, int> recipe)
        {
            if (_running || changed <= 0 || !ChemistryAutomation.CanReact(container)) return;
            _running = true;
            try
            {
                if (!ReactionCompletion.Finish(() => recipe(container), () => ChemistryData.Parts(container), out _,
                    container.SyncModifiedState)
                    && !_reported)
                {
                    _reported = true;
                    EventLog.Write("[Chemistry] rapid reaction step budget reached; remaining reaction deferred to native ticks");
                }
            }
            catch (Exception e)
            {
                if (!_reported)
                {
                    _reported = true;
                    EventLog.Write("[Chemistry] rapid reaction failed; native ticks retained: " + e);
                }
            }
            finally { _running = false; }
        }
    }

    [HarmonyPatch(typeof(ChemicalHelper), nameof(ChemicalHelper.HandleNomoreReaction))]
    internal static class RapidNomorePatch
    {
        [HarmonyPostfix] private static void Postfix(GameItem container, int __result)
            => ChemistryRapidReactions.Complete(container, __result, ChemicalHelper.HandleNomoreReaction);
    }
    [HarmonyPatch(typeof(ChemicalHelper), nameof(ChemicalHelper.HandleMicrobeAlcohol))]
    internal static class RapidAlcoholDisinfectionPatch
    {
        [HarmonyPostfix] private static void Postfix(GameItem container, int __result)
            => ChemistryRapidReactions.Complete(container, __result, ChemicalHelper.HandleMicrobeAlcohol);
    }
    [HarmonyPatch(typeof(ChemicalHelper), nameof(ChemicalHelper.HandleOrganicWasteMineralization))]
    internal static class RapidMineralizationPatch
    {
        [HarmonyPostfix] private static void Postfix(GameItem container, int __result)
            => ChemistryRapidReactions.Complete(container, __result, ChemicalHelper.HandleOrganicWasteMineralization);
    }
    [HarmonyPatch(typeof(ChemicalHelper), nameof(ChemicalHelper.HandleAcidBaseMicrobeKill))]
    internal static class RapidAcidDisinfectionPatch
    {
        [HarmonyPostfix] private static void Postfix(GameItem container, int __result)
            => ChemistryRapidReactions.Complete(container, __result, ChemicalHelper.HandleAcidBaseMicrobeKill);
    }
    [HarmonyPatch(typeof(ChemicalHelper), nameof(ChemicalHelper.HandleStabilizingAgentSedimentation))]
    internal static class RapidStabilizationPatch
    {
        [HarmonyPostfix] private static void Postfix(GameItem container, int __result)
            => ChemistryRapidReactions.Complete(container, __result, ChemicalHelper.HandleStabilizingAgentSedimentation);
    }
    [HarmonyPatch(typeof(ChemicalHelper), nameof(ChemicalHelper.HandleProteinPrecipitation))]
    internal static class RapidProteinPatch
    {
        [HarmonyPostfix] private static void Postfix(GameItem container, int __result)
            => ChemistryRapidReactions.Complete(container, __result, ChemicalHelper.HandleProteinPrecipitation);
    }
    [HarmonyPatch(typeof(ChemicalHelper), nameof(ChemicalHelper.HandleLipobondAgentSedimentation))]
    internal static class RapidLipobondPatch
    {
        [HarmonyPostfix] private static void Postfix(GameItem container, int __result)
            => ChemistryRapidReactions.Complete(container, __result, ChemicalHelper.HandleLipobondAgentSedimentation);
    }
    [HarmonyPatch(typeof(ChemicalHelper), nameof(ChemicalHelper.HandleAcidBaseNeutralization))]
    internal static class RapidNeutralizationPatch
    {
        [HarmonyPostfix] private static void Postfix(GameItem container, int __result)
            => ChemistryRapidReactions.Complete(container, __result, ChemicalHelper.HandleAcidBaseNeutralization);
    }
    [HarmonyPatch(typeof(ChemicalHelper), nameof(ChemicalHelper.HandleChlorine))]
    internal static class RapidChlorinePatch
    {
        [HarmonyPostfix] private static void Postfix(GameItem container, int __result)
            => ChemistryRapidReactions.Complete(container, __result, ChemicalHelper.HandleChlorine);
    }
}
#endif
