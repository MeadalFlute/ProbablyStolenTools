using ProbablyStolenTools;

internal static class CheckpointItemInteractionTests
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("single and group drags block snapshots even on the release frame", () =>
        {
            foreach (bool held in new[] { false, true })
            {
                check(CheckpointItemInteraction.IsBusy(true, false, false, held));
                check(CheckpointItemInteraction.IsBusy(false, true, true, held));
                check(CheckpointItemInteraction.IsBusy(false, true, false, held));
            }
        });
        test("pending band and pending group drag block snapshots before ghosts start", () =>
        {
            check(CheckpointItemInteraction.IsBusy(false, false, true, true));
        });
        test("released static selection does not prevent automatic checkpoints", () =>
        {
            check(!CheckpointItemInteraction.IsBusy(false, false, true, false));
        });
        test("ordinary mouse clicks without item gestures do not block snapshots", () =>
        {
            check(!CheckpointItemInteraction.IsBusy(false, false, false, true));
            check(!CheckpointItemInteraction.IsBusy(false, false, false, false));
        });
    }
}
