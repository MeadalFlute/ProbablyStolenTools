# 语义化后端更新说明与 DS 审查、发布交接

日期：2026-11-12。请先阅读本文件，再阅读仓库 `AGENT.md`。

> 新增前向兼容问题：加载器警告属实，官方已公告 2026-12-10 起拒绝当前 DLL 使用的 dynamic-code / reflection-external。当前实机及 HardFlags 为空不代表下一版兼容。上传审查需加入此门槛，不能只删 manifest 声明或关闭开发开关。迁移方案及真实扫描探针见 `research/2026-11-12-loader-capability-migration.md`；生产代码尚未修改。

用户最新确认：关闭额外测试和冗余日志后，实机运行“看起来没什么问题了”；现在将整体更新交给 DS 做 code review 和后续上传。本文件是人工转交材料，不通过 DeepSeek Harness 或其他代理插件执行任务。

## 1. 交接状态

- 仓库：`<REPO>`。
- 游戏：`<GAME_DIR>`。
- 平台：Probably Stolen 0.46D，Unity / Mono / x64，游戏内置 IMod / Harmony。
- 当前源码、manifest 和已部署版本均为 `0.8.14`；本批开发尚未推送。
- 交接前源码 HEAD：`d34f42d`；最新运行时代码提交：`8b44b02`。
- 本地记录的 `origin/main`：`3fbd2a828fdd9732f15f8bfd3601d76a126e0f4e`，对应 0.8.14。交接前 main 领先 58 个提交；这只是本地远端跟踪状态，上传前应核实真实远端。
- 已配置 origin：`https://github.com/MeadalFlute/ProbablyStolenTools.git`。
- 请审查整个 `3fbd2a8..d34f42d`，不是只审查最后一轮日志降噪；本交接文件及随后纯文档提交不属于新的运行时改动。
- 用户已接受当前部署的发布模式短测，但不等于所有游戏状态、第三方 Mod 和极端失败路径均已覆盖。

后续任务顺序：独立审查 -> 处理真实问题并验证 -> 按仓库规则准备版本、部署和上传。不要把本文的验收结论当作无需读代码的审查结果。

## 2. 总体更新说明

此次将顾客/手动内存回溯从通用对象图推进为“完整对象图 + 精确类型语义载荷”的混合后端，减少重复表示、反射写入和开发期重复快照开销，同时保留原对象身份与完整验证。

**这不是以原版 SaveGame/LoadGame 替代顾客快照，也不是所有实体及集合已完成纯语义重建。**

原版存档不完整保存营业中的当前顾客/运行态，所以营业中回溯继续走内存后端；F5/F7 文件节点仍是独立路径。没有引入后台读取、分帧拼接可变状态、重播交易或重新生成顾客。

可用于发布说明的摘要：

- 引入单次遍历的精确实体载荷，所有顾客和手动节点使用同一生产捕获路径。
- 优化标签、完整整数数组、普通字段比较和二次修复，减少重复捕获、临时分配及不必要写回。
- 修复回溯后的顾客视觉恢复，完善群拖/框选与回溯的交互保护。
- 关闭开发期重复对照和默认详细诊断，保留关键耗时、异常及完整安全校验。

不要宣传“所有 Mod 状态均可恢复”“全游戏纯语义化完成”或固定百分比的整次性能提升。

## 3. 已实现的生产路径

### 3.1 精确类型完整 owner 块

`SemanticEntityBlocks` / `SemanticFieldBlockFactory` 在主 StateGraph 遍历中捕获一次 owner 的全部非 Visual 字段，精确准入：

| 类型 | 主要覆盖 |
| --- | --- |
| `GameItemElement` | 完整物品继承链的业务状态、引用与 Runtime 字段 |
| `GameGridInventory` | 完整库存继承链、成员/归属/锁/关联引用及 Runtime |
| `GameSlotInventory` | 完整单槽库存继承链及 Runtime |
| `PixelWindow` | 窗口业务字段、关系与 Runtime |
| `GridShapeBuilder` | 完整形状标量及原 `data` 数组引用 |

关键边界：

- Entity 与 Runtime 字段均进入同一完整 owner 块，不是只挑少量“关键字段”。
- 集合底层数组、字典容量/版本、闭包和其他可达对象继续由完整图保存，不重建列表/字典。
- 通过完整显式字段清单、精确类型及实际描述校验才启用；未知类型、子类或不匹配布局不强行套用。
- 实际写回原对象字段，不调用游戏业务 setter、库存移动、买卖或到店/离店回调。
- 引用按原身份比较；浮点按位比较；不引入业务对象自定义 Equals 调用。
- 对象数、槽数、逻辑字节预算及完整图覆盖保留，未借压缩格式放宽预算。

### 3.2 配套优化

- 精确 `TagState` 九字段块：保留三个字符串原引用及全部值；未知/筛选布局回退。
- 完整 `int[]` 块：复制整段数组，恢复到原数组；不裁剪容量，不只保存有效元素。
- 只读 Census：恢复前仍普查完整当前图，精确标签字段可直接消费，不保存另一份值载荷。
- 普通标量免写：逐槽检查后，只跳过精确未变化的框架标量写入。
- 强类型标量比较：减少装箱；无法生成时有原比较回退。
- 普通引用直接身份比较：减少不必要的全局 ID 查询。
- owner 二次修复：原生刷新后仍全图扫描，恢复变化 owner 的全部捕获字段；未知/超限等条件全图回退，最终有完整验证及必要的一次全图修复。

## 4. 视觉与交互修复

- 物品仍排除六个原生布局缓存：`appliedMinX`、`appliedMinY`、`appliedOrientation`、`appliedGridSize`、`appliedFlipped`、`validateLock`。
- 实际几何同步与最终捕获变换的应用顺序保留，不能因删除诊断而删除恢复动作。
- 顾客恢复包含原 renderer 身份、sprite、颜色/渲染开关、MaterialPropertyBlock 和完整局部变换链，包括 `RightLeave` 父级。
- 顾客路径保存已停止 tween 的游标、方向和速度；seek 时暂时屏蔽回调并在 finally 归还，不重播到店/离店。
- 顾客已离店时创建的手动节点应保持隐藏，不无条件强制出现。
- 单件拖拽、群拖及按键未释放的待拖拽/框选统一门控；静态选区在恢复预检通过后调用原版 Cancel 清理。
- 不调用 EndGroupDrag 提交被放弃时间线的移动，不改写原版群拖算法。

## 5. 开发期代码与发布默认值

| 开关 | 发布状态 | 含义 |
| --- | --- | --- |
| `Snapshot.SemanticEntityBlocks` | True | 新生产实体块路径 |
| `Snapshot.SemanticDevelopmentChecks` | False | 统一关闭额外旧图 oracle、shadow、audit、重复 Entity/Runtime 记录及旧试点准备 |
| `Logging.Verbose` | False | 默认不执行详细视觉/几何/等待探测，不输出常规群拖及议价转储等 |
| `Debug.Diagnostics/SelfTest/PreviewPanel` | False | 不启动诊断自动化或预览 |

旧单项 shadow/pilot 设置即使持久化为 True，也不能绕过 DevelopmentChecks 总门控；它们在生产实体块模式下不混用部分写回器。

独立旧图 oracle 和旧试点没有从源码硬删：它们是按需诊断和离线回归设施，正常发布不创建重复载荷。oracle 只读比较，从不替候选写回或掩盖错误。离线测试没有删除。

保留的默认日志：Session/语言状态、CAPTURE/RESTORE 阶段耗时与规模/后端、失败/拒绝/超限、真实 full 回退及来源、释放错误、群拖异常。成功 owner 修复不再每次重复打印长差异来源。

`PSTools_history.log` 业务记录及大小轮转保留；事务摘要默认不再重复广播到 Player.log。历史日志没有清空。

## 6. 最新实机证据

### 开发对照模式：2026-11-12 02:24:26.305 会话

- 8 次成功捕获、8 次恢复；最终全部 `diff=0`、`releaseOk=True`、owners/eligible，无最终 full 回退及 schema 失败。
- 首/手动节点共 2 次捕获 MATCH、4 次恢复 MATCH；所有成功节点实际启用实体块。
- 29 对群拖 START/END，用户确认画面与操作看起来正常。
- 普通捕获 281-378 ms；首节点 1652 ms 包含 oracle 938 ms，手动 1015 ms 包含 oracle 756 ms；恢复中位 636.5 ms。
- 另一次手动捕获 id8 在顾客路径未停稳时安全失败，265 ms，未接受部分节点；不能写成“该会话零失败”。

### 发布模式：2026-11-12 02:42:10.690 会话

对应运行时代码 `8b44b02`，用户本轮确认无明显问题：

- 3 次捕获：首节点 724 ms、普通节点 280 ms、手动节点 318 ms。
- 4 次恢复：636 / 521 / 500 / 542 ms，中位 531.5 ms。
- 所有恢复最终 `diff=0`、`releaseOk=True`、owners/eligible、`finalFallback=False`。
- 所有捕获 `developmentChecks=False`、`blockOracleMs=0`，恢复 `blockOracle=False`；无 SINGLEPASS 对照行。
- 实体块 4300-4306 个，证明额外对照关闭后新后端仍在实际工作。
- 该 Session 未见捕获/恢复 FAILED、UNAVAILABLE 或 schema fallback；本次读取的 Player.log 未见 Exception/StallWatchdog/Error 匹配。
- 默认详细群拖日志已关闭，不能再用 START/END 数量推断本次群拖覆盖；画面/操作以用户反馈为准。

上述为不同会话的小样本，不是严格 A/B。首节点冷编译、场景/顾客/物品变化和 oracle 开销必须分开讨论；不能直接用这些数值推导整个版本的百分比提速。

## 7. 审查重点与源码入口

请按严重度报告真实问题，给出文件/行号、触发条件、影响和建议测试；区分阻断项、非阻断建议和已知覆盖缺口。

| 入口 | 优先检查 |
| --- | --- |
| `StateGraph.cs` | 块/普通字段组合、预算、原身份、完整扫描、owner/full 修复、最终失败处理、释放 |
| `ManagedStateAccess.cs` / `SnapshotFieldReader.cs` | 强类型/反射回退、引用及位级比较、缓存不绑定某个节点实例 |
| `SemanticEntityBlocks.cs` / `SemanticFieldBlock.cs` | 精确 schema、完整继承链、字段布局冻结、逐槽引用发现、未知类型回退 |
| `SemanticItemSchemas.cs` / `SemanticInventorySchemas.cs` | Entity/Runtime/Visual 分类完整性、布局缓存排除、未分类字段不能静默漏存 |
| `SemanticTagBlock.cs` / `SnapshotIntArrayBlock.cs` | 全字段/全数组、逻辑预算、原容量和别名、owner 修复准入 |
| `SemanticCaptureOracle.cs` / `SemanticCheckpointSampling.cs` | 节点独立保活、最多两次采样、oracle 不写入、释放与跨节点隔离 |
| `SemanticEntityShadow.cs` / `SemanticRuntimeSnapshot.cs` / 各 Writer | 旧诊断路径受总门控、不与新块部分所有权混用、完整预检 |
| `CustomerCheckpoint.cs` | 捕获失败不污染历史、写前预检、写后失败保活、视觉顺序、历史/分支与真实回退日志 |
| `CustomerPathState.cs` | settled 条件、路径原身份、回调 finally 恢复、不重播业务 |
| `CheckpointItemInteraction.cs` / `MultiSelectDiagnostics.cs` | 群拖门控、Cancel 时机、诊断不改变输入或吞掉原异常 |
| `Plugin.cs` / `Rewind.cs` / 其他日志改动 | 发布默认关闭、旧配置不能绕门控、错误及实际恢复动作不随 verbose 被关 |

特别检查新增块是否只是“数目相同”，却实际漏字段、引用出边或共享集合；测试数量和 MATCH 行本身不能替代该检查。

原始报告里把 `after=...s` 当成捕获耗时并不可靠；现在应以 `captureMs` 和 RESTORE `ms(...)` 为准。该批不包含分帧捕获实现。

## 8. 验证命令与当前部署

```powershell
dotnet build src/ProbablyStolenTools/ProbablyStolenTools.csproj -c Release
dotnet run -c Release --project tests/CheckpointTests/CheckpointTests.csproj
dotnet run -c Release --project tests/ReleaseCompatibility/ReleaseCompatibility.csproj -- `
  "<GAME_DIR>" `
  src/ProbablyStolenTools/bin/Release/Mono/ProbablyStolenTools.dll release/manifest.xml
```

交接前结果：构建 0 警告/0 错误、373 项回归、57 个原版挂点/76 个字段、原生实体/双节点/owner/full 夹具通过；官方扫描 HardFlags/Error 为空。新增日志开关空配置/True/False 与开发开关独立性检查通过。

最新部署：2026-11-12 02:40:27，备份 `out/full-release-deploy-20261112-024027`。

DLL SHA256：`907804B9EF758E3FFBAF03D654AD7AF5778146BF54CD6738205668BF3C19D391`。

部署时 78 个存档区文件未变；随后只把 Mod 设置的 DevelopmentChecks 从 True 改为 False，其他 77 文件哈希相同。未由代理启动游戏；用户之后自行完成上述短测。

日志/设置目录：`%LOCALAPPDATA%Low\Questing Goose Studio\Probably Stolen`。文件为 PSTools.log、PSTools_errors.log（若存在）、PSTools_history.log、Player.log、PSTools.settings.xml。不要将玩家存档、完整日志、个人设置或任何凭据上传仓库。

## 9. 已知边界

- 未实现集合的逻辑重建；完整物理存储是有意保留，不是开发垃圾。
- 不保证所有第三方 Mod 的静态/opaque/图外状态可回溯；只审计捕获图内覆盖。
- 非默认议价组合及真实 Unity full fallback 等极端路径，缺少专项实机覆盖；离线有回退夹具。
- 顾客运动途中手动建点可安全拒绝；不能为消除 FAILED 日志而删除 settled 保护。
- 旧 Demo / IL2CPP 编译不通过，已弃用，不得以 Mono 结果宣称两边兼容。
- 不进一步扩大 schema、删除预检/完整校验或做异步/分帧捕获，以免发布收尾引入新风险。

## 10. 上传交接要求

1. 先输出整体审查报告。发现阻断缺陷先修复并增加相应测试；涉及恢复/视觉/交互行为的修复应重新部署并请求用户实测，不沿用旧验收。
2. 保留分阶段 git 历史及现有用户改动，不重置/压平历史，不修改无关 Mod、游戏程序集或原始存档。
3. 上传前核实 origin/main 和工作区。按仓库规则，本次 push 前将 manifest 的 ModVersion 与 Plugin 版本同步从 0.8.14 递增至 0.8.15；若期间版本已变化，重新判断，不能盲目覆盖。
4. 按 AGENT 的发布流程重新构建、跑两组测试、部署并完成所需验收；纯文档提交不单独加版本。同步中英文面向用户的更新说明，明确混合后端及性能数据限制。
5. 部署前确认游戏关闭。现有 Deploy-FullRelease.ps1 会强制停止游戏，因此游戏仍运行时不要直接执行；先请用户关闭，不替用户结束进程。
6. 仅使用已确认的 GitHub 仓库及正常凭据流程上传，正常 push，不 force push。需要临时 token 时向用户请求，不能把 token 写入文档、日志、命令历史或仓库。
7. 如果“上传”另指 Steam Workshop，当前交接未确认 Workshop 项目 ID 或发布工具；不能猜目标、创建重复条目或把 Git push 误报成 Workshop 更新，先与用户确认该渠道。
8. 上传后报告版本、commit、目标与远端核实结果；认证/网络/验收阻塞时如实说明停在哪一步，不宣称已发布。

回滚：关闭游戏，设置 `Snapshot.SemanticEntityBlocks=False` 且 `Snapshot.SemanticDevelopmentChecks=False` 后重启，新节点走旧普通实体/Tag/int 后端；也可使用对应备份 DLL。不要为回滚开启旧开发写回试点。内存节点本就不跨游戏重启保存。

## 11. 进一步阅读

- `research/2026-11-12-single-pass-entity-candidate.md`：最终单次遍历设计、独立 oracle、基准和部署。
- `research/2026-11-11-semantic-backend-progress.md`：完整阶段进展；开头的 02:13 待测状态已被本文最新验收覆盖。
- `research/2026-11-11-entity-backend-roadmap.md`：纯语义化与当前混合范围的区别。
- `research/2026-11-11-owner-reconciliation.md`、`2026-11-11-semantic-tag-blocks.md`：修复和标签块约束。
- `research/2026-11-12-customer-visual-rewind.md`、`2026-11-11-multi-select-rewind.md`：顾客贴图与群拖问题。

旧研究文档里的“尚未获准推送/待发布模式短测”是对应历史阶段的状态。用户已完成本次发布模式短测，并将后续审查、上传交给 DS；但这不取消先审查、版本一致、测试、部署和凭据保护要求。
