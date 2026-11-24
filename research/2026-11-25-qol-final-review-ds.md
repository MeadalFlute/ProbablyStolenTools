# 2026-11-25 QoL 最终候选 DS 审查

审查对象：工作区未提交改动，相对 `0815d69`（正式 0.9.7 发布后、本文档前）。交接依据 `research/2026-11-25-qol-final-review-handoff.md` 及 `AGENT.md` 05:29 记录。
审查侧重：安全边界、快照/业务图洁净度、性能、与既有开关的交互；本轮未操作实际游戏。

## 结论

**可以进入玩家实机验收，没有必须阻断发布的代码问题。** 静态边界（快照目标、业务图、原生字段）没有被新 UI 代码污染，新增挂点全部落在 UI 输入/窗口层；101 挂点 / 76 字段 / 仅 `files` 复核通过。

需要在实机确认的仍是交接里那 7 项，其中我建议把 **VBE 拖放入桶** 提到第一位（原因见 P2-2），并把 **Shift 单击与置顶/固定明细的并发** 补进第 2 项。

需要修正的是文档与代码的两处不一致（P2-1），以及三条非阻断建议（P2-3、P3-1～P3-4）。

## 复核记录（本轮实际执行）

| 项目 | 结果 |
| --- | --- |
| `dotnet build` Release | 0 警告 / 0 错误 |
| `CheckpointTests` | **467 项**通过 |
| `OptionalModCompatibilityTests` | **22 项**通过 |
| `NativeSaveHistoryTests` | 14 项通过 |
| 游戏 Mono 离线夹具（原版 + 合成 VBE 标签） | 通过，含本轮新增的 VBE 配方守卫、模组仓准入、小容器明细、固定提示生命周期 |
| `ReleaseCompatibility` 扫描 | **101 挂点 / 76 字段**、`SCAN Capabilities: files`、`HardFlags` 空 |
| 当前源码重建 SHA256 | `2A8F30EA4FA6E0208A666451A7E4C52FACAAEFFC256A8B2207AD71BE0533CDDD`（连续两次构建一致，可复现） |
| 已部署主 DLL | `2A8F30EA…`，与上述重建逐字节相同 |
| 已部署 helper | 808448 bytes / `6129A2D3…`，双 profile；与 `AGENT.md` 05:29 记录一致 |
| 部署记录 | `ExistingSaveFilesUnchanged=true`、`GameStarted=false`、`InGameValidated=false` |

补充说明：

- 05:21 记录里的 `D1956D3A…` 与 05:29 的 `2A8F30EA…` 差异已被 `AGENT.md` 解释为 05:28 误带单 beta helper 的中间部署；我按当前源码重建得到的是 `2A8F30EA…`，**以 05:29 部署与文档为准**。
- 真实 VBE DLL 不在本机 `Mods` 目录，也未在 `out` 下找到副本，因此我只跑了 `PSTOOLS_TEST_BREWING_DLL` 未设置时的合成夹具；"真实 VBE DLL 回归通过"这一条仍以 05:13/05:21 记录为准，本轮没有独立复现。
- 审查期间你仍在部署（`full-release-deploy-20261125-052805`、`052936`），若定稿前还有代码改动，扫描与哈希需重跑。

## 发现

### P2-1 文档与代码不一致（发布前必须对齐）

1. **公告修订号**：源码 `UpdateNotice.NoticeId => Plugin.Version + ":20261125-qol-r3"`（测试也用 r3），交接文件第 7 行仍写 `:20261125-qol-r2`。
2. **公告节数**：源码 `DetailsText()` 现有 **7 段**（磁吸、置顶、酿酒、机器模组槽、化学辅助、更新公告、VBE 入桶），交接第 50 行写"本次六节"。VBE 段是 05:29 追加的，交接未回填。

这两条都只影响记录可信度，不影响运行；但如果后续照交接复述给玩家会造成说明与弹窗内容不符。

### P2-2 `GameItem.CanTarget` 全局后缀是这批最大行为风险

`BrewingIngredientTargetPatch` 在 `GameItem.CanTarget` 上做 postfix，营养果/梦帽菇对桶装水/酿酒容器一律返回 false。原版 `CanTarget` 是**拖放接受门的公共查询**：不仅是 EndDrag 后续 Target/Dissolve 的入口，也可能被 VBE 自己的拖放处理当成"这个目标是否可放"的判断。

- 如果 VBE 的入桶走的是原生拖放（玩家把果子拖到桶上），被我们拦掉后可能连 VBE 的处理都收不到，表现为"营养果再也放不进桶"——这正好是本次要修的反面。
- 如果 VBE 走自己的配方槽/独立插入，则当前实现安全。

我的建议：实机第一项就验证「装 VBE 时首次把营养果拖进桶装水，能否正常被 VBE 接收并生成状态」；若不能，把守卫收窄成只拦 `ChemicalHelper.DissolveInto` 与原生 `Target`，不动 `CanTarget`。合成的离线夹具只证明"返回值被改写、无关原料不受影响"，证明不了 VBE 的接收路径。

`MayInsert` 与原生 `MayHaveValidInventorySlot` 的对照我做了反编译核对：除了被刻意省略的 `IsInsertLocked()` 与 `HopperHelper.MayBypassGridRules`（保守方向正确），所有权、旋转尺寸、双方 `mayInventoryAddItemFunc`（原生同样是 `BoolReduceOR`）、父级判定完全一致。这块没有问题。

### P2-3 普通 tooltip 每帧跑完整机器投影，建议缓存

`TooltipHooks` 现在对每次 `GetTooltip` 都调 `FacilityPreview.AddTooltip(..., warningsOnly: true)`，机器类物品会为此执行 `FacilityProjection.Purify/Evaporate/Consume` 的数组克隆与循环。悬停期间 tooltip 重建频率若为每帧，等于把原来"按住键才算"的算术搬到常驻路径。

建议：按 `(item, machine state)` 缓存 0.2～0.5 秒，或只在 `GetTooltip` 首次与状态标签变化时计算。这不影响正确性，但符合本仓库"简化防御换运行时效率"的一贯取向。

### P2-4 净水器警报的两种顺序问题（UX）

1. 空瓶/纯水（无实际去除量）都会提示「净水完成，需换水」，空瓶其实没水可换。
2. `CheckPower` 被移到 `HasRemoval` 之后：机器没电时也会先说「净水完成，需换水」，而玩家真正要处理的是断电。

建议改成：先 `CheckPower`，再判断 `total <= 0 || water < 0` 显示「无」，最后才是"无去除量 → 需换水"。安全性没有问题：`Purify` 对 `water < 0` 直接返回克隆，`HasRemoval` 随即为 false，不会走到 `result[water]` 的越界。

### P3-1 取消置顶后标题宽度不回滚

`NativeWindowGestures.Decorate` 会把标题 `sizeDelta.x` 扩到文字宽度，`Restore` 只还原 `text` 与 `overflowMode`。若原生 `Validate()` 不重算标题宽度，取消置顶的长标题窗口会保留被撑宽的标题区域。建议在 `Restore` 里同时记录/恢复原始 `sizeDelta`。

### P3-2 固定明细的挂载点与最大宽度

`PinnedCompositionTooltip` 用 `SetParent(owner.rectTransform, false)` 把自绘元素挂到**窗口根**。这与"不进原生业务图"的声明一致，但如果某个窗口根带 `VerticalLayoutGroup`，布局组仍会把它当子项排布、并在窗口 dirty 时把它挪走。

另外 `SetRichText(content)` 未传 `maxWidth`，成分行较长（多液体、长名称）时元素宽度可超出 `area.rect.width`；`PinnedTooltipLayout.Place` 只夹了 x 不缩宽，右边界会溢出。

建议：挂到窗口根下不参与布局的子节点，或给 `Place` 加宽度上限并传 `maxWidth`。

### P3-3 `Tick` 的每帧开销

`PinnedCompositionTooltip.Tick` 每帧对每个固定项做 `Containers.Exists(w => …)`（闭包分配）+ `FindParentWindow()` 向上遍历。通常 0～2 项，可接受；若以后允许固定更多，建议降到 0.1～0.25 秒节拍，并把 `Exists` 换成显式 for 循环避免闭包。

### P3-4 部署脚本缺少 helper 形态断言

05:28 那次把单 beta（670208）helper 部署进去，是人工发现后重部署修正的。`deployment.json` 已经记录了 `StaticLayoutsSHA256`，建议部署脚本在写盘前直接断言 `length == 808448 && sha == 6129A2D3… && modules == 4`，把这类事故挡在部署之前。

## 逐条对照交接声明

| 交接声明 | 复核结果 |
| --- | --- |
| Shift 磁吸/置顶仅处理可移动原生窗口、同父层、物理 Shift | 代码核对通过；`OnDrag` 前缀在物品拖动时因位置未变直接返回 |
| 置顶只调 Transform 兄弟顺序，不改 `focusStamp`/`visibleWindows` | 代码核对通过；副作用是视觉顺序与原生列表顺序分离 |
| 固定明细只持 Mod 所有权，外层容器关闭即清除 | 代码核对通过；离线夹具覆盖外层关闭、无关窗口隔离、重置释放 |
| 净水警报按同一次 Purify 结果判定 | 代码核对通过，与新增单测一致 |
| 公告独立分隔线、无空白段、计数条 6px/5 秒 | 代码核对通过（`Sections` 去空行 + 每段间 `AddSeparator`，锚点 0→1 填充） |
| 467 参考 / 22 可选 / 101 挂点 / 76 字段 / 仅 files | 本轮全部复跑通过 |
| 真实 VBE 离线回归通过 | **未独立复现**（本机无 VBE DLL），沿用记录 |
| 91 个存档文件、设置、程序集未变；未启动游戏 | 抽查 `deployment.json` 一致，未单独逐文件比对 |

## 实机验收建议顺序

1. **VBE 首次营养果/梦帽菇入桶**（对应 P2-2，最高风险）。
2. Shift 单击置顶：重复取消、多窗口顺序、普通聚焦、菜单/模态层；缩放后标题空间与棕色标记。
3. Shift 拖动磁吸：不干扰物品拖动与原生 Shift 双击转移。
4. Shift 单击固定成分明细：跟随物品与转移后窗口、滚动/重叠/裁剪/射线、外层关闭清除、加载与回溯清除。
5. 净水器：无去除量两种 tooltip 均警报；可去除时只在详情键下出现完整预测。
6. 公告：只展示一次、六/七段详情、分隔线与 6px 计数条（你已确认可见）。
7. 机器模组仓：运行中放入/取出、电池与原料仓仍不可动、关闭设置立即恢复。
8. 建立/恢复节点后确认固定提示与置顶装饰无残留、无物品视觉异常。

上述 1、4、5 项通过后，本批即可按 0.9.8（或你计划的版本号）收尾；`P2-1` 建议在提交前顺手改掉，`P2-3`/`P3-*` 可留到下一轮。

## 回执复核（2026-11-25 05:45 追加）

读 `2026-11-25-qol-final-review-receipt.md` 并自行复核，**撤回三条、更正一条、降级一条**，其余维持。

### 撤回

1. **P3-1 取消置顶后标题宽度**：反编译 `PixelWindow.Validate` 后确认，标题宽度由 `LayoutUtility.GetPreferredWidth(titleText.text.rectTransform)` 重算（`Validate` 先把 `titleText.text.text` 写回 `titleString`）。`Restore` 的「先恢复原文再 `Validate()`」已经把宽度收回，不存在留宽问题。原建议作废。
2. **P3-3 `Tick` 闭包分配**：`Containers.Exists(w => w.handler == null || !w.IsVisible())` 不捕获外部变量，C# 会缓存该委托，**没有每次调用的分配**；真实开销只剩 `FindParentWindow()` 的父链遍历。原表述不准确。
3. **P2-3 的范围**：`Evaporator` 在 `new bool[Liquid.Liquids.Count]` 与 `Evaporate` 之前就因 `WarningsOnly` 返回，`Hydroponic` 也在生长循环之前返回；warningsOnly 下只有**净水器**会执行 `Purify`（这是玩家指定判据所需）。原句"机器类物品都会执行 Purify/Evaporate/Consume"作废，剩余建议仅为"若实机悬停卡顿再针对净水器优化"。

### 更正我自己的归因

主 DLL `D1956D3A`（05:21）与 `2A8F30EA`（05:28/05:29）的差异**来自源码变化**（进度条方向 + 追加 VBE 文案），与单表 helper 误部署无关：`052805` 与 `052936` 的主 DLL 相同（都是 `2A8F30EA`），只有 helper 从 `509D8A98` 改为 `6129A2D3`（808448 双 profile）。本文上节"已解释为误带单 beta helper 的中间部署"一句对主 DLL 的归因不成立，以回执为准。

### 降级

- **P2-2**：回执反编译本机 VBE（Workshop `3842506016`）确认 `SetupWindow` 自建 6×6 `GameGridInventory` 原料仓，并以 `mayInventoryAddItemFunc` 只接收自家 `nutrifruit`/`dream_cap`，**不调用 `CanTarget`**；原生 `ItemMouseDragHandler` 是入仓查询成功后才独立判 `CanTarget`→`Target`。因此保留守卫合理，我不再主张收窄；风险降为"首次入桶实机确认"，并注记该结论只覆盖这一版扩展。

### 维持

- **P2-1** 已按回执修正（r3、七节、05:29 部署哈希）。
- **P2-4** 维持个人 UX 意见（空瓶/断电时的提示顺序），按玩家既定需求不改，无异议。
- **P3-2/P3-4** 保持建议，接受"未复现不改""不把 808448/`6129A2D3` 硬编码为通用规则"的理由；可改为按 manifest 声明版本集校验 profile 覆盖。

### 本轮我补跑的证据

- 真实 VBE：`E:\SteamLibrary\steamapps\workshop\content\4348910\3842506016\VanillaBrewingExpansion.dll`（46592 bytes，SHA256 `5253C36F…`）存在；设置 `PSTOOLS_TEST_BREWING_DLL` 后用隔离副本 `out/20261125-qol-review-deployed/` 复跑离线夹具，全部 PASS，其中包含 `real expansion callbacks=True (retained, never invoked)`，证明确实加载了真实扩展程序集。此项现在由我独立复现，不再是"沿用记录"。
- 部署对照：`052805`（main `2A8F30EA` / helper `509D8A98`）与 `052936`（main `2A8F30EA` / helper `6129A2D3`），与回执一致。

### 结论不变

本批仍为"可进入实机验收、无发布阻断项"。实机顺序按回执更新后的清单执行，**VBE 首次入桶与固定明细/置顶并发**这两项是玩家确认前不应宣称整批通过的关口。
