# Probably Stolen Tools — 交接文档

> 适用源码：正式版 **0.9.6**（2026-11-23）。平台为游戏自带 **Mono / IMod / Harmony**；MelonLoader / IL2CPP 路径已弃用，`#if !MONO` 分支只保证能编译，不再维护。
> 旧版本文档（0.2.1–0.6.0、Demo、早期性能优化过程）归档在 `research/archive/AGENT-0.6.0-history.md`，**不要**按它描述的环境或版本操作。

> 2026-11-23 兼容候选：本机 beta 已更新至 **1.0.0.16 / VERSION 100-Rev16**，游戏 MVID `d927a6e4-ae7a-4e65-a5e2-9f9624c131f3`；beta/universal manifest 已随之更新。下文 11-16 的 1.0.0.14、已部署哈希与规模是历史记录，不是本轮验收。当前新版单表 2915 类型/8235 字段/670208 bytes。可选 Mod 检测使用两版均有的公开 `ModLoader.Mods` 只读表，不调用旧正式版不存在的 `ModHelper.IsModRunning`，不引用第三方 DLL，也不新增权限/前置依赖。酿酒扩展 1.4.4 避免标注/改名/快速反应干预；领地与革命 1.1.18 暂时仅文件读档安全共存，启用时明确禁止内存建点/恢复并强制完整原生菜单载入，不能宣称完成完整实体回溯。维护范围/后续接入要求见 `research/2026-11-23-optional-mod-compatibility.md`。

本文只写当前仍成立的事实。实现细节以源码为准，阶段记录与实验数据看 `research/`。

2026-11-23 设置调整（覆盖上段 Claims 强制门控的旧说明）：`CustomerCheckpoint.DisableMemory=false` 控制全部内存建点/恢复。首次检测 Claims 时置 true，`Compatibility.HandledMemoryConflicts` 持久记录已处理 ID；玩家手动关掉后不再因检测/重启覆盖，移除 Claims 也不自动复位。强制启用不代表完整支持，相关 BUG 不受理；Claims 存在时文件读档仍强制完整原生载入并跳过顾客/门修复。`Rewind.WholeDayUseMemory=true` 仅控制 F5/F7，关闭走文件、个别节点仍可用。`Chemistry.AutoExtraction/RapidReactions` 缺省改为 false，不迁移覆盖旧值。审查入口见 `research/2026-11-23-settings-compatibility-review-handoff.md`。

最新部署为 `out/full-release-deploy-20261123-232330/deployment.json`（通用包 `D:\Program\GameProject\out\release-0.9.6-universal`，ZIP `609C1D8E…`、573261 bytes，构建提交 `8169488`、clean）：主 DLL `37E9FE8E…`、双 profile helper `6129A2D3…`（808448 bytes，0.46D + beta 1.0.0.16），manifest 声明 0.9.6，安装目录哈希与包一致。打包/部署按正式流程重跑：ReleaseCompatibility（84 挂点/76 字段、官方扫描仅 `files`、HardFlags/Error 空）与 Mono 夹具全部通过；90 个存档目录文件、其他本地 Mod、游戏程序集未变，未导入玩家配置。实机首启（游戏 1.0.0.16）加载 `version=0.9.6 layoutBackend=static-token-v1`，Claims 首次检测一次落盘 `DisableMemory=true` 与 `Compatibility.HandledMemoryConflicts=yexiaoyang.InteriorExpansion`（`PSTools.settings.xml` 23:24:33，无异常），随后 `memoryCheckpoints=False fileRestore=native-menu`；完整玩法矩阵仍待验收。此前 `9BA884F8…` / `BA005FA4…` / `54C4DFD2…` / `9547C8A0…` 均为更早候选。

2026-11-23 审查修复：禁用内存不再连带关闭卷帘门维护；首次冲突两个值批量赋值后显式保存，失败就地捕获/去重/5 秒重试，保存最新选择（未持久化前退出不保证跨进程记忆）。Mod 列表 1Hz 轮询，处理 ID Trim/缓存。F5 文件选择复用元数据，保留旧命名/较大 UTF-8 存档回退且不纳入清理；确认文案按锚点实际可用性判断。强行启用只追加状态提示，不追加确认弹窗，依赖偏好仍可编辑。逐项采纳与不采纳原因见 `research/2026-11-23-settings-compatibility-review-receipt.md`。

2026-11-23 推送：版本升至 **0.9.6**（三处 manifest、`Plugin.Version` 与 `MelonInfo` 同步），源码提交 `8169488` 并推送 `main`；随后按正式流程重新打包、部署并完成首次实机启动核对（见上段）。三轮审查见 `research/2026-11-23-settings-compatibility-review-ds.md`、`…-round2-ds.md`、`…-round3-ds.md`；遗留项：`RestorePreviousDay` 日期读取失败仍是静默返回（HEAD 既有、低危）。

2026-11-23 本轮开发候选已部署，主 DLL `9547C8A0…`、双 profile helper `6129A2D3…`（808448 bytes），记录 `out/full-release-deploy-20261123-141215/deployment.json`。基于 HEAD `48652b2` 的 dirty 工作区构建，未提交/打正式包/推送。430 参考 + 12 可选兼容 + 13 读档 + 14 历史测试、无第三方 DLL 的游戏 Mono 回归、真实 VBE 回调往返及最终合并 DLL 扫描通过；只有 `files`。未启动游戏，89 个存档和其他 Mod 校验未变。Claims 内存节点仍明确停用，组合实机待验收。

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

# 当前本机为游戏 beta 1.0.0.16；单版本验证
.\scripts\Test-StaticLayouts.ps1 -GameDir "<游戏目录>" -ManifestPath release/beta/manifest.xml
.\scripts\Deploy-FullRelease.ps1 -GameDir "<游戏目录>" -ManifestPath release/beta/manifest.xml

# 通用包：干净源码构建当前 beta 表，再合并另一版原始 helper
.\scripts\Package-FullRelease.ps1 -GameDir "<游戏目录>" -ManifestPath release/universal/manifest.xml `
  -AdditionalLayoutPaths "<稳定版原始helper>" -OutputRoot "<新输出目录>"
.\scripts\Deploy-FullRelease.ps1 -GameDir "<游戏目录>" -PackageDirectory "<输出目录>\ProbablyStolenTools"
```

当前 0.9.5 通用候选：**430 项**参考测试、13 项存档动作测试、14 项原版历史/齿轮 IO 测试、11 项玩家工具断言（含随包源码复建）；84 挂点/76 字段、16 项官方保护名单核对；双 DLL 扫描仅 `files`，`HardFlags/Error` 空。当前 beta 单表 2883 类型/8160 字段；合并 helper 783872 bytes，按游戏 MVID 自动选择 0.46D / 1.0.0.14 两套完整注册表。beta Mono 的精确布局、完整图往返、预算/边界/GC/MVID/失效令牌拒绝及 500 组耗水对照通过；正式版原始程序集不可用，**稳定分支未重跑实机/扫描验收**。离线通过不等于 Unity 活体验收。详见 `research/2026-11-16-beta-static-layout-compatibility.md` 与 `research/2026-11-16-universal-static-layouts.md`。

0.9.5 新增：**自动建立节点默认开启**（XML `CustomerCheckpoint.AutoCapture`）；关闭后不自动创建晨间/顾客/闭店/返店节点，手动建点与已有节点回溯仍可用，F5/F7 改走文件读档。设置页增加 **快速重载存档** 与 **重掷随机数** 两个带确认/门控的操作，快捷键默认未绑定；快速重载只读当前槽位磁盘档、不先保存/不覆盖活动档，重掷只影响后续随机源、不重建已有内容。见 `research/2026-11-16-save-actions.md` 与 `research/2026-11-16-automatic-checkpoints.md`。

上一批统一审查入口：`research/2026-11-15-feature-batch-review-handoff.md`，覆盖 `44cf260..48853fb`；回执见 `2026-11-15-feature-batch-review-receipt.md`，0.9.3 已由 DS 推送。本轮补 N1/N2 文件保护、N3 不匹配立即取消、N4 脏源码拒绝打包；beta 适配及构建来源以 `research/2026-11-16-beta-static-layout-compatibility.md` 为准，不把旧 0.46D 实测当作 beta 验收。

0.46D 的 2861 类型/8108 字段属于旧分支表，不能混用本轮 beta 的程序集或验收记录。通用包见 `research/2026-11-16-universal-static-layouts.md`：离线合并旧正式表与当前 beta 表，主 DLL 接口不变；只共享相同访问代码，各版本完整注册表/MVID 仍独立，未知指纹拒绝。当前安装为 `8fd29fd` clean 构建的 0.9.5 通用包（主 DLL `F5E8804A…`、helper `8503FA2D…`、ZIP `F2AA18BA…`，包 `out/universal-release-0.9.5-auto-on`）；正式版原始程序集仍缺，不能宣称正式分支重验通过。

### 版本与发布

玩家静态表工具的独立分发流程：运行 `scripts/Publish-StaticLayoutTool.ps1`，取得 `out/static-layout-tool-日期/ProbablyStolenTools-StaticLayoutGenerator-win-x64.zip`。在 GitHub 单独上传附件，不能把工具 exe/Mono.Cecil 放进 Mod 的 Assemblies 或 Mods。玩家完整用法见 `tools/StaticSnapshotLayouts/PLAYER-README.md` 与 README；先退出游戏，执行 `Generate-StaticLayouts.ps1 -GameDir "<游戏目录>"`，默认只生成。`-Install` 只支持默认 `Mods/ProbablyStolenTools` 安装，会把旧 helper 备份到工具 generated 目录，再只替换 helper。该工具不能自动适配任意第三方类型或修复游戏 API 变化，不代表新版本已验收。工具附件已打包测试，但**尚未上传 GitHub Release**，不要声称已可从 Release 下载。推送使用用户临时 token，不写入文件、日志或命令历史。

- 唯一真源：`release/manifest.xml` 的 `<ModVersion>`；`Plugin.Version` 必须同时改。
- 规则：**大版本、中间版本手动决定；小版本每次 `git push` 前 +1**。当前 **0.9.6**。
- 纯文档提交不单独升版；推送前重新构建 → 两组测试 → 部署 → 正常 `push`（不 force）。
- 推送使用用户临时 token，不写入文件、日志或命令历史。
- `release/manifest.xml` 只声明稳定游戏 0.46D；`release/beta/manifest.xml` 只声明 beta 1.0.0.16；`release/universal/manifest.xml` 声明两版。三个 ModVersion 必须与 Plugin.Version 一起更新。不能仅改 manifest 宣称新版本兼容。
- 用干净源码执行 `scripts/Package-FullRelease.ps1 -GameDir "<游戏目录>" -ManifestPath release/beta/manifest.xml` 构建 beta 包。通用包改用 universal manifest 并传 `-AdditionalLayoutPaths "<另一版原始helper>"`；离线合并和逐版本等价验证后，扫描实际合并产物。每包仍仅主 DLL、一个 helper、manifest、preview；正式包和工具包均拒绝 dirty 源码，保留源码提交/哈希/包体记录。`Deploy-FullRelease.ps1 -GameDir ... -PackageDirectory "<包目录>"` 验证当前分支 profile/扫描后部署。不能以 beta 通过代替两版实机验收。

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

液体规则速查：µL 存储（`容量 = mL×1000`）；水/酒精 **>65%** 为主成分；占比 = 成分/总量；植物质不竞争主成分名称。纯度排除水、酒精、脂合剂、脂肪、物理污染物、微生物、植物质、不溶性碎屑、三种菇类糊糊；蛋白/酸/碱 ≥1000µL 才扣；化学污染物按剩余量 0.7% 封顶；还原剂不排除。评级阈值 99/90/80/60/45/30。酒精 74–76% 为医用酒精，≥99.95% 为脱水乙醇。扩展化学容量默认开启：150mL 起、无上限；滴管/实验量器不改名，刻度水罐/储水罐例外。设置中化学主开关下依次为成分标注、容器改名、自动搅拌、快速萃取、快速化学反应、机器预览，均默认开启。`Chemistry.AutoExtraction` 仅控制酒精萃取三种糊糊，新增 `Chemistry.RapidReactions` 独立控制九个非热配方，八种总/子开关组合及未配置拒绝已做离线回归；萃取执行期间抑制反应重入，反应执行也有自身重入保护。旧容量/空名细分 XML 配置保留，不再占设置页子项。水培改名仅 tooltip，直接读所放种子原语言键。快速反应逐步同步原版计算标签，有 512 步硬上限，不加速温度总分派、蒸发/热失活/热分解/热水萃取。自动化受化学总开关、加载/恢复/夜间门控约束。

---

## 4. 节点时机与门控

- 顾客节点在开场对话播完后建立（`StoreClient.isMainDialogueStarted`，8 秒兜底）；不要用 `isAboutToStartMainDialog` 当门控。
- `Snapshot.StoreState`：营业(1)/闭店(0) 允许；手动节点允许同 state 回溯（含早市 state=2）；自动顾客节点仍 0/1。
- 允许跨顾客回溯；剧情/对话类顾客跳过建节点但不清空历史。
- 默认同会话保留当前/上一天，每天独立最多 20 个常规内存节点（包括自动闭店与手动节点），另保留一个不计入常规名额的 MORNING 完整图锚点；每日新增一次完整捕获成本。面板按日期/阶段/建立时刻倒序，今日开业前高于昨日全部节点；第三天释放更早图。闭店手动节点不互相覆盖，不覆盖自动闭店节点。关闭 `CustomerCheckpoint.KeepPreviousDay` 回到当天保留；跨天黑屏/结算/晨报/漏斗传输时拒绝写回。开启保留后，同场景离店只暂停恢复，返店后可继续使用；换场景/真正的文件读档仍清空。关闭保留时离店仍清空。
- F5/F7 优先恢复晨间内存锚点以保留历史；若昨日锚点不可用且还有内存节点，F5 提示并拒绝文件回退，不静默清空。新分支提交前可逆浏览“后续”节点，提交后仅保留最多 80 条已释放图的只读“丢弃”摘要，默认折叠且不能恢复。F6 选择面板向下一个可用节点（包括手动），↑/W、↓/S 切换，Enter 确认，Esc 取消；五个导航/确认键均可改，双击恢复不变。
- 未进入 F6 选择时，导航主键（默认 ↑/↓）也可直接进入选择并向对应方向移动一格；W/S 备用键仍仅在选择中生效，不抢占普通操作，文本输入时不启动选择。文件节点原位更新也会递增版本并取消旧选择。
- 真正离店后返店会等待对话、转场、回调、拖拽、开关门等连续稳定 0.5 秒，再建立独立“返店”内存节点，占当天常规名额；初始到店/重复返店回调不建重复点，不替换晨间锚点/自动闭店点，也不消费当前顾客自动捕获边界。
- 返店要求位置为 IN_STORE；CLOSED 状态允许原版整晚保留的卷帘门锁（不修改该锁），OPEN/MORNING 仍要求解锁。RETURN SHOP 记录真实返店；RETURN WAIT 每五秒检查、仅阻塞状态变化才记录。
- 文件层按 `(runID, slot, day)` 合并 MORNING：Mod 文件节点同日原位原子替换、清理旧重复后才做数量/容量 LRU；原版 `SaveFiles.TrimHistory` 前移除已验证的同日旧归档，保留当前旋转得到的最新档，避免同日重走挤掉其他日期。保持原版轮转、槽位写入、IO 重试和失败恢复；禁用回溯或原版清空历史时不拦截。未知/损坏/非晨间/异周目/异槽位/链接文件不参与按日去重。
- 原版每日唯一包含活动槽位：正常载入后、成功 WriteSlotFile 后清理不晚于活动档的同日历史；SaveGame postfix 不作为成功依据（原版吞部分异常）。同日保存轮转时临时预留一份回滚副本名额，写盘成功才删除，避免当天重存先挤掉其他日期。比活动档更新的异常历史副本保留，绝不自动替换活动槽位。
- 原版去重保护 PendingPoint.Path 及所有保留索引引用（含后置的无效索引项），只移除可验证重复；LoadGame/WriteSlotFile 回调也不得删除待恢复文件。载入日期/槽位/周目不匹配时立即取消 Pending/Settling/Busy 并释放临时文件选择，不再等 90 秒；磁盘恢复文件保留。
- `isGeneScannerScanning` 是设施开关，不是扫描动作，禁止作为任何门控。
- 文件节点回退只加载不写回：快照复制为 `pstools_restore.es3`，通过 `NewGameData.saveFile` 读取；保留 `.pre_restore` 备份；不覆盖当前存档。必须收到原版 `LoadGame` 完成且日期/槽位/runID 匹配后才能删除临时文件并重置内存节点，不能以 `PlayerStore` 存在代替加载完成。文件重载更换对象实例，不保留旧内存节点。
- 运行时文件节点仍全局按 `Rewind.MaxCheckpoints`（默认 20）及 `MaxDiskMB`（默认 256 MiB）限额；`.pre_restore` 每槽位一份覆盖，不随回溯次数无限新增。开发部署备份不由游戏内 Mod 自动删除；`scripts/Prune-DeploymentBackups.ps1` 默认仅预览，显式 `-Apply` 才可清理，保留最近 10 份及每种唯一 ES3/备份内容，删除前验证候选和保留副本哈希，拒绝链接、路径越界、变更/额外存档。独立恢复目录不参与清理。部署脚本发现游戏运行直接拒绝，禁止自动终止游戏。
- 快捷键支持键盘及鼠标中键/侧键 1/侧键 2，不接受左右键；捕获改键当帧仍屏蔽动作。

---

## 5. 日志与诊断

- `EventLog.Write` 品牌头：`[QuickSL]` / `[QuickSL/子系统]`；关键捕获/恢复/失败不随 verbose 关闭。
- 默认日志：Session/语言状态、CAPTURE/RESTORE 阶段耗时与规模/后端、失败/拒绝/超限、真实 full 回退、释放错误、群拖异常。
- 建点失败 toast、损坏节点悬停和 LastCaptureError 使用同一具体原因：静态表错误显示底层异常及生成工具/更新指引，日志输出项目地址；object/slot/byte 超限显示当前有效限额、建议 XML 项和齿轮入口。提示按纯文本绘制，避免 `<Entry .../>` 被 TMP 当标签吞掉；未知错误保留异常类型/消息。不再只显示“详情请查看日志”。
- 主菜单模组齿轮注册 `Snapshot.MaxObjects` / `MaxSlots` / `MaxBytesMB`，与既有 XML 双向同步，重启时 XML 为真源（手改不被 native JSON 旧值覆盖）。对象 1000–1000000、槽 10000–8000000、MiB 16–2048。上限仅控制拒绝边界，不扩大未超限节点的遍历量或按上限预分配；允许更大图后实际耗时/内存可能提高，字节预算不代表整个 Mod 的物理内存上限。
- `[Load] native ms(...)` 每次载入一条，区分 Entry/PreLoad/LoadGame/DecodeSaveItem/历史清理；原版解码后的 onLoaded 等阶段也处于化学自动化禁止区间。晨报关闭另记 `closeMs` 与 `morningAnchorPending`，关闭后晨间锚点同步完整捕获耗时仍见 `CAPTURE client=day-start captureMs`。不把原生载入、关闭回调与后续捕获混作同一耗时。
- `[QuickSL/Geom]` 只在物品视图异常（DESTROYED/MISSING/NOSPRITE/HIDDEN/MISPLACED）时输出。
- `PSTools.log` 4MiB × 3 归档；独立错误日志 1MiB × 2；`PSTools_history.log` 保留并轮转。
- 设置页「查看日志」用 `CustomUIManager.Window(...)` 构建，显示末尾 400 行。

---

## 6. 加载器兼容迁移

2026-11-16 beta 适配：在任何 KnownTypes/KnownFactories 令牌解析前，以固定 PlayerStore/object/HashSet/Vector3 模块锚点校验全部 MVID。KnownTypes/Factories 禁止内联；错误版本负例同时含不存在的类型令牌，须先版本拒绝。新版官方保护名单增加 ModWindowControl，生成器与运行时共享排除并离线核对官方全名单。保留全部其余业务闭包，不能泛化排除 CompilerGeneratedAttribute。单分支表复用相同泛型元数的空声明字段工厂，不减覆盖；beta 单表 664576 字节，同 beta/同边界未合并工厂基线 687616 字节。后续通用包通过离线 MergeStaticLayouts 合并两版，4871 工厂去重至 2055，保留完整的各版本注册表；按游戏 MVID 选择、主 DLL 再检查全部模块。无运行时 Assembly.Load/解压/生成，不扩大能力声明；当前 beta Mono/隔离负例通过，正式版活体未复验。

**0.9.5 的 Mono 发布路径已使用构建时静态布局表**，详见 `research/2026-11-12-static-runtime-graph-migration.md` 与 `research/2026-11-12-loader-migration-review-handoff.md`。构建时生成目标 Mono 字段表，包含私有 Entry/Slot 固定类型令牌；版本严格 MVID 门控，未知布局不动态回退。Mono 编译排除旧表达式生成路径，通用 GameFields 用官方 ModReflect；恢复字段/集合物理存储与安全检查不减。两个发布 DLL 均列入 manifest，仅声明/检测 files；ReleaseCompatibility 的退役能力门槛已加入，net8 宿主表隔离于 out。`scripts/Test-StaticLayouts.ps1` 全套通过；实机仍只覆盖简单节点回溯，完整功能矩阵和多 Mod 组合未验收。下方为迁移过程中的性能记录。

后续实机反馈：基本回溯未发现功能异常，但明显变慢。已独立优化批量物品布局的字段访问：两个固定私有缓存字段采用字面 FieldInfo，公开原点属性直接读取；其他低频访问仍用 ModReflect。完整离线测试及能力扫描通过，后续实机结果见下。该项耗时分布与边界见 `research/2026-11-12-static-layout-restore-performance.md`。

布局修复后实机 7 次恢复平均约 1.26 秒（此前约 2.31 秒），geometry 降至 3–4 ms，全部 diff=0。下一候选为静态读取器的常见 SZ 数组下标读取，其他字段路径和所有校验保持；完整离线测试及扫描通过，整次实机收益待确认。详见 `research/2026-11-12-static-vector-read-performance.md`。

数组候选后实机仅 3 次，平均约 1.31 秒、全部 diff=0，未证明整次提速。最新小候选将静态读取器的标量比较准入类型判断缓存到初始化，原有比较及全部校验不变；完整离线测试/扫描通过，局部基准只有小幅收益，不能宣传已恢复旧后端速度。详见 `research/2026-11-12-static-scalar-gate-performance.md`。

随后按用户要求将剩余保守优化合为一批：扫描内结构体数组单元素拆箱复用、逐槽 owner/存储描述复用、有界跨节点只读字段/数组布局共享、按模块去重 MVID 初始化验证。字段值/节点令牌仍独立，部分根字段不共享，写入/异常/释放边界有专门游戏 Mono 测试，所有普查/预检/修复/最终校验保留。全套离线验证通过；8 万对象/72.8 万槽的三组新旧大图对照全部 diff=0，但恢复相关阶段合计中位数旧 1649 ms / 新 1665 ms，**未证明显著提速**。详见 `research/2026-11-12-static-graph-batch-performance.md`，统一部署后再一次性实机验收。

该批实机 5 次平均 1286 ms，同样未明显提速。恢复阶段现另有精简候选：静态固定布局 `CensusForRestore` 仅遍历完整引用拓扑并保留所有布局/预算检查，不再探测当前标量；目标 `ValidateRestore` 对显式稳定存储及已批准块只验证原 owner 保留身份，不计算被丢弃的写前差异；未知后端/块保持原完整读取。owner 修复计划并入同一次完整差异扫描，消除后续全槽规划遍历。原生刷新后扫描及最终全量校验/失败 full fallback 保留；日志用 `censusMode` / `preflightMode` 识别实际路径。同夹具三组离线对照中普查 427→303 ms、预检 275→20 ms，恢复相关阶段合计中位数 1599→1237 ms（约 -22.6%），不含 Unity 刷新，实机待验收。证明、回退边界及验证见 `research/2026-11-12-restore-stage-consolidation.md`；不能把写前稳定存储证明推广成恢复后免校验。

最新阶段精简后的 16:37 会话简单节点测试：5 次恢复平均 **1004.8 ms**，上一批为 1285.8 ms，约缩短 21.9%；全部 diff=0 / releaseOk=True，走 topology / retained-storage，无 finalFallback。不是严格同操作 A/B，亦不是完整功能矩阵验收。首节点仍 1479 ms，初始化卡顿没有解决。暂未发现低风险且明确显著的进一步阶段删减，停止追加性能候选，转入审查。完整范围及风险清单见 `research/2026-11-12-loader-migration-review-handoff.md`。

2026-11-15 晨报关闭停顿跟进：载入后的晨间节点约 18.6 万对象/102 万槽，普通约 15 万对象/83 万槽，不能把旧 1.3 秒当作同规模对照。每日仍需完整读取新晨间锚点，类型缓存本已跨日保留。新增独立性能提交：ManagedObjects 弱身份表清理从每 4096 个反复扫全表改为按新身份数/表规模摊销，最终局部游戏 Mono 对照（双方均缓存回调）490.50→161.46ms，不等于整体快照提速；StateGraph 用现有 _sources 做唯一成员索引，不再保存第二个 HashSet，完整图串行对照 215.48→213.57ms，未显著提速。所有预算/完整恢复/校验保留。CAPTURE 额外记 identitySweepMs/identitySweeps/identityVisited 与 GC 三代次数。证明及实机待验收边界见 `research/2026-11-15-morning-capture-amortization.md`。

22:52 后续会话：冷捕获 1802ms（access 474ms），同规模 185714 对象晨间重载捕获 1365ms，正常跨天晨间 1129ms；首顾客 890ms，返店两次 937/899ms。两次恢复 1802/2020ms，均 diff=0，无 finalFallback；其中一次 owner 修复消除 1 项刷新差异。原版加载仍需 9022/11058ms，DecodeSaveItem 聚合 7618/10039ms；这不是晨间捕获已解决的开销，也不能据被补丁包裹的原版解码计时排除其他 Mod 的影响。跨夜另有 8.8 秒 watchdog，未完成归因，不能归到 1129ms 的随后晨间捕获。完整证据和待审查项见统一交接。

- 技术依据：`research/2026-11-12-static-type-token-proof.md` 证明构建时固定 ldtoken 可表达私有泛型 Entry/Slot。窄探针之后已完成生产源码替换及离线完整图测试，已有基本节点实机反馈，但完整功能矩阵仍待验收；工具/原生离线宿主绝不随 Mod 发布。
- 官方公告 **2026-12-10** 起拒绝 `dynamic-code` 与 `reflection-external`（以及 native/threads）。0.9.5 的 Mono 发布路径已迁移为构建时静态布局表，发布包只声明 `files`；仍只保证已收录的 0.46D / 1.0.0.14 构建，发行说明不得写“已兼容任意下一版”。
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

0.9.1 玩家报告 `Uncovered static snapshot layout: PaperBookRegistry+<>c`：生成器排除注册/信任组件，但运行时按游戏程序集继续展开其闭包。现以共享 `SnapshotTraversalBoundary` 统一边界，受保护引用只保留身份、不读写字段，显式受保护根拒绝；其他未知布局仍拒绝。游戏 Mono 回归和全套测试通过，已部署，玩家实机待测。详情见 `research/2026-11-12-protected-registry-capture-fix.md`；失败日志现含 manual/closed，另一份无日志的自动节点问题尚不能确认同因。

后续边界复查 `79cab6e`：游戏 owner 排除规则（含 ES3 前缀）已由生成器与运行时共享同一函数。目标程序集 111 个被排除类型/嵌套类型全部不展开，其余游戏类型均有表；不表示所有字段组合都支持。另修复 decimal? 的引用检查误入 Decimal 字段，以及 object 集合中的基本装箱标量/顶层 System 枚举缺表；保留原装箱身份及字段恢复，不降级为空对象。全套通过，最终双 DLL 已部署，实机待测；当时审查交接见 `research/snapshot-boundary-review-handoff.md`。第三方自动适配仍搁置；独立游戏布局生成器后于 2026-11-15 按新需求打包完成，尚未上传，见第 1 节。

DS 边界代码审查通过。部署哈希疑点已复核：原 FDC 与 DS 的 CB 主 DLL 在 3026 项类型/字段/方法记录（含 IL）中无差异，内嵌 Git informational version 不同，不能据哈希或提交时间断定旧包缺少代码。现从干净 `970e8c955f510e2bf88202cd8f77029bbfa81606` 完整验证、重复 Rebuild 并重部署，主 DLL `197099BB...`、helper `CBB8F36A...`；完整哈希与来源见 `research/2026-11-13-snapshot-artifact-provenance-followup.md`。后续交接同时记录构建提交号/干净状态/双哈希；纯文档提交变化也可能改变重建哈希，不要要求旧包始终等于文档更新后的 HEAD 构建。

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
2. `CheckpointTests` 430 项全过；`ReleaseCompatibility` 84 挂点/76 字段/漏斗门控/官方保护名单/`HardFlags` 通过；另跑存档动作 13 项、原版历史/齿轮 IO 14 项、玩家生成工具 11 项。beta 与稳定版需要各自验收，不混用实机记录。
3. 部署后：首/普通/手动节点往返、恢复后群拖、顾客贴图、正常交易/送客。
4. 物品：售出/移动/旋转/拆分合并/嵌套容器/前柜台回溯，贴图位置与朝向正确，无“空气占用”。
5. 多语言：切换语言后水质/纯度/评级/酿酒标签跟随；缺键回退；三个设置开关生效。
6. F5/F7：同会话优先内存锚点且保留节点；真正文件回退只加载不写回，`.pre_restore` 备份存在。
7. 关闭 `SemanticEntityBlocks` 的回滚模式可正常走 legacy。

---

## 10. 历史资料

- `research/archive/AGENT-0.6.0-history.md`：旧交接文档。
- `research/2026-11-12-single-pass-entity-candidate.md`：单次遍历实体后端设计与实机记录。
- `research/2026-11-12-customer-visual-rewind.md`、`2026-11-11-multi-select-rewind.md`：顾客视觉与群拖。
- `research/2026-11-12-semantic-backend-review-ds.md`、`2026-11-12-semantic-backend-review-followup.md`：本轮 DS 审查与整改。
- `research/2026-11-12-loader-capability-migration.md`：退役能力迁移方案。
- `research/modding-guide/ModdingGuide.zh-CN.md`：游戏 Mod API 指南（注意能力表可能落后于官方现行政策）。
