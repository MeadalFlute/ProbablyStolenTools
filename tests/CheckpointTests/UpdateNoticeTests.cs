using ProbablyStolenTools;

internal static class UpdateNoticeTests
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
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
