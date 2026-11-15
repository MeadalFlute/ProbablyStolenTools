using System.Collections.Generic;

namespace ProbablyStolenTools
{
    internal readonly record struct DayArchiveIdentity(int Slot, string Run, int Day)
    {
        internal static DayArchiveIdentity? From(SaveMetadata metadata) =>
            metadata != null && metadata.Slot >= 0 && metadata.Day >= 0 && metadata.State == 2
                && !string.IsNullOrEmpty(metadata.RunId)
                ? new DayArchiveIdentity(metadata.Slot, metadata.RunId, metadata.Day) : null;
    }

    internal static class DayArchivePolicy
    {
        // Input is newest/preferred first. Unreadable, non-morning and foreign-run
        // saves never qualify as duplicates based on the filename alone.
        internal static List<int> Duplicates(IEnumerable<SaveMetadata> metadata)
        {
            var seen = new HashSet<DayArchiveIdentity>();
            var duplicates = new List<int>();
            int index = 0;
            foreach (var item in metadata)
            {
                var identity = DayArchiveIdentity.From(item);
                if (identity.HasValue && !seen.Add(identity.Value)) duplicates.Add(index);
                index++;
            }
            return duplicates;
        }
    }
}
