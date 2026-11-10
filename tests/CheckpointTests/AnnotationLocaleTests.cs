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
                (9000, 4, "rustwater"), (8999, 5, "gutterflow"), (3500, 5, "gutterflow"),
                (3499, 5, "gutterflow"), (0, 5, "gutterflow")
            };
            foreach (var (purity, tier, key) in cases)
            {
                check(AnnotationLabels.WaterTier(purity) == tier);
                check(AnnotationLabels.Water(purity).Key == "mech_condition_" + key);
            }
            foreach (int purity in new[] { -1, int.MinValue, 10001, int.MaxValue })
                check(AnnotationLabels.Water(purity).Key == "mech_unknown_water");
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
            check(label.Resolve("zh-CN", (_, _) => null) == label.English);
        });

        test("annotation fallback prefers the English table before English literal text", () =>
        {
            var label = AnnotationLabels.Water(0);
            foreach (string code in new[] { "ru", "zh", "en" })
            {
                check(label.Resolve(code, (_, _) => null, (_, _) => "English table") == "English table");
                check(label.Resolve(code, (_, _) => "native", (_, _) => throw new Exception()) == "native");
                check(label.Resolve(code, (_, _) => throw new Exception(), (_, _) => "English table") == "English table");
                check(label.Resolve(code, (_, _) => null, (_, _) => throw new Exception()) == label.English);
            }
        });
        test("annotation cache isolates locale table and key and clears on language changes", () =>
        {
            var cache = new AnnotationTextCache();
            int calls = 0;
            NativeTranslation Read(string code, string table, string key)
            { calls++; return new(true, code + table + key); }
            var a = AnnotationLabels.Water(0);
            var b = a with { Table = "Item" };
            check(cache.Resolve("ru", a, Read) == "ru" + a.Table + a.Key);
            int before = calls;
            for (int i = 0; i < 100; i++) check(cache.Resolve("ru", a, Read) == "ru" + a.Table + a.Key);
            check(calls == before);
            cache.Resolve("ru", b, Read); check(calls == before + 1);
            cache.Resolve("ja", a, Read); cache.Resolve("ru", a, Read);
            check(calls == before + 3);
            cache.Clear(); cache.Resolve("ru", a, Read); check(calls == before + 4);
        });
        test("annotation cache retries loading failed and missing entries without pinning transient fallbacks", () =>
        {
            var cache = new AnnotationTextCache();
            var label = AnnotationLabels.WineGrade(6);
            bool ready = false;
            int calls = 0;
            NativeTranslation Read(string code, string table, string key)
            {
                calls++;
                return code == "en" ? new(true, "English grammar") : new(ready, ready ? "native grammar" : null);
            }
            check(cache.Resolve("ru", label, Read) == "English grammar");
            ready = true;
            check(cache.Resolve("ru", label, Read) == "native grammar");
            int before = calls;
            check(cache.Resolve("ru", label, Read) == "native grammar" && calls == before);
            cache.Clear();
            check(cache.Resolve("ru", label, (_, _, _) => throw new Exception()) == label.English);
            check(cache.Resolve("ru", label, Read) == "native grammar");
            cache.Clear();
            check(cache.Resolve(null, label, (_, _, _) => throw new Exception()) == label.English);
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
