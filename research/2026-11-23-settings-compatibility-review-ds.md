# 2026-11-23 设置与可选 Mod 兼容 Code Review（DS）

审查日期：2026-11-23
审查范围：HEAD `48652b2` 之上的 dirty 工作区（17 个改动文件 + 7 个未跟踪文件），重点是 `OptionalModCompatibility.cs`（新增）、`Plugin.cs`、`CustomerCheckpoint.cs`、`CheckpointPicker.cs`、`Panel.cs`、`Rewind.cs`，以及上一轮 VBE 排除（`WineAppraisal.cs` / `LiquidComposition.cs` / `ChemistryAutomation.cs`）。
审查口：`research/2026-11-23-settings-compatibility-review-handoff.md`。
当前版本：**0.9.5（未升版、未推送）**。

---

## 1. 结论

**代码层面通过（可提交、可推送），但建议先修 M1、M2 两项再进实机。**

- 安全面没有发现新问题：本轮新增代码只做只读 Mod 注册表查询、内存里的策略判定和一次配置写盘；没有新增文件 IO 路径、没有加载第三方程序集、没有 `Process.Start`/网络/反射能力变化，产物扫描仍是 `Capabilities: files`。
- 本轮声明的每条门控在代码里都能找到对应实现，且交接文档里“不能夸大”的边界描述与实现一致（见第 4 节逐条核对）。
- 发现 **1 个中等问题**：`DisableMemory` 顺带停掉了与内存无关的卷帘门看门狗（`CfgRepairStuckShutter`），而 Claims 用户默认就会落进这个状态。
- 发现 **1 个中低问题**：首次冲突检测里的配置写盘没有异常防护，任何 IO 失败会沿 `RewindOverlay.Update` 的 catch 走到 `_inputBroken=true`，本局剩余时间所有热键与所有 Tick 永久停摆（只能重启）。
- 另有 4 项低危（提示/交互/文案/持久值比较）与 2 项可优化性能点（均为小改动）。

---

## 2. 审查基线与复验

在我的环境里独立复跑（游戏目录 `E:\SteamLibrary\steamapps\common\Probably Stolen`，beta 1.0.0.16）：

| 项目 | 结果 |
|---|---|
| `dotnet build src\ProbablyStolenTools -c Release` | **0 警告 / 0 错误** |
| Mono 生产单表 | 2915 类型 / 8235 字段 / **670208 bytes**，与 `AGENT.md` 一致 |
| 主 DLL `ProbablyStolenTools.dll` SHA256 | **54C4DFD288FD09DCB30C1070777FDF19DBBEB5B391FA9B152C58953B8D985925**，与交接文档记录一致（独立重建复现） |
| 主项目直接构建出的 beta-only helper | 670208 bytes / `509D8A98…`，与合并 helper `6129A2D3…`（808448 bytes）不同 —— 符合“普通主项目构建只产生 beta 单表，不能当通用包”的说明 |
| `CheckpointTests` | **430 项全部通过** |
| `OptionalModCompatibilityTests` | **14 项全部通过**（实跑输出逐条 PASS） |
| `ReleaseCompatibility` | 8 项契约检查 + 84 挂点/76 字段 + 官方扫描器 + `Capabilities: files` / `HardFlags` 空 / `Error` 空 **通过**；随后在“配置写盘往返”步骤中断（见下） |

关于 `ReleaseCompatibility` 的中断：堆栈是 `ModConfig.Save()` → `File.Replace` → `UnauthorizedAccessException`。我用同样参数在本机受限 shell 里单独验证了 `[System.IO.File]::Replace` 会被拒绝，因此这是**审查环境的文件沙箱限制，不是代码回归**，也不推翻交接文档里“本站已通过”的记录。但顺带证明了一件事（见 M2）：`ConfigEntry.Value` 的写盘异常没有任何一层捕获，会原样向上冒。

**未复现/未覆盖**：Unity 实机外观与点击、VBE 的桶装酿造活体行为、Claims 1.2.13 业务审计 —— 与交接文档一致，不额外声明。

---

## 3. 问题清单

### M1【中】禁用内存节点会连带停掉与内存无关的卷帘门看门狗

位置：`src/ProbablyStolenTools/CustomerCheckpoint.cs:58-60`、`:183-215`、`:225-271`、`:1769-1808`

`Enabled` 现在被写成：

```csharp
internal static bool Enabled => Plugin.CfgCustomerCheckpoint?.Value == true && PatchesReady
    && OptionalModCompatibility.MemoryCheckpointsAllowed;
```

而 `Tick()` 在 `!Enabled` 时直接 `return`：

```csharp
if (!Enabled) { if (History.Items.Count > 0 || ...) Clear("已禁用"); return; }   // :195
if (!AutoCapture) { ...; ShutterWatchdog(); return; }                            // :196-205
...
ShutterWatchdog();                                                               // :214
```

`ShutterWatchdog()`（`:225-271`，由 `CfgRepairStuckShutter` 开关，默认 true，`Plugin.cs:358-359`）修的是游戏自己泄漏的 `lockShutterButtonFlag`，与内存快照无关；`Clear()` 不会碰这个标志（`:1776-1808` 只清快照/视觉/交互状态）。所以：

- 只要 `DisableMemory=true`，看门狗整局不运行；
- 而 `DisableMemory` 正是黑名单检测给所有 Claims 用户**默认打开**的开关（本轮的既定设计）；
- 结果恰好是“最容易收到闭不了店报告的那批玩家，自动修复被静默关掉”，设置项看起来仍然开着。

修复方向（保持“禁用后清空旧图”的行为）：把内存门控从 `Enabled` 里拆出来，`Tick` 只对内存路径判断，非内存维护（`ShutterWatchdog`）继续运行。例如：

```csharp
internal static bool Enabled => Plugin.CfgCustomerCheckpoint?.Value == true && PatchesReady;
internal static bool MemoryCapture => Enabled && OptionalModCompatibility.MemoryCheckpointsAllowed;
internal static bool AutoCapture => MemoryCapture && Plugin.CfgAutoCheckpoints?.Value == true;
```

`Tick` 里把 `!Enabled` 分支拆成“`!MemoryCapture` → Clear + 重置 armed 状态 + **仍然 ShutterWatchdog()** + return”，只有功能总开关真正关闭时才完全跳过。`DeferDestroy`/`NextCustomer`/`MultiSelectDiagnostics`/`Rewind.UseWholeDayMemory` 继续使用内存语义的属性即可，行为不变。改完需要同步更新 `AutomaticCheckpointFixture` 里 `Enabled == !disabled` 的断言。

### M2【中低】首次冲突检测的配置写盘失败会永久打断整个 overlay

位置：`src/ProbablyStolenTools/OptionalModCompatibility.cs:20-31`、`src/ProbablyStolenTools/MonoModConfig.cs:18-29`、`:73-82`、`src/ProbablyStolenTools/Rewind.cs:831-856`

`ApplyConflictDefault()` 直接赋值两次：

```csharp
Plugin.CfgDisableMemoryCheckpoints.Value = true;
Plugin.CfgHandledMemoryConflicts.Value = handled.Length == 0 ? ClaimsId : handled + "," + ClaimsId;
```

生产配置打开了 `SaveOnConfigSet`（`Plugin.cs:417`），所以每次赋值都会同步走 `ModConfig.Save()`；该函数用 `doc.Save(temp)` + `File.Replace(temp, _path, _path + ".bak")`（`MonoModConfig.cs:78-81`），在杀软占用、云同步锁文件、路径只读、两个游戏实例同时开等情况下会抛 `IOException`/`UnauthorizedAccessException`（我在本机受限环境里实测到同类异常确实会穿透 setter）。

而 `Refresh()` 在这些调用点都位于 try 之外：

- `OptionalModCompatibility.Tick()` ← `RewindOverlay.Update` 的 try 里（`Rewind.cs:835`）；
- `CustomerCheckpoint.Capture()` 的**第一行**，在自身 try 之前（`CustomerCheckpoint.cs:757-759`）；
- `CustomerCheckpoint.CaptureManual()` / `Restore(Snapshot)`（`:570-572`、`:1110-1112`，都没有自身 try）；
- `Rewind.Restore(Point)`（`Rewind.cs:479-480`）。

`RewindOverlay.Update` 的 catch 会把 `_inputBroken = true` 并只记一条日志（`Rewind.cs:847-854`），随后 `if (_inputBroken) return;`（`:856`）—— 本局剩余时间内**所有热键、所有 Tick（建点、看门狗、对话快进、交易）全部停摆**，玩家只能重启。一次配置写盘失败的代价与后果明显不成比例。

修复建议（很小）：

```csharp
private static void ApplyConflictDefault()
{
    try { /* 原逻辑 */ }
    catch (Exception e)
    {
        if (e.Message != _conflictWriteError)
            EventLog.Write("[Compatibility] conflict default write failed: " + e.GetType().Name + ": " + e.Message);
        _conflictWriteError = e.Message;   // 只记一次，避免每帧刷屏
    }
}
```

或者把 `Refresh()` 调用移进各调用点已有的 try 里。后者更彻底，但前者改动最小、语义最清楚（配置写不进去 → 本次不生效 → 下次再试，与交接文档“未成功持久化时可能下次重试”的描述一致）。

### P1【性能·低中】`Tick()` 每帧复制一次 `ModLoader.Mods`

位置：`src/ProbablyStolenTools/OptionalModCompatibility.cs:11-18`、`:71-90`；证据：`out/compatibility-20261123/game/ModLoader.decompiled.cs:93`

```csharp
public static List<LoadedMod> Mods => new List<LoadedMod>(mods);   // Rev16 每次返回副本
```

`Refresh()` 每次执行 `_mods = ModLoader.Mods`，而 `Tick()` 由 `RewindOverlay.Update` **每帧**调用（`Rewind.cs:835`）。因此每帧一次 `List<LoadedMod>` + 内部数组分配；Claims 存在时 `ApplyConflictDefault` 还会每帧再 `Split(',')` 一次（`:24-26`）。

单帧量很小（十几二十个 mod 时约 10²B），但在“游戏本身 GC 抖动”的前提下这是纯浪费的常驻垃圾，而且**这个状态在游戏内根本不会变**（改启用会置 `RestartRequired`，必须重启）。建议：

- 只在动作边界刷新（`Capture`/`Restore`/`CaptureManual`/打开面板时），`Tick` 用缓存值；
- 或者把 `Tick` 的刷新降到 1 Hz（`Time.unscaledTime` 节流）。

现有测试 `OptionalModCompatibilityTests/Program.cs:161-170` 只断言“`MemoryCheckpointsAllowed` 不触发读、`Refresh()` 恰好读一次”，上述两种节流方式都不会破坏它，但需要把测试意图写清楚（“快照在 Tick/动作边界更新”）。

### P2【性能·低】`RestorePreviousDay` 每次都全量读取并完整解析目录内所有 `.es3`

位置：`src/ProbablyStolenMod/Rewind.cs:320-350`；已有可复用组件：`src/ProbablyStolenTools/CheckpointFileCatalog.cs:14-43`

F7 的扫描对每个 `*.es3` 做 `File.ReadAllText` + `SaveMetadata.Read`，而 `SaveMetadata` 的 `Reader.Object("")` 会遍历完整文本并校验 `Position == text.Length`（`SaveMetadata.cs:9-19`），也就是每次按键都要把目录里所有存档全文读一遍、全量解析一遍。默认 `MaxCheckpoints=20`、实测存档约 1.0–1.3 MB（`backup_save/save_0.es3`），单次 F7 是“十几到几十 MB 的读 + 数千万字符的逐字符解析”，在主线程上容易看到可感知停顿。

同一个文件里的 `CheckpointFileCatalog.Refresh` 已经做了正确的事：按 `(Length, LastWriteTimeUtc)` 缓存解析结果、跳过 >16 MiB 与 reparse point（`:34-41`；`Read` 同守卫，`:14-27`）。建议 F7 扫描改为 `Catalog.Refresh(Root)` 后遍历 `Catalog.Entries`（保留同样的 `State/Slot/RunId/Day` 过滤与“取最近”排序），既省 CPU 又顺带补齐 reparse/尺寸守卫（当前手写扫描对这些没有显式检查）。

### L1【低】“强行启用”状态下没有任何风险提示

位置：`src/ProbablyStolenTools/Panel.cs:167-172`、`OptionalModCompatibility.cs:44-51`

`MemoryCheckpointWarning`（“存在mod冲突，已禁用内存节点……”）目前只在**被拒绝**时出现（`CaptureManual`、`Restore`、`CheckpointPicker.Begin`）。玩家在 Claims 运行中把 `DisableMemory` 关掉后，`MemoryCheckpointsAllowed` 变 true，一切提示消失，面板 hint 回到普通状态 —— 但按交接文档，这正是“强行启用、BUG 不受理”的状态。

建议：区分三种状态（无冲突且启用 / 冲突被禁用 / 冲突且强行启用），第三种在面板 hint 与设置行 tooltip 上显示“已强行启用内存节点，冲突风险自负”一类文案。不改任何门控，只补提示。

### L2【低】内存禁用时，三个依赖开关仍可点但无效

位置：`src/ProbablyStolenTools/Panel.cs:178-186`、`:727-741`

可交互性规则只有“鉴定子开关依赖自动鉴定”“化学子开关依赖化学主开关”两条。`AutoCapture => Enabled && CfgAutoCheckpoints`、`UseWholeDayMemory => AutoCapture && CfgWholeDayMemory`，所以 `DisableMemory=true` 时“自动建立节点 / 保留上一天节点 / 整日回退使用内存”三项点了也不生效，排查时容易被当成“开关坏了”。

建议复用化学主从开关的写法：`if (config == CfgAutoCheckpoints || config == CfgKeepPreviousDay || config == CfgWholeDayMemory) toggle.interactable = MemoryCheckpointsAllowed;`。

### L3【低】F5/F7 确认文案在“内存锚点其实不可用”时高估了内存路径

位置：`src/ProbablyStolenTools/Rewind.cs:379-434`（`requiresFileReload`）、`:266-281`、`:299-317`

`requiresFileReload` 只看配置（`!UseWholeDayMemory`、`KeepPreviousDay != true`）。但实际执行时：

- `RestoreDayStart` 在内存晨间节点缺失后会退到 `Rewind.Points` 的**磁盘文件**（`:273-278`），此时确认框写的是“优先使用内存节点并保留回溯历史”；
- `RestorePreviousDay` 在 `Nodes.Count == 0` 时会走到 `:319-355` 的文件扫描。

这两条是既有行为，但本轮把“整日回退使用内存”做成显式开关后更容易被玩家质疑文案不实。建议在 `Request()` 里预探测（`CustomerCheckpoint.DayStart(day)` / `Nodes.Count`），把“锚点确实可用”算进 `requiresFileReload` 的判定。

### L4【低】黑名单 ID 比较不做 `Trim`/去重

位置：`src/ProbablyStolenTools/OptionalModCompatibility.cs:24-28`

```csharp
foreach (string id in handled.Split(','))
    if (id == ClaimsId) return;
```

`string.==` 是序数比较，不做裁剪。玩家或工具把配置写成 `"a, yexiaoyang.InteriorExpansion"`（逗号后有空格）时会被当成“未处理”，于是再次自动置 `DisableMemory=true` 并再追加一次 ID，与“玩家手动选择永不被覆盖”的承诺相冲突的正是这种只差一个空格的边界。建议 `handled.Split(',').Select(s => s.Trim())`、写入时去重，并对大小写保持与 `ModManifest.ID` 相同的比较方式。

### 备注（非缺陷，供参考）

- `BrewingOwns` 的取词顺序已经是对的：先 `IsTag("VBE_STATE")`、再 `!IsRunning(BrewingId)`，只有装了 VBE 才会走到 `FindAllChildItems()`（`GraphUtils.FindAllChildrenType<GameItem>`，`out/decomp/game/GameItem.cs:1523-1526`，每次分配一个 List）。`LiquidComposition.NoRename/Analyze`、`WineAppraisal.IsUnfermentedBottle` 都在 tooltip 路径上，但都是 UI 事件驱动而非每帧，当前可以接受；若日后 VBE 用户反馈悬停掉帧，再考虑把“容器内有营养果/梦帽菇”的判定缓存到 tag。
- `Rewind.Capture` 每次捕获都要 `File.ReadAllBytes` + `Encoding.UTF8.GetString`（`Rewind.cs:190-191`），峰值约 2× 存档大小；`SaveMetadata` 本身有 16 MiB 上限（`SaveMetadata.cs:32`），实测存档 1.0–1.3 MB，安全但属于“存档长大就会痛”的写法，暂不需要动。
- `Panel` 里 `i != 1 && i != 2` 的鉴定索引依赖（`:182-183`、`:736`）本轮顺序调整后仍然正确（0 自动报价 / 1 自动提交 / 2 始终覆盖）；`_chemistryStart/_chemistryEnd` 用 `_settings.Count` 推导而不是字面量（`:315`、`:324`），这一点做得好。建议给两处字面量索引加一行注释，避免下次插行再踩。
- `CfgHandledMemoryConflicts` 为空字符串时 `Split(',')` 得到 `[""]`，不会误匹配，无需特判。

---

## 4. 交接声明逐条核对

| 交接声明 | 代码证据 | 结论 |
|---|---|---|
| `Chemistry.AutoExtraction/RapidReactions` 缺省 false，旧值不覆盖 | `Plugin.cs:279-284`；`ModConfig.Bind` 仅在键缺失时才用默认值（`ModConfig.cs:57-72`，旧 BepInEx 值仅首次导入） | 一致 |
| `CustomerCheckpoint.DisableMemory=false`、`Rewind.WholeDayUseMemory=true`、`Compatibility.HandledMemoryConflicts=""` | `Plugin.cs:402-407` | 一致 |
| 禁用文案中文/英文严格一致 | `OptionalModCompatibility.cs:46-49`，`\n` 与换行文本完全对应；测试 `Program.cs:154-160` 断言 | 一致 |
| 黑名单只有 Claims，不含 VBE/Perks | `OptionalModCompatibility.cs:6`；全文件无其他 ID | 一致 |
| 只在“未记录该 ID”时置 true 并记录 | `:20-31`（唯一写入点） | 一致 |
| 手动关闭后：继续帧更新/再检测/停用再启用/写盘重载都不再覆盖 | 全文件无复位路径；`Tick`/`Refresh` 只调用 `ApplyConflictDefault`，命中记录即 return；fixture 覆盖重载 | 一致 |
| 移除黑名单 Mod 不自动复位 | 同上，无“未运行则置 false”逻辑 | 一致 |
| 强行启用只解除策略门控 | `CustomerCheckpoint.cs:1119` 等预算/布局/MVID 检查不变 | 一致 |
| `UseWholeDayMemory = AutoCapture && CfgWholeDayMemory` | `Rewind.cs:264` | 一致 |
| F7 允许内存走今日晨间节点，否则走磁盘且不先保存 | `Rewind.cs:266-281` → `GameSaveActions.Reload()`（只读当前槽位文件，`:16-44`） | 一致 |
| F5 允许内存且保留上一天时优先昨日晨间图 | `Rewind.cs:299-317` | 一致 |
| 关闭 WholeDayUseMemory 不影响 F9/面板单节点/自动建点 | `UseWholeDayMemory` 仅出现在 `Rewind.cs:264,270,299,388` 四处 | 一致 |
| 关闭 AutoCapture 仍手动可用、F5/F7 走文件 | `Rewind.cs:270`、`CustomerCheckpoint.cs:759` | 一致 |
| 确认文案跟随有效路径；需要文件读档但 UI 不可用时拒绝 | `Rewind.cs:388-403`、`:426-433`；`CfgConfirmWholeDay` 关闭时直接执行（`:382`） | 一致（见 L3 的边界） |
| 设置排列：鉴定→顾客对话→水质/酿酒→化学→内存/自动/跨日/整日/确认→日志/动作/快捷键 | `Panel.cs:307-346`，21 个开关 | 一致 |
| 鉴定三首项保持原索引依赖 | `Panel.cs:182-183`、`:736` vs `:307-309` | 一致 |
| 21 行布局模型已补测 | `CheckpointDayTests.cs` 改 `toggleCount` 含 21、化学索引 8/15 | 一致 |
| Claims 强制原生菜单载入，独立于 DisableMemory | `Rewind.cs:475-480`、`:537-545`；`GameSaveActions.Reload` 本身已 `forceMenu: true` | 一致 |
| Claims 时 `PostLoadResume` 跳过顾客/卷帘门修复 | `Rewind.cs:1083-1093` | 一致 |
| 无第三方程序集硬引用、只声明 `files` | 本轮扫描 `Capabilities: files`、`HardFlags/Error` 空 | 一致 |

---

## 5. 安全专项

- **不写玩家存档**：本轮新增代码无存档路径读写；`DisableMemory` 只影响内存快照与文件回退的选择。
- **写盘范围**：只新增/复用 `PSTools.settings.xml`（`Application.persistentDataPath`）与 `PSTools.log`，都在 Mod 自己的目录内，命名固定无用户输入拼接。
- **不触碰加载器信任记录/Steam 类型/其他 Mod 文件**：`OptionalModCompatibility` 只读 `ModLoader.Mods`，不调用 `SetModEnabled`，也不引用第三方 DLL。
- **能力声明**：本轮没有新增反射/动态代码/线程/原生调用；`ReleaseCompatibility` 扫描只报 `files`。
- **失败降级**：策略失败的方向都是“禁用内存节点/拒绝操作”，不存在“检测不到就误开内存回溯”的分支（`MemoryCheckpointsAllowed` 默认 true，但只有白名单 ID 精确匹配才会自动置 false，误判方向安全）。
- 需要注意的只有 M2：**配置写盘的失败路径没有防护**，虽然不改任何游戏状态，但会把 mod 打成“本局失效”。这是本轮唯一有实际影响的安全相关缺口。

## 6. 性能专项小结

本轮新增代码的每帧成本只有 P1 那一处（一次 `List<LoadedMod>` 复制，Claims 时多一次 `Split`），其余都是动作/事件驱动。更值得后续处理的是 P2（F7 每次全量解析存档），其次是 `BrewingOwns` 在 VBE 用户下的 `FindAllChildItems` 分配（备注）。`Panel` 的仓储文案有 key 缓存（`CheckpointStorage.cs:11-26`），未引入每帧 IO。

## 7. 发布前必须完成

1. 修 M1、M2（建议），并同步更新 `AutomaticCheckpointFixture` 的 `Enabled` 断言。
2. 按 `AGENT.md:54-58` 的规则，把 **0.9.5 → 0.9.6**：`release/manifest.xml`、`release/beta/manifest.xml`、`release/universal/manifest.xml` 与 `Plugin.Version`（`Plugin.cs:24`）同时改。
3. 从最终源码重建 → 重跑 430 参考 + 14 可选测试 → 用 `Package-FullRelease.ps1 -AdditionalLayoutPaths` **重新合并 helper**（主项目直接构建只产生 670208 bytes 的 beta 单表，不能当通用包）→ 扫描实际产物 → 部署核对哈希。
4. 推送用临时 token，不写入文件/日志/命令历史，不 force push。
5. 实机按交接文档“待实机验收”5 项执行；建议补两条：**禁用内存节点时确认卡死卷帘门仍能被修复**（M1 回归）、**Claims 运行中关掉禁用后能见到冲突风险提示**（L1）。

## 8. 建议补充的实机验收项

1. Claims 启用 + `DisableMemory` 自动打开：连续两天营业，确认闭店按钮从未卡死（验证 M1 的场景本来就不该被内存门控影响）。
2. Claims 启用后手动关闭 `DisableMemory`：面板/提示是否明确显示“冲突中已强行启用”；F5/F7 是否仍走原生菜单载入（不重建顾客、不改卷帘门）。
3. 配置文件只读或被杀软占用时启用本 Mod：观察是否出现“热键全失效”并核对日志（M2 的可观测后果）。
4. 存档目录里存在 ≥10 个 `.es3` 时连续按 F7：记录 `EventLog` 时间戳，评估 P2 的主线程停顿。
