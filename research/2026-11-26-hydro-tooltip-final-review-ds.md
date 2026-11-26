# 2026-11-26 QuickSL 强制详情与水培预测 DS 最终审查

审查对象：整个 dirty 工作区（基线 `817af11`，版本仍 **0.9.8**，含未跟踪源码/测试），入口 `research/2026-11-26-hydro-tooltip-final-review-handoff.md`。本轮未启动游戏，未改代码（只读核对 + 独立重跑构建/测试/夹具）。

## 结论

**没有发现发布阻断级代码缺陷。** 交接里的哈希、测试数、挂点数、408 组分配等验证记录全部可复现，且当前源码重建出的主 DLL 与 18:28 部署**逐字节一致**。需要你在发布前决定的是两条可用性/验收事项（P2-1、P2-2），其余为可选改进。

## 独立复核记录

| 项目 | 我的结果 |
| --- | --- |
| Release 构建 | 0 警告 / 0 错误 |
| 主 DLL SHA256 | `41208059DA12F27492D83A3D5111920B7C58C79D4027C35F4450D29B2F5FC754`，**与部署完全一致** |
| `CheckpointTests` | **478 项通过** |
| `OptionalModCompatibilityTests` | 22 项通过 |
| 官方能力扫描 | 105 挂点 / 76 字段、`Capabilities: files`、`HardFlags` 与 `Error` 空 |
| 游戏 Mono 夹具（原版） | 28 项 PASS |
| 游戏 Mono + 真实 Proper Signboards 1.0.48 + 真实 VBE | 28 项 PASS，含 `hydroponic forecast: 408 deterministic size splits match native`、`item-details binding`、`smart extrusion admission`、`Proper Signboards 1.0.48` |
| 安装目录 | 主 DLL `41208059…`、helper `6129A2D3…`/808448 字节，与交接一致 |
| 部署记录 `out/full-release-deploy-20261126-182854/deployment.json` | 95 个存档文件、`ExistingGameAssembliesUnchanged=true`、`SettingsImported=false`、`GameStarted=false` |
| `git diff --check` | 无空白错误（仅既有 CRLF 规范化警告） |

结论：交接中"`478` / `22` / `105 挂点 76 字段` / `41208059` / `6129A2D3` / 95 存档"均属实；18:28 之后的准备阶段确实只动了文档。

## 发现（按严重程度）

### P2-1 公告"已读"在窗口创建成功时即持久化，仍无法区分"真的看见"（建议发布前决定）

`UpdateNotice.ShowBanner()` 的顺序是：`builder.Show()` → 判 `_banner.IsAlive` → 排版 → `Schedule.Shown(NoticeId)` → 写 `CfgUpdateSeen.Value` → `SavePending()`（`UpdateNotice.cs:96-131`）。

也就是说只要窗口**创建成功**就记已读，之后即使它被原生界面盖住、被场景重置销毁，玩家也永远失去这条公告。交接自己点出的边界（"已创建窗口不等于玩家实际看见"）在代码里确实存在，而且 r3 的可见性尚未实机确认；`[Updates] displayed notice=` 这行日志是在创建时写的（`UpdateNotice.cs:123`），所以日志同样**不能**证明可见性。原生晨报在 `EmporiumEntry.Start` 同步打开，正是最可能遮住刚创建横幅的时机。

建议（任选其一，都不需要改触发门控）：
1. 在 `Tick` 里等横幅存活满 2–3 秒（并 `IsAlive` + 父面板 active）再写已读，同时在那一刻补一条 `[Updates] notice visible for Ns` 日志，让下次日志能区分；
2. 保留"显示即已读"，但横幅在 2 秒内被销毁时回滚本次已读并允许下个就绪窗口重试。

不改也不构成数据风险，但若 r3 实测仍未看见，问题会永久无法自证。

### P2-2 灯牌长时间故障时，自动内存节点会"静默停止"（建议加一条可见提示）

`OptionalCaptureReady` 现在守住了 5 条自动边界（`CustomerCheckpoint.cs:417、522、550、586、601`），逻辑本身正确：`ReturnGate.Poll(false)` 只清 `_stableSince`、保留 `Pending`（`CheckpointReturnGate.cs:17-25`），`_settledSince`/`_captureSince` 归零后会在就绪时重新计时，边界不会丢。

问题在可见性：等待原因只在 `EventLog` 记一次（`SignboardCompatibility.cs:115`），游戏内唯一提示是**手动**捕获时的 `PanelNotice`（`CustomerCheckpoint.cs:589-592`）。如果 Proper Signboards 进入持久 `fault`（第三方检测到异常后很可能一直保持）或 `owner/run` 不匹配，自动节点就永久不再生成，玩家看到的只是"回溯列表怎么不涨了"，没有任何解释——这与"不停用普通内存节点"的设计意图相悖。

建议：同一原因持续等待超过 10–15 秒时，用已有的 `CompatibilityNotice` 弹一次"灯牌模组忙碌/故障，自动检查点暂停"，并保留手动提示；这类提示不涉及第三方反射，也不影响恢复严格性。

### P2-3 强制提示没有任何离线覆盖（测试缺口，交接已承认）

夹具 28 项里没有 `ForcedItemTooltip`（只有 `item-details binding` 覆盖按键态），而它新增的是**每帧输入 + 渲染覆盖**路径：目标选择、群拖排除、松键清理、场景/载入生命周期、与原生提示的优先级全部只能实机验证。建议至少把"目标是否可显示"的判定抽成纯函数（输入：拖拽项/群拖集合/目标/是否有详情键），在 `CheckpointTests` 里覆盖自身排除、群成员排除、无 tooltip、非就绪状态。

## 可选建议（P3）

1. **换行宽度与原生不一致**：`ForcedItemTooltip.cs:39` 把 `maxCharWidth` 写死 `100`，原生按目标类型取 60（普通）/72（机器）/90（喂食器）/120（手动机器）（`ItemTooltipHandler.cs:75-87`）。因此强制提示会比原生悬停更宽；又因为原生 `ShowTooltip` 只在内容变化时才 `SetRichText`（`OverlayHandler` 的 `previousRichTextCache`），同一目标有时沿用 60、有时是 100，宽度会跳。建议复刻原生的 tag 选择。
2. **每帧射线分配**：`ForcedItemTooltip.Tick` 每帧调用 `RenderHandler.RaycastElement`（`RenderHandler.cs:491` 内 `new PointerEventData` + `new List<RaycastResult>` + `RaycastAll`），按住详情键拖拽时是每帧 GC；100 ms 缓存只作用于 `GetTooltip()`。若要优化，可 8–15 Hz 采样目标、换目标时立即刷新。
3. **文本输入未排除**：`ItemDetailsInput.Held` 只看按键，重命名输入框里按住 Shift 打大写也会弹强制提示；建议加"当前有 `TMP_InputField` 焦点则跳过"。
4. **`RefreshSaveCache` 的 finally 可能 NRE**：`SignboardCompatibility.cs:194-200` 在 `Commit` 之后直接 `entry.Item.state.dict[...]`；若第三方 `Commit` 销毁了挂位物品，`state` 变 null 会让 finally 抛 NRE 覆盖掉原本的 `Commit` 异常信息。建议 finally 里同样判空跳过。
5. **尺寸名回退文案未经验证**：`SizedItemName` 首次使用 `item_size_*_of` 键（`FacilityPreview.cs:424-432`），zh 回退沿用"小型/大型/巨型{0}"；真实 zh 文案需实机核对。另外 `string.Format` 若原生模板含其它占位符会抛 `FormatException`，被上层 catch 后整个水培提示消失，建议 `try` 兜底纯回退。
6. **荧光莓 -1 的文案**：原生 -1 是"未熟/过熟"三态（`InsBerryUI.cs:34-52` 的 under1/under2/over1），提示统一写"过熟/Overripe"（`FacilityPreview.cs:397`）不够准确，建议改"成熟度异常/Under- or over-ripe"。
7. **r3 公告未提 VBE**：8 节里没有 Vanilla Brewing Expansion（r1/r2 有），从 0.9.7 升上来的玩家只会看到 r3。若 VBE 兼容仍算 0.9.8 的玩家可见新增，公告可补一句；工坊描述里已有。

## 核对成立的关键点

- **收获分配**：`HarvestSizes` 与原生 `SplitByHealth`（`PlantHarvest.cs:38-92`）逐分支一致，`allowHuge`/`qualityBonus` 语义对得上；各作物的 `reduceCount`/比例（nutrifruit `false`、kotton/dream_cap `true`、coffee `.5/.6/.7/.8`）与 `PlantList.cs:133-258` 一致，`GetAjustedQuality` = `Quality/ratio`（`ModuleHelper.cs:279-287`）与 `count` 公式（`HydroponicHelper.cs:217-227`）也一致。离线 408 组对照 + 边界用例（15/30/50/70/80/90/95/99）都能对上。
- **荧光莓概率**：`BerryProbabilities` 四段与 `RollBloomberryCondition`（`PlantList.cs:273-313`）以及 `InsBerryUI` 的 -2/1/2 映射一致（-2 发霉、1 正常、2 小果），概率质量各自为 1。
- **污染/净化**：`PollutingShare/PurifyingShare/POLLUTION` 的 .5/.35/.15 与截断顺序和原生 `Pollute`（`PlantWaterQuality`）一致；两个性状互斥（`Exclusive("water_quality")`，`PlantTraitList.cs:164-173`），且都在**每周期一次**的 `RunGrowTickHooks` 里执行，与投影"先跑 tick、再消耗、再性状"的顺序一致。
- **生长评分**：`HasCharacter('\u2192', true, false)` 只查询不添加字形（`tryAddCharacter:false`），阻塞/缺水/未工作时只显示当前值；`GrowthScore` 单行两端，含"不变也显示两端"的用例。
- **水培剩余量**：固定两位小数、uL/mL/L 阈值，且不影响通用成分表（`CompositionDetails.Amount` 仍 6 位）。
- **营养液判据**：水严格最大、矿物质/(水+矿物质) ≥ 1.5%、六类污染物**合计** ≤ 总液体 1%（含 long 分母与边界用例），且 `NoRename` 的滴管/量筒/<150 mL/VBE 排除在营养液分支之前，未绕过。
- **详情键**：`AddDetailsHint` 在 `ItemDetailsInput.Held` 为真时跳过，键名按实际绑定显示，禁止递归构 tooltip；强制提示取到的是被 `FinalizeTooltip` 修补后的详情版本。
- **灯牌**：`CaptureReady` 的就绪判定覆盖未初始化/owner-run/busy-dragging/fault，原因去重记日志；手动提示双语；`SceneUnloaded`/`Commit` 已校验 static + 零参数（真实 1.0.48 通过夹具 `Root` 非空证明）；Commit 失败抛出的是"原生图已写、缓存失败"的明确诊断，`finally` 恢复保留的 `TagState` 身份；空 `state` 已跳过。
- **公告内容**：8 节双语、`Revision = :20261126-chemistry-compat-r3`、r2→r3 独立确认、5 秒进度条、真实分隔线；中文与英文条目一一对应，`[置顶]/[PIN]` 与 `WindowGestureRules.PinLabel` 实际文案一致。

## 实机验收建议（未覆盖项）

1. 强制提示：普通物品、液体容器、机器、群拖、拖拽中松键、切场景/读档、暂停与面板打开时不残留。
2. 水培：营养液改名（1.5%/1% 边界）、生长评分箭头与 `-->` 回退、剩余量小数、四段荧光莓提示、kotton/dream_cap 尺寸名（中文回退文案）。
3. 公告 r3：进档后是否真的看见（这是 P2-1 的关键输入）；看见后重启不再出现。
4. 灯牌：摘/挂后回溯、回溯后保存重载；`fault` 状态下自动节点是否出现 P2-2 的静默现象。

## 发布判断

- 代码层面：**无阻断项**；`478/22/105-76/files/41208059/6129A2D3/95 存档` 全部复核通过，源码可重建出与部署一致的 DLL。
- 需要你决策：P2-1（公告消费时机）、P2-2（灯牌持久故障的可见提示）、P2-3（强制提示的离线测试缺口）。
- 本轮未提交、未升版、未推送、未改玩家设置/存档/第三方 Mod。
