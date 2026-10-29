using System;
using System.Text.RegularExpressions;

namespace ProbablyStolenTools
{
    internal static class InspectionText
    {
        internal const string MatchColor = "#39A78E";
        internal const string MismatchColor = "#BE514C";
        internal const string ActualColor = "#429CBD";
        private static readonly Regex Markup = new("<[^>]*>", RegexOptions.Compiled);

        internal static string Plain(string text)
            => string.IsNullOrWhiteSpace(text) ? "" : Markup.Replace(text, "").Trim();

        internal static string Feature(string category, string actual)
            => (string.IsNullOrWhiteSpace(Plain(category)) ? "实际状态" : Escape(category)) + "：" + Escape(actual);

        internal static string Escape(string text)
            => "<noparse>" + Plain(text) + "</noparse>";

        internal static string Color(string text, bool passed)
            => $"<color={(passed ? MatchColor : MismatchColor)}>{text}</color>";

        internal static string Water(string declared, string actual, bool matches)
        {
            string label = $"<color={(matches ? MatchColor : MismatchColor)}>{Plain(declared)}</color>";
            return matches ? label : label + $" (<color={ActualColor}>{Plain(actual)}</color>)";
        }
    }
}
