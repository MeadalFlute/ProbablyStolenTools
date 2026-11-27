using ProbablyStolenTools;

internal static class SettingsSearchTests
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("settings search clears to all rows and ignores case and punctuation", () =>
        {
            check(SettingsSearch.Matches(null, "abc"));
            check(SettingsSearch.Matches(" ", "abc"));
            check(SettingsSearch.Matches("WINDOW pin", "Window Pinning"));
            check(SettingsSearch.Matches("shift+middle", "Shift + middle-click"));
        });
        test("settings search supports Chinese and English noncontiguous words", () =>
        {
            check(SettingsSearch.Matches("自动名", "容器自动改名", "Container auto naming"));
            check(SettingsSearch.Matches("auto nam", "Container auto naming", "自动改名"));
            check(SettingsSearch.Matches("chem extr", "化学辅助 Chemistry", "Smart extrusion"));
            check(!SettingsSearch.Matches("库存排列", "窗口置顶 Window pinning"));
        });
        test("settings search requires every query token and accepts configuration identifiers", () =>
        {
            check(SettingsSearch.Matches("win pin", "Windows.PinGesture"));
            check(SettingsSearch.Matches("wpg", "Windows.PinGesture"));
            check(!SettingsSearch.Matches("win absent", "Windows.PinGesture"));
            check(!SettingsSearch.Matches("zzz", "窗口", null));
            check(SettingsSearch.Matches("++", "任何设置"));
        });
    }
}
