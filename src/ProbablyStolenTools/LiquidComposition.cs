using System;

namespace ProbablyStolenTools
{
    /// <summary>
    /// 液体成分分析：找出水/酒精之外占比最高的成分，并估算提纯后的纯度与评级。
    /// 仅用于 2999ml 及以下的容器（大桶不参与显示）。
    /// </summary>
    internal static class LiquidComposition
    {
        internal const int MaxVolumeMl = 2999;

        private const string Water = "water";
        private const string Alcohol = "alcohol";
        private const string Lipobond = "lipobond_agent";
        private const string Physical = "physical_contaminant";
        private const string Microbe = "microbe";
        private const string Chemical = "chemical_contaminant";
        private const string Protein = "protein";
        private const string Acid = "acid";
        private const string Base = "base";

        internal sealed class Analysis
        {
            internal bool Empty;
            internal bool WaterTop;
            internal bool AlcoholTop;
            internal string TopName;
            internal string TopIdentifier;
            internal int TopPercent;      // 水与酒精之外的占比
            internal int AlcoholPercent;  // 酒精占全液比例
            internal int Purity;          // 预期提纯后的产物纯度
            internal string Grade;
            internal string GradeColor;
            internal string SizeName;     // 微/小/标准/大
        }

        internal static bool IsSmallEnough(GameItem item)
        {
            try
            {
                int capacity = WaterHelper.GetTotalCapacityML(item);
                return capacity > 0 && capacity <= MaxVolumeMl;
            }
            catch { return false; }
        }

        internal static Analysis Analyze(GameItem item)
        {
            try
            {
                if (item == null || !item.IsTag("LIQUID_CONTAINER_TAG")) return null;
                int capacity = WaterHelper.GetTotalCapacityML(item);
                if (capacity <= 0 || capacity > MaxVolumeMl) return null;

                var result = new Analysis { SizeName = SizeFor(capacity) };
                var liquids = Liquid.Liquids;
                if (liquids == null || liquids.Count == 0) return null;
                int[] parts = WaterHelper.GetLiquidParts(item);
                if (parts == null || parts.Length != liquids.Count) return null;

                long total = 0;
                int water = 0, alcohol = 0, best = -1, bestAmount = 0;
                for (int i = 0; i < parts.Length; i++)
                {
                    int amount = parts[i];
                    if (amount <= 0) continue;
                    total += amount;
                    string id = liquids[i]?.identifier;
                    if (id == Water) { water = amount; continue; }
                    if (id == Alcohol) { alcohol = amount; continue; }
                    if (amount > bestAmount) { bestAmount = amount; best = i; }
                }
                result.Empty = total <= 0;
                if (result.Empty) return result;

                // 酒精占全液比例（用于 75% 医用酒精判定）
                result.AlcoholPercent = (int)Math.Round(alcohol * 100.0 / total);
                if (water >= bestAmount && water > 0)
                {
                    result.WaterTop = true;
                    return result;
                }
                if (alcohol >= bestAmount && alcohol > 0)
                {
                    result.AlcoholTop = true;
                    return result;
                }
                if (best < 0) return result;

                long denominator = total - water - alcohol;
                if (denominator <= 0) return result;
                int percent = (int)Math.Round(bestAmount * 100.0 / denominator);
                if (percent < 1) percent = 1;
                if (percent > 100) percent = 100;
                var liquid = liquids[best];
                result.TopName = string.IsNullOrEmpty(liquid.displayName) ? liquid.identifier : liquid.displayName;
                result.TopIdentifier = liquid.identifier;
                result.TopPercent = percent;

                ComputePurity(liquids, parts, out int purity);
                result.Purity = purity;
                Grade(purity, out string grade, out string color);
                result.Grade = grade;
                result.GradeColor = color;
                return result;
            }
            catch { return null; }
        }

        /// <summary>
        /// 预期提纯后的产物纯度：
        /// 排除水、酒精、脂合剂、物理污染物、微生物；蛋白/酸/碱仅排除 ≥1ml 的部分；
        /// 化学污染物按总量 0.7% 封顶后计入杂质。
        /// </summary>
        private static void ComputePurity(System.Collections.Generic.List<Liquid> liquids, int[] parts, out int purity)
        {
            purity = 0;
            double remaining = 0, product = 0, chemical = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                int amount = parts[i];
                if (amount <= 0) continue;
                string id = liquids[i]?.identifier;
                if (id == Water || id == Alcohol || id == Lipobond || id == Physical || id == Microbe) continue;
                if ((id == Protein || id == Acid || id == Base) && amount >= 1) continue;
                if (id == Chemical) { chemical += amount; continue; }
                remaining += amount;
                if (amount > product) product = amount;
            }
            // 化学污染物独立封顶：占比不超过 0.7%
            double maxChemical = 0.007 * remaining / 0.993;
            if (chemical > maxChemical) chemical = maxChemical;
            double total = remaining + chemical;
            if (total <= 0 || product <= 0) { purity = 0; return; }
            purity = (int)Math.Round(product * 100.0 / total);
            if (purity > 100) purity = 100;
        }

        internal static void Grade(int purity, out string grade, out string color)
        {
            if (purity >= 99) { grade = "纯品级"; color = "#7B3F9E"; }
            else if (purity >= 90) { grade = "药用级"; color = "#B0337A"; }
            else if (purity >= 80) { grade = "近药用级"; color = "#E8A0BE"; }
            else if (purity >= 60) { grade = "街头标准级"; color = "#8A8FA3"; }
            else if (purity >= 45) { grade = "掺兑级"; color = "#A0522D"; }
            else if (purity >= 30) { grade = "垃圾级"; color = "#E05A2B"; }
            else { grade = "淀粉级"; color = "#E8B08A"; }
        }

        internal static string SizeFor(int capacityMl)
        {
            if (capacityMl <= 250) return "微";
            if (capacityMl <= 450) return "小";
            if (capacityMl <= 750) return "标准";
            return "大";
        }

        /// <summary>酒精最高时的显示名：75% 视为医用酒精。</summary>
        internal static string AlcoholLabel(Analysis a)
            => a != null && a.AlcoholTop && a.AlcoholPercent == 75 ? "医用酒精" : "酒精";
    }
}
