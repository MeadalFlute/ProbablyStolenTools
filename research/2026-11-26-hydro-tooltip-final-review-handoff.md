# 2026-11-26 QuickSL 强制详情与水培预测最终审查交接

> DS回执已收到，无发布阻断项；玩家随后要求的公告r4/8秒/延迟确认与拆分预告见 `2026-11-26-hydro-tooltip-final-review-receipt.md`。下文18:28/r3/478项为审查基线，最新部署19:06/r4/480项以回执为准，增量未包含在DS原审查中。

## 审查范围

本文件是本轮最终入口，覆盖 `2026-11-26-hydro-tooltip-handoff.md` 中的旧部署、r2 和476项测试描述。版本仍为 **0.9.8**，HEAD 为 `817af11`；未提交、升版或推送。

请审查整个 dirty 工作区，包含未跟踪源码和测试，不能只看 `git diff`。重点是前次 DS 审查之后的新增功能、审查修复及最终显示微调；之前的化学/灯牌/智能挤出改动仍在同一工作区，不能在交接或打包时遗漏。

前次审查：`research/2026-11-26-chemistry-compat-review-ds.md`。
处理回执：`research/2026-11-26-chemistry-compat-review-receipt.md`。保留 DS 按玩家要求修改的智能挤出 **70%** 阈值，不回退为99%。

## 功能与约束

### 强制显示详情

- 按配置的详情键（默认Shift），在拖动物品、倾倒液体、使用工具时晚帧显示鼠标目标的原生tooltip；有详情版则由既有最终处理路径选择详情。
- `ForcedItemTooltip.cs` 为新增未跟踪源码；挂在 `RewindOverlay.LateUpdate`，不修改原生输入、不调用搬运/倾倒/工具业务回调。
- 忽略拖拽物品的fakeBackground，排除自身与群拖成员。同目标内容最多100ms重算一次，换目标立即刷新；松键、载入、停用时释放引用。
- 基础tooltip棕色提示使用实际绑定键；原生禁用tooltip仍保持禁用，水龙头已有的详情覆盖例外保留。物理Shift+中键的固定手势不随详情键改绑而变化。

### 水培预测

- 生长评分最终为单行 `{当前} --> {工作后}`；代码优先查询实际tooltip TMP字体及回退字体中的 `→`，缺字才用ASCII。即使评分不变也显示两端；水/电不足或未工作时只显示当前值。
- **玩家实机确认本机显示 `-->`，不是 `→`。这是当前字体检查的回退结果，不宣称原生字体支持箭头；本轮不引入系统字体或额外资源。**
- 工作后剩余液量固定两位小数、自动uL/mL/L，例如 `6.33L`。仅水培预测使用此格式，不改变通用成分表精度。
- 矿物质占比为 `矿物质/(水+矿物质)`；投影按原版顺序计算工作tick、消耗、净水/污染性状。
- 分品质显示非零作物数量；确定品质分配对照原生，随机数量保留范围、荧光莓随机品质显示期望并注明。不得推进游戏RNG、调用收获回调或实际生成物品。
- 没有扩展所有作物的额外返种预测；保留既有水培芦苇返种预测，不把未实现项写成已完成。

### 水培营养液改名

- 水是唯一最大成分，`矿物质/(水+矿物质)>=1.5%`，六类植物生长污染物合计占全部液体 `<=1%` 时，tooltip显示水培营养液及矿物质占比。
- 污染物：重金属、化学污染物、微生物、有机废物、酸、碱。判据是合计，不是每项分别<=1%。
- 只改显示，保留化学辅助/自动改名开关以及小容量、量筒、滴管、VBE等原有排除。不得更改真实名称或液体。

### 前次灯牌审查修复

- 已知受支持构建忙碌/未初始化/拖动/故障时，在消费自动捕获边界前等待；手动捕获给明确重试提示。未知构建仍仅提示并跳过专项适配，不停用普通内存节点。
- 不采纳“捕获异常后直接丢弃灯牌元数据”和“Commit失败仍报告恢复成功”：前者可能生成不能安全恢复的节点，后者可能留下存档缓存与已写原生图不一致。
- 补空state防护、Commit失败时明确原生图已写且缓存失败；仍按恢复失败处理、要求读档。不增加第三方反射泛化或新权限。
- SceneUnloaded/Commit校验static、零参数。细节及不采纳原因见回执。

### 更新公告

- 八节中英文内容：智能挤出、灯牌兼容、自动搅拌、化学成分详情、交易平板置顶、强制tooltip、水培预测、营养液命名。保留真实分隔线、紧凑排版、5秒灰底填充进度条。
- 最终ID为 `0.9.8:20261126-chemistry-compat-r3`；不依赖主版本号变化也能重播一次。
- 问题诊断：玩家XML已保存r2已展示记录，但旧日志不记录正常显示时间，**不能证明当时是否真正可见，也未确认触发条件写错**。原生载入在EmporiumEntry.Start中同步打开晨报；本轮不改触发门控、不手改玩家XML。
- 新增每公告一次的已读状态日志及成功创建展示日志。当前仍在成功Show后记已读；“已创建窗口”不等于已证明玩家实际看见，需重点审查这一边界。
- **r3公告实际可见性尚无明确验收回报，不应与上一轮功能验收混为一谈。**

## 审查重点

1. `ForcedItemTooltip.cs`、`Rewind.cs`、`Plugin.cs`：目标选择、群拖/自身排除、松键清理、载入与场景切换生命周期、晚帧覆盖是否影响原生提示；100ms缓存对实时内容的影响。
2. `DetailedCompositionTooltip.cs`、`TooltipHooks.cs`：提示只对真实可用详情出现，键名和语言回退正确；不递归构建tooltip。
3. `FacilityPreview.cs`、`FacilityProjection.cs`：评分单位/小数、工作顺序、剩余量和矿物质分母、作物/模组差异、随机性边界；字体检查不添加字形或更改字体状态。
4. `LiquidComposition.cs`：营养液阈值与既有排除规则；溢出安全和成分数组只读。
5. `SignboardCompatibility.cs`、`CustomerCheckpoint.cs`：自动边界未丢失、恢复拒绝在写入前与写入后各自语义、第三方缓存失败不被误报为完整成功。
6. `UpdateNotice.cs`、`UpdateNoticeSchedule.cs`：r3只展示一次、Show与已读持久化顺序、UI可见性/门控、日志不每帧刷屏；英文内容和中文一致。

## 验证记录

以下均来自最终运行时代码的18:28构建/部署；本次准备交接只读核对产物、更新文档，没有再改代码或重跑构建。

| 项目 | 结果 |
| --- | --- |
| Release构建 | 0警告、0错误 |
| CheckpointTests | **478项通过**，含剩余量两位小数、评分箭头/ASCII/无预测、r2到r3独立确认 |
| OptionalModCompatibilityTests | **22项通过** |
| 游戏Mono原版夹具 | 通过 |
| 游戏Mono + 真实Proper Signboards 1.0.48 + VBE | 通过 |
| 水培确定品质分配 | 408组与原生SplitByHealth一致，不执行收获或RNG |
| 最终ReleaseCompatibility | 105挂点/76字段，仅files；HardFlags/Error为空 |
| `git diff --check` | 无空白错误，既有CRLF规范化警告 |
| 玩家实机 | 前一轮功能报告验收通过；最终评分实机显示ASCII回退；r3公告可见性未明确确认 |

Mono夹具不执行真实Unity输入/渲染或第三方ClearViews/Commit序列化，不以夹具通过替代这些实机覆盖。正式版原始程序集仍未重新运行，不能宣称0.46D新增实测通过。

日志前缀：`out/20261126-hydro-display-`，后缀为 `build.log`、`tests.log`、`optional.log`、`fixture-build.log`、`compatibility.log`、`mono-vanilla.log`、`mono-optional.log`、`deploy.log`。

## 最终部署与复建

部署记录：`out/full-release-deploy-20261126-182854/deployment.json`。

| 产物 | SHA256 |
| --- | --- |
| 主DLL | `41208059DA12F27492D83A3D5111920B7C58C79D4027C35F4450D29B2F5FC754` |
| 双profile helper | `6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23` |

本次已重新核对构建目录与安装目录两份主DLL/helper哈希一致。helper为808448 bytes，保留0.46D和1.0.0.16两套profile；不是670208 bytes的单beta表。

部署脚本确认95个存档目录文件、游戏程序集未变；未导入设置、不启动游戏。审查准备阶段也未修改玩家设置、存档或第三方Mod。

复建入口按AGENT.md：Release主项目、CheckpointTests、OptionalModCompatibilityTests、MonoFixture/MonoHost、ReleaseCompatibility，再使用通用manifest部署。注意主项目构建会生成单beta helper；**最终扫描/打包/部署前必须恢复或重建合并表**，不能直接拿单表替换通用包。已验证的合并输入为 `out/20261126-fixes-merged/ProbablyStolenTools.StaticLayouts.dll`。

请回执优先列出按严重程度排序的问题、文件/行号和测试缺口，并区分发布阻断与可选建议。此次仅准备code review，不委派插件、不自动提交/升版/推送。
