#if MONO
using System;
using System.Diagnostics;
using HarmonyLib;

namespace ProbablyStolenTools
{
    // One aggregate line per native load; no per-item logging or graph inspection.
    internal static class NativeLoadDiagnostics
    {
        internal sealed class Scope
        {
            internal readonly long Started = Stopwatch.GetTimestamp();
        }
        private static int _depth, _decodes;
        private static double _preload, _load, _decode, _history, _longestDecode;
        private static long _started;
        internal static bool Active => _depth > 0;
        private static double Elapsed(long start) =>
            (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;

        internal static Scope Enter()
        {
            if (_depth++ == 0)
            {
                _started = Stopwatch.GetTimestamp();
                _preload = _load = _decode = _history = _longestDecode = 0;
                _decodes = 0;
            }
            return new Scope();
        }
        internal static void Exit(Scope scope, bool load, Exception error)
        {
            if (scope == null) return;
            if (load) _load += Elapsed(scope.Started);
            if (--_depth != 0) return;
            EventLog.Write(FormattableString.Invariant(
                $"[Load] native ms(total={Elapsed(_started):F1},preload={_preload:F1},load={_load:F1},decode={_decode:F1},history={_history:F1},loadOther={Math.Max(0, _load - _decode - _history):F1}) inventories={_decodes} maxDecodeMs={_longestDecode:F1} ok={error == null}"));
        }
        internal static void RecordPreload(long start)
        {
            if (Active && start != 0) _preload += Elapsed(start);
        }
        internal static void RecordDecode(long start)
        {
            if (!Active || start == 0) return;
            double ms = Elapsed(start);
            _decode += ms;
            _longestDecode = Math.Max(_longestDecode, ms);
            _decodes++;
        }
        internal static void RecordHistory(long start)
        {
            if (Active) _history += Elapsed(start);
        }
    }

    [HarmonyPatch(typeof(EmporiumEntry), "Start")]
    internal static class NativeEntryTimingPatch
    {
        [HarmonyPrefix]
        private static void Prefix(out NativeLoadDiagnostics.Scope __state) => __state = NativeLoadDiagnostics.Enter();
        [HarmonyFinalizer]
        private static void Finalizer(NativeLoadDiagnostics.Scope __state, Exception __exception) =>
            NativeLoadDiagnostics.Exit(__state, false, __exception);
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.PreLoadStore))]
    internal static class NativePreloadTimingPatch
    {
        [HarmonyPrefix]
        private static void Prefix(out long __state) =>
            __state = NativeLoadDiagnostics.Active ? Stopwatch.GetTimestamp() : 0;
        [HarmonyFinalizer]
        private static void Finalizer(long __state) => NativeLoadDiagnostics.RecordPreload(__state);
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.DecodeSaveItem))]
    internal static class NativeDecodeTimingPatch
    {
        [HarmonyPrefix]
        private static void Prefix(out long __state) =>
            __state = NativeLoadDiagnostics.Active ? Stopwatch.GetTimestamp() : 0;
        [HarmonyFinalizer]
        private static void Finalizer(long __state) => NativeLoadDiagnostics.RecordDecode(__state);
    }

    internal static class MorningReportDiagnostics
    {
        internal static long Begin(bool visible) =>
            visible && Plugin.CfgRewind?.Value == true && PlayerStore.Instance != null
                && (int)PlayerStore.Instance.storeState == 2 ? Stopwatch.GetTimestamp() : 0;
        internal static void End(long started, Exception error)
        {
            if (started == 0) return;
            double ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            bool pending = CustomerCheckpoint.DayStart(StoreStation.GetDayCounter()) == null;
            EventLog.Write(FormattableString.Invariant(
                $"[Load] morning-report closeMs={ms:F1} ok={error == null} morningAnchorPending={pending}; anchor duration is separate CAPTURE client=day-start captureMs"));
        }
    }

    [HarmonyPatch(typeof(AdvCalendarUIManager), nameof(AdvCalendarUIManager.CloseUI))]
    internal static class MorningCalendarCloseTimingPatch
    {
        [HarmonyPrefix]
        private static void Prefix(out long __state) =>
            __state = MorningReportDiagnostics.Begin(AdvCalendarUIManager.Instance?.UIPanel?.activeSelf == true);
        [HarmonyFinalizer]
        private static void Finalizer(long __state, Exception __exception) => MorningReportDiagnostics.End(__state, __exception);
    }

    [HarmonyPatch(typeof(StartOfDayUIManager), nameof(StartOfDayUIManager.HideUI))]
    internal static class MorningReportCloseTimingPatch
    {
        [HarmonyPrefix]
        private static void Prefix(out long __state) =>
            __state = MorningReportDiagnostics.Begin(StartOfDayUIManager.Instance?.UIPanel?.activeSelf == true);
        [HarmonyFinalizer]
        private static void Finalizer(long __state, Exception __exception) => MorningReportDiagnostics.End(__state, __exception);
    }
}
#endif
