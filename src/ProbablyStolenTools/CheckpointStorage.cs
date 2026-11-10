using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ProbablyStolenTools
{
    internal readonly record struct CheckpointDiskUsage(int Files, long Bytes, int Deleted, string Error);

    internal sealed class CheckpointUsageText
    {
        private (CheckpointDiskUsage Usage, long Limit, string Locale) _key;
        private string _text;
        internal string Get(CheckpointDiskUsage usage, long limit, string locale)
        {
            var key = (usage, limit, locale);
            if (_text != null && _key == key) return _text;
            _key = key;
            _text = LocalePolicy.Text(locale,
                $"存档节点：{usage.Files} 文件，{usage.Bytes / 1048576d:0.0} / {limit / 1048576} MiB",
                $"File checkpoints: {usage.Files} files, {usage.Bytes / 1048576d:0.0} / {limit / 1048576} MiB")
                + (usage.Error == null ? "" : "\n" + LocalePolicy.Text(locale, "清理未完成，详见日志", "Cleanup incomplete; see log"));
            return _text;
        }
    }

    internal static class CheckpointStorage
    {
        internal static bool IsOwnedName(string name)
            => name != null && name.Length > 28 && name[23] == '_'
                && name.EndsWith(".es3", StringComparison.OrdinalIgnoreCase)
                && DateTime.TryParseExact(name.Substring(0, 23), "yyyyMMdd_HHmmss_fffffff",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

        private static bool SafeRoot(string root)
            => System.IO.Directory.Exists(root) && (File.GetAttributes(root) & FileAttributes.ReparsePoint) == 0;

        // Only files created by this mod, directly inside its own directory, may be removed.
        internal static CheckpointDiskUsage Prune(string root, int maxFiles, long maxBytes,
            IEnumerable<string> protectedPaths = null)
        {
            if (maxFiles < 1 || maxBytes < 1) throw new ArgumentOutOfRangeException();
            if (!System.IO.Directory.Exists(root)) return default;
            if (!SafeRoot(root)) throw new IOException("Checkpoint directory is a link; cleanup skipped.");
            string fullRoot = Path.GetFullPath(root);
            var protectedFiles = new HashSet<string>((protectedPaths ?? Array.Empty<string>())
                .Where(path => path != null).Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
            var files = new DirectoryInfo(fullRoot).GetFiles()
                .Where(file => IsOwnedName(file.Name) && (file.Attributes & FileAttributes.ReparsePoint) == 0).ToArray();
            long bytes = files.Sum(file => file.Length);
            int count = files.Length;
            int deleted = 0;
            string error = null;
            foreach (var file in files.OrderBy(file => file.LastWriteTimeUtc).ThenBy(file => file.Name, StringComparer.Ordinal))
            {
                if (count <= maxFiles && bytes <= maxBytes) break;
                if (protectedFiles.Contains(file.FullName) || (file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                try
                {
                    long length = file.Length;
                    file.Delete();
                    bytes -= length;
                    count--;
                    deleted++;
                }
                catch (IOException e) { error = e.Message; }
                catch (UnauthorizedAccessException e) { error = e.Message; }
            }
            if (count > maxFiles || bytes > maxBytes)
                error = "Storage limit still exceeded; protected or locked checkpoints were preserved. " + error;
            return new CheckpointDiskUsage(count, bytes, deleted, error);
        }

        internal static void Touch(string root, string path)
        {
            if (!SafeRoot(root) || !IsOwnedName(Path.GetFileName(path))) return;
            var file = new FileInfo(path);
            if (!string.Equals(file.DirectoryName, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)
                || !file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0) return;
            // Persist explicit use without changing ES3 contents or relying on OS access-time policy.
            file.LastWriteTimeUtc = DateTime.UtcNow;
        }
    }
}
