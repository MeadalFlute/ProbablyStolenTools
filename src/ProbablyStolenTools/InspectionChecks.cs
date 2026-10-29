using System;
using System.Collections.Generic;

namespace ProbablyStolenTools
{
    internal readonly record struct InspectionCheck(string IssueTag, string LocalizationKey,
        string Chinese, string English);

    internal static class InspectionChecks
    {
        // These are the same issue tags and labels used by the native inspection
        // and confrontation pages. They supplement, never replace, actual conditions.
        private static readonly InspectionCheck[] Cigarette =
        {
            new("CIGARETTE_LETTERING_ISSUE", "inspection_lettering_issues", "印字", "Lettering"),
            new("CIGARETTE_COLOR_ISSUE", "inspection_color_issues", "颜色", "Color"),
            new("CIGARETTE_SEAL_ISSUE", "inspection_seal_issues", "封条", "Seal"),
            new("CIGARETTE_LOGO_ISSUE", "inspection_logo_issues", "标志", "Logo"),
            new("CIGARETTE_BOX_ISSUE", "inspection_box_issues", "包装盒", "Box")
        };
        private static readonly InspectionCheck[] Stamp =
        {
            new("STAMP_LETTERING_ISSUE", "inspection_lettering_issues", "印字", "Lettering"),
            new("STAMP_LOGO_ISSUE", "inspection_logo_issues", "标志", "Logo"),
            new("STAMP_SEAL_ISSUE", "inspection_seal_issues", "封条", "Seal"),
            new("STAMP_AMOUNT_ISSUE", "inspection_value_issues", "面额", "Value"),
            new("STAMP_CHIP_ISSUE", "inspection_chip_issues", "芯片", "Chip")
        };
        private static readonly InspectionCheck[] Injector =
        {
            new("ISSUE_INDICATOR", "inspection_indicator_issues", "指示器", "Indicator"),
            new("ISSUE_LABEL", "inspection_label_issues", "标签", "Label"),
            new("ISSUE_SERIAL", "inspection_serial_issues", "序列号", "Serial number"),
            new("ISSUE_BODY", "inspection_body_issues", "外壳", "Body"),
            new("ISSUE_COLOR", "inspection_color_issues", "颜色", "Color")
        };

        internal static IReadOnlyList<InspectionCheck> For(string itemTag) => itemTag switch
        {
            "CIGARETTE_TAG" => Cigarette,
            "FOOD_STAMP_TAG" => Stamp,
            "INS_INJECTOR_TAG" => Injector,
            _ => Array.Empty<InspectionCheck>()
        };

        internal static bool? AuthenticityPass(string category, string actualCondition) => category switch
        {
            "CATEGORY_GENUINE_CIGARETTE" => IsGenuine(actualCondition, "genuineCigarette"),
            "CATEGORY_GENUINE_STAMP" => IsGenuine(actualCondition, "genuineStamp"),
            "CATEGORY_GENUINE_INJECTOR" => IsGenuine(actualCondition, "genuineInjector"),
            _ => null
        };

        private static bool? IsGenuine(string actual, string genuine)
            => string.IsNullOrEmpty(actual) ? null : actual == genuine;
    }
}
