# Probably Stolen Tools — 交接文档

> 适用源码：正式版 **0.8.13**（2026-11-11，含待发布的报告修复与分步快照优化）。平台为游戏自带 **Mono / IMod / Harmony**；MelonLoader / IL2CPP 路径已弃用，`#if !MONO` 分支只保证能编译，不再维护。
> 旧版本文档（0.2.1–0.6.0、Demo、性能优化过程）已归档到 `research/archive/AGENT-0.6.0-history.md`，**不要**按它描述的环境或版本操作。

本文只写当前仍然成立的事实。历史细节看 `research/`，实现细节以源码为准。

---

## 1. 快速开始

```powershell
# 构建（需 src\ProbablyStolenTools\local.props 指向游戏目录）
dotnet build src\ProbablyStolenTools\ProbablyStolenTools.csproj -c Release

# 离线测试
dotnet run -c Release --project tests\CheckpointTests\CheckpointTests.csproj
dotnet run -c Release --project tests\ReleaseCompatibility\ReleaseCompatibility.csproj -- `
  "<游戏目录>" src\ProbablyStolenTools\bin\Release\Mono\ProbablyStolenTools.dll release\manifest.xml

# 部署（必须先关闭游戏）
.\scripts\Deploy-FullRelease.ps1
```

当前基线：**97 项**离线测试；正式版 **55 个** Harmony 挂点、**73 个**快照字段；`HardFlags` 为空。离线通过 **不等于** Unity 活体验收。

### 版本与发布

- **唯一真源**：`release/manifest.xml` 的 `<ModVersion>`；`Plugin.Version` 必须同时改。
- 规则：**大版本、中间版本手动决定；小版本每次 `git push` 前 +1**（0.8.12 → 0.8.13）。
- README / AGENT 等纯文档更新**不推进版本号**。
- 发布流程：改两个版本号 → 构建 → 两组测试 → 部署 → 游戏内验收 → 提交 → 推送。
- 快照优化按步骤独立提交；用户要求每轮验证通过后部署，便于游戏内测试，不自动启动游戏。未推送的测试轮次保持现有版本号。
- 推送需要用户临时 token；网络不稳时用带超时的 `git push`，交替 `http.sslVersion`（tlsv1.2 / tlsv1.3）与 `http.version`（HTTP/1.1 / HTTP/2），必要时用 GitHub API 确认远端 HEAD。

---

## 2. 目录与关键文件

```
src/ProbablyStolenTools/           Mod 本体
  CustomerCheckpoint.cs            顾客/手动节点：捕获、恢复、分支截断、VisualState
  Rewind.cs                        F5/F7 文件节点（只加载不写回）
  ManagedStateAccess.cs            业务对象图遍历与字段规则
  StateGraph.cs                    槽位/对象/引用序列化与差异校验
  SnapshotFieldReader.cs           只读字段读取委托（dynamic-code）
  LiquidComposition.cs             成分、纯度、评级、空瓶/容量判定
  AnnotationLabels.cs              原版水质/化学/酿酒语言键与回退文本
  WaterQualityPatch.cs / WineAppraisal.cs / TooltipHooks.cs / TooltipNamePatch.cs
  Panel.cs / PanelModel.cs / NativeUi.cs / LogWindow.cs
tests/CheckpointTests/             快照往返、布局与渲染回归
tests/ReleaseCompatibility/        对正式版 DLL 校验挂点与私有字段
scripts/Deploy-FullRelease.ps1     备份 + 部署
release/manifest.xml               ID、版本、能力声明
research/                          设计、逆向、问题记录（archive/ 为历史）
docs/workshop/                     创意工坊文本
```

---

## 3. 快照引擎硬规则

- 只遍历**游戏业务对象图**。Unity 对象（`UnityEngine.*`）与纯视觉字段不进业务快照，由 `VisualState` 单独保存。
- `GameItemElement` 的原生布局缓存**必须排除**在 Mono 业务快照之外：
  `appliedMinX`、`appliedMinY`、`appliedOrientation`、`appliedGridSize`、`appliedFlipped`、`validateLock`。
  只恢复这些缓存而不恢复实际 RectTransform，会让 `UpdateRectTransform()` 误判“无需布局”，表现为拖拽后回溯贴图不归位。
- **数组偏移必须在元素内部计算** `offset = i * stride + j`，并记录 `Storage.Index = i`。把偏移提到循环外会让所有元素都读成第一个元素，而且 `diff=0` 也可能掩盖（读写共用同一错误读取器）。
- 普通字段用反射读；同一路径累计读取 32 次后尝试生成只读委托，失败则回退。字段/类型缓存上限 4096，跨节点复用；每份快照的值/根/数组索引缓存相互隔离，卸载时清理。
- 快照上限可配置：`Snapshot.MaxObjects / MaxSlots / MaxBytesMB`，默认 **200000 / 3000000 / 256**。
- 超限跳过本次捕获，释放半成品并保留旧节点；不使用不完整快照，不自动无限加大上限。面板显示失败、提示去重，日志包含实际计数、各上限及来源路径。
- 对象上限在新身份入队前检查（包括显式根）；重复、空引用和环不重复计数。失败日志 `objects` 为已经展开的对象，`discovered` 为已发现数（对象超限时包括刚拒绝的一个），`pending` 为尚未展开的已接收对象。不能等出队时才检查，否则宽图会提前堆积超限队列。
- Mono 数组通过 `IBudgetedStateDescription` 在逐元素描述分配前检查剩余槽位/逻辑字节预算。提前拒绝时计数是已捕获部分，`array requires ... before expansion` 单独记录整个数组需求；不是实际内存估算。成功快照仍保留全容量，空数组和非法数组规则不变。
- 同一 Mono 访问器内，数组类型与长度都相同的只读描述可复用；缓存最多 4096 种形状、累计 65536 个描述槽，超过后正常展开，已有缓存仍可命中。每个数组的值/引用/对象身份分别保存，预算按每个数组计，不因复用打折；不跨节点共享。CAPTURE 追加 `arraySchemas(cached=...,hits=...,slotsReused=...)`，仅表示描述复用量，不是减少了业务槽位或节省了等量字节。
- 捕获仍在同一回调同步完成；**未启用分帧快照**。任何跨帧实现须独立提交，并解决业务图一致性，不能只靠 `timeScale=0`。
- 捕获后必须跑完整 `Differences()`；差异非空就视为恢复失败，不能再报成功。
- Mono 标量校验走 `IStateValueComparer`，只比较捕获值，不往节点值表登记新值；float/double 按位比较，保留正负零和 NaN 差别。其他后端仍走原字节比较；差异消息达到上限也必须扫描完整图。
- `Storage`、`StateGraph.Entry` 是只读值类型；普通字段索引为 `-1`，不能依赖结构体默认值 `0`。

---

## 4. 恢复流程（当前实现）

顺序不能乱：

1. 还原业务图（`StateGraph.Restore`，必要时第二次还原，因为原生 Validate/UI 刷新可能改动业务图）。
2. `foreach (var view in target.Visuals) view.Restore();`
   `VisualState` 保存并还原：父级、`anchorMin/anchorMax`、`pivot`、`sizeDelta`、`anchoredPosition3D`、`localScale`、`localRotation`、激活状态，以及 Image 的 sprite/color/`enabled`/`raycastTarget`。
3. `RestoreItemGeometry(target)`：按还原后的 `modifiedShape` / `modifiedXOrigin` 调一次原生 `UpdateRectTransform()`，并写回物品正常层与拖拽层的尺寸、锚点和背景原点；若写入后仍与期望不符，直接抛异常（不要静默成功）。
4. `foreach (var view in target.Visuals) view.RestoreTransforms();`
   以快照捕获的原生几何作为**最终**朝向/尺寸依据。旋转物品必须靠这一步，否则 `UpdateRectTransform()` 会用被清零的 `modifiedShape.orientation` 把已旋转贴图覆盖成未旋转。
5. 校验 `Differences()`；最后才丢弃/清理不在快照里的视图。

渲染父级一律用捕获时的真实 `TreeNodeRender.parent`：库存可能通过窗口或其他视图宿主渲染，**不要**把所有物品强行挂到 `parentInventory.inner`。

**不要**在恢复流程里重放库存接收、交易、顾客到达等业务回调，只刷新视觉面（`NegociationUIManager.RefreshUI()`、`BargainUIManager`）。

---

## 5. 节点时机与门控

- 顾客节点必须建立在**开场对话播完之后**（`StoreClient.isMainDialogueStarted`，8 秒兜底）。卖货顾客的商品在开场对话里才摆上柜台，抢跑会导致回溯后柜台为空。
- **不要用 `StoreUIManager.isAboutToStartMainDialog` 当门控**：它是“0.25 秒后开主对话”的瞬态标志，游戏自己的延时回调没跑完时会长期为 true，节点永远建不出来。
- `Snapshot.StoreState` 记录捕获时的 `store.storeState`。营业(1)/闭店(0) 允许；**手动节点允许在同一 state 下回溯**（含闭店后的早市 state=2）；自动顾客节点仍是 0/1。
- 允许跨顾客回溯；当前顾客不同时只写 `[QuickSL/Customer] F6 cross-client rewind`，不再拦截。`currentClientInstance` 与顾客贴图一起还原。
- 剧情/对话类顾客（`ClientIntent.DIALOGUE` 等）**跳过建节点但不清空历史**。
- 节点上限 **20**（`CheckpointHistory<Snapshot>`），面板行数 **24**，必须同步修改。手动建立的闭店节点要正常列入面板。
- settle 保留原生事件触发，并以 0.1 秒轮询兜底；日志按状态变化输出，未变化每 60 秒一条并统计压制次数。
- `captureMs` / `ms(...total=...)` 才是捕获耗时；旧 `after=...s`（新名 `arrivalWaitSeconds`）是顾客到达后的等待时间，不能用来统计卡顿。

### 不要挂的门控

`PlayerStore.isGeneScannerScanning` 是基因扫描仪**设施开关**（设施每秒重新置 true），不是“正在扫描顾客”。任何回溯/建节点/鉴定门控都不要用它（0.8.6 起已全部移除）。

---

## 6. 液体、纯度与酿酒

- 液体 tag 始终以**微升（µL）**存储：`容量 = mL × 1000`。界面不足 1mL 显示 µL 只是格式化；“满 1mL”阈值为 **1000**。
- 主成分：水占比 **> 65%** 为水为主；酒精 **> 65%** 为酒精为主；否则取“水/酒精之外占比最高”的成分；只剩水+酒精且都 ≤65% 时取占比较高的一侧。
- 显示：`成分（占比% | 纯度% | 评级）`；占比 = 该成分 ÷ 全液总量，最多两位小数。
- 纯度排除：水、酒精、脂合剂、脂肪、物理污染物、微生物、植物质、不溶性碎屑、**梦帽菇糊糊 / 梦魇菇糊糊 / 咖啡豆糊糊**；蛋白/酸/碱 ≥1000µL 才扣；化学污染物按剩余量的 0.7% 封顶。还原剂**不排除**（不可蒸发）。产物取剩余成分中占比最高者。
- 评级：≥99 纯品级；90–99 药用级；80–90 近药用级；60–80 街头标准级；45–60 掺兑级；30–45 垃圾级；<30 淀粉级（各有固定颜色）。
- 酒精标签：`酒精`；酒精 **74–76%** 为 `医用酒精`；**≥99.95%** 为 `脱水乙醇`。名字与水质行使用同一标签。
- 空瓶名字：`空瓶（迷你/小/标准/大）`（250/450/750/2000mL 档），不显示水质行。滴管、量筒/量杯、容量 <200mL 的容器**不改名**（`LiquidComposition.NoRename`）。
- 违禁酵母只加原版样式独立一行（`mech_contraband_low`，LightRed 粗体），不要打 `CONTRABAND_ITEM_TAG`，也不要拼进名字。

---

## 7. 工具提示

- 追加说明行走原生 `ModHook.OnCreateTooltipLate`（`TooltipHooks`）；不要再新增 `GetTooltipBasic` / `CreateWaterFeatureTooltip` 补丁做正文追加。
- 名称替换靠作用域：`TooltipNameScopePatch`（`GetTooltipBasic`）+ `TooltipNameScopeUsePatch`（`RichTextGenerator.UseItemOnOtherItem`）+ `TooltipNameScopeInsertPatch`（`RichTextGenerator.InsertItemInOtherItem`），作用域用**嵌套深度** `Push/Pop`。
- 作用域退出走 Harmony **Finalizer**，通过 `__state` 只退出本次已进入的作用域；不要吞掉原生异常，也不要改回仅成功时执行的 Postfix。
- `GameItem.GetDisplayName` 的后缀只在作用域内改名为成分名 / 空瓶尺寸 / 酒精标签；背包、交易列表、任务文本保持原名。
- 水质、化学成分/评级与酿酒品质优先用 `AnnotationLabels` 的原版 `Mechanic` / `Item` 键，**不再按中/英文限制标注**。缺键、空翻译、读取失败时先查英语表，再回退英文文本；有效水质 0–3499 仍为沟渠水，只有小于 0 或大于 10000 才是未知。本 mod 自有预测句、莓果品相、空瓶尺寸与脱水乙醇在其他语言下回退英文，不维护额外翻译表。
- 标注按 locale/table/key 缓存最多 2048 条，语言变化清空；加载中和读取失败的临时兜底不缓存。条目使用原生 SmartFormat 与来源语言 Formatter；不改用会 `WaitForCompletion()` 的同步取词，不强改项目 fallback 链。
- 本地化未就绪时保留原版名称/提示，不修改游戏语言、表或存档。Mono 只读已完成的 `SelectedLocaleAsync` 和 `GetTableAsync`，仅格式化已加载的条目；不调用同步等待。成分名按当前语言重新查键，不能直接依赖 `Liquid.displayName` 的旧语言静态缓存；注意 microbe/fat/coffee_mush 等 ID 与键名不一致。
- 双击快速鉴定只选自有 `MAGNIFYING_GLASS`，不自动使用钥匙卡；诊断也不调用其他工具的 targeting 回调。

---

## 8. 日志

- `EventLog.Write` 统一品牌头：`[QuickSL]` 或 `[QuickSL/子系统]`（`Customer`、`Rewind`、`Shutter` 等），并转发到 `Plugin.LogSource`。
- 文件：`%LOCALAPPDATA%Low\Questing Goose Studio\Probably Stolen\PSTools.log`、`PSTools_history.log`、`Player.log`。
- 主日志与历史日志各按 **4 MiB + 3 份归档**（`.1`–`.3`）轮转；失败另外写 `PSTools_errors.log`（**1 MiB + 2 份归档**），不受详细日志开关控制。旧超大日志首次轮转可能大于新上限，后续自然淘汰。
- `Logging.Verbose=false` 默认关闭额外双击/无业务上下文诊断；捕获、恢复、失败、业务上下文变化和 settle 状态变化始终保留。历史日志仍受 `History.Enabled` 与每秒 120 条限流控制，但不再达到 8 MiB 后永久停写。
- 收集问题请同时取同次游玩的 `PSTools*.log` 及轮转归档、`PSTools.settings.xml`、`Player.log`、`mod-audit.log`，并提供操作步骤和语言代码。
- 游戏内「设置 → 查看日志」用官方 `CustomUIManager.Window(...)` 构建（`LAYER_OVERLAY`、`BeginScrollFlexible`、`AddLabel`、`AddButton`），显示末尾 400 行。
- `[QuickSL/Geom]` 只在物品视图异常（DESTROYED / MISSING / NOSPRITE / HIDDEN / MISPLACED）时输出，是排查贴图问题的第一入口。

---

## 9. UI 维护规则（精简）

完整规则见 `research/2026-10-31-snapshot-performance.md` 与 `research/archive/0.5.2-native-ui.md`，这里只列不可违反的边界：

1. 原生窗框负责标题/边框/ContentRoot；只在自己的内容区手工布局。
2. 一个 RectTransform 只能有一个外部布局控制者；手摆控件设 `LayoutElement.ignoreLayout=true`。
3. Build 只建树，`PanelModel` 统一排全部行并计算 `ContentHeight`；新增设置要进模型。
4. 纵向滚动量要保留；不要改键/刷新时重置到顶部。
5. 坐标用固定参考区 `CustomUIManager.GetLayerRoot("overlay")` 做世界坐标换算，不要跨父级比较 `anchoredPosition`。
6. 改键捕获期间仍处理窗口几何；用掉的按键同帧不能触发动作（`CheckpointPanel.SuppressHotkeys` + `Plugin.Down`）。

---

## 10. 存档安全与硬约束

- **绝不主动调用 `PlayerEngine.SaveGame()`**，不修改存档文件。
- 顾客节点是纯内存恢复。「回到开业前 / 上一天」**只加载、不写回**：快照复制为 `pstools_restore.es3`，通过 `NewGameData.saveFile` 读取，载入结束后清理；仍保留 `.pre_restore` 备份。
- `Rewind.MaxCheckpoints=20` 控制跨会话文件节点数量，`Rewind.MaxDiskMB=256` 仅控制本 mod 检查点总容量（与单份内存快照 `Snapshot.MaxBytesMB` 无关）；设置页只在用量、预算、错误或语言变化时格式化用量文本。每 60 秒及新建后按 LRU 清理本 mod 时间戳文件；恢复使用时更新文件时间，不改 ES3 内容。只统计/处理目录直接子文件中符合命名规则且非链接的节点，未知文件、子目录不统计也不清理；待恢复节点计入用量但不清理，不能满足预算时报告错误。
- 部署用 `scripts/Deploy-FullRelease.ps1`：备份存档与 Mods、校验存档/游戏程序集/其他 Mod 不变、按完整路径关闭游戏、不自动启动。
- 不 `Process.Start` / `Application.OpenURL`；不修改加载器、信任记录、Steam 类型；不打包 `0Harmony`。
- 能力声明保持 `files`、`reflection-external`、`dynamic-code`，新增反射/动态代码必须同步清单并让用户按原生流程确认。

---

## 11. 已知坑

| 坑 | 结论 |
|---|---|
| `isAboutToStartMainDialog` 当门控 | 永久拒绝回溯；已移除 |
| `isGeneScannerScanning` 当忙状态 | 它是设施开关，已移除 |
| 恢复 `GameItemElement.applied*` 缓存 | `UpdateRectTransform()` 会跳过布局；必须排除 |
| 用 `Validate()` 重排物品 | `validateLock` 残留时会整段跳过；必须直接写锚点 |
| 恢复后调用 `UpdateInventoryStateUIImmediately()` | 会按旧布局覆盖锚点；最终锚点要写在它之后 |
| 旋转物品回溯后变默认朝向 | 最后必须 `RestoreTransforms()` 以捕获的 pivot/rotation 收尾 |
| 强挂 `parentInventory.inner` | 库存可能经窗口渲染；用捕获的真实父级 |
| 数组偏移提到循环外 | 所有元素读成第一个；`diff=0` 也可能掩盖 |
| 闭店手动节点 | 必须列入面板，不能只显示 `ClosedNode` |
| `F8/F9` 与游戏开发者控制台 | 游戏目录有 `dev.txt` 时冲突，启动日志会提示 |

---

## 12. 验收清单

1. `dotnet build -c Release`：0 警告 / 0 错误。
2. `CheckpointTests` 全过；`ReleaseCompatibility` 挂点/字段/`HardFlags` 通过。
3. 部署后启动游戏：建节点、F6 回溯、F5/F7 文件回溯、F9 手动节点、闭店/早市回溯。
4. 物品：柜台 ↔ 物品栏移动后回溯，贴图位置与朝向正确；旋转/翻转物品回溯后保持朝向；卖出后回溯，物品回到原容器且无“空气占用”。
5. 液体：水质行、占比/纯度/评级、空瓶、酒精标签、医用酒精、脱水乙醇、三种糊糊不计入纯度。
6. 工具提示：悬浮说明与拖拽“使用/倒入”提示的名字替换正确；小容器不改名。
7. 面板：拒绝原因逐条显示、损坏节点悬停原因、日志窗口、设置滚动与改键。
8. 语言：中/英/俄/日等切换后重开提示和交易行，水质、成分、化学等级、酒品质跟随原版语言；预测句允许回退英文。缺失翻译/加载中的表不阻塞、不抛出到游戏、不显示键名。

---

## 13. 历史资料

- `research/archive/AGENT-0.6.0-history.md`：旧交接文档（0.2.1–0.6.0、Demo/MelonLoader、性能优化历程）。
- `research/2026-11-10-item-render-rewind.md`：贴图/容器回溯问题与修复记录。
- `research/2026-11-10-native-save-rewind.md`：只加载不写回的文件回溯。
- `research/2026-11-10-player-report.md`：报告证据、计时误读、日志/存储/语言修复与分帧暂缓理由。
- `research/2026-11-10-native-annotation-locales.md`：后续改用原版语言键、资源表验证与英文回退。
- `research/2026-10-31-snapshot-performance.md`：快照性能与 UI 布局规则。
- `research/2026-11-10-snapshot-optimization-step1.md`：第一步标量校验减重、回归与隔离基准。
- `research/2026-11-11-snapshot-optimization-step2.md`：第二步数组预算提前检查及前两步合并部署记录。
- `research/2026-11-11-snapshot-optimization-step3.md`：本机最新耗时分布、同形数组描述复用、第三步验证和部署记录。
- `research/issue-2/`、`research/issue-3/`：GitHub issue 的附件与日志。
- `research/modding-guide/ModdingGuide.zh-CN.md`：游戏 Mod API 指南。
