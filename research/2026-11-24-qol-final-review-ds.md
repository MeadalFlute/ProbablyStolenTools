# 2026-11-24 QoL 最终 Code Review（DSH）

审查入口：`research/2026-11-24-qol-final-review-handoff.md`；基线 `8169488` 之后的全部工作区改动（含未跟踪新文件）。本轮只审查、不改代码。

## 一、结论

**通过，可以进入提交 / 升版 / 打包流程**。没有发现安全类问题（无新增文件写、无新增能力类别、无 RWR/加水/RNG 调用）；性能上新增开销集中在 tooltip 构建时的 TMP 测宽，且只在查看试剂容器或按住 Shift 时触发，不进入逐帧主循环。

需要处理的只有两项：一项是**发布前必修的文档一致性**（README 基线数字仍写 433/17/84），一项是**非 Mono 配置的编译隐患**（可顺手修）。其余为低风险建议与待实机项。

## 二、独立复核（未复用交接日志结论，本机重跑）

| 项目 | 结果 |
|---|---|
| 构建 | 0 警告 / 0 错误；静态表 `types=2915 fields=8235 factories=2033 bytes=670208` |
| 主 DLL | `B764A72A…`，与交接、`deployment.json`、安装目录三者一致 |
| helper | `6129A2D3…` / 808448 bytes，安装目录一致；merge 记录 `factoriesBefore=4894 factoriesAfter=2187 inputBytes=1353216 outputBytes=808448`，双 profile 精确保留 |
| `CheckpointTests` | 443 项通过（含新增 `CompositionDetailsTests` 9 项与 `AnnotationLocaleTests` 2 项） |
| `OptionalModCompatibilityTests` | 20 项通过 |
| `GameSaveActionTests` / `NativeSaveHistoryTests` | 13 / 14 项通过（后者需无沙箱环境，本机在放宽权限下跑完） |
| `ReleaseCompatibility` | 本机重跑 exit 0：`PASS 90 hook targets and 76 snapshot/private fields`、`SCAN Capabilities: files`、`SCAN HardFlags:` 空、`PASS official mod scanner and manifest capabilities`，其余原生图/预算/实体断言全 PASS |
| 部署记录 | `out/full-release-deploy-20261124-015204/deployment.json`：`SettingsImported=false`、`ExistingSaveFilesUnchanged=true`、`ExistingGameAssembliesUnchanged=true`、`GameStarted=false`、`InGameValidated=false`、`StoppedGameProcesses=[]`；`ModsBefore` 记录上一版 `913916DC…`（014319 候选），即本版确实整包替换 |
| `git diff --check` | 干净（仅既有的换行规范提示，`Plugin.cs`/`Rewind.cs` CRLF、csproj LF，与基线一致） |

结论：交接文档中的哈希、测试计数、扫描结论全部可复现，没有夸大。

## 三、需求逐条核对

| # | 需求 | 实现与证据 | 判定 |
|---|---|---|---|
| 1 | 店内固定水龙头：开关默认开，按住 Shift 追加水质/名称/占比，保留原连接说明，不虚构体积 | `DetailedCompositionTooltip.cs:105-109`（`PipeDetailsEnabled` 需开关+Shift）、`:124-144`（`Append` 保留五层并追加，`PipeRows` 只给占比）、`Plugin` 中 `WaterQuality.PipeComposition=true`；夹具 `CheckFaucetTooltip` 验证五层与尺寸/截断元数据不被破坏 | 通过 |
| 2 | 液体容器：Shift 明细开关默认开，按住完整替换，名称/实际含量/占比，含量 uL/mL/L，占比两位小数、痕量 `<0.01%`，按实际 TMP 文字宽度分列 | `DetailedCompositionTooltip.cs:146-159`（整结果替换）、`CompositionDetails.cs:65-94`（格式与 `<pos>` 绝对列位）；`CompositionDetailsTests` 覆盖 `1uL/999uL/1mL/999.999mL/1L`、`<0.01%`、列位含表头最大宽度 | 通过 |
| 3 | 列出所有非零成分，主成分橙、其余白，按未舍入占比稳定降序，相同占比保持注册顺序，主成分不置顶 | `CompositionDetails.cs:29-37`（非 reagent 时仅跳过 0）、`:39-54`（稳定插入排序）、`DetailedCompositionTooltip.cs:69-71`（颜色按 `row.Main` 而非行序）；测试含 1uL 痕量与并列占比顺序 | 通过 |
| 4 | 酸/碱/脂合剂/还原剂固定三行（零杂质也显示），不是稳定剂；重金属严格 <0.10% 时仅 tooltip 名称加星 | `CompositionDetails.cs:11-12`（集合含 `reducing_agent`、不含 `stabilizing_agent`）、`:33`（compact 模式保留两行杂质）、`:56-66`（`metal*1000 < total` 严格）；`TooltipNamePatch.cs:91-97` 只改 tooltip 字符串 | 通过 |
| 5 | 三个摘要项独立开关，默认仅当前占比，全关不留空括号，详细成分不受裁剪 | `InspectionText.cs:31-38`（`parts.Count==0` 时不加括号）；`Panel.cs:448-451`（摘要项还需「化学成分标注」）；酒精摘要只受当前占比控制（`WaterQualityPatch.cs:44`、`:100`） | 通过 |
| 6 | 工作中机器模组槽独立开关，默认关；只覆盖精确模组库存查询与新版显式拖拽，不写锁字段、不解锁电池/输入/输出 | `MachineModuleUnlock.cs:9-19`（MACHINE + `LOCKED_WORKING_TAG` + `ReferenceEquals(GetModuleInv(machine), inventory)`）、`:24-53`（仅 postfix 改返回值，`HarmonyPrepare` 缺方法时跳过）；夹具验证电池/非工作状态被排除、关闭后原锁字段与标签未变 | 通过 |
| 7 | 冲突提示用紧凑原生窗口，仅右上角 X，不超时、不响应 Escape；左下角小号 LightBlue 斜体来源标识，中文「快速SL」/其他语言「Quick SL」 | `CompatibilityNotice.cs:16-33`（`SetCloseOnEscape(false)`、尺寸按实测文字 340–540、来源标识 0.8 倍字号 + LightBlue + Italic + BottomLeft）、`:22` `InspectionLocale.Text`；`AnnotationLocaleTests` 覆盖 `zh/zh-CN/zh-Hans/zh-TW/ZH_hant` 与非中文回退 | 通过 |
| 8 | 同一冲突 Mod 只提示一次、跨重启保留；只有真正显示成功才记录；UI 未就绪每秒最多重试 | `OptionalModCompatibility.cs:124-147`（独立 `NotifiedMemoryConflicts`、批量赋值+限频保存）、`:168-173`（1 秒节流、`Show` 成功才 `RecordNotice`）；测试覆盖持久 IDs 抑制新会话提示、UI 未就绪不记账、写盘失败保留会话记忆并重试最新值 | 通过 |
| 9 | 内存禁用时只读索引匹配槽位/周目的当日及保留昨日晨间文件，显示「开业前 / 存档」，索引不触发原生保存 | `Rewind.cs:108-117`（按 store/day 变化索引、单次补锚点）、`:128-157`（过滤 state/slot/run/day，`Catalog.Refresh` 只读，失败也只记录一次且不重试）；扫描确认 `CheckpointFileCatalog` 只有文件读取类调用 | 通过 |

## 四、只读边界与安全

- 没有新增「写存档 / 调原生保存 / 加水 / 消耗 RNG」的调用：水龙头只读 `todayRustwaterPurity` / `todayGutterflowPurity` 与静态 `waterContaminantDistribution`（`DetailedCompositionTooltip.cs:75-103`），注释明确原生两种升级都走 `AddWater(grade:1)`、不使用相似命名的分布；夹具断言读取后 `networkUpgrade` 数量、水质字段、`IS_TOOLTIP_DISABLED_TAG` 均未变。
- VBE 排除仍在 `LiquidComposition.Analyze` 内先行返回（`LiquidComposition.cs:76-77`），`ContainerRows` 因此对 VBE 容器返回 null，tooltip 保持原样。
- 模块解锁只改 `IsInsertLocked` / `IsRemoveLocked` 返回值与拖拽拦截结果，不调用 `LockInv/UnlockInv`、不写标签；夹具逐字段核对。
- 能力扫描与上一版一致：仅 `files`，`HardFlags` 空，`Pending` 列表条目与 0.9.6 完全相同（都是既有存档/日志 IO），本轮新增的 `Catalog.Refresh` 调用属于既有 `CheckpointFileCatalog` 能力类别。
- 新增文本进入 tooltip 前都走 `InspectionText.Plain`（`WaterQualityPatch.cs:116-117`、`DetailedCompositionTooltip.cs:50`），第三方液体显示名里的富文本标签会被剥离，不会注入颜色/`<pos>` 破坏列布局。

## 五、发现

### R1（发布前必修，文档一致性）

`README.md:252` 与 `README_EN.md:241` 的「当前源码基线」仍写 **433 项**参考测试、**17 项**可选兼容、**84 个** Harmony 入口；本轮实际是 **443 / 20 / 90**。仓库的既有约定是基线数字随代码更新（上一轮 0.9.6 也是这样处理的），升版提交若不动这两行，发布说明会与实测对不上。建议在提交时同步改为 443 / 20 / 90，并把「16 项官方保护 owner 覆盖」等其余数字一并复核。

### R2（建议顺手修，非 Mono 配置编译）

`CompatibilityNotice.cs:30,32` 直接写 `TMPro.FontStyles.Italic` / `TMPro.TextAlignmentOptions.BottomLeft`，而该文件没有 `#if MONO` 包裹。Mono 配置下能编译（本次构建已验证），但 `GameRuntime != Mono` 时 TMP 命名空间是 `Il2CppTMPro`（见 `DetailedCompositionTooltip.cs:4-8`、`Panel.cs:8-10`），`TMPro.` 会解析失败。项目对 IL2CPP 分支的约定是「至少能编译」，而本机没有 MelonLoader 包目录，无法实际验证这条分支，属于潜在回归。修法与既有代码一致：改成 `Il2CppTMPro.FontStyles` / `Il2CppTMPro.TextAlignmentOptions`（`GlobalUsings.cs:6` 在 Mono 下把 `Il2CppTMPro` 别名到 `TMPro`，两种配置都成立，`Panel.cs:990` 用的就是这种写法）。若已决定彻底放弃非 Mono 分支，则应在 AGENT 里写明不再维护，而不是留一个半死的编译面。

### R3（性能，低）

`DetailedCompositionTooltip.cs:56-67` 在每次构建明细时对每个单元格调一次 `renderer.GetPreferredValues()`：容器 6 种成分即约 14 次完整文字排版，再加一次 `SetText` 复原。两条路径会触发：

1. **非 Shift 路径**：`WaterQualityPatch.cs:107` 对酸/碱/脂合剂/还原剂的普通 tooltip 也走 `AddReagent` → `AddRows`，即只要化学标注开着、悬停这类容器就会测宽；
2. Shift 路径：按住期间每次重建 tooltip 都测一遍。

单次量级约 0.2–0.5 ms，可接受；但如果原生 tooltip 在悬停期间是逐帧重建（前一份交接文档如此描述），这块就会变成每帧十几次 TMP 排版 + 字符串分配。建议加一个廉价的记忆化：以「各行纯文本 + `renderer.fontSize` + 语言」为 key 缓存列位结果，或在 `Time.frameCount` 内复用，命中即跳过 `GetPreferredValues` 与 `SetText`。这符合用户「在 Mod 自身约定范围内省略异常防御换效率」的偏好——测宽结果本来就与内容一一对应。

### R4（健壮性，低）

`:57-58` 在拿不到 renderer 时用 `value.Length * 16.0` 与固定 16 px 间距兜底。中英混排（CJK 约为拉丁字符两倍宽）时列位会明显偏窄，表现为「占比」列压到名称上。`renderer` 依赖 `OverlayHandler.current?.GetTooltipWindow()?.handler?.GetComponentInChildren<TextMeshProUGUI>()`，只要这条路径在某种 tooltip 形态（例如首次悬停、窗口刚创建、`handler` 尚未挂 TMP）返回 null 就会走到兜底。建议：兜底时按字符类别估算（ASCII 1 单位、其余 2 单位），取不到 renderer 时干脆退回「名称：占比」的简单格式，避免明显错位。

### R5（低，建议确认或加一行防护）

`DetailedCompositionTooltip.cs:170-197` 对 `GetTooltip` 与 `GetTooltipAdvanced` 各挂了一个内容相同的 postfix。容器路径是整结果替换（重复执行结果相同，无害），但**水龙头是追加**：若某个物品的 advanced 路径内部又调用了一次基础 `GetTooltip`，同一次生成里就会追加两遍水质与明细。现有夹具只单独验证了 `Append`，没有覆盖「两个 postfix 连续触发」。建议二选一：实机确认水龙头 tooltip 只出现一份明细；或加 2 行幂等防护——记录上次返回的 `nakedText`，若本次入参已与之相同则直接返回。

### R6（提示，可不动）

- `CompatibilityNotice.cs:19-41`：先 `SetSize(480,120).Center()`，测完文字后再次 `SetSize(width,height)`。若 `Center()` 在调用时就定下坐标，最终尺寸变大后窗口会轻微偏心。实机验收里若没看出来可以不动。
- `CompatibilityNotice.cs:15`：已有窗口存活时直接返回 true，不比对 `message`。当前只有 Claims 一个冲突源，消息固定，无影响；将来若加入第二个冲突 Mod，会出现「A 的窗口挡住了 B 的通知但 B 被记为已通知」。留一条注释即可。
- `CompositionDetails.cs:16` 的长度不一致抛 `ArgumentException`，而 `LiquidComposition.Analyze` 已用同样条件先行返回 null，实际几乎不可能触发；按「只读查看不该抛」的风格，可改为返回空列表（热路径上省一次异常构造）。
- `CompositionDetails.cs:56-66`：重金属为 0 时 `Star` 返回 true，即没有重金属的纯酸/碱也会加星。这与需求「严格低于 0.10% 加星」（0 属于严格低于）一致，测试也明确断言了；只是提醒这是「高纯度标记」而非「有杂质标记」，若语义不是这个意思需要调整。

## 六、性能小结

- 逐帧主循环没有新增分配：`Rewind.Tick` 只多两次整数比较（`_indexedStore/_indexedDay`），索引/补锚点都按 store/day 变化触发，失败也只记一次（`Rewind.cs:149-156`）。
- `MachineModuleUnlock.Allows` 只在 `__result == true`（已锁）时进入，且先看开关；`GetParentItem`/`GetModuleInv` 都是既有查询，不是逐帧热点。
- `OptionalModCompatibility.Tick` 仍是 1 Hz 刷新 + 每帧两次字符串比较（`NoticeRecorded` 在未命中时才会 `Split`），`NotifiedMemoryConflicts` 通常只有一两个 ID。
- 需要盯的只有 R3 的 TMP 测宽；建议按下文「发布前清单」里的记忆化改进后再看实机帧时间。

## 七、残余风险与待实机

1. **TMP 列对齐**：`<pos>` 用的是测量时的像素位置，真实字体、字号、长中文/长英文名称、不同 tooltip 字号下的表现只能实机看；离线测试覆盖了算法与兜底分支，不能替代视觉验收。
2. **水龙头双 postfix**（R5）：同一次生成是否只追加一次。
3. **R4 兜底路径**：首次悬停/窗口重建时列位是否错位。
4. **机器模组**：工作中换模块后的产出/耗电/跨夜结算仍按原生规则，属业务语义，不是本 Mod 能保证的；开关默认关闭是对的。
5. **0.46D**：稳定分支原始程序集不可用，只能保证新挂点被精确保留在 profile 里，不能声称已在 0.46D 实机跑过。
6. **R2 非 Mono 分支**：本机缺 MelonLoader 包目录，无法编译验证。
7. 交接文档已如实说明「来源标识英文回退」这次改动未单独实机复测，本报告同样不把它当作实机结论。

## 八、发布前清单（版本号与提交由本人在 push 前执行）

1. 修 R1：`README.md:252`、`README_EN.md:241` 的 433/17/84 → 443/20/90（顺手核对同段其他计数）。
2. 建议顺手修 R2（`Il2CppTMPro.` 两处）。
3. 可选 R3（列位记忆化）与 R5（幂等防护）；不修也可发，但要写进已知限制。
4. 干净源码重建 → 双 profile 合并（4894→2187、808448）→ 最终扫描（90 挂点/76 字段、仅 `files`、`HardFlags` 空）→ 部署 → 核对 `deployment.json` 哈希与 90 个存档文件未变。
5. 版本 0.9.6 → **0.9.7**（三处 manifest、`Plugin.Version`、`MelonInfo`）与 `AGENT.md` 部署段同步，然后按既有流程提交/推送（不 force）。
