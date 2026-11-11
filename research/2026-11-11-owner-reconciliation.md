# 第二次业务图写回：按变化对象修复

日期：2026-11-11，版本保持 0.8.14，不推送。基于 `2026-11-11-reconcile-difference-sources.md` 的可读实机证据实施；这是独立的安全工作阶段，不是直接按日志四字段做白名单。

## 依据与范围

17:28 会话的七次恢复中，六次刷新后只有磁带播放器 modifiedState.dict 的四个内部字段不同；另一次手动节点还包括 BargainUIManager.trackedClient。二次全图写回仍需 206-240 ms。更早会话还出现过原字典 buckets/entries 数组内容变化，不能假定永远只需重接四个引用/计数。

本轮保持第一次 Graph.Restore 完整，只优化同一同步回调中、原生刷新之后的第二次业务图写回。捕获、根与字段覆盖、预算、对象/数组/字符串身份、字典容量与版本、原生布局缓存排除、视觉恢复顺序均不变。不调用保存、不重放交易/库存回调、不重建集合、不裁剪容量、不分帧；未扩大语义载荷覆盖范围。

## 算法与安全条件

1. 原来的 reconcile-scan 改为 PrepareReconciliation，复用原完整比较循环。全部普通字段、全部字段块的所有字段仍读取一次；诊断上限 12 不影响扫描、变化槽数或变化对象收集。
2. 普通字段发现差异时记 owner；字段块 CollectDifferences 额外返回完整变化数，不能用有限诊断字符串推断块是否变化。计数非法则直接失败。
3. 仅明确声明“原始存储写入、无属性 setter/业务回调/跨 owner 写副作用”的 ManagedStateAccess 和精确 SemanticTagBlock 支持局部修复。未知访问器或任意未知块默认全图写回；不能因字段名相似自动许可。
4. 最多追踪 128 个变化 owner；超限仍扫描完整图，之后全图写回。局部计划会遍历已有捕获描述，将变化 owner 的**全部已捕获字段**纳入写回，绝不是只写不同槽位或日志前 12 条；筛选根仍只包含其捕获表面，未捕获字段不受影响。
5. 最多接受 8192 个待写逻辑槽，字段块按完整块字段数计；超过后丢弃局部索引并用原 Graph.Restore。只在生成计划时遍历内存中的捕获条目，不新增 live 字段读取、捕获或拓扑发现。临时索引和 owner 集合有界。
6. 应用局部计划前仍 Validate retained identities，保持普通标量 -> 块标量 -> 普通引用 -> 块引用顺序；每类内部保持原捕获顺序。结构体/结构体数组仍用已有逐层复制回写，引用仍使用真实存储和 GC 屏障。
7. 无差异时不写业务字段；配置禁用或不支持/规模超限且有差异时全图恢复。字典被替换后，扫描依然检查所有捕获的原数组/对象；变化的旧数组按 owner 全容量恢复，最后恢复原引用，不处理未捕获的新对象内部。
8. 计划只在当前同步恢复回调立即使用，一次性消费；中间任意图扫描、图写回或新计划会使旧计划失效，释放后也不可使用。它不是可跨帧缓存的补丁：图操作序号不能检测图外线程直接写字段，仍依赖现有同步恢复模型，不新增并发安全承诺。

## 最终校验与回退

常规路径仍为：首次全图写回 -> 原生刷新 -> 完整扫描/第二次写回 -> 全部 VisualState -> geometry -> 最终 transforms -> **完整最终校验** -> 清理弃置视图。

局部路径的最终校验如果**完整读完后仍有差异**，可且仅可回退一次：

全图 Restore -> 全部 VisualState -> geometry -> 最终 transforms -> 再次完整校验。

再次有差异仍沿原流程失败、要求重载，不能报成功。第一次完整写回、局部写回、读取、视觉或几何过程中的异常都直接传播，不以捕获异常的方式隐式重试；旧 full/none 模式也不增加重试。失败后的视图保留及 faulted 规则不变。最终回退发生在视图清理之前，尚未丢弃对象。

该回退只是防止已发现的校验不一致，不证明全部图外/视觉状态正确。首次 Unity 活体验收前不得继续放宽范围。

## 配置与日志

- 新配置 `Snapshot.OwnerReconcile=true`，启动 Session 追加 ownerReconcile。关闭后重启可恢复旧完整二次写回；不影响已保存节点的解释，不需改存档。部署不预改玩家 settings。
- RESTORE OK 保留现有阶段及 reconcileDetail，追加 `repair(mode=owners|full|none,reason=...,changedSlots=...,trackedOwners=...,plannedSlots=...,finalFallback=...)`。
- reason 为 eligible、disabled、access、block、owner-limit、slot-limit；none 表示本次没有业务差异，不等于跳过视觉/几何或校验。
- changedSlots 是完整扫描得到的实际不同槽数（按现有比较语义），不再受消息上限影响；reportedDiff 仍为最多 12 条诊断数。trackedOwners 是有界已跟踪数量，回退时不保证是实际不同对象总数；plannedSlots 是写回计划逻辑槽数，不是实际执行 SetValue 次数或内存字节。
- scan 包含完整读取、诊断格式化和捕获条目索引筛选；write 包含计划验证和写回。五项 reconcileDetail 和仍等于 reconcile。
- finalFallback=True 时，finalVerify 时间包含失败的完整校验、全图回退及其视觉/几何收尾和再次校验，不能当成纯扫描耗时。此时新增一条 `REPAIR FULL FALLBACK reason=final-diff`，最多 12 条、每条 128 字符的字段名，无业务值；随后异常也保留这条诊断。正常不增加每帧日志。

## 验证

- Mono Release：0 警告、0 错误。
- CheckpointTests：192 项全过，新增 29 项。覆盖完整 owner 表面、干净 owner 不写、零差异、消息上限之外的变化、末尾读取异常、对象/槽限制、配置禁用和未知后端/块、标量先引用后、结构体数组、字典扩容/删除/共享存储/Keys/Values/活动枚举器、脱离当前拓扑的原数组、循环身份、nullable/浮点位模式、筛选根、private/readonly、委托不调用、过去/未来及 GC、多轮随机混合图、一次性/失效计划、最终全图校验与回退、持久差异和回退异常。
- 新增模型布局测试涵盖反复过去/未来切换、位置/旋转/翻转、owner 修复和最终全图回退后的几何收尾。只是模型，不是 Unity 画面验证。
- ReleaseCompatibility：实际 PlayerStore/StoreStation/GameItemElement、数组和字典扩容下，局部修复与原递归全图快照独立校验一致；开关禁用仍完整恢复。实际 TagState 九字段及原生子类逐字段修复均与独立旧图/语义对照一致，字典共享/替换/扩容及原身份继续通过。
- 55 挂点 / 73 字段，HardFlags 为空，能力声明未增加。不重新构建已弃用 Demo/IL2CPP。

### 隔离稀疏变化基准

```powershell
dotnet run -c Release --project tests/ReleaseCompatibility/ReleaseCompatibility.csproj -- `
  "E:\SteamLibrary\steamapps\common\Probably Stolen" `
  src/ProbablyStolenTools/bin/Release/Mono/ProbablyStolenTools.dll release/manifest.xml --benchmark-reconcile
```

两个后端均为同一混合标签后端、同一直接字段写回；对照为旧来源扫描 + 完整 Restore，新路径为计划扫描 + owner Apply。使用真实程序集未初始化业务对象，在 .NET 6 运行，不启动 Unity。

每轮 1024 字典、16384 标签、32768 初始字典条目（含子类与别名），53253 对象 / 349232 逻辑槽。先预热，交替顺序五轮取中位数，每轮前强制 GC；只修改一个标签和向一个字典新增 80 项，以模拟刷新后的稀疏变更。每次均完整验证恢复结果，局部计划为 204 逻辑槽。不是大范围变化或真实存档基准。

两次独立运行：

| 指标（全图 / owner） | 运行 1 | 运行 2 |
| --- | ---: | ---: |
| 扫描 ms | 9.40 / 11.67 | 9.36 / 10.04 |
| 写回 ms | 39.22 / 0.74 | 42.29 / 0.75 |
| 扫描＋写回 ms | 48.35 / 12.38 | 52.72 / 10.74 |
| 扫描＋写回分配 B | 8952128 / 2898888 | 8952128 / 2898888 |

单项中位数之和不一定等于合计的中位数。新扫描有额外内存索引筛选开销，收益来自不再写干净 owner。基准不包含第一次全图恢复、普查、完整真实 UI/geometry、Shadow 或日志 I/O，不能据此承诺整局提速或 Unity 第二次写回达到相同比例。

## 下一步与停止边界

此轮是第二次写回策略首次实机变更，到部署后停止，等待用户方便时验证：

1. 重启游戏，同一存档开店取得 3-4 个顾客节点，另建至少一个手动节点。
2. 移动、旋转并买卖物品后，在面板按过去 -> 未来 -> 手动 -> 过去反复恢复，至少 6 次；检查数量/金钱、贴图/朝向与碰撞。方便时包含一次议价状态变化，不强制进行液体配方测试。
3. 完成后读取 RESTORE OK 的 repair 与阶段、RECONCILE DIFF、REPAIR FULL FALLBACK 和错误日志。无需 Verbose；任何异常停止继续回溯，保留日志并重载游戏，不以拖拽修正画面代替正确恢复。

用户正在休息，不自动启动游戏或发送定时提醒；此处确需用户操作后才能继续扩大或宣称实机通过。

## 部署

2026-11-11 17:47 已通过标准脚本部署至正式版 Mono，版本保持 0.8.14。

- 备份：`out/full-release-deploy-20261111-174741`（含存档、Mods 和 deployment.json）。
- 已部署 DLL SHA256：`842F1DB537C5BF6B4C15A27B4166CF635BB514ED985C9517EBF95745DB749B8D`，与构建产物一致。
- 78 个原有存档目录文件逐一哈希验证未变；游戏程序集与其他 Mod 原有文件验证未变。
- SettingsImported=False，未预改玩家设置；GameStarted=False，未启动游戏。
- 保持独立提交便于回滚，不推送、不改版本号。部署时首次 Unity 活体验收待用户操作；后续日志复核见下节。

## 首轮实机日志复核

用户完成测试后，检查 2026-11-11 17:50:09.806 启动的 PSTools 会话及 `GameLog_2026-11-11_17-50-18.txt`、Player.log。部署代码为独立提交 `611be0f`，Session 明确 ownerReconcile=True。对照为 17:28:15.347 会话，仍使用旧完整二次写回。

- 本次 5 次捕获（4 个顾客节点、1 个手动节点），9 次恢复，目标顺序为 1 -> 4 -> 5 -> 2 -> 3 -> 4 -> 1 -> 2 -> 5，包含重复往返、手动节点、买卖及新增/弃置视图。
- 全部恢复为 `diff=0 releaseOk=True`，全部 `mode=owners,reason=eligible,finalFallback=False`。没有 CAPTURE/RESTORE FAILED、语义 MISMATCH 或 REPAIR FULL FALLBACK。
- 本样本每次 changedSlots=4、trackedOwners=1、plannedSlots=10，来自 cassettePlayer.modifiedState 标签字典的四项变化，但写回的是整个 owner 的 10 个捕获槽，不是四字段白名单。
- 首节点语义捕获 MATCH，两次恢复该节点均独立 MATCH。仍只覆盖标签基类字段；不改变第三方对象 opaque 策略。

### 实际耗时

下表为各会话全部成功恢复的中位数，单位 ms。旧会话 7 次，新会话 9 次；目标图规模、操作顺序和运行抖动不完全相同，不是严格 A/B，也不能把阶段中位数相加作为整次中位数。

| 阶段 | 旧完整二次写回 | 新 owner 二次写回 |
| --- | ---: | ---: |
| 普查 census | 132 | 127 |
| 预检 preflight | 88 | 91 |
| 首次全图 write | 213 | 222 |
| 原生 refresh | 79 | 76 |
| 二次修复完整 reconcile | 359 | 157 |
| 其中完整 scan / 计划 | 63 | 70 |
| 其中第二次 write | 233 | 15 |
| 其中 views | 41 | 41 |
| 其中 geometry | 5 | 4 |
| 其中 transforms | 25 | 25 |
| 最终完整 finalVerify | 64 | 63 |
| 整次 total | 980 | 773 |

第二次写回旧范围 206-240 ms，新范围 14-16 ms，中位数下降约 94%；二次修复整体下降约 56%；整次恢复中位数下降约 21%，新范围 711-861 ms。完整扫描有额外计划成本，符合设计预期。首次完整写回未变快，不能将二次写回收益泛化到所有阶段。

捕获未改策略：本次 captureMs 为 854 / 339 / 415 / 438 / 298；首节点含读取器预热和只读 Shadow，后续 298-438 ms。不是本轮优化成果。后续捕获 walk 为 209-320 ms，仍是捕获主要成本。

### 限制与既有告警

- 本次所有 BARGAIN CAPTURE/BEFORE/AFTER 均为默认 False/0，不能视为非默认议价状态已验收。实机没有触发规模超限或最终差异回退，这些分支仍只有离线/原生夹具覆盖。
- 日志仅能确认捕获图、语义对照和几何收尾执行成功，不能单独证明贴图、朝向、碰撞、画面文字或图外第三方状态正确。
- GameLog 中 NotoSans-Medium SDF 中文缺字警告 278 条、TabletUI 隐藏父级测量警告 46 条；旧会话已有 187 / 31 条。当前仓库未检索到 TabletUI、Group_Discount、BargainPanelLeft 等相关实现。发生于恢复时的议价 UI 刷新，但不能仅凭日志断言具体所有者或与回溯完全无关；不是本轮首次出现，不在本次快照优化内修改全局字体或其他 Mod。
- 17:50:20 的 watchdog 在首次捕获 17:50:36 之前，记录场景加载后 8.6 s 恢复；17:52:47 ModLoader destroyed 在恢复结束且多个 Mod Disabled 之后。不能将二者计为此次局部写回卡顿或异常。
- Player.log 仍有其他 Mod 的 TryEquipItem 补丁参数不匹配与启动 trust-list 网络失败，旧会话也存在；不改动其他 Mod、加载器或信任配置。

此轮可记录为首轮实机日志检查通过，而非全面稳定性验收。下一轮应围绕首次全图写回、完整普查及捕获 walk 分别测量并独立改动；不删除预检/最终校验，不仅凭单对象差异样本重建集合，也不把本次日志当作扩大语义后端范围的充分证据。本次复核仅更新文档，不改变已部署 DLL，无需重复部署。
