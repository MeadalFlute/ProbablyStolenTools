using System;
using HarmonyLib;

namespace ProbablyStolenTools
{
#if MONO
    internal static class MachineModuleUnlock
    {
        internal static bool Allows(GameInventory inventory)
        {
            if (Plugin.CfgUnlockMachineModules?.Value != true || inventory == null) return false;
            try
            {
                var machine = inventory.GetParentItem();
                return machine != null && machine.IsGameItemType("MACHINE") && machine.IsTag("LOCKED_WORKING_TAG")
                    && ReferenceEquals(MachineHelper.GetModuleInv(machine), inventory);
            }
            catch { return false; }
        }
    }

    // Query-only override: existing locked bays work immediately and disabling
    // the option restores native locks without mutating snapshot/save fields.
    [HarmonyPatch(typeof(GameInventory), nameof(GameInventory.IsInsertLocked))]
    internal static class MachineModuleInsertUnlockPatch
    {
        [HarmonyPostfix]
        private static void Postfix(GameInventory __instance, ref bool __result)
        { if (__result && MachineModuleUnlock.Allows(__instance)) __result = false; }
    }

    [HarmonyPatch(typeof(GameInventory), nameof(GameInventory.IsRemoveLocked))]
    internal static class MachineModuleRemoveUnlockPatch
    {
        [HarmonyPostfix]
        private static void Postfix(GameInventory __instance, ref bool __result)
        { if (__result && MachineModuleUnlock.Allows(__instance)) __result = false; }
    }

    [HarmonyPatch(typeof(MachineHelper), "GetLockedModuleBayMachine")]
    internal static class MachineModuleDragUnlockPatch
    {
        // The explicit module-drag blocker was added after the stable build.
        [HarmonyPrepare]
        private static bool Prepare() => typeof(MachineHelper).GetMethod("GetLockedModuleBayMachine") != null;

        [HarmonyPostfix]
        private static void Postfix(ref GameItem __result)
        {
            if (__result != null && Plugin.CfgUnlockMachineModules?.Value == true
                && MachineModuleUnlock.Allows(MachineHelper.GetModuleInv(__result))) __result = null;
        }
    }
#endif
}
