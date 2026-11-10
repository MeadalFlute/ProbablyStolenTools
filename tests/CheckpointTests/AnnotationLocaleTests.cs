using ProbablyStolenTools;

internal static class AnnotationLocaleTests
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("native water keys and matching tiers preserve every original purity boundary", () =>
        {
            var cases = new[] {
                (10000, 0, "pure_water"), (9990, 0, "pure_water"), (9989, 1, "high_quality_water"),
                (9900, 1, "high_quality_water"), (9899, 2, "basewater"), (9800, 2, "basewater"),
                (9799, 3, "ghostwater"), (9600, 3, "ghostwater"), (9599, 4, "rustwater"),
                (9000, 4, "rustwater"), (8999, 5, "gutterflow"), (3500, 5, "gutterflow")
            };
            foreach (var (purity, tier, key) in cases)
            {
                check(AnnotationLabels.WaterTier(purity) == tier);
                check(AnnotationLabels.Water(purity).Key == "mech_condition_" + key);
            }
            foreach (int purity in new[] { 3499, 0, -1 })
                check(AnnotationLabels.WaterTier(purity) == 5 && AnnotationLabels.Water(purity).Key == "mech_unknown_water");
        });

        test("chemical and wine grade language keys retain existing score thresholds", () =>
        {
            var chemicals = new[] { (100d, "lab_pure"), (99d, "lab_pure"), (98.99, "pharma_grade"),
                (90d, "pharma_grade"), (89.99, "near_pharma"), (80d, "near_pharma"),
                (79.99, "street_standard"), (60d, "street_standard"), (59.99, "cut_product"),
                (45d, "cut_product"), (44.99, "junk_grade"), (30d, "junk_grade"), (29.99, "starch_grade") };
            foreach (var (purity, key) in chemicals)
                check(AnnotationLabels.ChemicalGrade(purity).Key == "mech_condition_" + key);
            foreach (var (score, key) in new[] { (9, "master_vintage"), (6, "master_vintage"), (5, "reserve"),
                (4, "production"), (3, "production"), (2, "homebrew"), (1, "bucket_brew"), (0, "undrinkable"), (-2, "undrinkable") })
                check(AnnotationLabels.WineGrade(score).Key == "mech_condition_wine_" + key);
        });

        test("annotation keys use native translations in every locale including non Chinese English locales", () =>
        {
            var label = AnnotationLabels.WineGrade(6);
            foreach (string code in new[] { "zh", "en", "ru", "de", "ja", "fr", "es", "pl", "ptBR", "ko", "it" })
            {
                int calls = 0;
                string value = label.Resolve(code, (table, key) => {
                    calls++;
                    check(table == "Mechanic" && key == label.Key);
                    return code + "-native";
                });
                check(calls == 1 && value == code + "-native");
            }
            string current = "first";
            check(label.Resolve("ru", (_, _) => current) == "first");
            current = "second";
            check(label.Resolve("ru", (_, _) => current) == "second");
        });

        test("missing blank failed and unready localization safely falls back without querying at startup", () =>
        {
            var label = AnnotationLabels.ChemicalGrade(99);
            foreach (string code in new[] { null, "", " " })
                check(label.Resolve(code, (_, _) => throw new Exception("must not query")) == label.English);
            foreach (string code in new[] { "en", "ru", "unknown" })
            {
                foreach (string value in new[] { null, "", " ", label.Key })
                    check(label.Resolve(code, (_, _) => value) == label.English);
                check(label.Resolve(code, (_, _) => throw new InvalidOperationException("invalid handle")) == label.English);
            }
            check(label.Resolve("zh-CN", (_, _) => null) == label.Chinese);
        });

        test("liquid localization maps native irregular keys and preserves custom liquid fallback names", () =>
        {
            foreach (var (id, key) in new[] { ("microbe", "microbes"), ("fat", "fat_oil"),
                ("coffee_mush", "coffee_bean_mush"), ("contraceptive", "contraceptive_hormones"),
                ("methylol", "n_methylol_base"), ("debris", "insoluble_debris"), ("caffeine", "caffeine") })
            {
                var label = AnnotationLabels.Liquid(id, "old-language");
                check(label.Key == "liquid_" + key && label.Table == "Mechanic");
                check(label.Resolve("ru", (_, _) => "localized") == "localized");
                check(label.Resolve("ru", (_, _) => null) == label.English && label.English != "old-language");
            }
            check(AnnotationLabels.Liquid("third_party", "Custom liquid").Resolve("ru", (_, _) => null) == "Custom liquid");
            check(AnnotationLabels.MedicalAlcohol.Table == "Item" && AnnotationLabels.MedicalAlcohol.Key == "item_rubbing_alcohol_name");
        });
    }
}
