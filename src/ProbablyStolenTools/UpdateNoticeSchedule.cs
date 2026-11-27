namespace ProbablyStolenTools
{
    internal static class UpdateNoticeContent
    {
        internal const string Revision = ":20261126-feature-split-preview-1";
        internal const float Duration = 8f;
        internal const string SummaryChinese = "辅助功能已独立为Shift工具箱；QuickSL专注回溯，双装共用面板。";
        internal const string SummaryEnglish = "Utilities now live in Shift Toolbox. QuickSL focuses on rewind; install both to share one panel.";
        internal const string DetailsChinese = "功能拆分：\nQuickSL保留回溯、检查点、快速重载和重掷随机数，以及回溯兼容保护。\n快速鉴定、水质与酿酒标注、Shift详情、化学辅助、窗口磁吸和置顶等功能已迁移至独立模组Shift工具箱（shift.toolbox）。\n---\n"
            + "设置与面板：\nShift工具箱首次启动会只读导入旧版QuickSL的辅助功能设置，不会覆盖自己已有的设置，也不会修改旧配置。\n两个模组均可单独使用；双装时在同一面板切换QuickSL与Shift工具箱。工具箱也可从游戏的模组设置打开。";
        internal const string DetailsEnglish = "Feature split:\nQuickSL retains rewind, checkpoints, quick reload, random reseeding and rewind compatibility safeguards.\nAppraisal, water and brewing labels, Shift details, chemistry assistance, window snapping and pinning now belong to the independent Shift Toolbox mod (shift.toolbox).\n---\n"
            + "Settings and panel:\nOn first launch, Shift Toolbox imports its owned settings from legacy QuickSL without changing the old configuration or overwriting its own existing values.\nEach mod works alone; together they share a panel with separate pages. Toolbox settings are also accessible from the game's mod settings.";
        internal static float Progress(float now, float until, float duration) =>
            float.IsNaN(now) || float.IsNaN(until) || duration <= 0 || float.IsNaN(duration)
                ? 0 : 1 - System.Math.Max(0, System.Math.Min(1, (until - now) / duration));
        internal static string[] Sections(string text)
        {
            var sections = new System.Collections.Generic.List<string>();
            var lines = new System.Collections.Generic.List<string>();
            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (line.Trim() == "---")
                {
                    if (lines.Count > 0) sections.Add(string.Join("\n", lines));
                    lines.Clear();
                }
                else if (!string.IsNullOrWhiteSpace(line)) lines.Add(line);
            }
            if (lines.Count > 0) sections.Add(string.Join("\n", lines));
            return sections.ToArray();
        }
    }
    internal sealed class UpdateNoticeSchedule
    {
        private string _shown;
        private float _readySince = -1, _visibleSince = -1;
        internal bool Acknowledged(string version, string persistedVersion) =>
            string.IsNullOrEmpty(version) || version == persistedVersion || version == _shown;
        internal bool Due(string version, string persistedVersion, bool ready, float now)
        {
            if (Acknowledged(version, persistedVersion)) return false;
            if (!ready) { _readySince = -1; return false; }
            if (_readySince < 0) _readySince = now;
            return now - _readySince >= .5f;
        }
        internal bool ConfirmVisible(bool visible, float now)
        {
            if (!visible) { _visibleSince = -1; return false; }
            if (_visibleSince < 0) _visibleSince = now;
            return now - _visibleSince >= 2f;
        }
        internal void Shown(string version) { _shown = version; _readySince = _visibleSince = -1; }
    }
}
