namespace ProbablyStolenTools;

// The linked snapshot code logs through the plugin; offline tests do not load Unity.
internal static class Plugin
{
    internal static SnapshotTestLog LogSource { get; } = new();
}

internal sealed class SnapshotTestLog
{
    internal void LogWarning(string message) => Console.Error.WriteLine(message);
}
