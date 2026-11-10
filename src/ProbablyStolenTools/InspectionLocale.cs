using System;
#if MONO
using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;
#endif

namespace ProbablyStolenTools
{
    internal static class InspectionLocale
    {
        private static string _reportedCode;
        internal static void LogState()
        {
            string code = Code;
            if (string.IsNullOrEmpty(code) || code == _reportedCode) return;
            _reportedCode = code;
            EventLog.Write($"[Locale] code={code} annotations={SupportsAnnotations}; native locale/tables are not modified");
        }

        internal static string Code
        {
            get
            {
                try
                {
#if MONO
                    var operation = LocalizationSettings.SelectedLocaleAsync;
                    if (!operation.IsValid() || !operation.IsDone || operation.Status != AsyncOperationStatus.Succeeded)
                        return null;
                    return operation.Result == null ? null : operation.Result.Identifier.Code;
#else
                    return LocHelper.GetCurrentLocaleCode();
#endif
                }
                catch { return null; }
            }
        }

        internal static bool SupportsAnnotations => LocalePolicy.SupportsAnnotations(Code);
        internal static string Text(string chinese, string english) => LocalePolicy.Text(Code, chinese, english);

        internal static string UI(string key, string chinese, string english)
        {
            try
            {
#if MONO
                if (string.IsNullOrWhiteSpace(Code)) return Text(chinese, english);
                var table = LocalizationSettings.StringDatabase.GetTableAsync("UI");
                if (!table.IsValid() || !table.IsDone || table.Status != AsyncOperationStatus.Succeeded
                    || table.Result == null || table.Result.GetEntry(key) == null) return Text(chinese, english);
#endif
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
