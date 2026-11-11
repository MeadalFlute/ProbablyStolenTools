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
            if (!InspectionLocale.AnnotationsReady) return null;
            if (Plugin.CfgWineAppraisal != null && !Plugin.CfgWineAppraisal.Value) return null;
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
        /// <summary>通过原生 ModHook.OnCreateTooltip 追加到悬浮说明：水位警告 / 预期品质 / 违禁品。</summary>
        internal static void AddTooltip(RichTextBuilder builder, GameItem item)
        {
            if (!InspectionLocale.AnnotationsReady) return;
            if (builder == null) return;
            if (Plugin.CfgWineAppraisal != null && !Plugin.CfgWineAppraisal.Value) return;
            try
            {
                string warning = WaterWarning(item);
                if (!string.IsNullOrEmpty(warning))
                    builder.AddLine(warning, true, RenderHandler.ColorPalette.LightRed, false, false, false, false,
                        RenderHandler.ColorPalette.None, RenderHandler.ColorPalette.None, RenderHandler.ColorPalette.None);
                string line = Describe(item);
                if (!string.IsNullOrEmpty(line))
                    builder.AddLine(line, true, RenderHandler.ColorPalette.None, false, false, false, false,
                        RenderHandler.ColorPalette.None, RenderHandler.ColorPalette.None, RenderHandler.ColorPalette.None);
                if (IsIllegalBottle(item))
                {
                    string tag = InspectionLocale.Label(AnnotationLabels.Contraband);
                    if (!string.IsNullOrEmpty(tag))
                        builder.AddLine(tag, true, RenderHandler.ColorPalette.LightRed, true, false, false, false,
                            RenderHandler.ColorPalette.Black, RenderHandler.ColorPalette.Black, RenderHandler.ColorPalette.Black);
                }
            }
            catch (Exception e) { Plugin.LogSource?.LogWarning("[Wine] tooltip append failed: " + e.Message); }
        }
        /// <summary>水量不足单独成行；水量足够时返回 null。</summary>
        internal static string WaterWarning(GameItem item)
        {
            try
            {
                if (!IsUnfermentedBottle(item)) return null;
                int waterMl = WaterHelper.GetCurrentCapacityML(item);
                if (waterMl >= MinWaterMl) return null;
                // 容器自己会显示水量，这里只提示“不足”。
                return InspectionLocale.Label(AnnotationLabels.NotEnoughWater);
            }
            catch { return null; }
        }
        /// <summary>未发酵酒瓶里放了违禁酵母（阴光莓果酿）。</summary>
        internal static bool IsIllegalBottle(GameItem item)
        {
            try
            {
                if (!IsUnfermentedBottle(item)) return false;
                var children = item.FindAllChildItems();
                if (children == null) return false;
                for (int i = 0; i < children.Count; i++)
                {
                    var child = children[i];
                    if (child != null && child.IsTag("WINE_YEAST_TAG") && child.IsTag("WINE_YEAST_ILLEGAL_TAG"))
                        return true;
                }
            }
            catch { }
            return false;
        }
        internal static string Describe(GameItem item)
        {
            try
            {
                if (IsBerry(item))
                {
                    int condition = TagInt(item, "BERRY_CONDITION_INT");
                    var (cn, en, score) = BerryCondition(condition);
                    if (EventLog.Verbose && _seenBerry.Add(condition))
                        EventLog.Write($"[Wine] berry condition {condition} -> {cn} ({score})");
                    return InspectionLocale.Text("品相：", "Quality: ") + InspectionLocale.Text(cn, en)
                        + " (" + Signed(score) + ")";
                }
                if (!IsUnfermentedBottle(item)) return null;

                int waterScore = 0;
                try { waterScore = WineHelper.GetWaterQualityScore(item); } catch { }

                int berryScore = 0, berryCount = 0;
                bool yeast = false;
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
                            }
                        }
                }
                catch { }

                int bonus = berryCount >= BerryBonusThreshold ? 1 : 0;
                int total = waterScore + berryScore + bonus + (yeast ? 1 : 0);
                // 预期品质为负：直接判定酿造失败（水量不足也会走到这里，不再提前返回）。
                string grade = total < 0 ? InspectionLocale.Text("酿造失败", "Brewing failed")
                    : InspectionLocale.Label(AnnotationLabels.WineGrade(total));
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
                lines.Add(InspectionLocale.Label(AnnotationLabels.NotEnoughWater) + $" ({waterMl}/{MinWaterMl} ml)");
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
                string wine = InspectionLocale.Label(AnnotationLabels.GloomWine);
                string contraband = InspectionLocale.Label(AnnotationLabels.Contraband);
                lines.Add(InspectionLocale.Text($"[酿造] {wine}（{contraband}，不计分）",
                    $"[Brew] {wine} ({contraband}, not scored)"));
                return lines;
            }

            int doubleBonus = berryCount >= BerryBonusThreshold ? 1 : 0;
            int yeastScore = hasYeast ? 1 : 0;
            int total = waterScore + berryScore + doubleBonus + yeastScore;

            string grade = InspectionLocale.Label(AnnotationLabels.WineGrade(total));
            lines.Add(InspectionLocale.Text($"[酿造] 预计品相：{grade}（{total} 分）",
                $"[Brew] Expected: {grade} ({total} points)"));
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

        private static string WaterName(GameItem bottle)
        {
            try
            {
                return InspectionLocale.Label(AnnotationLabels.Water(WaterHelper.GetWaterPurity(bottle)));
            }
            catch { }
            return InspectionLocale.Label(AnnotationLabels.Water(-1));
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
