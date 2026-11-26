using ProbablyStolenTools;

internal static class CompositionDetailsTests
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("composition shows all nonzero traces with the dominant component first", () =>
        {
            var ids = new[] { "heavy_metal", "water", "microbe", "chemical_contaminant", "caffeine" };
            var parts = new[] { 1, 999999, 0, 3, 9 };
            var before = parts.ToArray();
            var rows = CompositionDetails.Rows(ids, parts);
            check(rows.Count == 4 && rows[0].Id == "water" && rows[0].Main);
            check(rows.Skip(1).All(r => !r.Main) && rows.Any(r => r.Id == "heavy_metal" && r.Amount == 1));
            check(parts.SequenceEqual(before) && Math.Abs(rows.Sum(r => r.Percent) - 100) < 0.000001);
            check(rows.Select(r => r.Id).SequenceEqual(new[] { "water", "caffeine", "chemical_contaminant", "heavy_metal" }));
        });
        test("composition sorts raw shares descending with stable ties and independent main highlighting", () =>
        {
            var rows = CompositionDetails.Rows(new[] { "first", "main", "second", "trace" }, new[] { 9, 2, 9, 1 }, "main");
            check(rows.Select(r => r.Id).SequenceEqual(new[] { "first", "second", "main", "trace" }));
            check(rows[2].Main && !rows[0].Main);
            var pipe = new List<CompositionDetails.Row> {
                new("water", 0, 10, true), new("tiny", 0, 0.001, false),
                new("large", 0, 89.997, false), new("trace", 0, 0.002, false) };
            CompositionDetails.Sort(pipe);
            check(pipe.Select(r => r.Id).SequenceEqual(new[] { "large", "water", "trace", "tiny" }));
        });
        test("reagent details retain zero difficult impurities and omit removable substances", () =>
        {
            foreach (string reagent in new[] { "acid", "base", "lipobond_agent", "reducing_agent" })
            {
                var rows = CompositionDetails.Rows(new[] { "water", "heavy_metal", reagent, "chemical_contaminant", "plant_matter" },
                    new[] { 100, 0, 800, 0, 100 }, reagent);
                check(rows.Count == 3 && rows[0].Id == reagent && rows[0].Main && rows[0].Percent == 80);
                check(rows.Skip(1).All(row => row.Amount == 0 && !row.Main));
            }
        });
        test("reagent star is strict below 0.10 percent and includes absent heavy metals", () =>
        {
            var ids = new[] { "acid", "heavy_metal" };
            check(CompositionDetails.Star("acid", ids, new[] { 999001, 999 }));
            check(!CompositionDetails.Star("acid", ids, new[] { 999000, 1000 }));
            check(!CompositionDetails.Star("acid", ids, new[] { 998999, 1001 }));
            check(CompositionDetails.Star("acid", new[] { "acid" }, new[] { int.MaxValue }));
            check(!CompositionDetails.Star("acid", ids, new[] { 0, 0 }));
            check(!CompositionDetails.Star("caffeine", ids, new[] { 999999, 1 }));
            check(!CompositionDetails.IsReagent("stabilizing_agent"));
        });
        test("full reagent composition includes removable substances and all positive traces", () =>
        {
            foreach (string reagent in new[] { "acid", "base", "lipobond_agent", "reducing_agent" })
            {
                var ids = new[] { reagent, "water", "plant_matter", "heavy_metal", "chemical_contaminant", "microbe" };
                var parts = new[] { 990000, 9000, 998, 0, 1, 1 };
                var full = CompositionDetails.Rows(ids, parts, reagent, compactReagent: false);
                check(full.Count == 5 && full[0].Main && full[0].Id == reagent);
                check(full.Any(row => row.Id == "water") && full.Any(row => row.Id == "microbe" && row.Amount == 1));
                check(!full.Any(row => row.Id == "heavy_metal") && Math.Abs(full.Sum(row => row.Percent) - 100) < 0.000001);
                check(CompositionDetails.Rows(ids, parts, reagent).Count == 3);
            }
        });
        test("composition empty malformed and large-volume boundaries are explicit", () =>
        {
            check(CompositionDetails.Rows(new[] { "water" }, new[] { -1 }).Count == 0);
            var rows = CompositionDetails.Rows(new[] { "water", "alcohol" }, new[] { int.MaxValue, int.MaxValue });
            check(rows.Count == 2 && rows.All(row => row.Percent == 50));
            bool refused = false;
            try { CompositionDetails.Rows(new[] { "water" }, Array.Empty<int>()); } catch (ArgumentException) { refused = true; }
            check(refused);
        });
        test("composition text retains microlitre traces and invariant culture", () =>
        {
            var old = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
                check(CompositionDetails.Amount(1) == "1uL");
                check(CompositionDetails.Amount(999) == "999uL");
                check(CompositionDetails.Amount(1000) == "1mL");
                check(CompositionDetails.Amount(999999) == "999.999mL");
                check(CompositionDetails.Amount(1000000) == "1L");
                check(CompositionDetails.Amount(1000001) == "1.000001L");
                check(CompositionDetails.Percent(12.3456) == "12.35%");
                check(CompositionDetails.Percent(0.01) == "0.01%");
                check(CompositionDetails.Percent(0.0001) == "<0.01%");
                check(CompositionDetails.Percent(0) == "0.00%");
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = old; }
        });
        test("composition columns use the widest measured localized name and amount including headers", () =>
        {
            var names = new[] { "Name", "化学污染物", "梦帽菇萃取物" };
            var amounts = new[] { "Amount", "1uL", "999.999mL" };
            var shares = new[] { "Share", "<0.01%", "99.99%" };
            double Measure(string s) => s == "梦帽菇萃取物" ? 120 : s.Length * 8;
            var lines = CompositionDetails.Table(names, amounts, shares, Measure, 12);
            check(lines.Length == 3 && lines.All(line => line.Contains("<pos=132>") && line.Contains("<pos=216>")));
            check(lines[1] == "化学污染物<pos=132>1uL<pos=216><0.01%");
            var pipe = CompositionDetails.Table(names, null, shares, Measure, 12);
            check(pipe.All(line => line.Contains("<pos=132>") && !line.Contains("<pos=216>")));
            check(pipe[2] == "梦帽菇萃取物<pos=132>99.99%");
        });
        test("composition columns reject mismatched rows", () =>
        {
            bool refused = false;
            try { CompositionDetails.Table(new[] { "water" }, Array.Empty<string>(), new[] { "100%" }, s => s.Length, 1); }
            catch (ArgumentException) { refused = true; }
            check(refused);
        });
    }
}
