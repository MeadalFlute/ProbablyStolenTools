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

        internal const string ComponentColor = "#E08A2E";

        internal static string Composition(string name, int percent)
            => $"<color={ActualColor}>{Plain(name)} {percent}%</color>";

        /// <summary>水质行里的成分摘要：占比 | 纯度 | 评级。</summary>
        internal static string Component(string name, double percent, double purity, string grade, string gradeColor)
            => $"<color={ComponentColor}>{Plain(name)}</color> ({Number(percent)}% | {InspectionLocale.Text("纯度", "Purity")} {Number(purity)}% | <color={gradeColor}>{Plain(grade)}</color>)";

        /// <summary>最多两位小数、去掉末尾 0：99.63 / 100 / 0.17。</summary>
        internal static string Number(double value)
            => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        internal static string Water(string declared, string actual, bool matches)
        {
            string label = $"<color={(matches ? MatchColor : MismatchColor)}>{Plain(declared)}</color>";
            return matches ? label : label + $" (<color={ActualColor}>{Plain(actual)}</color>)";
        }
    }
}
