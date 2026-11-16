using System;
using System.Reflection;

public static partial class OfflineMonoFixture
{
    private static void CheckAutomaticCheckpointGates(Assembly mod)
    {
        var plugin = mod.GetType("ProbablyStolenTools.Plugin", true);
        var checkpoint = mod.GetType("ProbablyStolenTools.CustomerCheckpoint", true);
        var config = mod.GetType("ProbablyStolenTools.ConfigEntry`1", true).MakeGenericType(typeof(bool));
        var constructor = config.GetConstructors(All)[0];
        var enabled = plugin.GetField("CfgCustomerCheckpoint", All);
        var automatic = plugin.GetField("CfgAutoCheckpoints", All);
        var patches = checkpoint.GetField("PatchesReady", All);
        object oldEnabled = enabled.GetValue(null), oldAutomatic = automatic.GetValue(null), oldPatches = patches.GetValue(null);
        object Entry(bool value) => constructor.Invoke(new object[] { value, null, "offline" });
        bool Auto() => (bool)checkpoint.GetProperty("AutoCapture", All).GetValue(null);
        try
        {
            foreach (bool ready in new[] { false, true })
            foreach (bool on in new[] { false, true })
            foreach (bool auto in new[] { false, true })
            {
                patches.SetValue(null, ready);
                enabled.SetValue(null, Entry(on));
                automatic.SetValue(null, Entry(auto));
                if (Auto() != (ready && on && auto)) throw new Exception("Automatic capture gate mismatch.");
            }
            patches.SetValue(null, true);
            enabled.SetValue(null, Entry(true));
            foreach (var setting in new[] { null, Entry(false) })
            {
                automatic.SetValue(null, setting);
                if (Auto() || !(bool)checkpoint.GetProperty("Enabled", All).GetValue(null))
                    throw new Exception("Disabling automation disabled manual checkpoint support.");
                foreach (string method in new[] { "Arrival", "TryCapture", "TryCaptureDayStart", "TryCaptureClosed", "TryCaptureReturn" })
                    checkpoint.GetMethod(method, All).Invoke(null, null);
                // A null store is intentional: every automatic mode must return before accessing the graph.
                foreach (var mode in new[] { new[] { false, false, false }, new[] { true, false, false },
                    new[] { false, true, false }, new[] { false, false, true } })
                    checkpoint.GetMethod("Capture", All).Invoke(null,
                        new object[] { null, mode[0], false, mode[1], mode[2] });
            }
            Console.WriteLine("PASS actual game Mono: automatic capture gates (8 combinations), missing/false config skips all automatic paths; manual backend remains enabled.");
        }
        finally
        {
            enabled.SetValue(null, oldEnabled);
            automatic.SetValue(null, oldAutomatic);
            patches.SetValue(null, oldPatches);
        }
    }
}
