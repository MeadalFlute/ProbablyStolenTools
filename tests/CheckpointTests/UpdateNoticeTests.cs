using ProbablyStolenTools;

internal static class UpdateNoticeTests
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("replacement announcement has an independent acknowledgement and consumes once", () =>
        {
            var schedule = new UpdateNoticeSchedule();
            const string notice = "0.9.9" + UpdateNoticeContent.Revision;
            const string previous = "0.9.9:20261126-chemistry-compat-r3";
            check(!schedule.Due(notice, previous, true, 1));
            check(schedule.Due(notice, previous, true, 1.5f));
            schedule.Shown(notice);
            check(!schedule.Due(notice, "0.9.7", true, 9));
            check(new UpdateNoticeSchedule().Acknowledged(notice, notice));
        });
        test("current bilingual update has nine compact sections including a future split warning", () =>
        {
            foreach (string text in new[] { UpdateNoticeContent.DetailsChinese, UpdateNoticeContent.DetailsEnglish })
            {
                var sections = UpdateNoticeContent.Sections(text);
                check(sections.Length == 9 && sections.All(section => !section.Contains("\n\n")));
                check(text.Contains("70%") && text.Contains("Proper Signboards") && text.Contains("*") && text.Contains("1.5%"));
                check(!text.Contains("Vanilla Brewing Expansion") && !text.Contains("磁吸窗口"));
            }
            check(UpdateNoticeContent.SummaryChinese.Contains("智能挤出") && UpdateNoticeContent.SummaryEnglish.Contains("Smart extrusion"));
            check(UpdateNoticeContent.DetailsChinese.Contains("本次更新尚未拆分")
                && UpdateNoticeContent.DetailsEnglish.Contains("This update does not split"));
        });
        test("notice progress fills and clamps outside its eight-second lifetime", () =>
        {
            check(UpdateNoticeContent.Duration == 8);
            check(UpdateNoticeContent.Progress(2, 10, UpdateNoticeContent.Duration) == 0);
            check(UpdateNoticeContent.Progress(6, 10, UpdateNoticeContent.Duration) == 0.5f);
            check(UpdateNoticeContent.Progress(11, 10, UpdateNoticeContent.Duration) == 1);
            check(UpdateNoticeContent.Progress(0, 10, UpdateNoticeContent.Duration) == 0);
            check(UpdateNoticeContent.Progress(float.NaN, 10, UpdateNoticeContent.Duration) == 0);
            check(UpdateNoticeContent.Progress(0, 8, 0) == 0);
        });
        test("notice creation does not acknowledge and interruption restarts the visible interval", () =>
        {
            var schedule = new UpdateNoticeSchedule();
            check(!schedule.ConfirmVisible(true, 1));
            check(!schedule.ConfirmVisible(true, 2.9f));
            check(!schedule.Acknowledged("r4", "r3"));
            check(!schedule.ConfirmVisible(false, 3));
            check(!schedule.ConfirmVisible(true, 4));
            check(!schedule.ConfirmVisible(true, 5.99f));
            check(schedule.ConfirmVisible(true, 6));
            schedule.Shown("r4");
            check(schedule.Acknowledged("r4", "r3"));
        });
        test("black screen and report dismissal restart readiness before an interrupted notice retries", () =>
        {
            var schedule = new UpdateNoticeSchedule();
            check(!schedule.Due("r4", "r3", false, 0));
            check(!schedule.Due("r4", "r3", true, 10));
            check(!schedule.Due("r4", "r3", false, 10.25f));
            check(!schedule.Due("r4", "r3", true, 20));
            check(schedule.Due("r4", "r3", true, 20.5f));
            check(!schedule.ConfirmVisible(true, 20.5f));
            check(!schedule.ConfirmVisible(false, 21));
            check(!schedule.Due("r4", "r3", false, 21));
            check(!schedule.Due("r4", "r3", true, 30));
            check(schedule.Due("r4", "r3", true, 30.5f));
        });
        test("acknowledged notices skip readiness evaluation without losing session-only consumption", () =>
        {
            var schedule = new UpdateNoticeSchedule();
            int queries = 0;
            bool Ready() { queries++; return true; }
            bool Tick(string version, string persisted, float now) => !schedule.Acknowledged(version, persisted)
                && schedule.Due(version, persisted, Ready(), now);
            check(!Tick("v1", "v1", 0) && queries == 0);
            schedule.Shown("v1");
            check(!Tick("v1", "", 1) && queries == 0);
            check(!Tick("", "", 2) && queries == 0);
            check(!Tick("v2", "v1", 3) && queries == 1);
            check(Tick("v2", "v1", 3.5f) && queries == 2);
        });
        test("update details remove blank paragraphs and separate divider lines", () =>
        {
            var sections = UpdateNoticeContent.Sections("Title\r\n\r\nFirst paragraph\r\n\r\nSecond paragraph\r\n\r\n---\r\n\r\nNext section");
            check(sections.Length == 2);
            check(sections[0] == "Title\nFirst paragraph\nSecond paragraph");
            check(sections[1] == "Next section");
        });
        test("update details retain inline dashes without empty sections", () =>
        {
            var sections = UpdateNoticeContent.Sections("---\n\nText --- inline\n---\n---\n\n");
            check(sections.Length == 1 && sections[0] == "Text --- inline");
        });
        test("update notice waits for continuously ready gameplay and reports to close", () =>
        {
            var schedule = new UpdateNoticeSchedule();
            check(!schedule.Due("v1", "", false, 0));
            check(!schedule.Due("v1", "", true, 2));
            check(!schedule.Due("v1", "", false, 2.4f));
            check(!schedule.Due("v1", "", true, 5));
            check(!schedule.Due("v1", "", true, 5.49f));
            check(schedule.Due("v1", "", true, 5.5f));
        });
        test("display consumes update once without any click and IO failure cannot repeat it in session", () =>
        {
            var schedule = new UpdateNoticeSchedule();
            schedule.Shown("v1");
            check(!schedule.Due("v1", "", true, 10));
            check(!schedule.Due("v1", "", false, 20));
            check(!schedule.Due("v1", "", true, 30));
        });
        test("persisted update version suppresses restarts and a later version becomes eligible", () =>
        {
            var schedule = new UpdateNoticeSchedule();
            check(!schedule.Due("v1", "v1", true, 0));
            check(!schedule.Due("v2", "v1", true, 2));
            check(schedule.Due("v2", "v1", true, 2.5f));
            schedule.Shown("v2");
            check(!schedule.Due("v2", "v2", true, 8));
            check(!schedule.Due("", "", true, 10));
        });
    }
}
