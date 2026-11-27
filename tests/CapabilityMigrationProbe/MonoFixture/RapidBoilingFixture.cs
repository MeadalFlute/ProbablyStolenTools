using System;
using System.Reflection;

public static partial class OfflineMonoFixture
{
    private static void CheckRapidBoiling(Assembly mod, Assembly game)
    {
        var rules = mod.GetType("ShiftToolbox.RapidBoilingRules", true).GetMethod("Remove", All);
        bool Remove(string id, double temp, bool evaporative, double boiling)
            => (bool)rules.Invoke(null, new object[] { id, temp, evaporative, boiling });
        // Liquid registration requires a live Unity localization database. Check the
        // metadata contract here without invoking that static constructor headlessly.
        var liquid = game.GetType("Liquid", true);
        if (liquid.GetField("boilingPoint", All)?.FieldType != typeof(double)
            || liquid.GetField("isEvaporative", All)?.FieldType != typeof(bool))
            throw new Exception("Native liquid evaporation metadata changed.");
        foreach (var entry in new[] { Tuple.Create("lipobond_agent", 55.0), Tuple.Create("alcohol", 78.37), Tuple.Create("water", 100.0) })
        {
            if (Remove(entry.Item1, entry.Item2, true, entry.Item2)
                || !Remove(entry.Item1, entry.Item2 + 0.001, true, entry.Item2))
                throw new Exception("Rapid boiling strict boundary failed: " + entry.Item1);
        }
        if (Remove("microbe", 69.999, false, 0) || !Remove("microbe", 70, false, 0)
            || Remove("dream_cap_extract", 160, true, 0))
            throw new Exception("Rapid boiling disinfection/reaction isolation failed");
        var plugin = mod.GetType("ShiftToolbox.Plugin", true);
        var runtime = mod.GetType("ShiftToolbox.RapidBoiling", true);
        var chemistry = plugin.GetField("CfgChemistry", All);
        var boilingConfig = plugin.GetField("CfgRapidBoiling", All);
        object oldChemistry = chemistry.GetValue(null), oldBoiling = boilingConfig.GetValue(null);
        var settingType = mod.GetType("ShiftToolbox.ConfigEntry`1", true).MakeGenericType(typeof(bool));
        object Setting(bool value) => settingType.GetConstructors(All)[0].Invoke(new object[] { value, null, "boiling-fixture" });
        try
        {
            for (int mask = 0; mask < 4; mask++)
            {
                chemistry.SetValue(null, Setting((mask & 1) != 0));
                boilingConfig.SetValue(null, Setting((mask & 2) != 0));
                if ((bool)runtime.GetProperty("Enabled", All).GetValue(null) != (mask == 3))
                    throw new Exception("Rapid boiling setting isolation failed: " + mask);
            }
            var admission = runtime.GetMethod("IsHeatingContainer", All);
            if ((bool)admission.Invoke(null, new object[] { null, 1, null }))
                throw new Exception("Rapid boiling admitted absent machine/slot");
        }
        finally
        {
            chemistry.SetValue(null, oldChemistry);
            boilingConfig.SetValue(null, oldBoiling);
        }
        Console.WriteLine("PASS actual game Mono rapid boiling: native metadata field contract, strict threshold rules, disinfection onset, master/child switch isolation and absent-machine exclusion; live Liquid registration/heating requires gameplay.");
    }
}
