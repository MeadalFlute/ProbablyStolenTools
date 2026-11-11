# 精确 TagState 混合字段载荷

日期：2026-11-11。版本保持 0.8.14，不推送。用户授权继续推进，需要操作时通知。

## 为什么选择这个边界

最后一次旧后端实机会话始于 16:16:36：

- 捕获总计 1056 / 587 / 585 / 520 ms，其中 walk 为 699 / 370 / 359 / 257 ms，verify 为 89 / 166 / 146 / 207 ms。
- 恢复总计 1415 / 1408 / 1795 ms，write 为 326 / 366 / 458 ms，reconcile 为 637 / 601 / 688 ms，census 为 140 / 148 / 237 ms。
- 首节点 16189 个精确 TagState，占 145701 / 562352 = 25.91% 的旧图字段槽。完整字典相关槽约 62.33%，但不代表可以删除。
- 三次恢复均 diff=0、releaseOk=True，只读标签对照匹配。

本轮只压缩精确标签字段，避免集合存储重建的 identity/version/capacity/别名风险。不触碰原生保存，不修改视觉、几何和最后校验顺序。

## 实现

StateGraph 提供可选的 IStateBlockAccess/IStateBlock，ManagedStateAccess 的捕获工厂仅在新节点启用。新 SemanticTagBlock 对精确原生 TagState 使用一个强类型载荷，替代九个 Entry 和六次标量值令牌读/登记：

- 六个标量直接读取/写回，float/double 按位。
- 三个字符串同时保存原引用及发现用身份；null、空串、同文字不同对象保持区别。
- 三个字符串通过 FieldInfo 写真实字段，保留 GC 写屏障；不调用公开 valueString setter，避免 null 被变成空串。
- 字段数量、名称、类型、非 readonly 及主图描述严格匹配；描述在访问器内校验一次，不给每个标签重复反射校验。筛选根、所有 TagState 子类继续使用普通字段路径。
- 原图对象发现、retain、顺序、路径、对象/槽位/逻辑字节上限不变。标量仍按 8 字节令牌、引用按 IntPtr.Size 计预算。这是逻辑计数，不是实际内存。
- 字典、数组、Keys/Values、活动枚举器、比较器策略完全不变。独立可达 TagState 也可压缩，不要求必须来自字典。
- Restore 在所有普通标量与块标量完成后才写引用。不重放业务回调，不在部分写入异常后自动回退重试。
- Differences 对所有普通字段、所有块的全部九字段做完整扫描，即使消息已达到上限。
- VisitCapturedSlots 从块中展开捕获的九字段及引用，引用审计仍可完整计数；不读取 live 字段。Census 不构造块、不保存值令牌，保持原逐字段普查。
- 节点各自持有载荷；关闭捕获工厂不影响已有节点，不尝试重新解释已有数据。

默认配置 `Snapshot.SemanticTagBlocks=true`，可关闭后重启让新节点恢复普通路径；无需改存档。启动日志给开关值，捕获行给 `backend=hybrid-exact-tag tagBlocks=N`。结构不匹配时 `backend=legacy-tag-schema-mismatch`，在任何写入之前走普通路径。既有只读 Shadow 继续保留，仍是每上下文最多一次采样。

## 验证

- Mono Release：0 警告 / 0 错误。
- CheckpointTests：140 项通过，新增十项混合载荷回归。
- ReleaseCompatibility：55 挂点 / 73 字段 / HardFlags 为空。
- 实际安装版 TagState、ItemStateModifier、GameGridInventory、CombatAbilityEffect 夹具：主图与普通图对象/槽/字节/来源路径相同；精确标签与独立标签压缩，子类及筛选根不压缩。
- 九字段逐个修改及独立旧图校验；null 字符串、原引用、负零、NaN 位模式；共享字典、entries/视图/活动枚举器别名、集合扩容/替换、子类额外字段、混合后端过去/未来节点反复恢复均通过。
- 工厂关闭后旧节点仍可用；Census 不创建块；限制提前失败、完整扫描、捕获/读取/写入异常传播、释放后拒绝使用通过。
- 额外尝试旧 IL2CPP/Demo 构建：失败（23 错误、2 警告），报错在本轮未修改的 LiquidComposition、InspectionLocale、LogWindow、Panel、Rewind、DoubleClickInspectionPatch、SemanticTagOwnershipAudit 等旧兼容路径，涉及原生 API/集合类型差异及 ManagedObjects。没有为本轮优化扩散修复已弃用后端，不能声称该配置编译通过；正式 Mono 重新构建/验证后部署。

可复现隔离基准：

```powershell
dotnet run -c Release --project tests/ReleaseCompatibility/ReleaseCompatibility.csproj -- `
  "<GAME_DIR>" `
  src/ProbablyStolenTools/bin/Release/Mono/ProbablyStolenTools.dll release/manifest.xml --benchmark-tags
```

仅使用未初始化的原生业务对象，1024 个字典、16384 个标签（含 16 个子类）、32768 个条目；不启动 Unity、不调用存档。每后端先预热一轮，随后交替五轮，逐次强制 GC；捕获/校验/恢复及捕获线程分配量各取中位数。每轮恢复前修改标签值，恢复后完整校验。两后端均 53253 对象 / 349232 槽 / 2793856 逻辑字节。

| .NET 6 隔离标签夹具 | 普通路径 | 混合路径 | 变化 |
| --- | ---: | ---: | ---: |
| 捕获 | 52.05 ms | 38.09 ms | -26.8% |
| 校验 | 13.58 ms | 8.72 ms | -35.8% |
| 写回 | 58.64 ms | 38.12 ms | -35.0% |
| 捕获分配量 | 74896904 B | 48818856 B | -34.8% |

这不是 Unity Mono 测量，也不含视觉恢复、真实业务图、Shadow 和普查。夹具精确标签槽比例高于真实图，不能把此表当整局提速承诺。实际收益必须等下一次游玩的 CAPTURE/RESTORE 分阶段日志。

## 实机门槛

本轮首次真实写入强类型载荷，先部署到这里，等待以下操作后再扩大覆盖：

1. 启动游戏，开店等待首个顾客节点；创建手动节点。
2. 改变液体量/配方或物品状态，移动并旋转物品，成交出售至少一件，再恢复操作前节点；查看液体、数量、金钱、贴图和碰撞是否一致。
3. 再等两三个顾客建节点，通过面板跨节点向过去/未来反复切换，至少回到首个采样节点一次。
4. 异常时停止继续试验，保留当前日志和具体节点/操作，不以拖拽修正后的视觉状态代替正确恢复。

日志应有非零 tagBlocks、完整 CAPTURE/RESTORE 阶段，采样节点应有 SemanticTags RESTORE MATCH。未进行 Unity 活体验收前，不继续字典裁剪/重建、子类载荷或跨帧捕获。

## 部署

标准脚本部署完成：

- 备份：`out/full-release-deploy-20261111-163148`。
- DLL SHA256：`A644736A0440F4568FFC054CB02DB346E681CE9FD31A479ACA27BCF7AE4BD80B`，构建与部署一致。
- 78 个已有存档目录文件、游戏程序集和其他 Mod 哈希不变；未导入设置。
- 未检测到游戏进程，无关闭进程；未启动游戏。
- 部署时尚无混合写入的 Unity 实机日志；之后的首轮结果见下节。

## 首轮实机日志验收

用户报告完成测试。会话始于 2026-11-11 16:33:04.903，游戏日志为 `GameLog_2026-11-11_16-33-14.txt`，结束于约 16:36:10。读取 PSTools.log 的最后一个 Session，避免与同文件旧会话混算；同时核对 PSTools_history.log、Player.log 和游戏日志。

### 正确性证据

- 五次捕获均成功，全部 `backend=hybrid-exact-tag`，tagBlocks 分别为 16190 / 16197 / 18169 / 17763 / 17763。
- 节点为囤水者、护士、农夫、酿酒师及手动节点；恢复顺序为 id=1、2、4、5，四次均 `diff=0 releaseOk=True`。本次日志没有同节点反复恢复，不能记成已完成长时间、多轮往返压力测试。
- 首节点独立对照 `CAPTURE MATCH`、`RESTORE MATCH diff=0`；OWNERSHIP 正常展开 145710 个标签逻辑字段槽，unknownMapSlots=0、standaloneStates=0。说明块载荷没有让只读审计漏掉九字段。
- 历史日志可见真实买卖、现金变化与跨顾客恢复后的现金/队列变化，不是仅空操作恢复。
- 本会话没有 CAPTURE/RESTORE FAILED、标签对照失败及 Geom 的 MISSING/NOSPRITE/HIDDEN/MISPLACED/DESTROYED 报告；错误日志最后修改仍是 15:32:05。
- 这是主图与独立标签对照的首轮实机通过，不是对所有液体配方、视觉状态、第三方 Mod 内部状态和长时间稳定性的全面保证。日志不能替代玩家逐项观察贴图/碰撞，也没有足够细的液体操作日志证明每项变化都已覆盖。

### 与前一会话比较

参照 16:16:36 的普通后端会话，对应客户与场景相近，但物品、条目及容量有少量差异；机器状态与 GC 不受控，不是严格同状态 A/B。常规捕获仅比较 id=2/3/4，排除冷启动首节点及新增加的手动节点；恢复仅比较双方都有的 id=1/2/4。

| 同类小样本中位数 | 普通路径 | 混合路径 | 下降 |
| --- | ---: | ---: | ---: |
| 常规捕获总计（各 3 次） | 585 ms | 443 ms | 24.3% |
| 捕获 walk | 359 ms | 313 ms | 12.8% |
| 捕获 verify | 166 ms | 77 ms | 53.6% |
| 恢复总计（各 3 次） | 1415 ms | 1251 ms | 11.6% |
| 恢复 write | 366 ms | 305 ms | 16.7% |
| 恢复 reconcile | 637 ms | 488 ms | 23.4% |
| 恢复 finalVerify | 96 ms | 66 ms | 31.3% |

原始捕获总计：旧 1056 / 587 / 585 / 520 ms，新 1018 / 421 / 475 / 443 ms，手动 453 ms。新恢复总计 1253 / 1193 / 1251 / 1232 ms。

首节点改善很小（1056 -> 1018 ms）：walk 699 -> 707 ms，Shadow 158 -> 143 ms，verify 89 -> 71 ms；不能宣称首次建节点卡顿已解决。单次酿酒师 walk 反而从 257 增至 331 ms，也说明并非所有阶段、每次采样都同比提速。新常规 verify 稳定在 76–77 ms，是这批样本里最一致的改善。

剩余耗时仍集中于捕获 walk 和恢复 reconcile。reconcile 包含二次业务图恢复及视觉/几何处理，不等于纯 UI 开销；不能为提速直接跳过二次恢复或完整验证。后续优先细分这些阶段，再决定下一轮小改动，不因此次 MATCH 放开字典重建、子类载荷或跨帧捕获。

### 其他告警

- Watchdog 的 10.4 秒加载停顿于 16:33:21 恢复，早于首个捕获 16:33:48；本次捕获/恢复期间没有新的 watchdog 报告。
- 第三方闭包仍按既有策略仅保留身份；Pen 的 TryEquipItem 挂点参数告警及信任列表网络失败不属于本轮快照载荷变更。
- 新旧游戏日志均有 109 条 NotoSans-Medium SDF 缺字警告，另有 inactive-parent 测量警告（旧 18、新 19）。这是值得单独排查的文字渲染证据，不能直接等同之前没有细节的玩家“语言问题”，也不能归因于本轮标签压缩。
- 16:36:10 在多个 Mod 的 disabled 消息之后出现 ModLoader 对象销毁错误；记录为退出阶段告警，不是快照差异或写回异常，当前不修改加载器。

本次只补验收记录，不改 DLL、不重新部署、不启动游戏或修改存档。
