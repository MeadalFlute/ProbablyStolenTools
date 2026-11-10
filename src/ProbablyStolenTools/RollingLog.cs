using System;
using System.IO;
using System.Text;

namespace ProbablyStolenTools
{
    internal static class RollingLog
    {
        private static readonly object Gate = new();
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        internal static void Append(string path, string line, long maxBytes = 4L * 1024 * 1024, int archives = 3)
        {
            if (maxBytes < 1 || archives < 1) throw new ArgumentOutOfRangeException();
            string record = line + Environment.NewLine;
            lock (Gate)
            {
                var file = new FileInfo(path);
                if (file.Exists && file.Length > 0 && file.Length + Utf8.GetByteCount(record) > maxBytes)
                {
                    string oldest = path + "." + archives;
                    if (File.Exists(oldest)) File.Delete(oldest);
                    for (int i = archives - 1; i >= 1; i--)
                        if (File.Exists(path + "." + i)) File.Move(path + "." + i, path + "." + (i + 1));
                    File.Move(path, path + ".1");
                }
                File.AppendAllText(path, record, Utf8);
            }
        }
    }

    internal sealed class DiagnosticGate
    {
        private string _last;
        private double _at;
        private int _suppressed;

        internal bool ShouldWrite(string state, double now, out int suppressed)
        {
            suppressed = 0;
            if (state == _last && now >= _at && now - _at < 60)
            {
                _suppressed++;
                return false;
            }
            _last = state;
            _at = now;
            suppressed = _suppressed;
            _suppressed = 0;
            return true;
        }

        internal void Reset() { _last = null; _at = 0; _suppressed = 0; }
    }
}
