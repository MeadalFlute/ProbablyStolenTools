using System;
using System.Collections.Generic;
using System.Globalization;

namespace ProbablyStolenTools
{
    internal static class CompositionDetails
    {
        internal readonly record struct Row(string Id, long Amount, double Percent, bool Main);

        internal static bool IsReagent(string id)
            => id == "acid" || id == "base" || id == "lipobond_agent" || id == "reducing_agent";

        internal static List<Row> Rows(string[] ids, int[] parts, string main = null, bool compactReagent = true)
        {
            if (ids == null || parts == null || ids.Length != parts.Length) throw new ArgumentException("Composition length mismatch.");
            long total = 0;
            int largest = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                total += Math.Max(0, parts[i]);
                if (parts[i] > 0 && (largest < 0 || parts[i] > parts[largest])) largest = i;
            }
            var rows = new List<Row>();
            if (total == 0) return rows;
            if (main == null && largest >= 0) main = ids[largest];
            bool compact = compactReagent && IsReagent(main);
            // Retain both difficult impurities even at zero.
            for (int i = 0; i < parts.Length; i++)
            {
                bool primary = ids[i] == main;
                long amount = Math.Max(0, parts[i]);
                if (compact ? !primary && ids[i] != "chemical_contaminant" && ids[i] != "heavy_metal" : amount == 0) continue;
                rows.Add(new Row(ids[i], amount, amount * 100.0 / total, primary));
            }
            return Sort(rows);
        }

        internal static List<Row> Sort(List<Row> rows)
        {
            // Stable insertion sort preserves registration order for equal shares.
            for (int i = 1; i < rows.Count; i++)
            {
                var row = rows[i];
                int j = i;
                while (j > 0 && rows[j - 1].Percent < row.Percent)
                {
                    rows[j] = rows[j - 1];
                    j--;
                }
                rows[j] = row;
            }
            return rows;
        }

        internal static bool Star(string main, string[] ids, int[] parts)
        {
            if (!IsReagent(main) || ids == null || parts == null || ids.Length != parts.Length) return false;
            long total = 0, metal = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                total += Math.Max(0, parts[i]);
                if (ids[i] == "heavy_metal") metal += Math.Max(0, parts[i]);
            }
            return total > 0 && metal * 1000L < total;
        }

        internal static string Percent(double value)
            => value > 0 && value < 0.01 ? "<0.01%" : value.ToString("0.00", CultureInfo.InvariantCulture) + "%";

        internal static string Amount(long microlitres)
            => microlitres < 1000 ? microlitres.ToString(CultureInfo.InvariantCulture) + "uL"
                : microlitres < 1000000 ? (microlitres / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mL"
                : (microlitres / 1000000.0).ToString("0.######", CultureInfo.InvariantCulture) + "L";

        internal static string[] Table(string[] names, string[] amounts, string[] percentages,
            Func<string, double> measure, double gap)
        {
            if (names.Length != percentages.Length || amounts != null && amounts.Length != names.Length)
                throw new ArgumentException("Composition table length mismatch.");
            double first = 0, second = 0;
            for (int i = 0; i < names.Length; i++)
            {
                first = Math.Max(first, measure(names[i]));
                if (amounts != null) second = Math.Max(second, measure(amounts[i]));
            }
            string Position(double value) => "<pos=" + value.ToString("0.##", CultureInfo.InvariantCulture) + ">";
            string amountStop = Position(Math.Ceiling(first + gap));
            string percentStop = amounts == null ? amountStop : Position(Math.Ceiling(first + gap) + Math.Ceiling(second + gap));
            var lines = new string[names.Length];
            for (int i = 0; i < lines.Length; i++)
                lines[i] = names[i] + (amounts == null ? "" : amountStop + amounts[i]) + percentStop + percentages[i];
            return lines;
        }
    }
}
