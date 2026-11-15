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
        private static void Prefix(List<SaveHistoryEntry> history, int keepCount)
        {
            if (Plugin.CfgRewind?.Value != true || keepCount < 1 || history == null || history.Count < 2) return;
            try
            {
                var metadata = history.Select(Read).ToArray();
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
}
#endif
