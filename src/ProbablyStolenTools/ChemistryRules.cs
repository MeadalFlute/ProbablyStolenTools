using System;
using System.Collections.Generic;

namespace ProbablyStolenTools
{
    internal static class ChemistryRules
    {
        internal static string Product(string id) => id switch
        {
            "dream_cap_mush" => "dream_cap_extract",
            "nightmare_cap_mush" => "nightmare_extract",
            "coffee_mush" => "caffeine",
            _ => id
        };

        internal static string Signature(string[] ids, int[] parts)
        {
            long total = 0;
            for (int i = 0; i < parts.Length; i++) total += Math.Max(0, parts[i]);
            var products = new SortedSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < parts.Length; i++)
            {
                string id = ids[i];
                if (id == null || id == "water" || id == "alcohol" || id == "plant_matter"
                    || parts[i] <= 0 || (long)parts[i] * 100 < total) continue;
                products.Add(Product(id));
            }
            return string.Join("|", products);
        }

        internal static long BlendVolume(int[] parts, int loss)
        {
            double multiplier = 1.0 - Math.Max(0, Math.Min(100, loss)) / 100.0;
            long volume = 0;
            foreach (int part in parts)
                volume += Math.Max(0, (long)Math.Round(part * multiplier, MidpointRounding.AwayFromZero));
            return volume;
        }

        // Prepare every resulting amount before changing any game tags.
        internal static int[] Extract(string[] ids, int[] parts)
        {
            int plant = Array.IndexOf(ids, "plant_matter");
            int alcohol = Array.IndexOf(ids, "alcohol");
            if (plant < 0 || alcohol < 0 || parts[alcohol] <= 0) return null;
            var result = (int[])parts.Clone();
            bool changed = false;
            for (int i = 0; i < ids.Length; i++)
            {
                string output = Product(ids[i]);
                if (output == ids[i] || parts[i] <= 0) continue;
                int target = Array.IndexOf(ids, output);
                if (target < 0) return null;
                long extract = (long)result[target] + parts[i];
                long waste = (long)result[plant] + parts[i];
                if (extract > int.MaxValue || waste > int.MaxValue) return null;
                result[target] = (int)extract;
                result[plant] = (int)waste;
                result[i] = 0;
                changed = true;
            }
            long total = 0;
            foreach (int part in result) total += Math.Max(0, part);
            return changed && total <= int.MaxValue ? result : null;
        }
    }
}
