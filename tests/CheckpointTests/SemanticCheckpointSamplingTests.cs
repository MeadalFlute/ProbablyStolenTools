using ProbablyStolenTools;

internal static class SemanticCheckpointSamplingTests
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("semantic sampling admits the first node and one later manual node only", () =>
        {
            var samples = new SemanticCheckpointSampling();
            check(samples.TryBegin(false, true, out var first) && first == "primary");
            check(!samples.TryBegin(false, true, out var ordinary) && ordinary == "none");
            check(samples.TryBegin(true, true, out var second) && second == "manual-probe");
            for (int i = 0; i < 30; i++)
                check(!samples.TryBegin(i % 2 == 0, true, out var exhausted) && exhausted == "none");
        });
        test("manual first checkpoint does not consume the later independent manual probe", () =>
        {
            var samples = new SemanticCheckpointSampling();
            check(samples.TryBegin(true, true, out var first) && first == "primary");
            check(samples.TryBegin(true, true, out var second) && second == "manual-probe");
            check(!samples.TryBegin(true, true, out _));
        });
        test("disabling additional sampling preserves the existing single-attempt behavior", () =>
        {
            var samples = new SemanticCheckpointSampling();
            check(samples.TryBegin(false, false, out var first) && first == "primary");
            for (int i = 0; i < 20; i++) check(!samples.TryBegin(true, false, out _));
            check(samples.TryBegin(true, true, out var second) && second == "manual-probe");
        });
        test("failed or removed semantic nodes never automatically free sample attempts", () =>
        {
            var samples = new SemanticCheckpointSampling();
            // No success/retention notification: both failed and evicted nodes consume their attempt.
            check(samples.TryBegin(false, true, out _));
            check(!samples.TryBegin(false, true, out _));
            check(samples.TryBegin(true, true, out _));
            check(!samples.TryBegin(true, true, out _));
        });
        test("context or file reload reset independently reopens both bounded sample attempts", () =>
        {
            var samples = new SemanticCheckpointSampling();
            for (int context = 0; context < 3; context++)
            {
                check(samples.TryBegin(false, true, out var first) && first == "primary");
                check(samples.TryBegin(true, true, out var second) && second == "manual-probe");
                check(!samples.TryBegin(true, true, out _));
                samples.Reset();
            }
        });
    }
}
