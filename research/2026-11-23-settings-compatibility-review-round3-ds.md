# 设置/兼容性 —— 第二轮审查跟进（第三轮 Code Review）

- 审查对象：`research/2026-11-23-settings-compatibility-review-receipt.md` 末尾新增的「第二轮审查跟进」小节及其描述的工作区改动
- 基线：`HEAD 48652b2` + dirty 工作区；版本仍 `0.9.5`，未提交、未推送
- 结论：**通过**。R1 / R2 / R4 均已按回执描述落地实现，R3 暂缓与 R5 不改的理由成立；新构建可复现、部署记录自洽；未发现新的安全问题，仅 1 项 HEAD 既有的低危遗留（见 §五）。

---

## 一、声称 vs 实测

| 回执声称 | 本人复核 | 结果 |
| --- | --- | --- |
| 主 DLL `9BA884F8…` | 重新 `dotnet build -c Release` 后 `Get-FileHash` = `9BA884F852384A94DFCAA5FE46B7DB567769532291C28D6FF76CD65CF32BE73B` | 一致（可复现） |
| helper `6129A2D3…` / 808448 bytes / 双 profile | `deployment.json` 同值；`out/review-round2-20261123-merged/ProbablyStolenTools.StaticLayouts.dll` = 808448 bytes | 一致 |
| 433 项参考 + 17 项可选兼容 | 现场重跑：`433 tests passed`、`Passed 17 optional-mod compatibility tests` | 一致 |
| 13 读档测试 | 现场重跑 `GameSaveActionTests`：`13 production save-action tests passed` | 一致 |
| 14 历史/齿轮 IO 测试（沿用上一轮） | 本机 12/13 项通过，最后一項在 `File.Replace` 处被 DSH 沙箱拒绝（第一轮已独立确认该拒绝为环境限制，非代码问题）；该路径本轮未改动，回执也已声明沿用 | 环境受限，接受 |
| 构建 0 警告 / 0 错误 | 同上，`0 个警告 / 0 个错误`，2915 类型 / 8235 字段 / 670208 bytes | 一致 |
| README 中英文同步 433 + 17 | `README.md:248`、`README_EN.md:237` 均为 433 与 17 项可选兼容测试 | 一致（R2 已修） |
| 面板优先级：状态 → 失败提示 → 节点选择 | `Panel.cs:168-172`：`Status`（168-169）→ `LastCaptureError`（170-171）→ `CheckpointPicker.Hint`（172） | 一致（R1 已修） |
| 确认界面锚点探测加局部异常防护 | `Rewind.cs:391-399` try/catch，记录 `类型: 消息`，保持 `hasMorningAnchor=false` | 一致（R4 已修） |
| 部署记录 `full-release-deploy-20261123-224852`，未改配置/未启动游戏 | `Version=0.9.5`、`InGameValidated=false`、`SettingsImported=false`、`ExistingSaveFilesUnchanged=true`、`ExistingGameAssembliesUnchanged=true` | 一致 |
| 扫描仍仅 `files`、HardFlags/Error 空、84 钩子 + 76 字段 | `out/review-round2-20261123-universal-scan.log:89/94/96/97/100` | 一致 |
| 仍不升版、不提交、不推送 | 三处 manifest 与 `Plugin.cs:24` 均 `0.9.5`；`git status` 无提交 | 一致 |

**改动范围核对**：按文件修改时间，22:40（第二轮审查结束）之后被写入的仅 `Panel.cs`、`Rewind.cs`、`README.md`、`README_EN.md`、`AGENT.md` 五个文件，与回执声称的修复面完全一致，没有夹带其它源码改动。

**日志可信度**：`static-suite` 两轮日志哈希不同、差异只在 `已用时间`/bench 耗时行 → 确为重新执行；`merge.log` 两轮逐字节相同（同一确定性合并输入）；`universal-scan.log` 两轮逐字节相同——因合并产物、清单与钩子集合均未变化且扫描输出不含时间戳，属正常，但也因此**不能单独证明"重新扫描过"**；该结论已用产物哈希（主 DLL `9BA884F8`、helper `6129A2D3`/808448）与部署记录交叉确认。

## 二、R1 复查（面板提示优先级）

```csharp
string hint = ...顾客节点/等待/已闭店/尚未开业...;            // 164-167
if (!MemoryCheckpointsAllowed || HasMemoryConflict) hint = Status;  // 168-169
if (CustomerCheckpoint.LastCaptureError != null) hint = ...;        // 170-171
if (CheckpointPicker.Active) hint = CheckpointPicker.Hint;          // 172
```

- 目标达成：Claims 强行启用场景下，"最近节点建立失败"重新可见，冲突/强制状态退居其后；`CheckpointPicker` 提示仍保持最高优先级，不被抢占。
- 未按我建议"拼成长行"、也未新增弹窗，符合回执的取舍（避免状态行过长与重复提示）。
- 残余（可接受）：内存被禁用且从未建点失败时，状态行仍恒为冲突/禁用文案，不再显示"顾客节点 n/20 / 等待顾客节点 / 已闭店"。禁用内存后本就没有顾客节点活动，信息量损失可以忽略。

## 三、R4 复查（确认界面锚点探测异常防护）

```csharp
try {
    int day = StoreStation.GetDayCounter() - (action == PreviousDay ? 1 : 0);
    hasMorningAnchor = CustomerCheckpoint.DayStart(day) != null;
} catch (Exception e) {
    EventLog.Write("[Rewind] confirmation anchor unavailable: " + e.GetType().Name + ": " + e.Message);
}
```

- 异常不再沿 `Request → RewindOverlay.Update` 逃逸，因此不会触发 `_inputBroken` 的会话级热键停摆；`hasMorningAnchor` 保持 `false`，沿用既有"按文件路径处理 / 面板缺失时取消"的规则，方向保守。
- 会记录异常类型与消息（`e.Message` 不含堆栈，避免日志膨胀），与同文件既有诊断风格一致。
- 一处语义说明（非缺陷）：探测异常时确认文案会偏向"将读盘且丢失全部内存节点"，而 `Execute` 内重新探测若成功仍可能走内存路径——属**过度告警**而非漏告警，方向安全。

## 四、R3 暂缓 / R5 不改 复查

- **R3**：接受暂缓。该回退路径只在"用户目录里存在旧命名文件或 >16MiB 字节且字符数仍达标的存档"时触发，常规自有文件已走缓存；在缺证据时引入额外缓存会扩大状态面。建议给一个**可判定的触发条件**写进文档或 issue：例如单次 F5 `Stopwatch` 超过 200ms、或旧命名/大文件数量 ≥20 时再做有界缓存（只读字典，不写入 `_entries`，不影响清理语义）。这样既不现在改，也不至于永远无人跟进。
- **R5**：不改合理，`Awake` 前的宿主未就绪窗口自愈（重启重新检测），不值得引入初始化顺序逻辑。

## 五、本轮残余观察（1 项，低危、HEAD 既有）

**`RestorePreviousDay` 的日期读取失败是静默返回** — `src/ProbablyStolenTools/Rewind.cs:296-297`

```
int today;
try { today = StoreStation.GetDayCounter(); }
catch { return; }
```

R4 已让"确认阶段"的同类异常有日志，但 `Execute` 真正执行时这一处仍是"吞掉并静默返回"：用户按下 F5、确认框正常弹出并确认，之后什么都不发生，也没有 toast 与日志。该写法在 HEAD 即存在、非本轮引入，且与 R4 修复后的诊断风格不一致。建议后续顺手改为 `catch (Exception e) { EventLog.Write(...); PanelNotice.Show("无法读取当前天数，已取消", true); return; }`。不影响本轮通过。

## 六、安全与性能结论

- 安全：本轮仅改显示顺序、异常包裹与文档，无新增 IO、无新增可见性/权限、无新增第三方依赖；配置写入路径与删除路径均未变动。
- 性能：`Panel` 每帧的取值为两次 ≤20 元素字符串比较（`MemoryCheckpointsAllowed` 读配置、`HasMemoryConflict` 扫快照），无分配；`Request` 仅在玩家按键时多一次 `DayStart` 线性查找（节点 ≤20）；无热路径回归。

## 七、推送前清单（版本号改动由本人在 push 前执行）

1. 三处 manifest `ModVersion` 与 `Plugin.cs:24` 一并 0.9.5 → **0.9.6**（`release/manifest.xml`、`release/beta/manifest.xml`、`release/universal/manifest.xml`）。
2. 干净源码构建 → `CheckpointTests`(433)、`OptionalModCompatibilityTests`(17)、`GameSaveActionTests`(13)、`NativeSaveHistoryTests`(14，需无沙箱环境)、`ReleaseCompatibility` + Mono 夹具、静态表合并/可复现性。
3. 重新部署并核对 `deployment.json` 两个哈希；更新 `AGENT.md` 部署段与 README 基线（已同步，只需改版本号）。
4. 视情况顺手处理 §五 的静默返回（2–3 行）。
5. 以上通过后向用户索取 fine-grained token 并 `git push`（不 force）。
