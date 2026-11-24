# 2026-11-24 QoL 审查交接

> 当前审查入口已整理为 `2026-11-24-qol-final-review-handoff.md`。用户已验收本文件四项微调；最终仅补来源标识英文回退并重建部署，当前主 DLL 为 `B764A72A…`、443 参考及 20 可选兼容测试通过。下文保留历史过程，不作为最终哈希或待验收状态。

## 验收后的四项微调：当前候选

用户已确认下节「实测回报修复」全部通过实机验收。本节覆盖下方旧部署和旧水龙头行为；本节改动尚待实机验收。

- 部署：`out/full-release-deploy-20261124-014319/deployment.json`。主 DLL `913916DC14D43935A1C693E09B9B4911195A96DD0128D2F0CEC801BE2D29D289`；双 profile helper `6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23`，808448 bytes。仍为 0.9.6 dirty 候选，未提交/推送/升版。
- 冲突窗口新增底部左对齐「快速SL」小字，采用原版 ModSourceHelper 的 LightBlue/斜体样式，小于正文，并把标识高度计入紧凑窗口尺寸；仅保留原生右上角 X。
- 新配置 `Compatibility.NotifiedMemoryConflicts` 按 Mod ID 记录已成功显示的冲突弹窗。与 `HandledMemoryConflicts`（首次禁用默认值）明确分离，旧用户首次升级仍提示一次；UI 未就绪不写记录。成功显示后立即尝试保存，失败保留当前进程去重、沿用五秒限频/错误去重重试，保存最新设置，不覆盖玩家之后的强制启用选择。磁盘持续不可写且退出时不能保证跨进程去重。
- 水龙头的明细追加和隐藏 tooltip 查询覆盖都要求任一 Shift 按住，且水管明细开关开启；松开不追加明细、返回原生查询结果，不动隐藏标签。仍保留连接说明及全部原生 RichText 层，水龙头只显示占比。此开关不依赖容器 Shift 明细开关。
- 容器、试剂三行及水龙头统一按未舍入的占比稳定降序，不以主成分强制置顶；相同占比保留原液体注册顺序，主成分橙色独立于行序。固定零杂质行仍保留。
- `out/qol-refinement-20261124-static-suite.log`：441 参考、20 可选兼容通过；新增失败 UI 不记账、重启持久 IDs、禁用后重新启用不重弹、落盘失败限频及最新 override 保留、稳定排序/痕量排序/主成分独立着色回归。真实游戏 Mono 验证通知 XML 保存重载和 Shift 真假/开关关闭/原生本来可见查询边界，完整图回归通过。
- `out/qol-refinement-20261124-merge.log` 双 profile 精确保留；`…-universal-scan.log` 官方扫描仅 files、HardFlags/Error 空；`…-optional-native.log` 原版及真实 VBE 身份往返回归通过。稳定分支原始程序集缺失，不代表稳定版新增挂点重新实机验收。
- 部署脚本校验 90 个存档目录文件、其他本地 Mod 及游戏程序集不变，未导入配置/启动游戏。待实机：左下角来源标识无遮挡、窗口依旧紧凑；同一冲突 Mod 关闭窗口并重启不再提示；按住/松开 Shift 的水龙头连接说明与水质切换；各种成分占比降序及原颜色保持。

## 实测回报修复：当前候选

以下信息覆盖后文首轮候选的部署哈希、排版、关闭方式及测试计数；首轮记录保留用于追溯。

- 当前部署：`out/full-release-deploy-20261124-012906/deployment.json`，主 DLL `88941921B5D9ECF97442DF9368B4BCA0E78755F518176BB9E3E7BD3F08BF0C63`；helper 不变，双 profile 808448 bytes。仍为 0.9.6 dirty 候选，未提交/推送。
- 用户确认“水管”就是店内固定水龙头，**只显示占比，不虚构出水体积**；容器新增真实含量列。
- 确认当前 beta 原生 `MiscItemDirectory.Faucet()` 开启 `IS_TOOLTIP_DISABLED_TAG`，购升级回调会解除，但 `PlayerStore.AlwayCall()` 载入时仅更新连接说明而不解除该标签。这会阻止整个 tooltip，不只是 Mod 钩子。
- 新增 `FaucetTooltipVisibilityPatch`：仅明细开关开启且已有解锁水源时，覆盖 `HasTooltip` 查询；不写标签、不保存、不改水质。缺升级或未解锁不制造提示；字典使用 TryGetValue，缺少另一升级条目也不抛错。
- 水龙头明细从中途 late hook 移至两个最终 tooltip postfix，使用原生 RichText 的全部五层逐层追加，不丢连接说明/格式/来源。无论是否按 Shift 都不把水龙头当容器清空。水质来源按实际填充逻辑 Rust 优先，仍使用两者共用 grade=1 分布，不消耗 RNG。
- 容器明细为「名称 / 含量 / 占比」三列，水龙头为「名称 / 占比」两列。使用实际 tooltip TMP 字体的 GetPreferredValues 测量每列最大宽度，再用 `<pos=…>` 固定列起点，取代单个 Tab。此接口会修改 TMP 处理缓冲，因此测量完成/异常均用 SetText 恢复原文本缓冲，避免原生 tooltip 缓存未重绘时错显。
- 体积按 `<1000uL`、`<1000000uL`、其余分别选 uL/mL/L，保留到 1uL 的真实精度；百分比固定两位小数，非零且 `<0.01%` 标为 `<0.01%`，避免把存在的痕量伪装成零。星标仍按原始整数含量判断，不能按显示舍入值判断。
- 冲突窗口按本地化文字实际测量宽高，宽度 340–540、高度至少 110，去掉底部重复关闭按钮，只保留原生标题 X，仍不超时关闭/不响应 Escape。
- `out/qol-fix-20261124-static-suite.log`：构建零错误/警告，**440 参考 + 17 可选兼容**通过；游戏 Mono 新回归复现原生隐藏标签，验证解锁源/缺条目/两种水源优先级/禁用开关/原始标签不变，以及最终追加保留五层文本与尺寸/截断元数据；完整图回归通过。
- `out/qol-fix-20261124-universal-scan.log`：最终合并 DLL **90 挂点/76 字段**，只有 `files`，HardFlags/Error 空。`out/qol-fix-20261124-optional-merged-native.log` 下原版和真实 VBE 身份往返、新水龙头回归通过。双 profile 精确等价通过。
- 部署脚本确认 90 个存档目录文件、游戏程序集、其他本地 Mod 未变；未导入玩家配置、未启动/终止游戏。仍需实机确认：紧凑窗口无大留白、右上角单关闭入口；水龙头连接说明与水质同时存在；长中文/英文名称、各体积单位和痕量下列对齐，无遮挡/裁切；Shift 松开恢复原提示。

## 构建与范围

- 基线提交：`8169488`。本轮是未提交工作区候选，未升版/推送/生成正式发布包，版本仍为 0.9.6。
- 安装游戏：beta 1.0.0.16，MVID `d927a6e4-ae7a-4e65-a5e2-9f9624c131f3`。
- 已部署通用 manifest，包含 0.46D + 1.0.0.16 静态 profile；旧正式版原始程序集不可用，仅保留和离线验证已有 profile，不代表稳定版新挂点已实机验收。
- 部署记录：`out/full-release-deploy-20261124-003203/deployment.json`。
- 主 DLL SHA256：`24BD0F120C4407676C642FFC2778556514E96720D2CA18C3C185D6C66D8B0C33`。
- helper SHA256：`6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23`，808448 bytes，与上一正式版相同。
- 安装与构建目录双 DLL 哈希一致。90 个存档目录文件、游戏程序集和其他本地 Mod 校验未变；未启动/关闭游戏，未导入或修改玩家配置。

## 需求确认

- 全成分明细显示所有实际非零成分，包括极微量；不列不存在的零成分。
- 酸、碱、脂合剂、还原剂固定显示主成分、化学污染物、重金属三行，两种杂质为零也显示。
- 用户最初称“中和剂”，核对当前官中后已明确指 **还原剂 `reducing_agent`**，不是稳定剂 `stabilizing_agent`。
- 星标以全液体总量为分母，重金属严格 `<0.10%` 才显示，等于边界不显示。

## 配置与行为

| XML key | 新配置缺省值 | 范围 |
|---|---|---|
| `WaterQuality.PipeComposition` | true | 水管全成分明细，独立开关 |
| `Chemistry.ShiftComposition` | true | 任一 Shift 临时替换容器完整 tooltip，独立于化学主开关 |
| `Chemistry.ShowCurrentShare` | true | 摘要当前百分比 |
| `Chemistry.ShowExpectedPurity` | false | 摘要预期纯度 |
| `Chemistry.ShowGrade` | false | 摘要评分 |
| `Machines.UnlockWorkingModules` | false | 工作中机器模组仓锁定查询覆盖 |

- 设置页 27 项开关，化学分区为索引 9 到 20（结束不含）；摘要三项还受“化学成分标注”开关控制。Shift 明细不依赖化学主开关。
- 旧设置不强制迁移覆盖。快速萃取/快速反应仍默认关闭；已有配置值不变。
- 普通摘要可任意组合三项；全关时只剩名称，无空括号。酒精摘要只使用当前百分比开关。
- 独立明细/试剂三行不受摘要三开关裁剪，避免隐藏用户要求的杂质信息。

## 实现边界

### 明细与命名

- `CompositionDetails.cs`：无游戏依赖的明细/星标/数值格式模型。long 累计体积；非零痕量不丢失，最小显示 0.001mL；极低百分比以 `<0.000001%` 标明。
- `DetailedCompositionTooltip.cs`：原生液体语言键，名称与含量以 Tab 分隔、每成分独立一行；主成分橙色、其余白色。容器显示体积及百分比，水管显示百分比（无固定取水量）。
- 原版两种水管升级均调用 `AddWater(grade:1)`，因此使用 `waterContaminantDistribution`，不能按升级名字改用其他相似命名分布。仅读今日水质字段，不生成临时容器、不加水、不消耗随机数。
- Shift 在 `GameItemElement.GetTooltip` / `GetTooltipAdvanced` 最终 postfix 替换完整结果，避免 late hook 后的原版风味/容器说明残留。原版逐帧重新生成 tooltip，松键即恢复；异常保留原结果并去重记录。
- `TooltipNamePatch.cs`：只在原有 tooltip 命名作用域加星，不写物品名称或存档。VBE 现有命名排除逻辑不变；Shift 明细是只读查看，不执行化学反应。
- `InspectionText.cs` / `WaterQualityPatch.cs`：普通 tooltip 与议价摘要共用组合规则。

### 工作模组仓

- `MachineModuleUnlock.cs`：需同时满足开关开启、MACHINE、LOCKED_WORKING_TAG、库存与 `MachineHelper.GetModuleInv(machine)` 完全同一引用。
- 覆盖 `IsInsertLocked` / `IsRemoveLocked` 返回值；不改 `overrideLock*`、工作标签或保存字段，不调用 LockInv/UnlockInv。
- beta 另有 `GetLockedModuleBayMachine` 显式拖拽拦截，同样限匹配模组仓。
- 旧正式版无显式拖拽方法，通过 HarmonyPrepare 字面量查找决定是否安装该单独挂点；官方扫描未新增反射/代码生成能力。
- 不绕过电池、输入、输出、物品自身或非工作状态的锁。工作中换模块的具体机器业务结果仍需实机验收，默认关闭。

### 通知与文件节点

- `CompatibilityNotice.cs`：原生 CustomUIManager 窗口，无计时关闭，不响应 Escape，需按钮或窗口 X 关闭。
- `OptionalModCompatibility.cs`：只有成功创建提示才标记已通知；UI 未就绪每秒最多重试一次。无冲突时重置通知状态。首次检测落盘、手动强启与失败重试策略未变。
- `Rewind.cs`：内存禁用时每个 store/day 上下文只读索引 Mod 自有晨间文件，过滤槽位/周目/状态/相邻日期，复用原面板的“开业前 / 存档”展示及原生文件恢复。
- 索引本身不调用游戏保存、不创建备份。重载后等待稳定，若缺活动日锚点则仅尝试一次现有文件 Capture，避免历史日与活动槽位不匹配时每两秒重读文件。
- 旧名字/超 16MiB UTF-8 存档的旧恢复回退保留，不新纳入展示索引和清理。

## 验证

- `out/qol-20261124-static-suite.log`：构建零错误/警告，438 项参考 + 17 项可选兼容测试通过；完整静态布局/图往返/预算/MVID/非法 token 等回归通过。
- 游戏 Mono QoL 夹具：8 种标注组合；精确模组仓插入/移除/显式拖拽覆盖，电池/非工作状态排除，关开关即时复原且原锁字段/标签未变；晨间文件槽位/周目/状态/日期边界、昨日保留开关、元数据、重复索引不增节点/不写档。
- 13 项 `GameSaveActionTests` + 14 项 `NativeSaveHistoryTests` 通过。
- `out/qol-20261124-merge.log`：双 profile 精确等价通过，4894 个原始工厂合并为 2187，808448 bytes。
- `out/qol-20261124-universal-scan.log`：最终双 DLL 89 挂点/76 字段，官方扫描只有 `files`，HardFlags/Error 空；500 组原版耗水对照及既有本地图回归通过。
- `out/qol-20261124-optional-merged-native.log`：合并表下无第三方环境与真实 VBE 回调身份往返通过（保留回调，不调用业务回调）。
- 离线 Mono 夹具有既有 Unity runtime/class-library 提示，但进程退出成功、所有断言通过；不是游戏实机验收结果。

## 实机验收重点

1. 两种水管升级的今日数字、主成分橙色/其余白色、Tab 对齐与长名称；未升级水管不显示虚构水质。
2. 先按 Shift 再悬停、悬停后按/松任一 Shift，确认完整提示切换、松键恢复；检查小容器、空容器、痕量和第三方液体。
3. 酸/碱/脂合剂/还原剂三行，零杂质仍显示；0.10% 恰好不加星；稳定剂不套用此规则，真实物品名不变。
4. 摘要三开关任意组合、全关、议价界面同步及中文/英文/原生其他语言；细目不随摘要项消失。
5. 默认未解锁；已开工机器开开关即可操作模组仓，关后立即锁回；电池/输入/输出仍按原规则，观察产出/耗电/跨夜。
6. 冲突提示长期保留，按钮及 X 能关闭、不每帧重弹；内存禁用下首节点仍为“开业前 / 存档”，可选择/双击恢复，切换日期/槽位不串档。

不把离线通过当作 tooltip 排版、原生窗口交互或机器业务正确性的实机结论。本轮未启动游戏。
