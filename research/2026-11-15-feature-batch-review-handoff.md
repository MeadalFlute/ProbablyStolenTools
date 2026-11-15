# 跨天回溯、化学辅助与晨间优化：统一 Code Review 交接

日期：2026-11-15。供 DS 直接读取本地源码审查，不使用旧插件。

## 1. 审查范围与来源

- 发布基线：`44cf2609194673ca1b9c8eb97bff5e846336bf47`（0.9.2）。
- 本批代码终点：`48853fb2ecec6e98899b3f212eb4f481095eaa65`。
- 完整差异：`git diff 44cf260..48853fb`，70 个文件，包含测试和过程文档。
- 最后两项增量：`git diff da65961..48853fb`，晨间身份表优化、失败指引、齿轮设置和独立工具。
- 性能变更单独提交 `bd44ce1`；指引/设置/工具提交 `48853fb`。
- 本交接和 AGENT 的后续纯文档更新不改变以上代码终点。
- 版本仍 0.9.2；没有推送、上传附件或创建 Release。本交接不是正式审查通过声明。

本次交接前复核了最新实机日志、关键增量与存档清理路径，重跑离线套件，未发现新增阻断问题。不代表对 70 个文件完成独立逐行审查。

## 2. 当前功能与实现边界

### 回溯与历史

- 当前/上一日各自最多 20 个顾客、手动、自动闭店节点，另各保留一份完整晨间锚点；第三日释放更早图。
- F5/F7 优先内存晨间锚点，保留历史；昨日锚点不可用且仍有内存节点时，拒绝悄悄走文件重载。
- 真正文件加载以原版 LoadGame 完成且 day/slot/runID 相符为依据；更换运行时后必须清除旧图。
- 浏览旧节点不立刻删未来；真实操作提交新分支后，释放未来图并留下最多 80 条只读折叠“丢弃”摘要。
- 面板日期/阶段/时刻倒序；F6、独立上下键启动选择、确认/取消、双击恢复、鼠标中键/侧键改键。
- 离店暂停，真实返店在稳定 0.5 秒后建独立节点；CLOSED 允许整晚常驻卷帘锁，OPEN/MORNING 仍要求解锁。
- 文件节点与原版归档按 run/slot/day 去重；成功 WriteSlotFile 后才能移除同日回滚副本，失败不能用 SaveGame postfix 假定成功。
- 活动存档只读；不主动 SaveGame，不覆盖活动 ES3。Mod 文件总容量/LRU、每槽 `.pre_restore` 覆盖规则保留。

### 化学辅助

- 150mL 起、无最大容量的标注/改名；植物质不竞争主成分；空容器名称和水培种子 tooltip 名。
- 拖放到搅拌机后按原版 BlendInto 搅拌；主要产物匹配、三类糊糊归一、批次容量预留、不同产物原位保留；补原版音效。
- 正量酒精触发三类糊糊全量萃取，允许超容量，完整计划和 int 溢出检查后写入。
- “快速化学反应”独立于“快速萃取”，九个明确非热反应白名单，保留原比例/取整和条件；逐步同步状态，最多额外 512 步，无变化即停。
- 四类设施 tooltip 只读预计今夜产物/去除量，缺水/缺容器警告；荧光莓使用实际 wine_berry 语言键，统一名称（数量）排版。
- 主/子功能默认开启，可分别关闭；加载、恢复、夜间等禁止区间不执行自动化。
- 水培“显示 13、实际 +14”尚未复现，不擅改数值；异常工作核对日志仍保留。

### 晨间优化与可诊断性

- 弱身份表按新身份数量/表规模摊销清理，活对象 ID 不变；不按节点/天清理字段布局缓存。
- StateGraph 以已有 `_sources` 去重，处理顺序改存 List，消除重复 HashSet；预算/完整恢复/最终 diff 不删减。
- CAPTURE 新增 identitySweepMs/identitySweeps/identityVisited 与三代 GC 次数。
- 失败 toast、失败行及 LastCaptureError 给具体异常、实际预算/XML 示例；完整异常及项目地址仍进日志。
- 原版 Mod 齿轮注册三项限额，XML 重启时为真源，双向镜像、禁用释放事件。
- 提高预算不扩大未超限图、不按上限预分配，但新允许的大图本身会增加内存/耗时；逻辑字节预算不是进程物理内存上限。
- 独立 Windows x64 自包含生成器只重建已安装游戏的固定静态表，不提供任意第三方类型适配，不修复游戏 API 改动。

## 3. 最新实机证据

会话：`2026-11-15 22:52:15` 至 `22:56:22`，游戏 0.46D，候选 48853fb。

| 节点 | objects | captureMs | access | walk | items | verify | identitySweepMs |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 首次载入 day95 晨间 | 159072 | 1802 | 474 | 925 | 57 | 342 | 41.2 |
| 再载入 day94 晨间 | 185714 | 1365 | 0 | 965 | 70 | 330 | 41.9 |
| 首顾客 waterStockpiler | 150498 | 890 | 0 | 591 | 45 | 254 | 0 |
| 闭店 | 149506 | 933 | 0 | 614 | 43 | 276 | 0 |
| 返店一 | 149505 | 937 | 0 | 614 | 47 | 275 | 0 |
| 返店二 | 149509 | 899 | 0 | 466 | 61 | 369 | 0 |
| 正常跨天 day95 晨间 | 154089 | 1129 | 0 | 748 | 51 | 330 | 0 |

- 共 7 次捕获、2 次恢复；本会话无 CAPTURE/RESTORE FAILED、schema fallback 或 finalFallback=True。
- 两次恢复 total=1802/2020ms，最终均 diff=0 / releaseOk=True。
- 闭店恢复曾发现刷新后 1 项差异，通过 owners 模式修复（1 owner / 19 slots），不是漏校验，也未 full fallback。
- 最后一次 F5 记录 previous-day in-memory restore day=94; history retained。
- 返店实际建点两次，不仅有 RETURN SHOP 日志；按日去重记录为 active slot unchanged。
- 同规模 185714 对象晨间旧样本 2775/3276ms，本次 1365ms；非严格 A/B、样本少，不能宣传稳定百分比或保证所有存档都回到 1.3 秒。
- 仅一个正常跨天晨间样本；次日首顾客未测，不能把本次重载后的首顾客当作完整跨天矩阵。

仍有独立停顿：

- 原版加载聚合 9022/11058ms，其中 DecodeSaveItem 7618/10039ms，历史清理 309/156ms。
- 计时包住原版入口及其他已挂载补丁，不能据此将所有解码开销归罪于原版、或证明本 Mod 完全无影响。
- 22:54 跨夜 watchdog 报 8.8 秒停顿；缺同阶段细分，不作因果结论。随后晨间 1129ms 发生在 22:56，不能混为同一次停顿。
- 游戏日志另有重复目录、缺失物品标识、前柜台放不下导致物品销毁等警告；没有足够证据归因本 Mod，不能把“无快照失败”写成“游戏无警告”。
- `PSTools_errors.log` 最新更新时间仍为 20:58，是旧错误，不代表 22:52 会话新增失败。

日志副本：`out/review-evidence-20261115-2252/`，含 PSTools.log、错误日志、Player.log 和对应 GameLog。仅为本地证据，未上传玩家数据。

## 4. 建议优先审查

1. **存档删除与回滚安全**：NativeSaveHistoryPatch、DayArchivePolicy、CheckpointFileCatalog。核对 run/slot/day/阶段、原版文件命名、链接拒绝、活动档与较新历史保护、删除前重读、IO/索引失败及成功写盘时机。IO 夹具用 stub API，不等于真实 ES3 写盘故障注入。
2. **跨天身份与分支释放**：CustomerCheckpoint、CheckpointHistory、CheckpointLoadGate、CheckpointReturnGate、Rewind。确认夜间/晨报/转场/加载门控、两个日预算独立、旧视图存活、失败不毁旧图、重复睡觉/回溯不产生旧身份写入。
3. **自动化真实原版调用顺序**：ChemistryAutomation、BlendBatchPlan、ChemistryRapidReactions、ReactionCompletion。核对单/群拖销毁顺序、批次预留、超容量/溢出、标签同步、反应重入和跨配方副作用；热反应不能误入白名单。
4. **预测只读与语言键**：FacilityPreview/Projection、HydroponicWorkWatch、TooltipNamePatch。不能读 tooltip 就补标签、推进 RNG 或工作；水培/鱼池预测限制必须与 UI 语义一致。
5. **身份优化证明**：ManagedRuntime.Id 的新对象计数/GC/弱表清理、StateGraph 的 `_sources` 成员资格及分区集合覆盖。运行时仍假定游戏主线程调用，不应因 CWT 本身线程安全而宣传整个注册器线程安全。
6. **错误/UI/配置生命周期**：CheckpointFailureMessage、Panel、NativeSnapshotSettings。检查包装异常、静态布局/缺 API 指引是否过度承诺、最大支持值、长 toast 与 XML 纯文本、重启 XML 优先级和事件解绑；原生齿轮与异常长文本缺专项实机证据。
7. **独立工具与发布边界**：Generate-StaticLayouts.ps1、Publish-StaticLayoutTool.ps1、Program.cs 的 PLAYER_LAYOUT_TOOL 分支。输出/安装路径、链接、运行中拒绝、旧 helper 备份、生产表复现、随包源代码与许可证；不要把 exe/Cecil/net8 测试表放进 Mods。
8. **离线运维脚本**：Prune-DeploymentBackups、Restore-Day94、Deploy-FullRelease。只能手动运行，部署不自动结束/启动游戏；清理需验证保留副本。day94 恢复早已完成，不因审查重复运行。

## 5. 本次重跑验证

- `scripts/Test-StaticLayouts.ps1 -GameDir <game>`：Release 构建 0 警告/0 错误，430 参考测试，84 挂点/76 字段、漏斗门控、500 组耗水对照。
- 游戏 Mono 静态表 2861 类型/8108 字段精确对照；完整图往返、预算、边界、身份/GC、版本拒绝、生成字节复现通过。
- 官方扫描两 DLL 合并仅 files；HardFlags 和 Error 为空，manifest 匹配。
- 原版历史/齿轮设置 IO 夹具 10 项通过。
- 已打包玩家工具 10 项断言通过；测试安装只作用于 out 内的复制夹具，不改真实游戏 helper。
- 证据：`out/validation-20261115-review-ready.log`、`out/validation-20261115-review-native-settings.log`、`out/validation-20261115-review-player-tool.log`。

本次只更新交接文档，未追加运行时改动。实机已验证基本流程，不代表九种反应、所有语言/设施/多 Mod/故障恢复完成穷举验收。

## 6. 部署与工具附件的精确来源

主 DLL / helper 均由干净代码提交 `48853fb2ecec6e98899b3f212eb4f481095eaa65` 构建。
本次完整验证重建后，构建产物与当前游戏 Mods 内文件哈希仍一致：

```text
ProbablyStolenTools.dll
BA01F2C2E9D1B35D8A9257449DAD187B56F171BF64EC576105A048FDD1F9B405
ProbablyStolenTools.StaticLayouts.dll
CBB8F36AECB701BD46BEB87EAB53B63F4782E222056B7FEE0C452D7A2C8E1386
```

部署目录：`E:\SteamLibrary\steamapps\common\Probably Stolen\Mods\ProbablyStolenTools`。
部署备份：`out/full-release-deploy-20261115-224834`，当时 75 个存档文件验证未变，未启动游戏。
独立 day94 恢复目录：`out/save-recovery-day94-20261115-214109`，勿清理或再次执行恢复。

工具 zip：`out/static-layout-tool-48853fb/ProbablyStolenTools-StaticLayoutGenerator-win-x64.zip`。
工具 package.json：BuildCommit=48853fb 全号，BuildDirty=false。

```text
ZIP SHA256
FE6EA31D49E275A5A1EFC876CE2E4FB16397BD5B3F42BED91FA37EAD11B5A5C0
```

**尚未上传 GitHub**，本机缺发布凭据；不要给玩家声称附件已可下载。生成器仅匹配当前接口，未来游戏版本仍需验收。
发布前按 AGENT 统一升小版本、构建/验证/部署并记录新的干净提交及双哈希，再上传主包与独立工具附件。纯文档提交变化导致重建 informational version 改变时，不能仅凭主 DLL 哈希不同断言漏代码。

## 7. 历史说明

下面是按时间写的过程记录，存在“当时待测/旧行为”，不要混用作当前规范；冲突时以当前源码、AGENT 和本交接为准：

- `2026-11-15-chemistry-crossday-handoff.md`：基础需求、算法与预测局限。
- `2026-11-15-feedback-followup.md`：语言键/排版/反应、读档竞态与水培未复现问题。
- `2026-11-15-rewind-picker-followup.md`、`2026-11-15-rapid-reaction-toggle.md`：晨间锚点、分支摘要、F6、独立快速反应。
- `2026-11-15-return-archive-followup.md`、`2026-11-15-active-history-morning-followup.md`：返店、归档去重、活动档保护和备份清理。
- `2026-11-15-morning-capture-amortization.md`、`2026-11-15-snapshot-guidance-player-tool.md`：最后两项增量的证明、测试和发布限制。
