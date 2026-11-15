#if MONO
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;

namespace ProbablyStolenTools
{
    // Run before the native count-based trim, not after an older day has been deleted.
    // Rotation, slot writing, IO retry and RestoreFromHistory remain native.
    [HarmonyPatch(typeof(SaveFiles), "TrimHistory")]
    internal static class NativeSaveHistoryPatch
    {
        [HarmonyPrefix]
        private static void Prefix(List<SaveHistoryEntry> history, ref int keepCount)
        {
            if (Plugin.CfgRewind?.Value != true || keepCount < 1 || history == null || history.Count == 0) return;
            try
            {
                var metadata = history.Select(Read).ToArray();
                var saving = NativeSaveScopePatch.Saving;
                // Keep the rotated rollback copy until WriteSlotFile really succeeds.
                // Reserve its slot so replaying today cannot evict another date first.
                if (saving != null && DayArchiveIdentity.From(metadata[0]).HasValue && (int)saving.storeState == 2
                    && metadata[0].Slot == saving.saveSlotId && metadata[0].RunId == saving.runID
                    && metadata[0].Day == StoreStation.GetDayCounter() && keepCount < int.MaxValue)
                    keepCount++;
                var duplicates = DayArchivePolicy.Duplicates(metadata);
                int removed = 0;
                foreach (int index in duplicates.OrderByDescending(i => i))
                {
                    var entry = history[index];
                    string path = SaveFiles.AbsolutePath(entry.file);
                    // Duplicate references to one file must not delete the retained copy.
                    bool sharedFile = history.Take(index).Any(e => e != null
                        && string.Equals(e.file, entry.file, StringComparison.OrdinalIgnoreCase));
                    if (!sharedFile)
                    {
                        if (DayArchiveIdentity.From(CheckpointFileCatalog.Read(path))
                            != DayArchiveIdentity.From(metadata[index])) continue;
                        File.Delete(path);
                    }
                    history.RemoveAt(index);
                    removed++;
                }
                if (removed > 0)
                    EventLog.Write($"[Rewind] native history deduplicated={removed} before trim keep={keepCount}; newest per run/slot/day retained");
            }
            catch (Exception e)
            {
                EventLog.Write("[Rewind] native history deduplication incomplete; native trim retained: " + e.Message);
            }
        }

        internal static void ReconcileActive(int slot)
        {
            if (Plugin.CfgRewind?.Value != true) return;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                string activePath = SaveFiles.AbsolutePath(SaveFiles.Slot(slot));
                var active = CheckpointFileCatalog.Read(activePath);
                var identity = DayArchiveIdentity.From(active);
                if (!identity.HasValue || active.Slot != slot) return;
                var stamp = File.GetLastWriteTimeUtc(activePath);
                var history = SaveFiles.LoadHistory(slot);
                var metadata = history.Select(Read).ToArray();
                // Never replace/delete the active slot. An unexpectedly newer history
                // copy is preserved for recovery rather than silently losing progress.
                for (int i = 0; i < metadata.Length; i++)
                    if (DayArchiveIdentity.From(metadata[i]) == identity
                        && File.GetLastWriteTimeUtc(SaveFiles.AbsolutePath(history[i].file)) > stamp)
                        metadata[i] = null;
                var duplicates = DayArchivePolicy.Duplicates(metadata, active);
                var retained = new HashSet<string>(history.Where((_, i) => !duplicates.Contains(i))
                    .Select(e => e.file), StringComparer.OrdinalIgnoreCase);
                var deleted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int removed = 0;
                foreach (int index in duplicates.OrderByDescending(i => i))
                {
                    string file = history[index].file;
                    string path = SaveFiles.AbsolutePath(file);
                    if (!deleted.Contains(file))
                    {
                        if (DayArchiveIdentity.From(CheckpointFileCatalog.Read(path))
                            != DayArchiveIdentity.From(metadata[index])) continue;
                        if (!retained.Contains(file)) { File.Delete(path); deleted.Add(file); }
                    }
                    history.RemoveAt(index);
                    removed++;
                }
                if (removed == 0) return;
                if (history.Count == 0) SaveFiles.IndexDeleteKey("history_" + slot);
                else SaveFiles.IndexSave("history_" + slot, history);
                EventLog.Write($"[Rewind] native active/history deduplicated={removed} slot={slot} day={active.Day}; active slot unchanged");
            }
            catch (Exception e)
            {
                EventLog.Write("[Rewind] native active/history cleanup incomplete; active slot unchanged: " + e.Message);
            }
            finally { NativeLoadDiagnostics.RecordHistory(started); }
        }

        private static SaveMetadata Read(SaveHistoryEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.file) || Path.GetFileName(entry.file) != entry.file
                || !entry.file.EndsWith(".es3", StringComparison.OrdinalIgnoreCase)
                || !SaveFiles.TryParseHistoryName(Path.GetFileNameWithoutExtension(entry.file),
                    out int slot, out int day, out var stamp) || stamp.Year <= 1) return null;
            var metadata = CheckpointFileCatalog.Read(SaveFiles.AbsolutePath(entry.file));
            return metadata?.Slot == slot && metadata.Day == day && entry.dayCount == day ? metadata : null;
        }
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.SaveGame))]
    internal static class NativeSaveScopePatch
    {
        internal static PlayerStore Saving;
        [HarmonyPrefix]
        private static void Prefix(PlayerStore __instance, out PlayerStore __state)
        {
            __state = Saving;
            Saving = __instance;
        }
        [HarmonyFinalizer]
        private static void Finalizer(PlayerStore __state) { Saving = __state; }
    }

    // SaveGame catches some failures itself, so its postfix is NOT proof of a
    // committed slot. Only this successful write permits deleting the rollback copy.
    [HarmonyPatch(typeof(PlayerStore), "WriteSlotFile")]
    internal static class NativeSlotWrittenPatch
    {
        [HarmonyPostfix]
        private static void Postfix(PlayerStore __instance) =>
            NativeSaveHistoryPatch.ReconcileActive(__instance.saveSlotId);
    }
}
#endif
