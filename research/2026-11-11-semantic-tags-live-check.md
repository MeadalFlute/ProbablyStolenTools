# 首次语义标签实机检查与子类兼容修正

日期：2026-11-11，版本保持 0.8.14。本轮只修正只读实验，不启用语义写入，不修改存档。

## 证据与结论

检查本机存档目录的 PSTools.log、PSTools_errors.log、Player.log 和 GameLog_2026-11-11_15-20-50.txt。主日志跨多次运行追加，统计仅取本次 15:20:43 会话，营业日 75，不能混入此前 14 时的日志。

首个样本在 15:32:05 失败：

```text
[QuickSL/SemanticTags] CAPTURE FAILED id=1 readOnly=true legacyAuthoritative=true elapsedMs=5:
System.InvalidOperationException: Semantic tags do not cover TagSystem subclasses.
```

语义阶段摘要为 10ms（含捕获失败及日志等），后续 8 次捕获 semanticShadow=0；没有 CAPTURE MATCH 或 RESTORE MATCH。按每个上下文仅一次尝试的规则，这是预期失败隔离，不是语义后端成功或性能改善。

本次 9 次旧后端捕获均成功；3 次恢复均 diff=0、releaseOk=True。恢复目标分别为 id=2、6、9，本来也没有回到本次尝试采样的 id=1。日志不能代替肉眼贴图验收。

## 耗时

单位 ms：

| 阶段 | 次数 | 最小 | 中位数 | 最大 |
| --- | ---: | ---: | ---: | ---: |
| 全部捕获 total | 9 | 479 | 526 | 999 |
| 排除首次后的捕获 total | 8 | 479 | 523.5 | 550 |
| 排除首次后的 walk | 8 | 323 | 367.5 | 400 |
| 排除首次后的 items | 8 | 31 | 37 | 53 |
| 排除首次后的 verify | 8 | 87 | 104.5 | 156 |
| 恢复 total | 3 | 1548 | 1619 | 1838 |
| 恢复 census | 3 | 155 | 178 | 320 |
| 恢复 preflight | 3 | 137 | 168 | 182 |
| 恢复 write | 3 | 437 | 497 | 499 |
| 恢复 refresh | 3 | 87 | 92 | 115 |
| 恢复 reconcile | 3 | 578 | 592 | 635 |
| 恢复 finalVerify | 3 | 96 | 107 | 108 |

捕获总计 5171ms，walk 3677ms（71.11%）、verify 1044ms（20.19%）。图规模约 10.1–10.8 万对象、56.3–63.3 万槽。恢复中 write+reconcile 合计 3238/5005ms（64.70%）。

这些是本次分布，不是与旧会话的控制变量 A/B。语义样本未成功，更不能据此评价语义表示的实际成本或未来提速。

唯一 watchdog 卡顿发生在 15:20:52–15:20:57 初始加载，报告恢复后累计 10.2s，早于 15:32 首次捕获，不与快照重合。15:38:05 另有 ModLoader 对象销毁日志，位于会话结束阶段，不归因为标签对照；本轮不修改加载器。

## 根因

首版测试仅用了精确 TagSystem/TagState，而实机旧图包含原版子类。反编译安装程序集确认：

- CombatAbilityEffect、StationFeature 直接继承 TagSystem。
- GameInventoryFunc 继承 TagSystem，具体库存进一步继承该类。
- ItemStateModifier 继承 TagState，另有 operation、condition、priority 字段。

旧异常未打印具体实例类型，因此无法从该日志确定首先触发的是哪个类型；但原版库存已足以复现此误拒。不是已证实的第三方 Mod 类型冲突。

## 修正

保持基类字段白名单，允许继承链全部属于原版程序集的 TagSystem/TagState 子类，直接以基类静态类型读取非虚属性/字段。只对 dict 与九个标签字段进行只读投影，不调用子类业务方法。

子类额外字段仍由原完整 StateGraph 捕获/恢复/比较；语义 MATCH 仅表示标签基类投影匹配，不表示完整子类或整个游戏状态被语义后端覆盖。日志新增：

```text
scope=tag-base-fields extraFields=legacy-only
derivedSystems=... derivedStates=...
systemTypes=[...] stateTypes=[...]
```

每类最多列出六种类型，其他种类聚合计数。第三方继承链仍拒绝，错误现在包含 AssemblyQualifiedName 与旧图来源路径。失败不会损坏旧节点，不增加重试频率，不改变视觉顺序。

## 验证与复测

- Mono Release：0 警告 / 0 错误；115 项 CheckpointTests 全过。
- 55 挂点、73 字段通过；原生扫描 HardFlags 为空，声明能力未变。
- 安装版 GameGridInventory/CombatAbilityEffect/ItemStateModifier 真实类型夹具：基类九字段逐项变更可检出，字典共享/扩容/重复恢复通过。
- 故意只改 ItemStateModifier.priority 和 CombatAbilityEffect.duration，语义基类投影不报差异而旧图报差异，旧图恢复后原值正确，证明边界明确。
- 动态测试程序集构造第三方 TagSystem/TagState 子类，仍被拒绝并输出类型名，已有两份快照保持零差异。动态类型仅在离线测试工具生成，不进入 Mod DLL。

下次进入游戏后，在最早的采样节点建立后移动或交易，再回溯该节点（通常首个顾客节点）。需要同时看到 CAPTURE MATCH 与相同 id 的 RESTORE MATCH。无需完成整天；若仍失败，保留新的具体类型及来源。原后端仍权威，不启用语义写入。

## 部署

已通过标准脚本部署至 `<GAME_DIR>\Mods\ProbablyStolenTools`。

- 备份：`out/full-release-deploy-20261111-154616`。
- DLL SHA256：`F2CB052FF9F9D9CF0D5FAE75F1446CF2E246858CD0719436CC8F5C09DFA1774B`，构建与部署一致。
- 78 个已有存档目录文件、游戏程序集和其他 Mod 哈希核验不变。
- SettingsImported=False；未启动游戏。本修正版仍待实机复测。
