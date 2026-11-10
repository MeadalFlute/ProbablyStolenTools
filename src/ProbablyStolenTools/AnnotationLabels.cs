using System;
using System.Collections.Generic;

namespace ProbablyStolenTools
{
    internal readonly record struct NativeLabel(string Table, string Key, string Chinese, string English)
    {
        internal string Resolve(string code, Func<string, string, string> read,
            Func<string, string, string> readEnglish = null)
        {
            if (string.IsNullOrWhiteSpace(code)) return English;
            try
            {
                string value = read(Table, Key);
                if (Valid(value, Key)) return value;
            }
            catch { }
            try
            {
                string value = readEnglish?.Invoke(Table, Key);
                if (Valid(value, Key)) return value;
            }
            catch { }
            return English;
        }

        internal static bool Valid(string value, string key) => !string.IsNullOrWhiteSpace(value) && value != key;
    }

    internal readonly record struct NativeTranslation(bool Ready, string Text);

    internal sealed class AnnotationTextCache
    {
        private const int Limit = 2048;
        private readonly Dictionary<(string Locale, string Table, string Key), string> _entries = new();
        private string _locale;

        internal string Resolve(string code, NativeLabel label, Func<string, string, string, NativeTranslation> read)
        {
            if (_locale != code) { Clear(); _locale = code; }
            if (string.IsNullOrWhiteSpace(code)) return label.English;
            var key = (code, label.Table, label.Key);
            if (_entries.TryGetValue(key, out var cached)) return cached;
            NativeTranslation current = default, english = default;
            string value = label.Resolve(code,
                (table, entry) => (current = Read(code, table, entry, read)).Text,
                (table, entry) => (english = Read("en", table, entry, read)).Text);
            // Never pin a temporary fallback while the requested table is still loading.
            if (current.Ready && (NativeLabel.Valid(current.Text, label.Key)
                || english.Ready && NativeLabel.Valid(english.Text, label.Key)))
                Store(key, value);
            return value;
        }

        private NativeTranslation Read(string code, string table, string key,
            Func<string, string, string, NativeTranslation> read)
        {
            var cacheKey = (code, table, key);
            if (_entries.TryGetValue(cacheKey, out var cached)) return new NativeTranslation(true, cached);
            try
            {
                var result = read(code, table, key);
                if (result.Ready && NativeLabel.Valid(result.Text, key)) Store(cacheKey, result.Text);
                return result;
            }
            catch { return default; }
        }

        private void Store((string Locale, string Table, string Key) key, string value)
        {
            if (_entries.Count < Limit) _entries[key] = value;
        }

        internal void Clear() { _entries.Clear(); _locale = null; }
    }

    internal static class AnnotationLabels
    {
        private static NativeLabel Mechanic(string key, string chinese, string english)
            => new("Mechanic", key, chinese, english);

        internal static NativeLabel WaterHeading => Mechanic("item_feature_category_water_purity", "水质", "Water quality");
        internal static NativeLabel PurityHeading => Mechanic("item_feature_category_chemical_purity", "纯度", "Purity");
        internal static NativeLabel Contraband => Mechanic("mech_contraband_low", "违禁品", "Contraband");
        internal static NativeLabel NotEnoughWater => Mechanic("mech_wine_no_water", "水量不足，无法开始发酵", "Not enough water to start fermentation");
        internal static NativeLabel MedicalAlcohol => new("Item", "item_rubbing_alcohol_name", "医用酒精", "Rubbing alcohol");
        internal static NativeLabel GloomWine => new("Item", "item_wine_gloomberry_name", "阴光莓果酿", "GloomBerry Wine");

        internal static int WaterTier(int purity)
            => purity >= 9990 ? 0 : purity >= 9900 ? 1 : purity >= 9800 ? 2
                : purity >= 9600 ? 3 : purity >= 9000 ? 4 : 5;

        internal static NativeLabel Water(int purity)
        {
            if (purity < 0 || purity > 10000) return Mechanic("mech_unknown_water", "水质未知", "Unknown water");
            if (purity >= 9990) return Mechanic("mech_condition_pure_water", "纯水", "Pure water");
            if (purity >= 9900) return Mechanic("mech_condition_high_quality_water", "优质水", "High quality water");
            if (purity >= 9800) return Mechanic("mech_condition_basewater", "基础水", "Base water");
            if (purity >= 9600) return Mechanic("mech_condition_ghostwater", "幽灵水", "Ghost water");
            if (purity >= 9000) return Mechanic("mech_condition_rustwater", "铁锈水", "Rust water");
            return Mechanic("mech_condition_gutterflow", "沟渠水", "Gutter water");
        }

        internal static NativeLabel ChemicalGrade(double purity)
        {
            if (purity >= 99) return Mechanic("mech_condition_lab_pure", "纯品级", "Lab pure");
            if (purity >= 90) return Mechanic("mech_condition_pharma_grade", "药用级", "Pharmaceutical grade");
            if (purity >= 80) return Mechanic("mech_condition_near_pharma", "近药用级", "Near pharmaceutical grade");
            if (purity >= 60) return Mechanic("mech_condition_street_standard", "街头标准级", "Street standard");
            if (purity >= 45) return Mechanic("mech_condition_cut_product", "掺兑级", "Cut product");
            if (purity >= 30) return Mechanic("mech_condition_junk_grade", "垃圾级", "Junk grade");
            return Mechanic("mech_condition_starch_grade", "淀粉级", "Starch grade");
        }

        internal static NativeLabel WineGrade(int score)
        {
            if (score >= 6) return Mechanic("mech_condition_wine_master_vintage", "琼浆玉液", "Master Vintage");
            if (score == 5) return Mechanic("mech_condition_wine_reserve", "臻品窖藏", "Reserve");
            if (score >= 3) return Mechanic("mech_condition_wine_production", "量产酒液", "Production Wine");
            if (score == 2) return Mechanic("mech_condition_wine_homebrew", "手工自酿", "Homebrew");
            if (score == 1) return Mechanic("mech_condition_wine_bucket_brew", "粗糙桶酿", "Rough Bucket Brew");
            return Mechanic("mech_condition_wine_undrinkable", "难以下咽", "Undrinkable");
        }

        internal static NativeLabel Liquid(string identifier, string nativeName)
        {
            string key = identifier switch
            {
                "microbe" => "liquid_microbes",
                "fat" => "liquid_fat_oil",
                "coffee_mush" => "liquid_coffee_bean_mush",
                "contraceptive" => "liquid_contraceptive_hormones",
                "methylol" => "liquid_n_methylol_base",
                "debris" => "liquid_insoluble_debris",
                _ => "liquid_" + identifier
            };
            // Unknown mod liquids retain their own name when no native key exists.
            string english = identifier switch
            {
                "water" => "Water", "heavy_metal" => "Heavy metals", "microbe" => "Microbes",
                "organic_waste" => "Organic waste", "mineral" => "Minerals",
                "chemical_contaminant" => "Chemical contaminants", "physical_contaminant" => "Physical contaminants",
                "microplastic" => "Microplastics", "dream_cap_mush" => "Dream cap mush",
                "dream_cap_extract" => "Dream cap extract", "plant_matter" => "Plant matter",
                "alcohol" => "Alcohol", "caffeine" => "Caffeine", "protein" => "Protein",
                "fat" => "Fat / oil", "starch" => "Starch", "sugar" => "Sugar",
                "coffee_mush" => "Coffee bean mush", "contraceptive" => "Contraceptive hormones",
                "zyanide" => "Zyanide", "nightmare_extract" => "Nightmare extract",
                "nightmare_cap_mush" => "Nightmare cap mush", "chlorine" => "Chlorine",
                "synaprest" => "Synaprest", "fanta" => "Fanta", "methylol" => "N-Methylol base",
                "reducing_agent" => "Reducing agent", "acid" => "Acid", "base" => "Base",
                "stabilizing_agent" => "Stabilizing agent", "debris" => "Insoluble debris",
                "lipobond_agent" => "Lipobond agent", "livestock_steroid" => "Livestock steroid",
                "livestock_immunity" => "Livestock immunity", "wine_extract" => "Wine extract", "nomore" => "Nomore",
                _ => string.IsNullOrWhiteSpace(nativeName) ? identifier : nativeName
            };
            return Mechanic(key, string.IsNullOrWhiteSpace(nativeName) ? english : nativeName, english);
        }
    }
}
