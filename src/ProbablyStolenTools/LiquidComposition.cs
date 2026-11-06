using System;

namespace ProbablyStolenTools
{
    /// <summary>
    /// 液体成分分析：找出“水/酒精”之外占比最高的成分。
    /// 占比分母排除水与酒精，避免稀释后的百分比看起来毫无意义。
    /// </summary>
    internal static class LiquidComposition
    {
        internal sealed class Top
        {
            internal string Name;
            internal string Identifier;
            internal int Percent;
            internal int Amount;
        }

        internal static Top TopComponent(GameItem item)
        {
            try
            {
                if (item == null || !item.IsTag("LIQUID_CONTAINER_TAG")) return null;
                if (item.IsTag("WINE_CONTAINER_TAG")) return null;
                if (WaterHelper.GetTotalVolume(item) <= 0) return null;
                var liquids = Liquid.Liquids;
                if (liquids == null || liquids.Count == 0) return null;
                int[] parts = WaterHelper.GetLiquidParts(item);
                if (parts == null || parts.Length != liquids.Count) return null;

                int water = 0, alcohol = 0, best = -1, bestAmount = 0;
                long total = 0;
                for (int i = 0; i < parts.Length; i++)
                {
                    int amount = parts[i];
                    if (amount <= 0) continue;
                    total += amount;
                    string id = liquids[i]?.identifier;
                    if (id == "water") { water = amount; continue; }
                    if (id == "alcohol") { alcohol = amount; continue; }
                    if (amount > bestAmount) { bestAmount = amount; best = i; }
                }
                if (best < 0 || bestAmount <= 0) return null;
                // 水或酒精本身就是最高成分时，维持原来的水质显示。
                if (water >= bestAmount || alcohol >= bestAmount) return null;
                long denominator = total - water - alcohol;
                if (denominator <= 0) return null;
                int percent = (int)Math.Round(bestAmount * 100.0 / denominator, MidpointRounding.AwayFromZero);
                if (percent < 1) percent = 1;
                if (percent > 100) percent = 100;
                var liquid = liquids[best];
                return new Top
                {
                    Name = string.IsNullOrEmpty(liquid.displayName) ? liquid.identifier : liquid.displayName,
                    Identifier = liquid.identifier,
                    Percent = percent,
                    Amount = bestAmount
                };
            }
            catch { return null; }
        }
    }
}
