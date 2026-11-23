# 设置/兼容性修复回执 —— 第二轮 Code Review

- 审查对象：`research/2026-11-23-settings-compatibility-review-receipt.md`（回执）及其描述的当前工作区改动
- 基线：`HEAD 48652b2` + dirty 工作区；版本仍 `0.9.5`，未提交/未推送
- 结论：**通过**。回执所列 M1/M2/P1/P2/L1–L4 均已在代码中落地且与回执描述一致；未发现新的阻断级安全或性能问题。剩余 4 项低危（1 处 UX、1 处文档数字、1 处回退读放大、1 处防御不一致）建议在推送前顺手处理，均非阻断。

---

## 一、独立复核结果（回执声称 vs 本人实测）

| 回执声称 | 本人复核 | 结果 |
| --- | --- | --- |
| 构建 0 警告 / 0 错误 | `dotnet build -c Release`：`0 个警告 / 0 个错误` | 一致 |
| 单表 2915 类型 / 8235 字段 / 670208 bytes | 构建输出 `types=2915 fields=8235 factories=2033 bytes=670208` | 一致 |
| 主 DLL SHA256 `BA005FA4…` | `Get-FileHash` = `BA005FA4A8B791F9D6652D0315DD6750FEA3F6DFCF17CDE8BA6D0AFB81417EC2` | 一致（可复现构建） |
| helper `6129A2D3…` 808448 bytes | `deployment.json` 记录 `6129A2D3A826…`，`StaticLayoutsSHA256` 同值 | 一致（本轮未重建合并 helper） |
| 433 项参考测试 | `CheckpointTests`：`433 tests passed` | 一致 |
| 17 项可选兼容测试 | `OptionalModCompatibilityTests`：`Passed 17` | 一致 |
| 13 读档 / 14 历史测试 | `out/review-fixes-20261123-save-actions.log:14`、`…native-history.log:15` | 一致 |
| 8 组自动建点开关 + 192 组确认路径 + 维护独立性 | `…static-suite.log:652` PASS 记录，且与源码逐条对照 | 一致 |
| 冲突保存失败不改变开关锁、5 秒重试、最新值为准、恢复日志 | `…static-suite.log:686` PASS + `OptionalModCompatibilityTests` 第 13 项 | 一致 |
| 部署扫描仍仅 `Capabilities: files`，`HardFlags`/`Error` 空 | `…universal-scan.log:94-97` | 一致 |
| 未启动游戏、未改写玩家配置、存档与程序集未变 | `deployment.json`：`InGameValidated=false`、`SettingsImported=false`、`ExistingSaveFilesUnchanged=true`、`ExistingGameAssembliesUnchanged=true`、`GameStarted=false` | 一致（回执如实标注"实机待验收"） |
| `git diff --check` 通过 | 退出码 0（仅 CRLF 提示，属既有换行配置） | 一致 |

复核受限说明：本机 DSH 文件沙箱拒绝 `File.Replace`，因此 `tests/CapabilityMigrationProbe` 的 Mono 夹具与 `ReleaseCompatibility` 的配置写盘步骤无法在本人环境跑完（第一轮已确认为环境原因，非代码回归）。上述"192 组 / 配置失败恢复"两项以维护者日志 + 本人逐行读代码交叉确认；其余数字均由本人现场重跑得到。

---

## 二、M1（禁用内存连带关闭卷帘门维护）复查

`CustomerCheckpoint.cs:58-60` 拆分为：

```
MaintenanceEnabled => CfgCustomerCheckpoint?.Value == true && PatchesReady
Enabled            => MaintenanceEnabled && MemoryCheckpointsAllowed
AutoCapture        => Enabled && CfgAutoCheckpoints
```

- 取值链 `MaintenanceEnabled ⊃ Enabled ⊃ AutoCapture` 方向正确：关闭内存不再影响维护判定。
- `Tick()`（`:195-205`）`Clear("已禁用")` 后不再提前 return，进入 `!AutoCapture` 分支并 `if (MaintenanceEnabled) ShutterWatchdog();`（`:204`）——内存禁用但功能开启时维护仍每帧运行，修复目标达成。
- 功能整体关闭（`CfgCustomerCheckpoint=false` 或补丁未就绪）时 `MaintenanceEnabled=false`，看门狗依旧不运行，行为与 0.9.5 一致，没有把维护扩到"未启用"状态。
- 内存关闭时 `Observe()` 之后 `_armed/_closeArmed/_captureSince/_settledSince` 每帧复位、`ReturnGate.CancelPending()`、`SettleLog.Reset()`：与旧实现等价（旧实现同样在 `!AutoCapture` 分支复位）。
- 其余 `!Enabled` 判定（`:349/:574/:1120/:1626/:1642/:1851/:1901`）全是内存相关路径，保持语义正确；`:574` 在建点前已被 `:570-572` 的内存告警拦截，提示比原来更准确。

结论：M1 修复完整，未见遗漏的维护入口。

## 三、M2（首次冲突写盘未捕获）复查

`OptionalModCompatibility.cs:26-58`：

- 两条赋值在 `config.SaveOnConfigSet = false` 窗口内批量完成（`:41-49`），`finally` 恢复原值，因此在 `SaveOnConfigSet=true` 的正式配置下不会"逐条写盘"；单次显式 `Save()` 由 `SavePendingDefault()`（`:60-75`）执行并有自己的 try/catch，异常不再逃逸到 `RewindOverlay.Update`。
- `_pendingConfig`/`_retryAfterUtcTicks` 设计使失败后**本会话内保持禁用**（`_claimsHandled=true`），不每帧重试，也不会把玩家的手动覆盖反复改回去；5 秒节流 + 错误串去重（`_writeError`），恢复时打印 `save recovered`。
- 重试写入的是**当次读到的** `DisableMemory`/`Handled` 当前值（测试第 13 项：玩家在失败窗口内关掉开关，重试成功落盘为 `DisableMemory=false`），语义正确。
- 已知且已被回执声明的边界：写盘一直失败且玩家此时退出，则下一次启动会重新检测并再置 `true`，不能承诺跨进程记忆。这是无持久介质时的必然结果，可接受。

未发现的漏洞：`SaveOnConfigSet` 的临时关闭与 `Panel.cs`/`QuickInspectionPatch.cs` 的同类窗口不会交错（均为同步单线程、中间不跑 Tick）；`ApplyConflictDefault` 在 `SaveOnConfigSet` 原本为 false 时也会正确恢复为 false。

## 四、P1 / P2 / L1–L4 复查

- **P1（每帧拷贝 Mod 列表）**：`Refresh()` 才读 `ModLoader.Mods`（`:19-22`），`Tick()` 1Hz 节流（`:124-132`，`_nextRefreshAt = now + 1f`），动作边界（`CaptureManual:570`、`Capture:758`、`Restore:1109`、`Rewind.Restore:484`、`RestorePreviousDay` 经 `Restore` 收敛）保持即时，面板显示走配置项 `MemoryCheckpointsAllowed` 因而始终即时。`IsRunning` 改为下标遍历 `_mods ?? ModLoader.Mods`，快照建立后无分配（首帧前 `_mods` 为空时仍会退回一次拷贝，首帧 `Tick` 即建好，可忽略）。测试第 14/15 项覆盖"tick 内共享快照、动作边界刷新"。
- **P2（F5 全量解析）**：`Rewind.RestorePreviousDay` 先 `Catalog.Refresh`，逐文件 `Catalog.ReadForRestore`（`Rewind.cs:323-343`），筛选条件与 HEAD 完全一致（`State==2`、槽位、`Day<today`、`RunId`、按天优先 + mtime 最新），未引入语义漂移。`ReadForRestore` 对旧命名/超 16MiB 字节文件回退到原读取路径，且拒绝 reparse point（比旧实现更严），`Deduplicate` 仍只处理自有命名（新增 3 项读档测试覆盖）。
- **L1（状态可见性）**：`Panel.cs:170-171` 在禁用或冲突时显示 `Status`；`Status` 区分"已禁用（仅文件读档）"与"已强行启用（mod冲突，BUG不受理）"，中文文案与 handoff 要求逐字一致（测试第 16 项断言）。
- **L2（设置项可编辑）**：未采纳（依赖偏好仍可编辑），回执已说明理由，且与 handoff "强行启用" 的可用性要求一致——属有意取舍，非遗漏。
- **L3（确认文案与实际路径一致）**：`Rewind.RequiresFileReload`（`:373-377`）按动作/锚点/保留节点判定；`Request`（`:379-438`）据此选择"内存优先"或"读盘且丢失全部内存节点"文案，面板缺失时读盘路径直接取消、内存路径立即执行；`PreviousDay` 锚点缺失但有保留节点时仍保留历史（`RestorePreviousDay:308-316`），与确认文案的"内存优先"不冲突（最终是拒绝而非破坏）。192 组组合测试与实现公式一致。
- **L4（Trim/缓存）**：`handled.Split(',')` + `id.Trim() == ClaimsId`，并缓存比较结果避免每帧 split；测试覆盖空白、重复 ID、不重写配置。

## 五、对回执"表述纠正"的复核

1. **F7/F5**：确认。`CfgKeyDay=F7` → `RequestDayStart`（本日开业前，内存优先），`CfgKeyPrevDay=F5` → `RequestPreviousDay`（全目录扫描）。第一轮报告把全目录扫描写成 F7 属表述错误，接受纠正。
2. **`_inputBroken` 不阻断维护 Tick**：确认。`Rewind.cs:833-846` 的维护块在 `:861  if (_inputBroken) return;` 之前执行，首块异常只让热键失效、维护继续。接受纠正。
3. **Claims 只强制"文件读档"走原生菜单**：确认。`forceMenu |= HasMemoryConflict` 仅作用于文件回退；`UseWholeDayMemory` 不含 Claims 判定，故强行启用内存且锚点可用时 F5/F7 仍可用内存。接受澄清。
4. **仅用 `Catalog.Entries` 会漏掉旧命名/大文件**：确认。`Refresh` 只登记自有命名且字节 ≤16MiB 的文件；因此新增 `ReadForRestore` 而非改用目录项遍历是正确的工程选择，比我第一轮的建议更稳妥。

## 六、本轮新发现（均低危，不阻断）

**R1（低 · UX）面板状态行会盖掉建点失败提示** — `src/ProbablyStolenTools/Panel.cs:168-173`

```
if (CustomerCheckpoint.LastCaptureError != null) hint = ...;      // 168-169
if (!MemoryCheckpointsAllowed || HasMemoryConflict) hint = Status; // 170-171
```

`HasMemoryConflict` 为真（即 Claims 用户在强行启用内存后）时，状态行**永久**是冲突提示，会把"最近节点建立失败，旧节点仍保留"以及"顾客节点 n/20 / 等待顾客节点 / 已闭店"全部覆盖——而强行启用恰恰是最容易建点失败的场景。建议把 `:170-171` 移到 `:168` 之前（让失败提示优先），或把冲突提示改为追加后缀而非整行替换。

**R2（低 · 文档）README 测试基线仍是旧数字** — `README.md:248`、`README_EN.md:237`

两处仍写 `CheckpointTests` **430 项**，与 `AGENT.md:12` 的 433、以及实测 433 不一致；同时两句都未提及新增的 17 项可选兼容测试。建议把 430→433，并补"17 项可选兼容测试"。

**R3（低 · 性能）回退读取不回填缓存，旧命名/大文件每次 F5 全量读** — `src/ProbablyStolenTools/CheckpointFileCatalog.cs:58-63`

缓存命中只对"已登记 + 元数据非空 + 长度/时间一致"的文件生效。旧命名文件与 >16MiB 字节的 UTF-8 存档每次都走 `File.ReadAllText`（18MiB 用例即每按一次 F5 读 18MB）。保留兼容路径是对的，但建议在 `ReadForRestore` 内用一个小型只读字典按 `(FullName, Length, Ticks)` 记住回退解析结果（不写入 `_entries`，不影响清理语义）；另外可给回退读加一个宽松字节上限（如 64MiB）以避免异常大文件被整读进内存——现有行为已如此，属加固而非回归。

**R4（低 · 健壮性）新增的 `GetDayCounter()` 调用未按同文件惯例包裹** — `src/ProbablyStolenTools/Rewind.cs:391`

`RestorePreviousDay` 对同一调用是 `try { today = StoreStation.GetDayCounter(); } catch { return; }`（`:296-297`），而 L3 新增的 `Request` 里直接调用。该调用若抛异常会经 `RewindOverlay.Update` 进入 `_inputBroken`，本会话热键永久失效。现有 `CheckpointPicker` 也在每帧直接调用同一 API，属既有假设、触发概率很低；但新调用点与邻近防御风格不一致，建议同样用两行 try/catch 兜住（失败时按"无锚点"处理即可）。

**R5（极低 · 边界）`Plugin.Instance?.Config == null` 时赋值不落盘** — `OptionalModCompatibility.cs:39-53`

此时 `config==null` → `_pendingConfig=null`，两个值只改内存、不保存。仅可能在 `Awake` 绑定前触发，重启后会重新检测并再次置位，无需修改；记录在案以免后续误判为"偶尔不生效"。

## 七、安全与性能结论

- **安全**：本轮改动没有新增读写的攻击面。新增的文件读取只发生在 `PSTools_Checkpoints` 目录枚举结果上（无路径拼接、无外部输入），并新增 reparse point 拒绝；删除逻辑（`Deduplicate`）仍严格限定自有命名；配置写入仍是 `Save()` 的 tmp + `File.Replace`，失败路径已被捕获、去重、限频。`ModLoader.Mods` 只读使用，未反射、未加载第三方程序集。
- **性能**：热路径明显改善——每帧不再拷贝 Mod 列表、不再 split 配置串、不再每帧重建状态字符串；F5 由"逐文件全量解析"降为"缓存命中 + 仅新文件解析"。残余开销均为每帧 ≤20 次字符串比较量级（`Tick` 内 `HasMemoryConflict`/`IsRunning` 各一次、面板显示再一次），无 GC 分配。按 mod 既定约定，异常防御保持在动作边界与一次性路径上，未在每帧路径加 try/catch。

## 八、推送前清单（版本号改动由本人在 push 前执行）

1. 三处 manifest `ModVersion` 与 `Plugin.cs:24` `Version` 一并 0.9.5 → **0.9.6**：`release/manifest.xml`、`release/beta/manifest.xml`、`release/universal/manifest.xml`。
2. 干净构建 → `CheckpointTests`（433）、`OptionalModCompatibilityTests`（17）、`Test-StaticLayouts.ps1`（含 `ReleaseCompatibility` 与 Mono 夹具，需在无沙箱环境下跑）。
3. 重新部署并核对 `deployment.json` 哈希；更新 `AGENT.md` 记录与 README 测试基线（含 R2）。
4. 建议同时处理 R1/R4（各 2–3 行），R3 视时间取舍。
5. 上述通过后才向用户索取 fine-grained token 并 `git push`（不 force）。
