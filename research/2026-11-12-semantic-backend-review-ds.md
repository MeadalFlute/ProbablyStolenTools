# 语义化后端 Code Review 交接（DS → GPT）

日期：2026-11-12
审查范围：`3fbd2a8..d34f42d`（58 个提交）+ DS 的审查修复提交 `b99864b`
当前 HEAD：`b99864b`
当前版本：`0.8.14`（尚未升版、尚未推送）

## 1. 结论

**有条件通过。** 核心语义化路径未发现会直接丢状态或写错对象的阻断缺陷；发现并已修复 **1 个恢复门控问题**，另有若干非阻断建议和需要实机覆盖的缺口。

- 实体块 schema 的完整性校验是可靠的：字段增删、类型变化、未知子类都会回退 legacy，而不是静默漏存。
- 块与 legacy 槽位通过 `slots.Count == Fields.Length`、名称唯一、`IsReference`、`Size==8` 对齐，不存在“数目相同但漏字段”的路径。
- 写回顺序（值 → 引用 → 视觉/几何 → 最终全量校验）与 full fallback 逻辑成立。
- 发布默认关闭开发对照，oracle/shadow 不写回。

## 2. 验证基线（DS 环境）

```text
Release 构建            0 警告 / 0 错误
CheckpointTests         373 项通过
ReleaseCompatibility    57 挂点 / 76 字段，HardFlags 为空
工作区                  干净（审查修复已提交）
```

交接声明中的数字与实测一致。

## 3. 已修复问题（需 GPT 确认）

### P1：pilot 写回没有重新检查 DevelopmentChecks

文件：`src/ProbablyStolenTools/CustomerCheckpoint.cs:1038`、`:1113-1120`

`SemanticEntities` 只在 `Snapshot.SemanticDevelopmentChecks=true` 时创建，pilot 开关（`SemanticRuntimeRestore` / `SemanticShapeRestore` 等）默认都是 `true`。原实现恢复时只判断 `target.SemanticEntities != null`，因此：

1. 用户在会话中开启 DevelopmentChecks 并建立节点；
2. 不重启，直接把 DevelopmentChecks 设为 `false`；
3. 回溯这些旧节点时，仍可能走部分写回的 pilot，而不是完整 `Graph.Restore()`。

这与交接文档“旧单项 shadow/pilot 设置不能绕过 DevelopmentChecks 总门控”的声明不一致。正常发布默认从启动起就是 `false`，普通玩家不会生成 shadow 载荷，因此定级为中危而非阻断。

DS 已修复（`b99864b`）：

```csharp
bool developmentChecks = Plugin.CfgSemanticDevelopmentChecks?.Value == true;

runtimePilot = developmentChecks && Plugin.CfgSemanticRuntimeRestore?.Value == true && ...;
if (developmentChecks && (runtimePilot || (target.SemanticEntities != null && ...TryRestoreEntities(...))))
```

修复后恢复路径在关闭开发检查时一律走完整 `Graph.Restore()`。

**请 GPT 确认：**

- 该门控符合原设计意图；
- 是否需要在开启/关闭 DevelopmentChecks 时清理已捕获节点上的 `SemanticEntities`（可选加固）；
- 若保留 mid-session 切换能力，建议补一条离线/实机说明：切换后旧节点的恢复行为。

## 4. 非阻断观察（建议排期，不要求本次发布前全改）

| 编号 | 位置 | 现象 | 影响 | 建议 |
|---|---|---|---|---|
| N1 | `SemanticEntityBlocks.cs:45,62` | schema 失败只在捕获日志打一次 `SINGLEPASS SCHEMA FALLBACK`；`BlockMode` 为 `legacy+legacy-entity-schema-mismatch` 时，面板/会话日志不够醒目 | 玩家反馈时难以判断当前是否在用 legacy 实体后端 | 在 Session 日志与面板状态中明确显示当前后端；schema 失败附带缺失字段/类型摘要 |
| N2 | `SnapshotIntArrayBlock.cs:23` | `RestoreValues` 每次整体 `Array.Copy`，未变化也写 | 仅性能 | 增加逐元素变化检测或调用方跳过；注意保持 `CollectDifferences` 全量比较 |
| N3 | `SemanticTagBlock.cs:104` | `RestoreValues` 无条件写六个标量字段 | 仅性能 | 可复用 `CollectDifferences` 结果，仅在变化时写回 |
| N4 | `CustomerCheckpoint.cs:1722` | `CustomerVisual` 构造要求 `StoreClientMono.Instance/image/父级` 都存在，否则抛 `Customer view missing.` | 顾客已离店时的 F9 手动节点可能直接捕获失败；与“已离店手动节点保持隐藏”的表述不完全一致 | 明确这是拒绝路径还是应支持“无顾客手动节点”；若应支持，保存/恢复时允许空顾客视图并保持隐藏 |
| N5 | `SemanticEntityShadow.cs` / `SemanticRuntimeSnapshot.cs` | 开发期 oracle 额外做一次完整捕获；首节点 1652ms 中含 oracle 938ms | 仅开发模式 | 发布不受影响；文档已说明不要把开发数字当发布性能 |
| N6 | `SemanticEntityBoundaryPlan.cs` | 对第三方/未知实体采用严格抛错而非猜测布局 | 安全，但开发期对 Mod 组合更敏感 | 保持现状；若报错信息对玩家不可读，可在会话日志里补类型来源 |

## 5. 覆盖缺口（建议实机验证）

离线测试和交接的单存档短测不能覆盖：

1. 售出/移动/旋转/拆分/合并/嵌套容器/前柜台物品回溯；
2. 跨顾客、重复回溯、未来节点往返、分支淘汰；
3. 顾客离店后的 F9 手动节点（N4）；
4. 关闭 `Snapshot.SemanticEntityBlocks` 后的 legacy 回滚路径；
5. 第三方 Mod 给 `GameItemElement` / `GameGridInventory` 增加子类或字段时的 schema fallback 与状态完整性；
6. 关闭 DevelopmentChecks 后旧节点的恢复（P1 修复的实机确认）。

建议每项至少记录：`CAPTURE ... backend=... owners/eligible`、`RESTORE ... diff=0 releaseOk=True`、是否出现 `SINGLEPASS SCHEMA FALLBACK`、`REPAIR FULL FALLBACK`。

## 6. 发布状态与交接边界

- 未推送；`origin/main` 仍为 `3fbd2a8`，本地领先 59 个提交（含 `b99864b`）。
- 推送前由 DS 负责：版本 `0.8.14 → 0.8.15`（`Plugin.Version` + `release/manifest.xml`）→ 构建 → 两组测试 → 部署（需游戏关闭）→ 正常 `push`，不 force。
- 交接文档与 12 份新增研究文档中的本地路径已统一脱敏为 `<REPO>` / `<GAME_DIR>` / `%LOCALAPPDATA%Low\...`，不要再写回真实用户名或绝对路径。
- Steam Workshop 发布不在本文件范围；需用户单独确认项目与工具。
- 回滚：关闭游戏，设置 `Snapshot.SemanticEntityBlocks=False`、`Snapshot.SemanticDevelopmentChecks=False` 后重启；不要为回滚开启旧 pilot writer。

## 7. 给 GPT 的下一步

1. 确认 P1 门控修复符合设计；如需调整，提交独立 commit 并说明原因。
2. 决定 N1-N6 中哪些进入本次发布前的加固（建议至少 N1）。
3. 若修改恢复/视觉/交互行为，必须重新跑 `CheckpointTests` + `ReleaseCompatibility`，并请求用户实机验收，不沿用旧结论。
4. 不要重置/压平现有 Git 历史；不要改动无关 Mod、游戏程序集或原始存档。
5. 完成后再把发布交给 DS；token 由用户单独提供，不写入仓库或日志。
