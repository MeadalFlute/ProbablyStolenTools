using ProbablyStolenTools;

internal static class RapidBoilingTests
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("rapid boiling preserves native strict evaporation boundaries", () =>
        {
            foreach (var entry in new[] { ("lipobond_agent", 55.0), ("alcohol", 78.37), ("water", 100.0) })
            {
                check(!RapidBoilingRules.Remove(entry.Item1, entry.Item2 - 0.001, true, entry.Item2));
                check(!RapidBoilingRules.Remove(entry.Item1, entry.Item2, true, entry.Item2));
                check(RapidBoilingRules.Remove(entry.Item1, entry.Item2 + 0.001, true, entry.Item2));
                check(!RapidBoilingRules.Remove(entry.Item1, 160, false, entry.Item2));
            }
        });
        test("rapid boiling completes disinfection from native 70 degree onset", () =>
        {
            check(!RapidBoilingRules.Remove("microbe", 69.999, false, 0));
            check(RapidBoilingRules.Remove("microbe", 70, false, 0));
            check(RapidBoilingRules.Remove("microbe", 100, false, 0));
        });
        test("rapid boiling excludes every other reaction and invalid temperature", () =>
        {
            foreach (string id in new[] { "dream_cap_extract", "nightmare_extract", "coffee_mush", "organic_waste",
                "dream_cap_mush", "nightmare_cap_mush", "caffeine", "fanta", "methylol", "chlorine", "", "custom_liquid" })
                check(!RapidBoilingRules.Remove(id, 160, true, 0));
            foreach (double degrees in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                foreach (string id in new[] { "water", "alcohol", "lipobond_agent", "microbe" })
                    check(!RapidBoilingRules.Remove(id, degrees, true, 0));
            check(!RapidBoilingRules.Remove("water", 160, true, double.NaN));
        });
        test("rapid boiling reads current native evaporation metadata", () =>
        {
            check(!RapidBoilingRules.Remove("water", 110, true, 120));
            check(RapidBoilingRules.Remove("water", 121, true, 120));
        });
    }
}
