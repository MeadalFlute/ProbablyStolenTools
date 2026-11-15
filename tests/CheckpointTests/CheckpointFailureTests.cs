using ProbablyStolenTools;

internal static class CheckpointFailureTests
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("snapshot limit guidance uses actual effective caps and explicit XML keys", () =>
        {
            foreach (var (kind, key, expected) in new[] {
                ("object", "Snapshot.MaxObjects", "300000"),
                ("slot", "Snapshot.MaxSlots", "4500000"),
                ("byte", "Snapshot.MaxBytesMB", "384") })
            {
                var error = new SnapshotLimitException(kind, 100, 200, 300, 200000, 3000000,
                    256L * 1048576, 10, "source", 200001);
                foreach (bool zh in new[] { true, false })
                {
                    string message = CheckpointFailureMessage.Describe(error, zh);
                    check(message.Contains(key) && message.Contains($"value=\"{expected}\"")
                        && message.Contains("PSTools.settings.xml") && !message.Contains("source"));
                }
            }
            var maximum = new SnapshotLimitException("object", 100, 200, 300, 1000000, 3000000, 256L * 1048576, 10, "source");
            check(!CheckpointFailureMessage.Describe(maximum, true).Contains("<Entry"));
        });
        test("wrapped static layout failures show cause and generator recovery instead of generic log advice", () =>
        {
            foreach (var cause in new Exception[] { new TypeLoadException("bad type"),
                new InvalidOperationException("Snapshot layout build does not match loaded module: Assembly-CSharp.dll"),
                new InvalidOperationException("Uncovered static snapshot layout: Something"),
                new FileNotFoundException("missing", "ProbablyStolenTools.StaticLayouts.dll") })
            foreach (bool zh in new[] { true, false })
            {
                string message = CheckpointFailureMessage.Describe(new TypeInitializationException("registry", cause), zh);
                check(message.Contains(cause.GetType().FullName) && message.Contains("GitHub")
                    && message.Contains(zh ? "生成" : "generator"));
            }
        });
        test("unexpected capture failures preserve concrete exception and actionable report information", () =>
        {
            string unexpected = CheckpointFailureMessage.Describe(new Exception("oops\n<value>"), true);
            check(unexpected.Contains("System.Exception: oops <value>") && unexpected.Contains("bug report"));
            check(CheckpointFailureMessage.Describe(new IOException("disk full"), false).Contains("disk space"));
            check(CheckpointFailureMessage.Describe(null, false).Contains("Unknown"));
        });
    }
}
