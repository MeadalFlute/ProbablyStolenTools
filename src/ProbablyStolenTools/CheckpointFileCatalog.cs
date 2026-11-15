using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ProbablyStolenTools
{
    internal sealed class CheckpointFileCatalog
    {
        internal sealed record Entry(string Path, long Length, long Ticks, SaveMetadata Metadata);
        private Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
        internal IEnumerable<Entry> Entries => _entries.Values;

        internal static SaveMetadata Read(string path)
        {
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists || file.Length > 16 * 1024 * 1024
                    || (file.Attributes & FileAttributes.ReparsePoint) != 0) return null;
                return SaveMetadata.Read(File.ReadAllText(path).TrimStart('\uFEFF'));
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (FormatException) { return null; }
            catch (OverflowException) { return null; }
        }

        internal void Refresh(string root)
        {
            if (!System.IO.Directory.Exists(root)) { _entries.Clear(); return; }
            if (!CheckpointStorage.SafeRoot(root)) throw new IOException("Checkpoint directory is a link; indexing skipped.");
            var next = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in new DirectoryInfo(root).GetFiles())
            {
                if (!CheckpointStorage.IsOwnedName(file.Name) || (file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (!_entries.TryGetValue(file.FullName, out var entry)
                    || entry.Metadata == null || entry.Length != file.Length || entry.Ticks != file.LastWriteTimeUtc.Ticks)
                    entry = new Entry(file.FullName, file.Length, file.LastWriteTimeUtc.Ticks, Read(file.FullName));
                next[file.FullName] = entry;
            }
            _entries = next;
        }

        internal Entry Find(SaveMetadata metadata)
        {
            var identity = DayArchiveIdentity.From(metadata);
            return identity == null ? null : Entries.Where(e => DayArchiveIdentity.From(e.Metadata) == identity)
                .OrderByDescending(e => e.Ticks).FirstOrDefault();
        }

        internal int Deduplicate(string root, IEnumerable<string> protectedPaths)
        {
            Refresh(root);
            var keep = new HashSet<string>((protectedPaths ?? Array.Empty<string>())
                .Where(p => p != null).Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
            var ordered = Entries.OrderByDescending(e => keep.Contains(e.Path))
                .ThenByDescending(e => e.Ticks).ThenBy(e => e.Path, StringComparer.Ordinal).ToArray();
            int deleted = 0;
            foreach (int index in DayArchivePolicy.Duplicates(ordered.Select(e => e.Metadata)))
            {
                var entry = ordered[index];
                if (keep.Contains(entry.Path)) continue;
                // Revalidate attributes/metadata immediately before removal.
                if (DayArchiveIdentity.From(Read(entry.Path)) != DayArchiveIdentity.From(entry.Metadata)) continue;
                File.Delete(entry.Path);
                _entries.Remove(entry.Path);
                deleted++;
            }
            return deleted;
        }
    }
}
