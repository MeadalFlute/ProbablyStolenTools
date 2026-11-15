using System;
using System.Collections.Generic;

namespace ProbablyStolenTools
{
    // Pure next-cycle arithmetic. Callers own these arrays; never runs machine callbacks or RNG.
    internal static class FacilityProjection
    {
        internal static long Total(int[] parts)
        {
            long total = 0;
            foreach (int part in parts) total += Math.Max(0, part);
            return total;
        }

        internal static int Remove(int[] parts, int index, int water, int budget, int floor)
        {
            if (index < 0 || water < 0 || parts[water] <= 0 || budget <= 0) return 0;
            int minimum = (int)Math.Min(parts[index], (long)parts[water] * Math.Max(0, floor) / 10000);
            int removed = Math.Min(parts[index] - minimum, budget);
            parts[index] -= removed;
            return removed;
        }

        internal static int[] Purify(int[] source, int water, int cap, long budget, int[] priorities)
        {
            var result = (int[])source.Clone();
            if (water < 0 || source[water] <= 0) return result;
            long total = Total(source);
            long available = Math.Min(budget, total - source[water] - total * (10000 - cap) / 10000);
            var groups = new SortedDictionary<int, List<int>>();
            for (int i = 0; i < result.Length; i++)
            {
                if (i == water || source[i] <= 0 || priorities[i] == int.MaxValue) continue;
                if (!groups.TryGetValue(priorities[i], out var group))
                    groups[priorities[i]] = group = new List<int>();
                group.Add(i);
            }
            foreach (var group in groups.Values)
            {
                if (available <= 0) break;
                long sum = 0, removed = 0;
                foreach (int i in group) sum += result[i];
                long allocation = Math.Min(sum, available);
                for (int j = 0; j < group.Count; j++)
                {
                    int i = group[j];
                    int take = (int)Math.Min(result[i], j == group.Count - 1
                        ? allocation - removed : result[i] * allocation / sum);
                    result[i] -= take;
                    removed += take;
                }
                result[water] = checked(result[water] + (int)removed);
                available -= removed;
            }
            return result;
        }

        internal static int[] Evaporate(int[] source, bool[] volatileParts, int budget)
        {
            var removed = new int[source.Length];
            long total = 0;
            int remaining = 0;
            for (int i = 0; i < source.Length; i++)
                if (volatileParts[i] && source[i] > 0) { total += source[i]; remaining++; }
            long allocation = Math.Min(Math.Max(0, budget), total), used = 0;
            if (total == 0) return removed;
            for (int i = 0; i < source.Length; i++)
            {
                if (!volatileParts[i] || source[i] <= 0) continue;
                remaining--;
                removed[i] = (int)Math.Min(source[i], remaining == 0
                    ? allocation - used : allocation * source[i] / total);
                used += removed[i];
            }
            return removed;
        }

        internal static int[] Consume(int[] source, int millilitres)
        {
            var result = (int[])source.Clone();
            long total = Total(source);
            if (total <= 0) return result;
            long amount = Math.Min(total, Math.Max(0L, (long)millilitres * 1000));
            long used = 0;
            var order = new List<int>();
            var remainders = new long[result.Length];
            for (int i = 0; i < result.Length; i++)
            {
                int take = (int)(source[i] * amount / total);
                result[i] -= take;
                used += take;
                remainders[i] = source[i] * amount % total;
                if (remainders[i] > 0) order.Add(i);
            }
            order.Sort((a, b) =>
            {
                int compare = remainders[b].CompareTo(remainders[a]);
                if (compare == 0) compare = source[b].CompareTo(source[a]);
                return compare == 0 ? a.CompareTo(b) : compare;
            });
            for (int j = 0; j < order.Count && used < amount; j++, used++) result[order[j]]--;
            return result;
        }

        internal static int HealthDamage(int percentage, double sensitivity, double effect)
        {
            int p = (int)Math.Min(1000000, Math.Round(percentage * sensitivity));
            int damage = p <= 10 ? 0 : p <= 15 ? 15 : p <= 20 ? 20 : p <= 25 ? 30
                : p <= 30 ? 40 : p <= 40 ? 60 : p <= 60 ? 80 : p <= 100 ? 160 : p <= 200 ? 320 : 640;
            return (int)(damage * effect);
        }
    }
}
