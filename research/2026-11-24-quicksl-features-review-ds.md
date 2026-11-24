# 2026-11-24 QuickSL 批次 Code Review（DSH）

审查入口：`research/2026-11-24-quicksl-features-review-handoff.md`。范围是基线 `04df653` 之后工作区里的全部改动，包含上一批未提交的 QoL 文件与本批新增文件。本轮只审查，不改代码。

## 一、结论

**通过**：可以进入提交 / 升版 / 打包。新增的四块（详情键、存档外设置入口、双击鉴定优化、快速烧开水、更新简报）都没有越过只读边界，没有新增能力类别；离线证据可复现；本轮没有必修项，只有 3 条非阻塞建议和 5 组只能实机确认的行为。

## 二、上轮回执核对

| 上轮项 | 回执声明 | 复核结果 |
|---|---|---|
| R1 | README 433/17/84 → 450/20/92，并区分本机 beta 与未重测稳定版 | 属实。`README.md:258`、`README_EN.md:254` 已是 450/20/92 且明确写「本机 beta」「稳定版原始程序集未重新测试」 |
| R2 | `CompatibilityNotice` 改 `Il2CppTMPro` | 属实。`:30`、`:32` 已改为 `Il2CppTMPro.FontStyles` / `Il2CppTMPro.TextAlignmentOptions`，非 Mono 分支不再有裸 `TMPro.` |
| R5 | 水龙头最终结果加引用幂等保护 | 属实。`DetailedCompositionTooltip.cs:134` 用 `ReferenceEquals(result, _lastFaucetResult)` 拦截两层 postfix 的重复追加 |
| R3/R4 | 未做，属非阻塞建议 | 确认未做。测宽仍每次调用 `GetPreferredValues`，无 renderer 时仍按 `Length*16` 估宽；与本轮新增的「详情键组合 tooltip」叠加后，按住详情键时每次重建都会测一遍，量级与上轮评估一致，仍可接受 |

## 三、独立复核（本机重跑，未引用交接日志）

| 项目 | 结果 |
|---|---|
| 构建 | 0 警告 / 0 错误；静态表 `types=2915 fields=8235 factories=2033 bytes=670208` |
| 主 DLL | `573D2C33…`，与交接、`deployment.json`、安装目录三者一致 |
| helper | 安装目录 `6129A2D3…` / 808448，与 `deployment.json` 一致；源码侧 beta 单版 helper 为 `509D8A98…`（670208），合并产物仍为 808448，符合既有流程 |
| `CheckpointTests` | **450** 项通过（新增 `RapidBoilingTests` 4 项、`UpdateNoticeTests` 3 项、`CompositionDetailsTests` 9 项） |
| `OptionalModCompatibilityTests` | **20** 项通过 |
| `ReleaseCompatibility` | 本机重跑 exit 0：`PASS 92 hook targets and 76 snapshot/private fields`、`SCAN Capabilities: files`、`SCAN HardFlags:` 空、`PASS native manifest parser: QuickSL display, stable ID, bilingual description and versions 0.46D, 1.0.0.16`、`PASS separate opt-in rapid boiling hooks only native hotplate heating` |
| 部署记录 | `out/full-release-deploy-20261124-030020/deployment.json`：`Version=0.9.6`、`SettingsImported=false`、`ExistingSaveFilesUnchanged=true`、`GameStarted=false`、`InGameValidated=false`、`DeclaredGameVersions=0.46D,1.0.0.16` |
| 其他一致性 | `git diff --check` 干净（仅既有换行提示）；`scripts`/`tests`/`src` 中已无 `GameVersions.GameVersion` 旧元素引用 |

## 四、本轮需求逐条核对

| # | 需求 | 实现与证据 | 判定 |
|---|---|---|---|
| 1 | 显示名改 QuickSL，中英介绍同列一个原生 Description；修正支持版本元素为 `<Version>`；三份 manifest 分别列 0.46D / 1.0.0.16 / 两者 | 三份 manifest 已改；`scripts/Package-FullRelease.ps1`、`Deploy-FullRelease.ps1` 同步读 `.Version`；`tests/ReleaseCompatibility/Program.cs` 直接调用原生 `ModLoader.ParseManifestForTools`，断言名称/ID/双语描述/版本列表，本机 PASS | 通过 |
| 2 | 详情按键统一按住逻辑，默认 `LeftShift`；绑定任意 Shift 时两侧有效；改其他键后仅新键有效；None 停用；改键当帧屏蔽；Shift 双侧等价纳入热键冲突 | `ItemDetailsInput.cs:7-22`；`Plugin.cs:140-145`、`:190-198` 冲突判断含 Shift 等价与 `IsShift`；`Panel.cs` 快捷键显示对 Shift 统一显示「Shift」；夹具 `CheckItemDetailsInput` 覆盖两侧 Shift、F10/Mouse2/Mouse3/LeftControl、None | 通过 |
| 3 | 最终 postfix 组合容器明细 + 酿酒 + 机器预测；水龙头保留连接描述并追加；未按详情键不追加；鉴定摘要中的酿酒行受详情键控制；各功能原开关与 VBE 排除不变 | `DetailedCompositionTooltip.cs:150-165`（组合 + 空内容回退原生）、`:126-148`（水龙头独立路径 + 幂等）、`TooltipHooks.cs` 已移除酿酒/机器预览、`QuickInspectionPatch.cs:161`、`ContainerRows` 仍走 `LiquidComposition.Analyze`（VBE 排除） | 通过 |
| 4 | 存档外设置：原生配置窗口 postfix 追加「打开QuickSL功能面板」，主菜单可开功能与改键，不伪装成配置项、不改设置注册表 | `NativeQuickSLPanel.cs:7-25`（仅 `Manifest.ID == Plugin.Guid` 时追加）；`Panel.cs:77-84`（`OpenOptions` 请求位）、`:134-165`（`_menuAccess` 允许菜单期显示与改键、关闭按钮仅菜单期显示）、`:522` 改键捕获条件放宽 | 通过（交互待实机） |
| 5 | 双击鉴定：移除「鉴定页已打开」拒绝；完成目标/场景/锁定/拖拽/归属/真实放大镜检查后才关闭旧页并调用真实 `magnifier.Target`；找不到合法放大镜不关闭旧页 | `DoubleClickInspectionPatch.cs`：删除 `panelOpen` 拒绝、成功后 `ui.CloseUI()` 再 `Target`、候选增加 `identifier == "magnifier"`；夹具 `CheckMagnifierBoundary` 覆盖标签机/钥匙卡/自定义工具、自身、禁用、未拥有 | 通过（开合顺序待实机） |
| 6 | 快速烧开水：独立开关默认关；只挂 `MachineHeatingPlate.HandleHeating` postfix；校验加热板第 1 体槽、父子引用、归属、液体容器、拒绝停机/酒瓶/VBE/恢复/载入/夜间/未就绪；读刚写入的 state 温度；脂合剂/酒精/水按 `isEvaporative` + `boilingPoint` 严格大于，微生物 ≥70；其他物质不动；失败回滚并刷新原生特征/容量/贴图 | `RapidBoilingRules.Remove`（`RapidBoiling.cs:11-19`，含 NaN/Inf 拒绝与严格 `>`）、`RapidBoiling.Heated`（`:41-97`，含 `_running` 重入、全部场景门控、`originals` 回滚、`WaterFeatureHelper`/`SpriteHelper` 刷新）、`HotplateRapidBoilingPatch`（`:100-106`）；`ReleaseCompatibility` 断言挂点只指向 `HandleHeating`；单测覆盖阈值/边界/排除/无效温度/动态 `boilingPoint`；Mono 夹具验证原生字段合约与开关隔离 | 通过（热处理效果待实机） |
| 7 | 更新简报：正常商店且各窗口关闭稳定 0.5 秒后展示；480×116 顶部居中、按画布约束；约 3 秒自动关、无多余关闭按钮、不抢 Escape；点击打开可滚动详情（原生关闭 + Escape）；扫描同层小窗口向下错开；展示即记账、重启不重复、保存失败 5 秒重试且不中断游戏 | `UpdateNoticeSchedule.cs`（0.5 秒连续就绪、`_shown` 会话内消费）、`UpdateNotice.cs:20-39`（Tick + 1 秒重试）、`:41-50`（`Ready()` 全部窗口/载入/夜间/恢复门控）、`:52-81`（窗口尺寸、隐藏原生关闭按钮、root Button 点击、`Schedule.Shown` + 配置记账 + 立即保存）、`:106-117`（5 秒重试、去重日志）、`:142-161`（详情窗口 + Escape + 画布约束）；`UpdateNoticeTests` 3 项覆盖稳定期、消费一次、持久抑制与升版后可再次提示 | 通过（点击与布局待实机） |

## 五、发现（均为非阻塞）

**P1（性能，建议顺手改）**：`UpdateNotice.Tick` 每帧都会先求值 `Ready()`，即使当前版本早已记账（`Plugin.CfgUpdateSeen.Value == Plugin.Version`）。C# 先算实参再调用，所以 `Schedule.Due(..., Ready(), ...)` 里的这串 Unity 窗口查询对已看过简报的玩家是**每帧固定开销**：`InShop()` 里三次单例访问，`Ready()` 里再八次 `Instance` / `UIPanel.activeSelf` / `blackscreen.activeSelf`。单次只有微秒级，但它是常驻的、且属于「看一眼版本号就能跳过」的类型。建议在 `UpdateNoticeSchedule` 暴露 `bool Acknowledged(string version, string persisted)`，`Tick` 先判它再算 `Ready()`；与上一轮 R3/R4 同属「省掉不必要开销」的小改。

**P2（健壮性，建议实机确认）**：`UpdateNotice.cs:65-68` 用 `_banner.root.AddComponent<Button>()`，`targetGraphic = _banner.root.GetComponent<Graphic>()`。若原生窗口 root 上没有 `Graphic`（纯容器节点），`targetGraphic` 为 null，按钮不会响应点击——表现为「简报正常出现、3 秒消失，但点了没反应」。这正好对应待实机项 5。若发现点击无效，可改为给 root 挂透明 `Image`（`raycastTarget=true`）再赋给它。

**P3（提示，可不动）**：
- `NativeQuickSLPanel.cs:14-23` 在**已经 Show 出来**的原生窗口上再 `new CustomUIBuilder(...).AddButton(...)`。能否在新窗口上叠加元素取决于原生 builder 是否按当前内容重排；实机项 1 会验证。另 `catch` 每次打开都写一条日志、未去重，若按钮长期加不上会持续刷屏，可加一个 `_reported` 标志。
- `UpdateNotice.DetailsText` 在详情键被设为 `None` 时会把「按住已停用」拼进正文，属边界文案问题，影响极小。
- 三份 manifest 的 `<Description>` 现在完全相同（beta 不再单独说明「Beta candidate for 1.0.0.16」）。这符合「用户可见介绍」的定位，版本区分靠 `<Version>` 列表，不算问题，仅记录变化。
- `RapidBoiling.Heated` 用 `List<string> keys` + `Contains` 去重，成分数量个位数，O(n²) 可忽略；不必改。

## 六、只读与安全结论

- 快速烧开水是唯一会写状态的路径，且只写**已被原生加热写入温度的那个液体容器**的液体标签；失败回滚原 `TagState`（含 `valueEnabled`），随后调用原生特征/容量/贴图刷新，不碰存档文件、不新增保存调用。
- 详情 tooltip、机器预测、酿酒标注全部只读；`TooltipHooks` 移除酿酒/机器预览后，原生 hook 只剩水质行，按住详情键才组合完整明细，未按键时原生提示不受影响。
- 双击鉴定只调用真实 `magnifier.Target`，要求 `identifier == "magnifier"` + 拥有 + 未禁用 + `MayTarget/CanTarget`，不会命中标签机/钥匙卡的目标回调。
- 存档外设置只追加按钮并打开本 Mod 自己的面板，不改游戏设置注册表；存档相关按钮沿用原有「无存档拒绝」门控。
- `ReleaseCompatibility` 本机重跑：能力类别仍只有 `files`，`HardFlags` 空，挂点从 90 增到 92（+2：`ModConfigWindow.Open`、`MachineHeatingPlate.HandleHeating`），字段数不变。

## 七、待实机验收（与交接一致，此处只标我的关注点）

1. 模组页显示 QuickSL、两版支持与中英描述；主菜单齿轮窗口里新按钮是否真的可见可点（对应 P2/P3 的布局疑问）。
2. 左右 Shift、其他键、松开恢复；容器长名称与试剂三列对齐（上轮已知风险，未改测宽逻辑）。
3. 鉴定页打开时双击另一可鉴定物品：旧页关闭、新页开启；无放大镜/被禁用/不可鉴定不误关。
4. 烧开水四类物质阈值边界、混合酒精+水在酒精阈值只去酒精、空瓶容量/名称/贴图、化学总开关关闭立即停止。
5. 简报首次出现、3 秒关闭、点击展开/滚动/关闭、跨重启不重复；不要直接改玩家存档。

## 八、发布前清单

1. 无需修任何代码；P1 若愿意顺手改，属 5 行内改动，可放本轮。
2. 干净源码重建 → 双 profile 合并（4894→2187、808448）→ 最终扫描（92 挂点/76 字段、仅 `files`、`HardFlags` 空）→ 部署 → 核对 `deployment.json` 与安装目录哈希、90 个存档文件未变。
3. 版本 0.9.6 → **0.9.7**（三处 manifest `ModVersion`、`Plugin.Version`、`MelonInfo`）与 `AGENT.md` 同步；`Updates.LastShownVersion` 保持不清空，升版后每位玩家会看到一次新简报。
4. 提交前确认 `research/` 下未跟踪的交接文档与两份 review 报告按惯例一并纳入或不纳入（保持既有习惯）。
5. 按既有流程 push（不 force），不自动使用协作插件上传。
