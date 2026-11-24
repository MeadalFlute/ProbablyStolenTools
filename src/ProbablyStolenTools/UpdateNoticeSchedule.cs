namespace ProbablyStolenTools
{
    internal static class UpdateNoticeContent
    {
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
        private float _readySince = -1;

        internal bool Acknowledged(string version, string persistedVersion) =>
            string.IsNullOrEmpty(version) || version == persistedVersion || version == _shown;

        internal bool Due(string version, string persistedVersion, bool ready, float now)
        {
            if (Acknowledged(version, persistedVersion)) return false;
            if (!ready) { _readySince = -1; return false; }
            if (_readySince < 0) _readySince = now;
            return now - _readySince >= 0.5f;
        }

        internal void Shown(string version) { _shown = version; _readySince = -1; }
    }
}
