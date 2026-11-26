namespace ProbablyStolenTools
{
    internal static class UpdateNoticeContent
    {
        internal const string Revision = ":20261126-chemistry-compat-r4";
        internal const float Duration = 8f;
        internal const string SummaryChinese = "新增智能挤出、灯牌适配与Shift强制提示，完善水培预测；后续计划拆分回溯与工具箱功能";
        internal const string SummaryEnglish = "Smart extrusion, signboard support and Shift tooltips; improved hydroponic forecasts. Rewind and toolbox features are planned to split into separate mods.";
        internal const string DetailsChinese =
            "智能挤出：\n放入化学挤出机的容器中，主成分占比达到70%且存在唯一匹配配方时，自动选择对应产品，不自动生产。\n"
            + "默认开启，可在化学辅助中独立关闭；低纯度、无匹配或配方有歧义时不会自动选择产品。\n---\n"
            + "灯牌兼容：\n回溯功能适配“更合理的灯牌 | Proper Signboards”模组，现在回溯时若将灯牌取下，不再会导致对方报错。\n---\n"
            + "自动搅拌：\n优化一次放入大批量咖啡豆的运行代码，减少卡顿。\n---\n"
            + "化学成分详情：\n按住详情键（默认Shift）或固定详情时，化学品会显示全部非零组分，普通悬停仍显示精简信息。\n"
            + "优质试剂名称前的星形标记改为字体支持的*。\n---\n"
            + "交易平板置顶：\n现在平板Shift+鼠标中键置顶与取消置顶后，会在来客名称前显示棕色[置顶]。\n---\n"
            + "强制显示提示：\n倾倒液体、移动物品或使用工具时，按住详情键（默认Shift）可查看目标物品的提示；有详情版本时优先显示详情。\n"
            + "支持详情的物品会在普通提示中显示棕色的按键提示，并跟随自定义详情键。\n---\n"
            + "水培产物预测：\n新增生长评分、工作后剩余营养液和矿物质比例，按品质分别显示作物产量，零产量不显示。\n"
            + "随机品质按期望估算，不消耗游戏随机数；矿物质比例按矿物质除以水与矿物质之和计算。\n---\n"
            + "营养液改名：\n水为主成分、矿物质比例达到1.5%、影响生长的污染物合计不超过总液体1%时，自动显示为水培营养液及其矿物质比例。\n"
            + "仅改变提示中的名称，不修改物品或液体。\n---\n"
            + "功能拆分预告：\n后续计划将本模组拆分为独立的回溯模组和Shift工具箱，名称及迁移方式尚未最终确定。\n"
            + "本次更新尚未拆分，无需调整订阅；正式拆分前会另行说明功能归属与设置迁移方式。";
        internal const string DetailsEnglish =
            "Smart extrusion:\nInserting a container into a chemical finisher selects the unique recipe matching an ingredient at or above 70%. Production remains manual.\n"
            + "Enabled by default; switch it off independently under chemistry assistance. Lower purity, unmatched or ambiguous contents do not select a product automatically.\n---\n"
            + "Signboard compatibility:\nRewind now supports Proper Signboards, preventing its data errors when rewinding after removing a signboard.\n---\n"
            + "Auto blending:\nOptimized large batches of coffee beans to reduce pauses.\n---\n"
            + "Chemical composition details:\nHold the details key (Shift by default) or pin details to see every nonzero chemical component. Ordinary hover remains compact.\n"
            + "The premium reagent name marker is now a font-compatible *.\n---\n"
            + "Negotiation tablet pinning:\nShift-middle-click the tablet to pin or unpin it. A brown [PIN] now precedes the customer name.\n---\n"
            + "Forced tooltips:\nHold the details key (Shift by default) while pouring, moving items or using tools to inspect the target tooltip, preferring its detailed version when available.\n"
            + "Items with details show a brown hint using your configured details key.\n---\n"
            + "Hydroponic forecasts:\nAdded growth scores, remaining nutrient solution and mineral percentage after work, and crop counts by quality, omitting zero yields.\n"
            + "Random qualities show expected counts without advancing game RNG. Mineral percentage uses minerals divided by water plus minerals.\n---\n"
            + "Nutrient solution names:\nWater-dominant containers with at least 1.5% minerals and growth-affecting contaminants totaling at most 1% of all liquid display as hydroponic nutrient solution with its mineral percentage.\n"
            + "Only the tooltip name changes; items and liquids are unchanged.\n---\n"
            + "Planned feature split:\nA future update is planned to separate rewind and Shift toolbox features into independent mods. Names and migration details are not final.\n"
            + "This update does not split the mod or require subscription changes. Feature ownership and settings migration will be explained before the split.";

        internal static float Progress(float now, float until, float duration) =>
            float.IsNaN(now) || float.IsNaN(until) || duration <= 0 || float.IsNaN(duration)
                ? 0 : 1 - System.Math.Max(0, System.Math.Min(1, (until - now) / duration));

        internal static string[] Sections(string text)
        {
            var sections = new System.Collections.Generic.List<string>();
            var lines = new System.Collections.Generic.List<string>();
            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (line.Trim() == "---")
                {
                    if (lines.Count > 0) sections.Add(string.Join("\n", lines));
                    lines.Clear();
                }
                else if (!string.IsNullOrWhiteSpace(line)) lines.Add(line);
            }
            if (lines.Count > 0) sections.Add(string.Join("\n", lines));
            return sections.ToArray();
        }
    }

    internal sealed class UpdateNoticeSchedule
    {
        private string _shown;
        private float _readySince = -1;
        private float _visibleSince = -1;

        internal bool Acknowledged(string version, string persistedVersion) =>
            string.IsNullOrEmpty(version) || version == persistedVersion || version == _shown;

        internal bool Due(string version, string persistedVersion, bool ready, float now)
        {
            if (Acknowledged(version, persistedVersion)) return false;
            if (!ready) { _readySince = -1; return false; }
            if (_readySince < 0) _readySince = now;
            return now - _readySince >= 0.5f;
        }

        internal bool ConfirmVisible(bool visible, float now)
        {
            if (!visible) { _visibleSince = -1; return false; }
            if (_visibleSince < 0) _visibleSince = now;
            return now - _visibleSince >= 2f;
        }

        internal void Shown(string version) { _shown = version; _readySince = _visibleSince = -1; }
    }
}
