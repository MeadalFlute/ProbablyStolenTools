using System;

namespace ProbablyStolenTools
{
    internal static class InspectionLocale
    {
        internal static string Text(string chinese, string english)
        {
            try
            {
                string locale = LocHelper.GetCurrentLocaleCode();
                if (!string.IsNullOrEmpty(locale))
                    return locale.IndexOf("zh", StringComparison.OrdinalIgnoreCase) >= 0 ? chinese : english;
            }
            catch { }
            return chinese;
        }

        internal static string UI(string key, string chinese, string english)
        {
            try
            {
                string value = LocHelper.GetLocalizedUI(key, Array.Empty<Il2CppSystem.Object>());
                if (!string.IsNullOrWhiteSpace(value) && value != key
                    && !LocHelper.TryParseMissingTranslation(value, out _, out _))
                    return InspectionText.Plain(value);
            }
            catch { }
            return Text(chinese, english);
        }

        internal static string Verdict(bool passed)
            => passed ? Text("通过", "Passed") : Text("未通过", "Failed");
    }
}
