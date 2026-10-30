using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProbablyStolenTools
{
    /// <summary>
    /// 酿酒相关鉴定：
    /// - 荧光莓：标出品相（正常 +1 / 较小 +2 / 未成熟 0 / 过熟 0 / 发霉 -1）；
    /// - 未开始发酵的酒瓶：按水 + 莓果 + 酵母预算出总分与预计品相。
    /// 水质分直接调用游戏的 <see cref="WineHelper.GetWaterQualityScore"/>，不自己另写一套。
    /// </summary>
    internal static class WineAppraisal
    {
        private const int MinWaterMl = 749;      // 游戏酿酒最低水量
        private const int BerryBonusThreshold = 2;

        internal static bool IsBerry(GameItem item)
            => item != null && SafeTag(item, "BERRY_TAG");

        internal static bool IsWineBottle(GameItem item)
            => item != null && SafeTag(item, "WINE_CONTAINER_TAG");

        /// <summary>未开始发酵（可以预打分）的酒瓶。</summary>
        internal static bool IsUnfermentedBottle(GameItem item)
            => IsWineBottle(item) && TagInt(item, "WINE_CONTAINER_STATE") == 0;

        /// <summary>返回要追加到鉴定摘要里的行；不相关时返回 null。</summary>
        internal static List<string> Lines(GameItem item)
        {
            try
            {
                if (IsBerry(item)) return BerryLines(item);
                if (IsUnfermentedBottle(item)) return BottleLines(item);
            }
            catch (Exception e)
            {
                Plugin.LogSource?.LogWarning("[Wine] appraisal failed: " + e.Message);
            }
            return null;
        }

        // ---------------- 物品描述用的一行结论 ----------------

        /// <summary>写进物品描述的精简结论；与酿酒无关时返回 null。</summary>
        internal static string Describe(GameItem item)
        {
            try
            {
                if (IsBerry(item))
                {
                    int condition = TagInt(item, "BERRY_CONDITION_INT");
                    var (cn, en, score) = BerryCondition(condition);
                    if (_seenBerry.Add(condition))
                        Plugin.LogSource?.LogInfo($"[Wine] berry condition {condition} -> {cn} ({score})");
                    return InspectionLocale.Text("品相：", "Quality: ") + InspectionLocale.Text(cn, en)
                        + " (" + Signed(score) + ")";
                }
                if (!IsUnfermentedBottle(item)) return null;

                int waterMl = 0;
                try { waterMl = WaterHelper.GetCurrentCapacityML(item); } catch { }
                if (waterMl < MinWaterMl)
                    return InspectionLocale.Text($"无法酿酒：水量 {waterMl}/{MinWaterMl} ml",
                        $"Cannot brew: {waterMl}/{MinWaterMl} ml water");

                int waterScore = 0;
                try { waterScore = WineHelper.GetWaterQualityScore(item); } catch { }

                int berryScore = 0, berryCount = 0;
                bool illegal = false, yeast = false;
                try
                {
                    var children = item.FindAllChildItems();
                    if (children != null)
                        for (int i = 0; i < children.Count; i++)
                        {
                            var child = children[i];
                            if (child == null) continue;
                            if (child.IsTag("WINE_INGREDIENT_TAG"))
                            {
                                berryCount++;
                                berryScore += BerryCondition(TagInt(child, "BERRY_CONDITION_INT")).score;
                            }
                            else if (child.IsTag("WINE_YEAST_TAG"))
                            {
                                yeast = true;
                                if (child.IsTag("WINE_YEAST_ILLEGAL_TAG")) illegal = true;
                            }
                        }
                }
                catch { }

                if (illegal)
                    return InspectionLocale.Text("阴光莓果酿 · 违禁品", "GloomBerry Wine - contraband");

                int bonus = berryCount >= BerryBonusThreshold ? 1 : 0;
                int total = waterScore + berryScore + bonus + (yeast ? 1 : 0);
                string grade = InspectionLocale.Text(GradeName(total, out string gradeEn), gradeEn);
                return InspectionLocale.Text(
                    $"预计：{grade}（{total} 分｜水{Signed(waterScore)} 莓{Signed(berryScore)}"
                    + (bonus > 0 ? "+1" : "") + (yeast ? " 酵母+1" : "") + "）",
                    $"Expected: {grade} ({total} pts | water {Signed(waterScore)}, berries {Signed(berryScore)}"
                    + (bonus > 0 ? ", two-berry +1" : "") + (yeast ? ", yeast +1" : "") + ")");
            }
            catch (Exception e)
            {
                Plugin.LogSource?.LogWarning("[Wine] describe failed: " + e.Message);
                return null;
            }
        }

        // ---------------- 荧光莓 ----------------

        private static List<string> BerryLines(GameItem item)
        {
            int condition = TagInt(item, "BERRY_CONDITION_INT");
            var (chinese, english, score) = BerryCondition(condition);
            return new List<string>
            {
                InspectionLocale.Text("品相：", "Berry quality: ") + InspectionLocale.Text(chinese, english)
                    + " (" + (score >= 0 ? "+" : "") + score + ")"
            };
        }

        /// <summary>品相整数 → 名称与分值。未知值按“正常 0 分”处理并记录一次。</summary>
        private static (string chinese, string english, int score) BerryCondition(int condition)
        {
            int score = condition < 0 ? condition + 1 : condition;
            switch (condition)
            {
                case 2: return ("较小", "Small", score);
                case 1: return ("正常", "Normal", score);
                case 0: return ("未成熟", "Unripe", score);
                case -1: return ("过熟", "Overripe", score);
                case -2: return ("发霉", "Moldy", score);
            }
            if (_unknownBerry.Add(condition))
                Plugin.LogSource?.LogWarning($"[Wine] unknown berry condition {condition}; score {score}");
            return (condition < 0 ? "发霉" : "正常", condition < 0 ? "Moldy" : "Normal", score);
        }

        private static readonly HashSet<int> _unknownBerry = new HashSet<int>();
        private static readonly HashSet<int> _seenBerry = new HashSet<int>();

        // ---------------- 酒瓶 ----------------

        private static List<string> BottleLines(GameItem bottle)
        {
            var lines = new List<string>();
            int waterMl = 0;
            try { waterMl = WaterHelper.GetCurrentCapacityML(bottle); } catch { }
            if (waterMl < MinWaterMl)
            {
                lines.Add(InspectionLocale.Text(
                    $"水量不足 {waterMl}/{MinWaterMl} ml，无法酿酒",
                    $"Not enough water {waterMl}/{MinWaterMl} ml - cannot brew"));
                return lines;
            }

            int waterScore = 0;
            try { waterScore = WineHelper.GetWaterQualityScore(bottle); } catch { }
            string waterName = WaterName(bottle);

            int berryScore = 0, berryCount = 0;
            bool illegalYeast = false, hasYeast = false;
            var berryParts = new List<string>();
            try
            {
                var children = bottle.FindAllChildItems();
                if (children != null)
                    for (int i = 0; i < children.Count; i++)
                    {
                        var child = children[i];
                        if (child == null) continue;
                        if (child.IsTag("WINE_INGREDIENT_TAG"))
                        {
                            berryCount++;
                            int condition = TagInt(child, "BERRY_CONDITION_INT");
                            var (cn, en, score) = BerryCondition(condition);
                            berryScore += score;
                            berryParts.Add(InspectionLocale.Text(cn, en) + (score >= 0 ? "+" : "") + score);
                        }
                        else if (child.IsTag("WINE_YEAST_TAG"))
                        {
                            hasYeast = true;
                            if (child.IsTag("WINE_YEAST_ILLEGAL_TAG")) illegalYeast = true;
                        }
                    }
            }
            catch (Exception e) { Plugin.LogSource?.LogWarning("[Wine] ingredients: " + e.Message); }

            if (illegalYeast)
            {
                lines.Add(InspectionLocale.Text("[酿造] 阴光莓果酿（违禁品，不计分）",
                    "[Brew] GloomBerry Wine (contraband, not scored)"));
                return lines;
            }

            int doubleBonus = berryCount >= BerryBonusThreshold ? 1 : 0;
            int yeastScore = hasYeast ? 1 : 0;
            int total = waterScore + berryScore + doubleBonus + yeastScore;

            string gradeCn = GradeName(total, out string gradeEn);
            lines.Add(InspectionLocale.Text($"[酿造] 预计品相：{gradeCn}（{total} 分）",
                $"[Brew] Expected: {gradeEn} ({total} points)"));
            lines.Add(InspectionLocale.Text("  水质：", "  Water: ") + waterName + " " + Signed(waterScore)
                + $"（{waterMl} ml）");
            if (berryCount > 0)
                lines.Add(InspectionLocale.Text("  莓果：", "  Berries: ") + string.Join(" + ", berryParts.ToArray())
                    + " = " + Signed(berryScore) + (doubleBonus > 0
                        ? InspectionLocale.Text("，双果 +1", ", two berries +1") : ""));
            else
                lines.Add(InspectionLocale.Text("  莓果：未放入", "  Berries: none"));
            lines.Add(InspectionLocale.Text("  酵母：", "  Yeast: ")
                + (hasYeast ? InspectionLocale.Text("有 +1", "yes +1") : InspectionLocale.Text("无 +0", "none +0")));
            return lines;
        }

        private static string GradeName(int score, out string english)
        {
            if (score >= 6) { english = "Master Vintage"; return "琼浆玉液"; }
            if (score == 5) { english = "Reserve"; return "臻品窖藏"; }
            if (score >= 3) { english = "Production Wine"; return "量产酒液"; }
            if (score == 2) { english = "Homebrew"; return "手工自酿"; }
            if (score == 1) { english = "Rough Barrel Brew"; return "粗糙桶酿"; }
            english = "Undrinkable";
            return "难以下咽";
        }

        private static string WaterName(GameItem bottle)
        {
            int purity = 0;
            try { purity = WaterHelper.GetWaterPurity(bottle); } catch { }
            try
            {
                int index = WaterFeatureHelper.GetPurityArrayIndex(purity);
                switch (index)
                {
                    case 0: return InspectionLocale.Text("纯水", "Pure water");
                    case 1: return InspectionLocale.Text("优质水", "High quality water");
                    case 2: return InspectionLocale.Text("基础水", "Base water");
                    case 3: return InspectionLocale.Text("幽灵水", "Ghost water");
                    case 4: return InspectionLocale.Text("铁锈水", "Rust water");
                    case 5: return InspectionLocale.Text("沟渠水", "Gutter water");
                }
            }
            catch { }
            return InspectionLocale.Text("水质未知", "unknown water");
        }

        private static string Signed(int value) => (value >= 0 ? "+" : "") + value;

        // ---------------- 标签读取 ----------------

        private static bool SafeTag(GameItem item, string tag)
        {
            try { return item.IsTag(tag); } catch { return false; }
        }

        private static int TagInt(GameItem item, string tag)
        {
            try { return item.GetTagReadonly(tag).GetInt(); } catch { return 0; }
        }
    }
}
