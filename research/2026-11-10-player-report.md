# 玩家报告排查与修复（2026-11-10）

> 后续语言策略已按用户要求改为复用原版语言键，见 `2026-11-10-native-annotation-locales.md`。本文“非中英文停用”的描述是 `41ff21f` 时的历史方案。

## 证据边界

玩家附件位于 `logs\Probably Stolen`，只有：

- `Player.log`：119516 bytes。
- `mod-audit.log`：280087 bytes。
- `GameLog_watchdog.txt`：3911 bytes。

本次附件没有 `PSTools.log`、`PSTools_history.log`、设置文件；Player/audit 中也未找到本 mod ID、品牌头或加载记录。因此不能核实原报告的 959 次捕获、80 次超限，也不能确认这些附件与原报告属于同次游玩。

`Player.log:1494` 的 `LEDGER_LOCALE: ru` 是其他 mod 输出的语言观测。日志确有 `Attempting to use an invalid operation handle`：

- 第 60、135–137 行：TraitsProbablyFun 初始化/打补丁时，`IngotPurityHelper` / `ItemConditionList` 静态初始化进入原版本地化。
- 第 180 行：WolfCompanion 本地化预加载失败。
- 第 232–234 行：More Device Upgrades 初始化 `WineHelper` / `Liquid` 时失败。
- 第 1492–1493 行：原版 MainMenu 表读取失败。

这些是本地化初始化/句柄异常的证据，不足以确定最终根因，更不能直接归责于本 mod。未尝试修补其他 mod 或全局吞异常。Watchdog 有 11 次 EmporiumMenu 超时，但缺少同场次本 mod 计时，不能证明由顾客捕获造成。

## 捕获耗时与分帧决定

旧日志 `after=...s` 是当前时间减顾客到达时间，包含对话、走动和等待；手动节点旧值甚至可能包含上一位顾客的等待。真正执行耗时是 `ms(access=...,walk=...,items=...,verify=...,total=...)`。

新增只读分析脚本：

```powershell
.\scripts\Measure-CaptureLog.ps1 -Path <PSTools.log>
```

对维护者本机备份 `out/report-pretest-20261110-231638/PSTools.log` 实测：935 条捕获，其中 456 条含执行计时、479 条没有；执行中位数 510 ms、P95 1108 ms、最大 2556 ms、超过 5 秒为 0。旧等待时间最大 1202.89 秒。**这些不是玩家数据**，既不能支持报告所列执行时长，也不能排除玩家的大对象图确有更长卡顿。

新版同时输出 `arrivalWaitSeconds` 与 `captureMs`，失败记录 `elapsedMs`。

本轮**没有实现分帧快照**，因此没有需要回滚的分帧代码。原因：

1. 当前逐对象遍历业务图；跨帧期间交易、集合、委托闭包、随机状态可能变化，容易生成从未真实存在过的混合节点。
2. `timeScale=0` 不能阻止所有 Update、非缩放时间回调和其他 mod 写入；后台线程也不能直接访问 Unity 对象。
3. 不能用“摘要/关键字段快照”冒充可恢复节点，或把顾客到达节点悄悄改为成交后节点。

后续实验必须独立 Git commit，先定义业务冻结/取消协议或版本检测与重试边界，再处理捕获和完整校验的每帧预算。验收必须覆盖遍历中交易、移动/销毁/旋转物品、跨天/场景取消、多 mod 回调和失败释放，并记录最长单帧用时，而不只是整份快照平均耗时。

## 已实施修复

- 超限抛带对象/槽位/字节计数、上限、队列及来源路径的异常；释放半成品，保留旧节点，不自动放宽内存预算。失败递增面板 revision，并显示持久状态；重复限额通知去重，手动重试仍提示。
- settle 原生事件保留，轮询降至 0.1 秒一次；日志按状态变化和 60 秒心跳输出，保留压制计数。
- 主/历史日志各 4 MiB + 3 归档；独立错误日志 1 MiB + 2 归档。旧超大文件首轮保留，不截断；经过后续轮转淘汰。详细诊断默认关闭，关键捕获/恢复/失败不关闭。
- 文件节点数量/总容量跨会话扫描，默认 20 份/256 MiB。只删除本 mod 目录中的直接时间戳文件，LRU 按创建/显式恢复使用时间，保护新节点和待恢复节点。未知文件、原版槽位、备份、子目录、链接不删除；不能满足预算时显示/记录清理错误。
- 超出整个磁盘预算的单份文件不创建，旧节点保留；同尺寸重复拒绝提示去重。未改自然存档内容。
- 非中英文本 mod 界面回退英文；名称/水质/酿酒追加停用，保留游戏文本。避免启动同步取 Locale 或等待本地化表；仅已完成的异步结果用于判断。非中文不强制中文字体。
- 名称作用域用 Harmony Finalizer 清理，以 `__state` 区分本次是否已进入；异常不被吞掉。修复超过 8 层时计数不平衡的问题。

## 钥匙卡核查

`DoubleClickInspectionPatch.FindMagnifier` 只接受自有、可用的 `MAGNIFYING_GLASS`，实际 `Target` 只用该返回值。旧日志 `cand(...)` 为诊断遍历，不是“自动使用候选队列”；已移除非放大镜工具的诊断查询，以免第三方 targeting 回调有副作用。

本机正式版 `LockHelper` 反编译确认：`TryOpenLock(key, box, keyLockId)` 必须通过 `CanOpenLock`，后者比较目标 `LOCK_ID`，仅 `LOCK_ID_COMMAND` 通配。`MayTarget/CanTarget` 通过不能证明能成功开锁。未新增全局钥匙卡补丁；如仍可用错卡开锁，需要同次 session 的完整 mod 列表、箱/卡 identifier、复现步骤和实际消费结果。

## 验证与剩余验收

- Release Mono 构建：0 警告、0 错误。
- CheckpointTests：71 项通过，包括超限释放/旧图保留、轮转、settle 去重、跨会话容量、LRU、保护文件、锁定文件、未知文件和语言策略。
- ReleaseCompatibility：55 挂点、73 字段；官方 scanner 能力与清单一致，HardFlags 为空；作用域异常清理、12 层嵌套和真实业务图往返通过。
- 这些是离线托管/结构测试，不是 Unity UI 或玩家 55-mod 环境复现。尚未启动游戏，尚未验收语言切换、实际卡顿或游戏内文件清理。
- 第一次带修复启动时，会按新预算清理既有时间戳文件节点；部署前完整备份保存这些历史文件。

已用 `scripts/Deploy-FullRelease.ps1` 部署到本机正式版 Mods，备份为
`out/full-release-deploy-20261110-232444`。备份和部署校验覆盖 316 个既有存档目录文件；
存档内容、游戏程序集和其他 mod 未变，未导入/覆盖设置，未启动游戏。DLL SHA256：
`595461ADDF608E583CD4E56BD887311A6BD97BF1F10BAF429ED09DAA9B5A408C`。
保持 0.8.13，未推送；后续发布须按版本规则递增并重新部署验收。

仍需玩家补交**问题发生的同次游玩**的 `PSTools.log` 及轮转归档、`PSTools.settings.xml`，并说明“语言问题”是乱码、错译、语言被改、缺失文本还是启动异常，附界面截图和触发操作。不能仅凭俄语代码认定问题原因。

## 第三批附件复核

用户后续提供 `logs\Probably Stolen\3`，说明是其他 mod 的反馈经熟人转交，目前无法联系原玩家。本轮仅只读检查附件，未修改运行代码或游戏文件。

| 文件 | 大小（字节） | 实际内容 |
| --- | ---: | --- |
| `$RNEPFRJ.log` | 119516 | Probably Stolen 的 Unity Player 日志，与第一批 Player.log、第二批同名文件 SHA256 完全一致 |
| `$R3ZCYB4.txt` | 776896 | Slay the Spire 2 / MegaDot 日志，不是本游戏 |
| `$RWGKTKZ.log` | 1327847 | Slay the Spire 2 / MegaDot 日志，不是本游戏 |

第三批唯一的 Probably Stolen 日志 SHA256：
`F5DAA32DFF738A71548B4ED22F3D2534CC68B1EDCFB0BF59B1F094E1A2E6384C`。

两个其他游戏文件开头均出现 `MegaDot v4.5.1.m.14.mono.custom_build` 和 `Slay the Spire 2\release_info.json`，不能把其中的语言代码、异常或硬件信息用于 Probably Stolen 分析。

第二批三个文件也分别与第一批 Player / audit / watchdog 的哈希一致。因此第三批没有新增本游戏的独立运行样本，仍没有 PSTools 日志、设置或本 mod 加载/运行记录。

重复的 Player 日志继续支持以下有限结论：

- `:136–177`：TraitsProbablyFun 打补丁期间，IngotPurityHelper / ItemConditionList 静态初始化进入原版本地化并报无效操作句柄，随后初始化失败。
- `:180–197`：WolfCompanion 的本地化预加载失败，但该 mod 随后仍记录初始化成功，不能把两者混同。
- `:233–273`：More Device Upgrades 打补丁期间，Liquid 静态初始化报同类异常，随后初始化失败。
- `:1492–1493`：原版 MainMenu 两个键也出现句柄错误；不是仅有某条 mod 翻译缺失。
- `:1494`：Ledger 观测到 `ru`，不代表已知玩家实际看到的界面症状。

这些栈表明异常路径涉及启动期同步等待本地化资源，但不足以确认最初是谁破坏句柄、是否有加载时序/重入问题，或与证书错误是否存在因果关系。不能直接归责于某个 mod，也不能将本批用于证明本 mod 快照造成卡顿。

当前将此反馈标记为“附件混入其他游戏，相关日志重复，原始症状不明，归因待证据”。没有理由仅凭本批附件新增全局本地化补丁或调整快照逻辑。
