using System;
using System.IO;

namespace ProbablyStolenTools
{
    internal static class CheckpointFailureMessage
    {
        internal const string ProjectUrl = "https://github.com/MeadalFlute/ProbablyStolenTools";
        internal static string Describe(Exception error, bool chinese)
        {
            string T(string zh, string en) => chinese ? zh : en;
            var cause = error;
            for (int i = 0; i < 16 && cause?.InnerException != null; i++) cause = cause.InnerException;
            if (cause == null) return T("未知错误：未提供异常。", "Unknown error: no exception was provided.");
            if (cause is SnapshotLimitException limit)
            {
                string key, detail;
                long current, maximum;
                if (limit.Kind == "object")
                {
                    key = "Snapshot.MaxObjects"; current = limit.MaxObjects; maximum = 1000000;
                    detail = T($"对象超过 {current:N0} 上限（已发现 {limit.Discovered:N0}）。",
                        $"Object limit {current:N0} exceeded ({limit.Discovered:N0} discovered).");
                }
                else if (limit.Kind == "slot")
                {
                    key = "Snapshot.MaxSlots"; current = limit.MaxSlots; maximum = 8000000;
                    detail = T($"字段槽超过 {current:N0} 上限。", $"Field-slot limit {current:N0} exceeded.");
                }
                else
                {
                    key = "Snapshot.MaxBytesMB"; current = limit.MaxBytes / 1048576; maximum = 2048;
                    detail = T($"快照超过 {current} MiB 字节上限。", $"Snapshot byte limit {current} MiB exceeded.");
                }
                long suggested = Math.Min(maximum, Math.Max(current + 1, current + current / 2));
                return "SnapshotLimitException: " + detail + "\n"
                    + (current >= maximum
                        ? T("已达支持的最大上限，请报告节点规模；不要继续调大。", "Supported maximum reached; report the snapshot size instead of raising it further.")
                        : T("可在模组齿轮设置调整，或退出游戏后修改 PSTools.settings.xml：",
                            "Use the mod's gear settings, or exit the game and edit PSTools.settings.xml:") + "\n"
                            + $"<Entry key=\"{key}\" value=\"{suggested}\" />" + "\n"
                            + T("更高上限允许更多内存占用；未触及上限的节点不会因此多遍历对象。",
                                "Higher limits permit more memory use; below-limit graphs do not traverse extra objects."));
            }
            string raw = cause.GetType().FullName + ": " + (cause.Message ?? "").Replace("\r", " ").Replace("\n", " ");
            if (raw.Length > 240) raw = raw.Substring(0, 240) + "...";
            bool layout = cause is TypeLoadException || cause is MissingFieldException || cause is MissingMethodException
                || cause is BadImageFormatException
                || cause is FileNotFoundException missing && (missing.FileName ?? "").Contains("ProbablyStolenTools.StaticLayouts")
                || (cause.Message ?? "").IndexOf("snapshot layout", StringComparison.OrdinalIgnoreCase) >= 0
                || (cause.Message ?? "").IndexOf("static snapshot field", StringComparison.OrdinalIgnoreCase) >= 0;
            if (layout)
                return raw + "\n" + T("静态布局表不适用。请访问 GitHub 获取手动生成工具，或等待 Mod 更新。",
                    "Static layouts are incompatible. Get the manual generator from GitHub, or wait for a mod update.")
                    + "\n" + T("项目地址已写入日志；生成后仍失败时请报告上方异常。",
                        "Project URL is in the log. If regeneration fails, report the exception above.");
            return raw + "\n" + (cause is IOException || cause is UnauthorizedAccessException
                ? T("检查磁盘空间、文件权限或是否被占用；请附上此错误与日志。",
                    "Check disk space, permissions and file locks; include this error and the log.")
                : T("请在 bug report 中附上此错误、触发操作与日志。",
                    "Include this error, the triggering action and the log in your bug report."));
        }
    }
}
