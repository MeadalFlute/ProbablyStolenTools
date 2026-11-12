# 加载器迁移及恢复性能优化审查交接

> 本文保留初次交接时的 0.9.0 基线与性能证据。当前已为 0.9.1；最新边界修复、字段表数量、部署哈希和审查入口以 `snapshot-boundary-review-handoff.md` 为准，不要使用下方旧哈希上传。

日期：2026-11-12。交给 DS 进行 code review，不调用上传插件，不自动推送或发布。

## 审查范围

版本仍为 **0.9.0**。运行代码审查终点 `fafff48`，此前部署记录终点 `17223b4`；本交接及实机结果仅补文档，部署 DLL 不变。

建议整体比较：

```powershell
git diff 5eeaf82..17223b4 -- src tools tests scripts release README.md
git show fafff48
```

`5eeaf82` 是本轮加载器迁移前的语义后端发布基线。先前语义后端审查见 `2026-11-12-semantic-backend-review-ds.md` 及 `2026-11-12-semantic-backend-review-followup.md`，本轮重点是静态布局迁移及之后的性能修改，不把旧后端的历史验收自动视为新后端全覆盖。

| 实现提交 | 内容 |
|---|---|
| `47fc6cf` | Mono 发布路径迁移至构建时固定字段布局 |
| `cd40aea` | 物品布局复位避免逐件重复 ModReflect 检查 |
| `4480418` | 常见向量数组预编译读取 |
| `06b839f` | 缓存不可变标量比较准入 |
| `e6447a3` | 扫描内结构体元素读取复用、有界布局共享 |
| `fafff48` | 引用拓扑普查、稳定存储预检、修复规划并入差异扫描 |

## 当前实现与不可退让的语义

- 仍是完整托管对象图加精确实体/Tag/int[] 块，不是原版 SaveGame/LoadGame，也不是用集合 Clear/Add 重建逻辑内容。
- 构建工具用 Cecil 生成 `ProbablyStolenTools.StaticLayouts.dll`，以固定类型令牌和字面字段查询访问目标布局；生产 Mono 编译排除旧表达式代码生成路径。
- 表覆盖 2807 类型、106 泛型模板、8027 字段、4 模块；严格 MVID 门控。未知布局拒绝，不退回动态枚举。
- 数组、集合底层存储、容量/版本、readonly 字段、共享引用和对象身份保留；逻辑预算逐槽累计，标量令牌 8 字节。对象上限计算已发现对象，含待处理队列。
- opaque 第三方对象只保留身份，不宣称能恢复其内部状态；没有扩大第三方 Mod 状态覆盖承诺。
- 首次写入前失败不写业务状态；写入后异常仍 fault/reload，不释放可能已接回的旧视图。
- 不修改存档、不调用 SaveGame、不重播交易/到店回调，不修改加载器/扫描器/信任记录。

## 源码入口

| 路径 | 审查内容 |
|---|---|
| `tools/StaticSnapshotLayouts/Program.cs` | 固定布局生成、私有泛型类型令牌、模块门控、确定性 |
| `src/ProbablyStolenTools/SnapshotStaticLayouts.cs` | 运行期表装载及版本拒绝 |
| `src/ProbablyStolenTools/SnapshotFieldReader.cs` | 静态读取、数组路径、比较语义；旧后端条件编译 |
| `src/ProbablyStolenTools/ManagedStateAccess.cs` | owner 保留、存储路径、扫描缓存、共享 schema、释放 |
| `src/ProbablyStolenTools/StateGraph.cs` | 普查/预算、稳定预检、修复计划、写序及最终验证 |
| `src/ProbablyStolenTools/CustomerCheckpoint.cs` | 恢复阶段、视觉收尾、新对象清理、异常与日志 |
| `src/ProbablyStolenTools/GameFields.cs` | 两个布局缓存字段的固定查询及原点读取 |
| `src/ProbablyStolenTools/SemanticFieldBlock.cs` | 精确实体块稳定存储契约 |
| `src/ProbablyStolenTools/SemanticTagBlock.cs` | Tag 块稳定存储契约 |
| `src/ProbablyStolenTools/SnapshotIntArrayBlock.cs` | int[] 块稳定存储契约 |
| `src/ProbablyStolenTools/ProbablyStolenTools.csproj` | Mono 编译排除、生成表引用及构建顺序 |
| `scripts/Deploy-FullRelease.ps1` / `release/manifest.xml` | 双 DLL 发布与声明，不带离线工具 |

## 优先审查问题

1. **稳定存储预检是否成立**：只批准固定 CLR owner、固定 FieldInfo/内嵌值类型路径和不可变数组边界；引用/Nullable 为叶子。捕获读过的路径是否可能在身份仍有效时失效？访问器单独 Dispose、空保留表、节点释放是否都拒绝？未批准后端/任一自定义块必须保留完整读预检。不能据此免除写后校验。
2. **拓扑普查是否等价**：只免去用不到的当前标量读取；引用、源路径、已发现对象、所有布局及三种预算检查必须与原普查一致。新物品/窗口分类及异常释放不得漏掉。普通 Census 不改变语义。
3. **扫描缓存是否越界**：结构体数组只缓存本次读取 pass 的同一 owner/index；对象切换、结束、失败、写入、自定义值比较/散列回调前后均清空。公开独立读取不得看见旧值；不得跨恢复写入复用缓存。
4. **共享 schema 是否保留业务对象**：只能共享不可变元数据；部分根字段不共享。核对类型、Unity 基类、快慢模式、元素模式、数组长度的 key 完整性，以及 4096 条/65536 槽上限和 peer Dispose 隔离。
5. **修复计划是否完整**：Walk 保证同 owner 普通槽连续，且普通槽与块二选一；计划必须包含整个 dirty owner，不只是差异槽。超过差异样本数仍扫描到底。owner/槽上限、scalar-before-reference 顺序、revision 与一次性限制、full fallback 均保留。
6. **精确值/存储行为**：readonly 嵌套结构体、nullable/枚举、NaN/负零、协变/锯齿数组、List/Dictionary/HashSet 原存储、闭包及旧节点不可达数组不得被“逻辑等价”替换。
7. **能力及版本边界**：两个发布 DLL 合并扫描；不只看 manifest。错误 MVID、缺 helper、未知类型必须清晰拒绝，不加载 net8 宿主表。测试工具、Cecil、原生宿主和负例不得进入发行包。
8. **视觉/故障边界**：第二次业务修复之后重放视图，再几何、变换，最后完整校验；不能因简单节点测试通过就删掉。原生选区 Cancel 只清理静态选区，不提交群拖。Dispose 异常应保留恢复成功与 releaseOk=False 诊断。

## 实机与离线证据

最新本地 `PSTools.log`：2026-11-12 16:37 会话，3 次捕获、5 次恢复。上一批是 15:57 会话的 5 次恢复。

| 阶段 | 上批平均 ms | 本批平均 ms |
|---|---:|---:|
| census | 291.6 | 200.4 |
| preflight（含对象分类） | 214.4 | 32.0 |
| write（含入口准备） | 240.8 | 238.6 |
| refresh | 80.8 | 83.0 |
| reconcile | 280.8 | 276.8 |
| finalVerify | 175.0 | 171.6 |
| total | 1285.8 | 1004.8 |

本批中位数 1003 ms，范围 957–1078 ms；平均缩短 **21.9%**。图约 10.9–11.6 万对象、60.2–67.7 万槽。两批规模相近但不是严格相同操作 A/B，不能作为所有存档的性能保证。

5 次均 diff=0 / releaseOk=True / topology / retained-storage / finalFallback=False；本会话 PSTools.log 无失败或异常行。每次原生刷新确实改变 4 槽，修复覆盖 1 owner / 10 槽，修复扫描平均 179.4 ms。最终校验位于几何及变换更新之后，不可复用修复前结果。

捕获 1479 / 701 / 587 ms；首节点 access 412 ms，初始化仍明显。当前没有低风险且明确显著的进一步阶段删减，暂不追加新的优化架构。旧表达式后端曾约 0.55 秒，当前约 1 秒，**不声称完全恢复旧性能**。

最新完整离线结果保存在本地 `out/restore-stage-static-tests.log`：Release 0 警告/错误，380 参考测试、57 Harmony 挂点/76 字段、游戏 Mono 精确表及混合完整图夹具、六轮双向恢复、预算边界、身份/释放拒绝、未知根、错误 MVID、确定性构建通过；两个发布 DLL 官方合并扫描仅 files，HardFlags/Error 为空。这是前轮实现完成时的验证，本轮只改文档，未重跑。

大图阶段精简离线对照：80002 对象/728000 槽，同夹具每版 9 个稳态样本，恢复相关五阶段合计中位数 1599.24 → 1237.18 ms（-22.6%）。Write 反而增加，不能只报有利阶段。游戏 Mono 离线宿主不是 Unity，不能替代实机。

详细原始路径、测量方法和证明：

- `2026-11-12-restore-stage-consolidation.md`
- `2026-11-12-static-runtime-graph-migration.md`
- `2026-11-12-static-graph-batch-performance.md`
- `2026-11-12-static-type-token-proof.md`

## 复核与发布

仓库根目录运行：

```powershell
.\scripts\Test-StaticLayouts.ps1 -GameDir '<GAME_DIR>'
```

该套件构建并运行离线验证，不启动游戏。需目标游戏程序集、游戏 Mono 及本地构建配置；详见 `tools/StaticSnapshotLayouts/README.md` 和 `tests/CapabilityMigrationProbe/README.md`。

当前已部署包记录：`out/full-release-deploy-20261112-162727/deployment.json`。

- 主 DLL：`83AD0742D14E9DEDBC1E233A324A7FA050B76DA69DF2F093815670203BC54D11`
- helper：`F2096125D20042E3609E56636FC17A566A5446A2E5B9209CB8059C34EAE70DC0`
- 此次部署未关闭/启动游戏，未导入设置，原存档目录文件/游戏程序集/其他 Mod 校验未变。本轮只补文档，不重新部署。

最新用户只反馈简单节点回溯，不能标记整轮售出/旋转/拆分合并/嵌套容器/柜台/群拖/顾客贴图/交易送客矩阵重新全过；多 Mod 组合和未来游戏版本同样未全面验收。审查通过后需要决定是否补这些场景，不能用离线通过代替。

发布只发正式双 DLL、manifest、preview；不打包 Harmony/Cecil/宿主工具。部署前必须确认游戏已退出，**部署脚本含关闭游戏逻辑，不要在玩家运行中直接调用**。不主动保存、不改存档、不动其他 Mod。未授权不推送/上传；后续推送前按 AGENT 版本规则处理。当前扫描通过只代表当前版本，不保证未来加载器兼容。

请审查方按严重级别返回文件/行号、可复现风险与缺失测试；性能建议区分可安全删除的重复工作与需要新增互操作证明的架构改动。
