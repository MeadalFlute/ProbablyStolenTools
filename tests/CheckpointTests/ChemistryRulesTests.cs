using ProbablyStolenTools;

internal static class ChemistryRulesTests
{
    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        var ids = new[] { "water", "alcohol", "plant_matter", "dream_cap_mush", "dream_cap_extract",
            "nightmare_cap_mush", "nightmare_extract", "coffee_mush", "caffeine", "mineral" };
        test("blend excludes carriers and sub-one-percent impurities", () =>
        {
            var parts = new[] { 90000, 0, 1000, 9000, 0, 0, 0, 0, 0, 999 };
            check(ChemistryRules.Signature(ids, parts) == "dream_cap_extract", "primary signature");
        });
        test("blend keeps exactly one percent and normalizes three extracts", () =>
        {
            check(ChemistryRules.Signature(new[] { "water", "coffee_mush" }, new[] { 99, 1 }) == "caffeine", "one-percent inclusive");
            check(ChemistryRules.Product("nightmare_cap_mush") == "nightmare_extract", "native nightmare id");
            check(ChemistryRules.Signature(new[] { "coffee_mush", "caffeine" }, new[] { 50, 50 }) == "caffeine", "deduplicated");
        });
        test("blend signature independent of registry ordering", () =>
        {
            check(ChemistryRules.Signature(new[] { "b", "a" }, new[] { 50, 50 })
                == ChemistryRules.Signature(new[] { "a", "b" }, new[] { 50, 50 }), "stable");
        });
        test("blend rounds loss per component away from zero", () =>
        {
            check(ChemistryRules.BlendVolume(new[] { 1, 1, 3 }, 50) == 4, "per-part rounding");
            check(ChemistryRules.BlendVolume(new[] { 100 }, 100) == 0, "full loss");
        });
        test("one unit alcohol extracts all mush without consumption", () =>
        {
            var source = new[] { 100, 1, 10, 20, 5, 30, 0, 40, 0, 0 };
            var result = ChemistryRules.Extract(ids, source);
            check(result != null && result[1] == 1 && result[2] == 100, "carrier and plant matter");
            check(result[3] == 0 && result[4] == 25 && result[5] == 0 && result[6] == 30
                && result[7] == 0 && result[8] == 40, "all outputs");
            check(source[3] == 20 && source[2] == 10, "no source mutation");
        });
        test("extraction requires alcohol and complete output registry", () =>
        {
            check(ChemistryRules.Extract(ids, new[] { 0, 0, 0, 10, 0, 0, 0, 0, 0, 0 }) == null, "no alcohol");
            check(ChemistryRules.Extract(new[] { "alcohol", "plant_matter", "coffee_mush" }, new[] { 1, 0, 10 }) == null, "missing output");
        });
        test("extraction refuses total integer overflow without mutating", () =>
        {
            var source = new[] { "alcohol", "plant_matter", "coffee_mush", "caffeine" };
            check(ChemistryRules.Extract(source, new[] { 1, int.MaxValue - 5, 5, 0 }) == null, "sum overflow");
            check(ChemistryRules.Extract(source, new[] { 1, 0, 100000, 0 })[3] == 100000, "no capacity constraint");
        });
        test("purifier preserves total, cap, priorities and source", () =>
        {
            var source = new[] { 500000, 200000, 200000, 100000 };
            var result = FacilityProjection.Purify(source, 0, 9901, 250000, new[] { int.MaxValue, 1, 0, int.MaxValue });
            check(result.SequenceEqual(new[] { 750000, 150000, 0, 100000 }), "priority and ineligible");
            check(FacilityProjection.Total(result) == 1000000 && source[0] == 500000, "volume and purity");
            var capped = FacilityProjection.Purify(new[] { 990000, 10000 }, 0, 9901, 50000, new[] { int.MaxValue, 0 });
            check(capped[0] == 990100 && capped[1] == 9900, "purity floor");
        });
        test("purifier proportional group remainder and absent water", () =>
        {
            var result = FacilityProjection.Purify(new[] { 100, 3, 4 }, 0, 10000, 5, new[] { int.MaxValue, 0, 0 });
            check(result.SequenceEqual(new[] { 105, 1, 1 }), "last receives remainder");
            check(FacilityProjection.Purify(new[] { 0, 30 }, 0, 10000, 20, new[] { int.MaxValue, 0 })[1] == 30, "no water");
        });
        test("evaporator splits only volatile parts and handles empty input", () =>
        {
            var result = FacilityProjection.Evaporate(new[] { 3, 4, 100 }, new[] { true, true, false }, 5);
            check(result.SequenceEqual(new[] { 2, 3, 0 }), "proportional");
            check(FacilityProjection.Evaporate(new[] { 0 }, new[] { true }, 300000)[0] == 0, "empty");
        });
        test("plant and fish removal respect native water-relative floors", () =>
        {
            var parts = new[] { 100000, 5000 };
            check(FacilityProjection.Remove(parts, 1, 0, 10000, 100) == 4000 && parts[1] == 1000, "floor");
            check(FacilityProjection.Remove(parts, 1, 0, 10000, 100) == 0, "floor not removed twice");
        });
        test("hydroponic water consumption uses largest remainder and exact volume", () =>
        {
            var parts = new[] { 1001, 1000, 1000 };
            var after = FacilityProjection.Consume(parts, 1);
            check(after.SequenceEqual(new[] { 667, 667, 667 }), "native largest remainder order");
            check(FacilityProjection.Total(after) == 2001 && parts[0] == 1001, "exact volume and no mutation");
            check(FacilityProjection.Total(FacilityProjection.Consume(parts, 100)) == 0, "overdraw");
        });
        test("hydroponic health thresholds match native contamination arithmetic", () =>
        {
            check(FacilityProjection.HealthDamage(10, 1, 1) == 0
                && FacilityProjection.HealthDamage(11, 1, 1) == 15
                && FacilityProjection.HealthDamage(201, 1, 1) == 640, "thresholds");
        });
    }
}
