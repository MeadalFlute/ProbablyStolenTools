#if MONO
using System;
using System.Collections.Generic;
using HarmonyLib;

namespace ProbablyStolenTools
{
    internal static class MultiSelectDiagnostics
    {
        internal static int Count(ItemMultiSelectHandler handler)
            => GameFields.Get<List<GameItemElement>>(handler, "selectedItems")?.Count ?? 0;

        internal static int TryCount(ItemMultiSelectHandler handler)
        {
            try { return EventLog.Verbose && CustomerCheckpoint.Enabled ? Count(handler) : -1; }
            catch { return -1; }
        }

        internal static void Write(ItemMultiSelectHandler handler, string phase, int before = -1, Exception error = null)
        {
            try
            {
                if (!CustomerCheckpoint.Enabled) return;
                if (error != null)
                    EventLog.Write($"[MultiSelect] {phase} FAILED before={before}: " + error);
                if (!EventLog.Verbose) return;
                EventLog.Write($"[MultiSelect] {phase} before={before} selected={Count(handler)} " +
                    $"hasSelection={handler.HasSelection} groupDragging={handler.IsGroupDragging} " +
                    $"checkpoint={CustomerCheckpoint.ActiveId} restoring={CustomerCheckpoint.Restoring}" +
                    (error == null ? "" : " error=" + error));
            }
            catch { /* Diagnostics must never change native input handling. */ }
        }
    }

    [HarmonyPatch(typeof(ItemMultiSelectHandler), "StartGroupDrag")]
    internal static class MultiSelectStartDiagnosticsPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ItemMultiSelectHandler __instance, out int __state)
            => __state = MultiSelectDiagnostics.TryCount(__instance);

        [HarmonyFinalizer]
        private static void Finalizer(ItemMultiSelectHandler __instance, int __state, Exception __exception)
            => MultiSelectDiagnostics.Write(__instance, "START", __state, __exception);
    }

    [HarmonyPatch(typeof(ItemMultiSelectHandler), "EndGroupDrag")]
    internal static class MultiSelectEndDiagnosticsPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ItemMultiSelectHandler __instance, out int __state)
            => __state = MultiSelectDiagnostics.TryCount(__instance);

        [HarmonyFinalizer]
        private static void Finalizer(ItemMultiSelectHandler __instance, int __state, Exception __exception)
            => MultiSelectDiagnostics.Write(__instance, "END", __state, __exception);
    }
}
#endif
