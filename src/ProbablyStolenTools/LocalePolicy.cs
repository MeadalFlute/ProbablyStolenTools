using System;

namespace ProbablyStolenTools
{
    internal static class LocalePolicy
    {
        internal static bool IsLanguage(string code, string language)
            => string.Equals(code, language, StringComparison.OrdinalIgnoreCase)
                || code != null && (code.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase)
                    || code.StartsWith(language + "_", StringComparison.OrdinalIgnoreCase));

        internal static string Text(string code, string chinese, string english)
            => IsLanguage(code, "zh") ? chinese : english;
    }
}
