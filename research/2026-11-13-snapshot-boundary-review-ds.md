# 快照边界增量 Code Review 交接（DS → 后续维护者）

日期：2026-11-13
审查对象：`c4e2b1e..79cab6e`（`9a2a484`、`79cab6e` 两个运行时代码提交 + 交接文档 `7c9913f`）
审查基线：`c4e2b1e`（0.9.1 发布）；审查时 HEAD：`7c9913f`；`origin/main`：`c4e2b1e`
当前版本：**0.9.1**（未升版、未推送）
审查方式：源码静态审查 + 官方静态布局套件 + 双 DLL 扫描；**未启动游戏**，实机矩阵未覆盖。

---

## 1. 结论摘要

| 项目 | 结论 |
|---|---|
| 边界共享规则、受保护 owner、nullable decimal、boxed scalar 代码 | **通过**，未发现会直接写坏状态或绕过根拒绝的缺陷 |
| 离线验证 | **通过**：380 项参考测试、57 挂点/76 字段、双 DLL 扫描仅 `files`、HardFlags 空 |
| 部署产物 | **阻断**：交接部署的主 DLL 与从最终源码重建的 DLL 哈希不一致；helper 一致。部署时间早于最终提交 |

**处理建议：先重建/重部署并核对主 DLL 哈希，再谈发布与实机验收。当前部署不能声称包含最终边界修复。**

---

## 2. 审查范围与关键文件

```text
c4e2b1e  0.9.1 发布基线
9a2a484  protected registry 闭包 opaque 修复
79cab6e  统一排除规则；nullable/boxed 标量边界；元数据与图回归
7c9913f  docs: 本批审查与部署交接
```

重点文件：

| 文件 | 审查内容 |
|---|---|
| `src/ProbablyStolenTools/SnapshotTraversalBoundary.cs` | 共享排除函数（生成器与运行时 Compile Link 同一源） |
| `src/ProbablyStolenTools/ManagedStateAccess.cs` | 根拒绝、`Traversable`、`ExcludedGameOwner`、`HasReferences`、boxed owner |
| `tools/StaticSnapshotLayouts/Program.cs` | 生成器是否复用排除规则、标量/枚举白名单 |
| `tools/StaticSnapshotLayouts/StaticSnapshotLayouts.csproj` | 共享源链接，避免两份名单 |
| `src/ProbablyStolenTools/SnapshotStaticLayouts.cs` | MVID 门控与未知布局拒绝 |
| `tests/CapabilityMigrationProbe/MonoFixture/BoundaryCoverageFixture.cs` | 实际 Mono 边界夹具 |
| `tests/CapabilityMigrationProbe/MonoFixture/ProtectedBoundaryFixture.cs` | 受保护 owner/闭包/根拒绝夹具 |

---

## 3. 验证证据

命令：

```powershell
.\scripts\Test-StaticLayouts.ps1 -GameDir '<GAME_DIR>'
```

完整输出：本机 `out/boundary-review-run.log`（本地审查证据，不随 Mod 发布）。

结果：

```text
Release 构建            0 警告 / 0 错误
CheckpointTests         380 项通过
ReleaseCompatibility    57 Harmony 挂点 / 76 快照/私有字段
双 DLL 官方扫描         Capabilities: files；HardFlags 空；Error 空
静态表                  2861 类型 / 106 泛型模板 / 8108 字段 / 4 模块
                        exact-order/type/attributes/module-MVID 与反射一致
边界普查                excluded=111 serialization=0 admittedWithoutLayout=0
受保护 owner            registry 闭包/嵌套 owner opaque；委托目标、身份往返、
                        拓扑普查、显式根拒绝均通过；未读取受保护字段
nullable 标量           decimal/date/guid 空值/值转换恢复；含引用 nullable 结构体仍拒绝
boxed 标量              基本类型/decimal/框架枚举的原字段、原身份、别名、浮点位恢复
确定性                  重建 helper 字节一致；错误 MVID 在返回字段前拒绝
```

未验证：Unity 实机、原玩家存档、第三方 Mod 组合、完整功能矩阵。

---

## 4. 阻断项：部署主 DLL 与源码构建不一致

### 4.1 证据

| 产物 | 交接部署记录 | 从 `79cab6e` 干净重建 |
|---|---|---|
| 主 DLL `ProbablyStolenTools.dll` | `FDC17F0BDE3FF13FF7B842A01E81A89F3383F6C6F7CAD566C18CDD0932785F75` | `CB954887E1981A4999409A0FEAE060D779250982803060EAE48AB643693B2661` |
| helper `ProbablyStolenTools.StaticLayouts.dll` | `CBB8F36AECB701BD46BEB87EAB53B63F4782E222056B7FEE0C452D7A2C8E1386` | `CBB8F36AECB701BD46BEB87EAB53B63F4782E222056B7FEE0C452D7A2C8E1386` |

辅助证据：

- 部署记录时间 `2026-11-13 00:01:33`，而 `79cab6e` 提交时间 `2026-11-13 00:01:55`，部署早于最终提交 22 秒；
- 本地从 HEAD 连续构建两次、`-t:Rebuild`、显式 `-p:GameDir`，主 DLL 均为 `CB954887…`，helper 恒为 `CBB8F36A…`；
- 说明部署包主 DLL 很可能是 `9a2a484` 或提交前工作区构建，**不包含最终 boxed scalar / 共享边界修改**；helper 却是新表。

### 4.2 影响

- 玩家当前测试的“最终双 DLL”不是最终源码产物；
- 不能用该部署宣称边框修复（尤其 boxed scalar 表扩展）已实机验证；
- 若直接发布，可能出现主 DLL 不认识新 helper 表、或边界行为仍是旧版。

### 4.3 必须动作

1. 在 `79cab6e`/当前 HEAD 上重新构建正式 Mono 双 DLL；
2. 运行 `scripts/Test-StaticLayouts.ps1`；
3. 关闭游戏后部署，核对 `deployment.json` 主 DLL 应为 `CB954887…`、helper 应为 `CBB8F36A…`（或记录新的重建哈希）；
4. 让原问题玩家复测手动/普通建点与回溯，附新的 `CAPTURE FAILED`（含 manual/closed）。

---

## 5. 代码审查通过项

### 5.1 共享边界

[`SnapshotTraversalBoundary.cs:5`](../src/ProbablyStolenTools/SnapshotTraversalBoundary.cs) 只有一份 `IsProtectedOwner` / `IsExcludedGameOwner`：

- `IsProtectedOwner`：`ModLoader`、`ModTrust*`、`ModScanner`、`ModCapability`、`ModSecurityDebug`、`ModSteam*`、`ModWorkshop`、`SteamManager`、`PaperBookRegistry`、`ModReflect`；
- `IsExcludedGameOwner`：上述 + 最外层 `ES3` 前缀；
- 生成器通过 `Compile Include` 直接编译同一源文件，不再复制名单；
- 两端都取 `Outer`/`DeclaringType` 最外层名称，规则一致。

### 5.2 运行时边界

- [`ManagedStateAccess.cs:292`](../src/ProbablyStolenTools/ManagedStateAccess.cs)：显式根若为被排除游戏 owner，直接抛 `Excluded snapshot root`，不允许通过根绕过；
- [`ManagedStateAccess.cs:493`](../src/ProbablyStolenTools/ManagedStateAccess.cs)：`Traversable` 优先判断排除，被保护类型返回 false，只保留身份、零槽，不读字段、不执行构造器/方法/回调；
- [`ManagedStateAccess.cs:518`](../src/ProbablyStolenTools/ManagedStateAccess.cs)：运行时额外确认类型来自 `_gameAssembly`，避免按名称误排除其他程序集；
- 委托仍保留原身份/调用链，只追踪目标引用，不调用回调。

### 5.3 nullable 与 boxed 标量

- [`ManagedStateAccess.cs:570`](../src/ProbablyStolenTools/ManagedStateAccess.cs)：`HasReferences` 对 `decimal` 明确返回 false，与“decimal 是标量叶子”一致；`Nullable<decimal>` 可恢复，含引用 nullable 结构体仍拒绝；
- 生成器 [`Program.cs`](../tools/StaticSnapshotLayouts/Program.cs) 补基本数值/字符/decimal owner 及 System 顶层枚举；boxed scalar 作为独立 owner 保存原箱内字段，保留箱身份、别名和浮点位模式；
- 没有放开全部框架枚举；没有把 IntPtr/指针等原本不支持路径放进白名单。

### 5.4 静态表与发布

- helper 与主 DLL 成对构建、扫描、分发；manifest 声明两个 DLL；
- MVID 严格门控；错误 MVID/未知布局拒绝；
- 主 DLL 无运行时动态生成/枚举回退；
- 双 DLL 合并扫描仅 `files`，无退役能力（`dynamic-code` / `reflection-external`）。

---

## 6. 非阻断问题与建议

| 编号 | 位置 | 现象/风险 | 建议 |
|---|---|---|---|
| N1 | `ManagedStateAccess.Traversable` | 未知 framework 可变类型会拒绝整次捕获（第三方类型才 opaque），玩家可能把它理解成“快照坏了” | `Capture failed` 日志明确区分“类型不在迁移白名单”，并在面板提示；不要静默回退动态枚举 |
| N2 | `tools/.../Program.cs` | 当前加入所有 `System` 顶层枚举；未来扩大枚举/框架类型范围可能重新触发官方扫描退役能力 | 保持“仅基本标量 + System 顶层枚举”；任何扩展先跑双 DLL 合并扫描 |
| N3 | `StateGraph.ValidateRestore` / `IStateStableStorageBlock` | 新增自定义块若未实现稳定存储标记，会退回完整读取（安全但变慢） | 在 AGENT“新增块/新增后端”说明中把该标记列为契约 |
| N4 | 测试工程 | `tests/ReleaseCompatibility/bin/Release/net6.0` 是旧产物，直接运行会误报 `unlisted-reference` | 统一用 `dotnet run`（net8 输出）或 `Test-StaticLayouts.ps1`；可删除旧 net6 产物避免误用 |
| N5 | 静态布局缓存 | `SnapshotStaticLayouts.Closed` 与 shared schema 有上限但不清理 | 不支持插件热重载；重启即可。若未来支持热重载，需要重建/清理 |
| N6 | 受保护 owner 根拒绝顺序 | 根检查发生在 `Retain` 之后，异常信息有类型但无来源路径 | 可把 `SourceOf`/manual/closed 加进失败日志；当前已有 `CAPTURE FAILED manual/closed` |
| N7 | 覆盖范围 | 仅简单节点实机；售出/旋转/拆分合并/嵌套容器/柜台/群拖/顾客贴图/交易送客未覆盖 | 发布前按 AGENT 验收清单补测，不要用离线套件代替 |
| N8 | 性能 | 本轮无新实测；首节点初始化卡顿仍未解决 | 不因补表宣称提速；后续单独测 `captureMs` / `RESTORE ms(...)` |

---

## 7. 需要补的实机场景

1. 原问题玩家：手动节点、普通顾客自动节点分别建点并回溯，确认不再出现 `Uncovered static snapshot layout: PaperBookRegistry+<>c`；附 `CAPTURE FAILED manual=... closed=...`（若仍失败）。
2. `decimal?` / `DateTime?` / `Guid?` 物品字段回溯（含空值与非空值）。
3. `object[]` / 集合中的 boxed 标量（bool/int/decimal/枚举）回溯，检查原箱别名与值。
4. 售出/移动/旋转/拆分合并/嵌套容器/前台柜台物品回溯。
5. 跨顾客、重复回溯、未来节点往返、分支淘汰。
6. 恢复后群拖、顾客贴图、正常交易/送客。
7. 关闭 `SemanticEntityBlocks` 的 legacy 回滚路径。
8. 第三方 Mod 组合：预期普通对象 opaque、未知布局拒绝；记录实际失败类型。

---

## 8. 发布与后续步骤

1. **先解决第 4 节**：重建/部署，核对主 DLL `CB954887…`（或新的重建哈希）与 helper `CBB8F36A…`。
2. 跑完整 `Test-StaticLayouts.ps1`，确认 380 项、双 DLL 扫描仅 `files`。
3. 按仓库规则升版本（当前 0.9.1 → 建议 **0.9.2**，patch +1），同步 `Plugin.Version` 与 `release/manifest.xml`。
4. 部署前确认游戏已关闭；部署脚本不在玩家运行时调用。
5. 正常 push，不 force；token 由用户单独提供，不写入仓库/日志。
6. 发布后报告 commit、远端 HEAD、双 DLL 哈希与实机复测结果。

当前审查状态：**代码通过，部署产物待重建；未推送、未升版。**

---

## 9. 参考记录

- `research/2026-11-12-protected-registry-capture-fix.md`
- `research/2026-11-12-loader-migration-review-handoff.md`
- `research/2026-11-12-restore-stage-consolidation.md`
- `research/2026-11-12-static-runtime-graph-migration.md`
- `research/2026-11-12-static-type-token-proof.md`
- 本地验证日志：`out/boundary-review-run.log`
- 部署记录：`out/full-release-deploy-20261113-000133/deployment.json`
