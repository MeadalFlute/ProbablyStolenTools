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
            EventLog.Write($"[Locale] code={code} annotations={AnnotationsReady} nativeKeys=true fallback=en; native locale/tables are not modified");
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

        internal static bool AnnotationsReady => !string.IsNullOrWhiteSpace(Code);
        internal static string Text(string chinese, string english) => LocalePolicy.Text(Code, chinese, english);

        internal static string UI(string key, string chinese, string english)
            => Label(new NativeLabel("UI", key, chinese, english));

        internal static string Label(NativeLabel label)
            => InspectionText.Plain(label.Resolve(Code, Read));

        private static string Read(string tableName, string key)
        {
#if MONO
            var table = LocalizationSettings.StringDatabase.GetTableAsync(tableName);
            if (!table.IsValid() || !table.IsDone || table.Status != AsyncOperationStatus.Succeeded)
                return null;
            // Format only an already-loaded entry. Never synchronously wait for localization.
            return table.Result?.GetEntry(key)?.GetLocalizedString();
#else
            return LocHelper.GetLocalized(tableName, key);
#endif
        }

        internal static string Verdict(bool passed)
            => passed ? Text("通过", "Passed") : Text("未通过", "Failed");
    }
}
