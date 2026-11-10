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
        private static readonly AnnotationTextCache Labels = new();
        internal static void LogState()
        {
            string code = Code;
            if (string.IsNullOrEmpty(code) || code == _reportedCode) return;
            _reportedCode = code;
            EventLog.Write($"[Locale] code={code} annotations={AnnotationsReady} nativeKeys=true fallback=en-table-then-en-text nonblocking=true; native locale/tables are not modified");
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
            => InspectionText.Plain(Labels.Resolve(Code, label, Read));

        private static NativeTranslation Read(string code, string tableName, string key)
        {
#if MONO
            var locale = LocalizationSettings.AvailableLocales.GetLocale(code);
            if (locale == null) return default;
            var table = LocalizationSettings.StringDatabase.GetTableAsync(tableName, locale);
            if (!table.IsValid() || !table.IsDone || table.Status != AsyncOperationStatus.Succeeded)
                return default;
            // StringTableEntry already applies SmartFormat. Use the source table's locale for grammar.
            return new NativeTranslation(true, table.Result?.GetEntry(key)?.GetLocalizedString(
                locale.Formatter, null, locale as UnityEngine.Localization.Pseudo.PseudoLocale));
#else
            return new NativeTranslation(true, LocHelper.GetLocalized(tableName, key));
#endif
        }

        internal static string Verdict(bool passed)
            => passed ? Text("通过", "Passed") : Text("未通过", "Failed");
    }
}
