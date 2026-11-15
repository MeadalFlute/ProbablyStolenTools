using System;

namespace ProbablyStolenTools
{
    internal static class ChemistryData
    {
        // Native GetTagReadonly inserts missing entries. Tooltip/planning reads must not.
        internal static TagState Tag(GameItem item, string key)
        {
            var tags = item?.modifiedState?.dict;
            return tags != null && tags.TryGetValue(key, out var value) ? value : null;
        }
        internal static int Int(GameItem item, string key) => Tag(item, key)?.GetInt() ?? 0;
        internal static string String(GameItem item, string key) => Tag(item, key)?.GetString() ?? "";
        internal static float Float(GameItem item, string key) => Tag(item, key)?.GetFloat() ?? 0;
        internal static int Capacity(GameItem item) => Int(item, "LIQUID_CONTAINER_CAPACITY");
        internal static int[] Parts(GameItem item)
        {
            var liquids = Liquid.Liquids;
            var result = new int[liquids.Count];
            for (int i = 0; i < result.Length; i++) result[i] = Math.Max(0, Int(item, liquids[i].currentPart));
            return result;
        }
        internal static string[] Ids()
        {
            var result = new string[Liquid.Liquids.Count];
            for (int i = 0; i < result.Length; i++) result[i] = Liquid.Liquids[i].identifier;
            return result;
        }
        internal static long Total(int[] parts)
        {
            long total = 0;
            foreach (int part in parts) total += Math.Max(0, part);
            return total;
        }
    }
}
