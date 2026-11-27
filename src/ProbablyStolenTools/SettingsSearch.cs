using System;
using System.Text;

namespace ProbablyStolenTools
{
    internal static class SettingsSearch
    {
        internal static bool Matches(string query, params string[] labels)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            string haystack = Normalize(string.Join(" ", labels));
            foreach (string word in query.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
            {
                string needle = Normalize(word);
                if (needle.Length == 0) continue;
                int index = 0;
                foreach (char c in haystack)
                    if (c == needle[index] && ++index == needle.Length) break;
                if (index != needle.Length) return false;
            }
            return true;
        }

        private static string Normalize(string value)
        {
            var text = new StringBuilder(value.Length);
            foreach (char c in value)
                if (char.IsLetterOrDigit(c)) text.Append(char.ToLowerInvariant(c));
            return text.ToString();
        }
    }
}
