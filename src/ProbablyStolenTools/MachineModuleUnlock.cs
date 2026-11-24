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

        internal static bool MayInsert(GameGridInventory inventory, GameItem item)
        {
            if (item == null || !Allows(inventory) || GraphUtils.FindParentEquals(inventory, item)
                || !GeneralHelper.MayOwnershipAcceptItem(item, inventory)) return false;
            var shape = item.modifiedShape;
            var space = inventory.inventoryShape;
            if (shape == null || space == null
                || (shape.width > space.width || shape.height > space.height)
                    && (shape.width > space.height || shape.height > space.width)) return false;
            // Keep native admission filters. Only the working-bay lock is omitted;
            // precise placement/collision checks still run in the native slot finder.
            return (item.mayInventoryAddItemFunc == null
                    || item.mayInventoryAddItemFunc.InvokeAllReduce(item, inventory, InvokeFuncExtensions.BoolReduceOR))
                && (inventory.mayInventoryAddItemFunc == null
                    || inventory.mayInventoryAddItemFunc.InvokeAllReduce(item, inventory, InvokeFuncExtensions.BoolReduceOR));
        }
    }

    [HarmonyPatch(typeof(GameGridInventory), nameof(GameGridInventory.MayHaveValidInventorySlot))]
    internal static class MachineModuleAdmissionUnlockPatch
    {
        // A simple IsInsertLocked call can be inlined by Mono. Cover the actual
        // admission boundary too, without temporarily changing native lock fields.
        [HarmonyPostfix, HarmonyPriority(Priority.First)]
        private static void Postfix(GameGridInventory __instance, GameItem item, ref bool __result)
        { if (!__result && MachineModuleUnlock.Allows(__instance)) __result = MachineModuleUnlock.MayInsert(__instance, item); }
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
