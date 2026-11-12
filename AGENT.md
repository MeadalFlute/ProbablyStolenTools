# Probably Stolen Tools — 交接文档

> 适用源码：正式版 **0.9.0**（2026-11-12）。平台为游戏自带 **Mono / IMod / Harmony**；MelonLoader / IL2CPP 路径已弃用，`#if !MONO` 分支只保证能编译，不再维护。
> 旧版本文档（0.2.1–0.6.0、Demo、早期性能优化过程）归档在 `research/archive/AGENT-0.6.0-history.md`，**不要**按它描述的环境或版本操作。

本文只写当前仍成立的事实。实现细节以源码为准，阶段记录与实验数据看 `research/`。

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

当前基线：**373 项**离线参考测试；正式版 **57 个** Harmony 挂点、**76 个**快照/私有字段检查；两个发布 DLL 合并扫描只剩 `files`，`HardFlags` 为空。静态迁移候选另有游戏 Mono 下 2807 类型/8027 字段精确对照、完整图六轮往返及版本拒绝测试。离线通过 **不等于** Unity 活体验收。

### 版本与发布

- 唯一真源：`release/manifest.xml` 的 `<ModVersion>`；`Plugin.Version` 必须同时改。
- 规则：**大版本、中间版本手动决定；小版本每次 `git push` 前 +1**。当前 **0.9.0**。
- 纯文档提交不单独升版；推送前重新构建 → 两组测试 → 部署 → 正常 `push`（不 force）。
- 推送使用用户临时 token，不写入文件、日志或命令历史。

---

## 2. 回溯架构（0.9.0）

顾客/手动节点采用**完整对象图 + 精确实体载荷**的混合后端，不是原版 SaveGame/LoadGame，也不是所有集合的逻辑重建。

### 2.1 精确实体块

`SemanticEntityBlocks` / `SemanticFieldBlock` 在主 `StateGraph` 遍历中一次捕获 owner 的全部非 Visual 字段：

| 类型 | 覆盖 |
|---|---|
| `GameItemElement` | 物品继承链业务字段、引用、Runtime 字段 |
| `GameGridInventory` | 库存继承链、成员/归属/锁/关联引用及 Runtime |
| `GameSlotInventory` | 单槽库存继承链及 Runtime |
| `PixelWindow` | 窗口业务字段、关系与 Runtime |
| `GridShapeBuilder` | 完整形状标量及原 `data` 数组引用 |

- 集合底层数组、字典容量/版本、闭包等仍由完整图保存；不重建列表/字典。
- 字段清单是**精确 schema**：逐 DeclaredOnly 类型字段数必须完全相等；不匹配时实体块回退普通字段图，不静默漏存。Mono 迁移候选的普通字段图同样使用静态表，未知布局或模块版本变化会拒绝建点，不回退动态枚举。
- 块与 legacy 槽位通过 `slots.Count == Fields.Length`、槽名唯一、`IsReference`、`Size==8` 对齐；不匹配即回退。
- 写回直接写原字段，不调用游戏业务 setter、库存移动、买卖或到店回调；引用按原身份。
- 预算仍按对象/槽/字节逐槽计数，不因块压缩而放宽。

### 2.2 生产默认值

| 配置 | 默认 | 说明 |
|---|---|---|
| `Snapshot.SemanticEntityBlocks` | true | 单次遍历实体块 |
| `Snapshot.SemanticDevelopmentChecks` | false | 关闭额外旧图 oracle/shadow/pilot |
| `Snapshot.TagBlocks` | true | 精确 `TagState` 九字段块 |
| `Snapshot.IntArrayBlocks` | true | 完整 `int[]` 块 |
| `Snapshot.OwnerReconcile` | true | 刷新后有界 owner 二次修复 |
| `Snapshot.SkipUnchangedScalars` | true | 未变化框架标量免写 |
| `Snapshot.TypedScalarComparisons` | true | 强类型标量比较 |
| `Snapshot.DirectReferenceComparisons` | true | 直接身份比较 |
| `Logging.Verbose` | false | 详细诊断/几何/群拖日志 |
| `Debug.Diagnostics/SelfTest/PreviewPanel` | false | 不启动自动化 |

旧单项 shadow/pilot 开关即使曾持久化为 true，也必须受 `SemanticDevelopmentChecks` 总门控；恢复入口在 `CustomerCheckpoint.Restore` 重新检查该值（`b99864b`），关闭后一律走完整 `Graph.Restore()`。

### 2.3 恢复顺序

1. 业务图还原（必要时第二次还原）；
2. `VisualState.Restore()`：父级、锚点/枢轴/尺寸/位置/缩放/旋转、Image sprite/color/enabled/raycast；
3. `RestoreItemGeometry()`：按还原后的 `modifiedShape` / `modifiedXOrigin` 调原生 `UpdateRectTransform()`，写回正常层与拖拽层尺寸、锚点、背景原点；不符即抛错；
4. `VisualState.RestoreTransforms()`：用捕获的原生几何收尾朝向，避免旋转被清零的 `modifiedShape.orientation` 覆盖；
5. `Differences()` 全量校验；owner 修复失败则 full fallback 并再次全量校验。

物品仍排除六个布局缓存：`appliedMinX`、`appliedMinY`、`appliedOrientation`、`appliedGridSize`、`appliedFlipped`、`validateLock`。

顾客恢复保存原 renderer 身份、sprite、颜色/渲染开关、MaterialPropertyBlock 和完整局部变换链（含 `RightLeave` 父级）；DOTween 路径保存游标/方向/速度，seek 时屏蔽回调并在 finally 归还，不重播到店/离店。

群拖/框选：单件拖拽、群组拖拽、按键未释放的框选/待拖拽统一门控；静态选区在预检通过、业务写入前调用原版 `Cancel()`，不调用 `EndGroupDrag` 提交被放弃的时间线。

---

## 3. 多语言标注

- 水质/纯度/评级/酿酒标注优先读取游戏原生语言键（`Mechanic` / `Item` / `UI` 表），随游戏语言切换。
- 回退链：当前语言表 → 英语表 → 代码内英文文本；不修改原生 locale 或表。
- 缓存键 `(locale, table, key)`，上限 2048，语言切换清空；加载中/读取失败的临时回退不缓存。
- 中文环境缺键时回退中文文本，其他语言回退英文文本；未识别的第三方液体保留其自带名称。
- 取词使用非阻塞 `GetTableAsync` 完成检查，不等待本地化加载；`StringTableEntry` 已有 SmartFormat，格式化使用来源 locale。
- 名称替换只作用于悬浮说明 + 拖拽“使用/倒入”提示；背包、交易列表、任务文本保持原名。
- 设置开关：`WaterQuality.Enabled`、`Chemistry.Enabled`、`Wine.Enabled`，默认均为 true。

液体规则速查：µL 存储（`容量 = mL×1000`）；水/酒精 **>65%** 为主成分；占比 = 成分/总量；纯度排除水、酒精、脂合剂、脂肪、物理污染物、微生物、植物质、不溶性碎屑、三种菇类糊糊；蛋白/酸/碱 ≥1000µL 才扣；化学污染物按剩余量 0.7% 封顶；还原剂不排除。评级阈值 99/90/80/60/45/30。酒精 74–76% 为医用酒精，≥99.95% 为脱水乙醇。空瓶显示尺寸；滴管/量筒/<200mL 不改名。

---

## 4. 节点时机与门控

- 顾客节点在开场对话播完后建立（`StoreClient.isMainDialogueStarted`，8 秒兜底）；不要用 `isAboutToStartMainDialog` 当门控。
- `Snapshot.StoreState`：营业(1)/闭店(0) 允许；手动节点允许同 state 回溯（含早市 state=2）；自动顾客节点仍 0/1。
- 允许跨顾客回溯；剧情/对话类顾客跳过建节点但不清空历史。
- 节点上限 20，面板行数 24；闭店手动节点必须列入面板。
- `isGeneScannerScanning` 是设施开关，不是扫描动作，禁止作为任何门控。
- 文件节点（F5/F7）只加载不写回：快照复制为 `pstools_restore.es3`，通过 `NewGameData.saveFile` 读取；保留 `.pre_restore` 备份；不覆盖当前存档。

---

## 5. 日志与诊断

- `EventLog.Write` 品牌头：`[QuickSL]` / `[QuickSL/子系统]`；关键捕获/恢复/失败不随 verbose 关闭。
- 默认日志：Session/语言状态、CAPTURE/RESTORE 阶段耗时与规模/后端、失败/拒绝/超限、真实 full 回退、释放错误、群拖异常。
- `[QuickSL/Geom]` 只在物品视图异常（DESTROYED/MISSING/NOSPRITE/HIDDEN/MISPLACED）时输出。
- `PSTools.log` 4MiB × 3 归档；独立错误日志 1MiB × 2；`PSTools_history.log` 保留并轮转。
- 设置页「查看日志」用 `CustomUIManager.Window(...)` 构建，显示末尾 400 行。

---

## 6. 加载器兼容迁移候选

**当前源码已接入静态图迁移候选**，详见 `research/2026-11-12-static-runtime-graph-migration.md`。构建时生成目标 Mono 字段表，包含私有 Entry/Slot 固定类型令牌；版本严格 MVID 门控，未知布局不动态回退。Mono 编译排除旧表达式生成路径，通用 GameFields 用官方 ModReflect；恢复字段/集合物理存储与安全检查不减。两个发布 DLL 均列入 manifest，仅声明/检测 files；ReleaseCompatibility 的退役能力门槛已加入，net8 宿主表隔离于 out。`scripts/Test-StaticLayouts.ps1` 全套通过，完整 Unity 活体验收仍未完成。下方旧阻断记录为历史，不再代表生产源码当前仍含命中。

后续实机反馈：基本回溯未发现功能异常，但明显变慢。已独立优化批量物品布局的字段访问：两个固定私有缓存字段采用字面 FieldInfo，公开原点属性直接读取；其他低频访问仍用 ModReflect。完整离线测试及能力扫描通过，后续实机结果见下。该项耗时分布与边界见 `research/2026-11-12-static-layout-restore-performance.md`。

布局修复后实机 7 次恢复平均约 1.26 秒（此前约 2.31 秒），geometry 降至 3–4 ms，全部 diff=0。下一候选为静态读取器的常见 SZ 数组下标读取，其他字段路径和所有校验保持；完整离线测试及扫描通过，整次实机收益待确认。详见 `research/2026-11-12-static-vector-read-performance.md`。

数组候选后实机仅 3 次，平均约 1.31 秒、全部 diff=0，未证明整次提速。最新小候选将静态读取器的标量比较准入类型判断缓存到初始化，原有比较及全部校验不变；完整离线测试/扫描通过，局部基准只有小幅收益，不能宣传已恢复旧后端速度。详见 `research/2026-11-12-static-scalar-gate-performance.md`。

- 技术依据：`research/2026-11-12-static-type-token-proof.md` 证明构建时固定 ldtoken 可表达私有泛型 Entry/Slot。窄探针之后已完成生产源码替换及离线完整图测试，尚无本轮实机验收；工具/原生离线宿主绝不随 Mod 发布。
- 官方公告 **2026-12-10** 起拒绝 `dynamic-code` 与 `reflection-external`（以及 native/threads）；当前 0.9.0 只保证**当前游戏版本**可用，发行说明不得写“已兼容下一版”。
- 迁移前源码依赖 `Expression.Compile` 与计算类型 `GetFields/GetMethod`；只删 manifest 声明或关闭 DevelopmentChecks **不是修复**，扫描器按编译进 DLL 的方法判断。现已从 Mono 发布编译排除此类路径，离线参考源码仍保留。
- 历史方案与普通 C# 查找链阻断见 `research/2026-11-12-loader-capability-migration.md`、`research/2026-11-12-runtime-graph-migration-feasibility.md` 与 `tests/CapabilityMigrationProbe/README.md`。其中集合阻断已由构建时令牌解决；ModReflect.Set 拒绝 readonly 的限制仍在，图写回继续使用固定 FieldInfo，不用 Clear/Add 偷换原存储语义。

---

## 7. 安全与部署约束

- 绝不主动调用 `PlayerEngine.SaveGame()`，不修改存档文件。
- 顾客节点纯内存恢复；文件回溯只加载不写回。
- 部署脚本备份存档/Mods、校验存档/游戏程序集/其他 Mod 不变、按完整路径关闭游戏、不自动启动。
- 不 `Process.Start` / `Application.OpenURL`；不修改加载器、信任记录、Steam 类型；不打包 `0Harmony`。
- 当前 Mono 迁移候选只声明 `files`；主 DLL 与 `ProbablyStolenTools.StaticLayouts.dll` 必须同时发货/扫描。Cecil、生成器、net8 宿主字段表、负例与原生离线宿主不得随 Mod 发布。

---

## 8. 已知坑

| 坑 | 结论 |
|---|---|
| `GameItemElement.applied*` / `validateLock` 恢复 | `UpdateRectTransform()` 会跳过布局，必须排除 |
| 用 `Validate()` 重排物品 | `validateLock` 残留会整段跳过，必须直接写锚点 |
| 恢复后调用 `UpdateInventoryStateUIImmediately()` | 会按旧布局覆盖锚点，最终锚点写在它之后 |
| 旋转物品回溯变默认朝向 | 最后必须 `RestoreTransforms()` 收尾 |
| 实体块 schema 增删字段 | 回退 legacy，不静默；观察 `SINGLEPASS SCHEMA FALLBACK` |
| 中途关闭 DevelopmentChecks | 旧节点不再走 pilot 写回，但已有诊断载荷不会立即释放 |
| 数组偏移提到循环外 | 所有元素读成第一个；`diff=0` 也可能掩盖 |
| `F8/F9` 与游戏开发者控制台 | 游戏目录有 `dev.txt` 时冲突，启动日志提示 |

---

## 9. 验收清单

1. `dotnet build -c Release`：0 警告 / 0 错误。
2. `CheckpointTests` 373 项全过；`ReleaseCompatibility` 57 挂点/76 字段/`HardFlags` 通过。
3. 部署后：首/普通/手动节点往返、恢复后群拖、顾客贴图、正常交易/送客。
4. 物品：售出/移动/旋转/拆分合并/嵌套容器/前柜台回溯，贴图位置与朝向正确，无“空气占用”。
5. 多语言：切换语言后水质/纯度/评级/酿酒标签跟随；缺键回退；三个设置开关生效。
6. 文件节点：F5/F7 只加载不写回；`.pre_restore` 备份存在。
7. 关闭 `SemanticEntityBlocks` 的回滚模式可正常走 legacy。

---

## 10. 历史资料

- `research/archive/AGENT-0.6.0-history.md`：旧交接文档。
- `research/2026-11-12-single-pass-entity-candidate.md`：单次遍历实体后端设计与实机记录。
- `research/2026-11-12-customer-visual-rewind.md`、`2026-11-11-multi-select-rewind.md`：顾客视觉与群拖。
- `research/2026-11-12-semantic-backend-review-ds.md`、`2026-11-12-semantic-backend-review-followup.md`：本轮 DS 审查与整改。
- `research/2026-11-12-loader-capability-migration.md`：退役能力迁移方案。
- `research/modding-guide/ModdingGuide.zh-CN.md`：游戏 Mod API 指南（注意能力表可能落后于官方现行政策）。
