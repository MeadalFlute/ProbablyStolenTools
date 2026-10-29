# 顾客快照首版实现状态

> 本文件以下内容为 0.2.1 历史排错记录。
> 当前 0.4.0 的历史列表、设置、中文名称、开业前及闭店节点以
> `0.4.0-settings-and-phase-nodes.md` 和 `AGENT.md` 顶部“当前事实”为准。
> 0.4.0 离线测试 20 项通过，新增功能仍未游戏内验收。

## 结论

首版源码和离线测试已经完成，用户确认 F6 能恢复大部分状态。
当前 0.2.1 的 lowerInventor 日志已有四次折扣后 F6 成功、diff=0，
前三次恢复后均再次触发议价；其他议价类型与完整业务流程仍未验收。
不能把编译通过、Harmony 成功加载或离线测试通过等同于功能可用。
DSH 集成不可用，由 Codex 直接接续开发和审查。

## 当前行为

- OnArrived 标记新顾客，开场对话及跟踪中的延迟回调结束后尝试捕获一次。
- 每个顾客一份基线，同顾客可重复 F6，下个顾客、日期或场景变化使旧基线失效。
- 只接受普通买、卖、买卖、交换、鉴定服务顾客；特殊顾客不支持。
- F6 只恢复内存，不复制存档、不调用 SaveGame、不重建顾客。
- F7 保留原开业前文件回退；旧面板检查点行仍是文件恢复入口。
- 拖拽、电话、扫描、交火、逮捕和顾客过渡期间拒绝 F6。

## 实现

- StateGraph.cs：按原对象身份捕获真实字段槽，保留循环及共享引用；值先于引用恢复。
- NativeStateAccess.cs：读取 IL2CPP 实例字段、数组及含引用结构体；强 GC handle 保活，
  引用通过写屏障写回，不复制对象头或可执行指针。
- 业务根：PlayerStore、StoreStation、EmporiumEntry、选定对话/交易/鉴定/电话字段、
  worldTags 及 RNG.random。Unity 原生对象不做完整内存复制。
- 视图：记录物品和窗口渲染根的父节点、本地位置/旋转/缩放及 active 状态。
- 快照内物品被消费时做业务删除和隐藏，暂缓销毁原渲染根；顾客结束提交删除。
- MetaHelper.WaitAndDo 回调增加 epoch 校验；恢复作废旧时间线回调。
- 不调用 InterruptDialog，避免执行被撤销对话的 endAction。
- Unity Random 使用解析后的 icall；首次使用运行往返探针。

## 实测与本次修复

游戏已加载 0.2.0，日志确认 Harmony patches applied。
读取原存档第 24 天，普通顾客 lowerInventor 到来后，捕获报错：

```text
Uncovered mutable type: .KeyCollection
```

原因是 IL2CPP 字典嵌套类自身的 namespace 为空，原类型白名单没有看到外层命名空间。
当前源码按 declaring-type 链构造完整名称，例如
System.Collections.Generic.Dictionary`2+KeyCollection；同时改善诊断字段名称。

本次另外调整：

- 预检阶段只收集基线外新物品，不再直接销毁渲染根。
- 恢复业务图和刷新后 diff=0，才销毁新时间线的物品视图、隐藏新增窗口。
- 部分写入失败时，不销毁可能已被重新引用的基线视图；要求重载游戏。
- F6 拒绝开场对话尚未启动完成及非营业状态。

这些修复已编译并重新部署，启动日志确认 Harmony patches applied。
随后用户反馈 `A baseline item has no live renderer`。第二次修复后重新部署并启动，
用户已在原存档第 24 天使 lowerInventor 到场；面板和日志均确认首次捕获成功。

重新部署前备份：
`<REPO>\out\customer-checkpoint-redeploy-20261022-010716`

上一次部署 DLL SHA256：
`62E9C3659E42E8724D1210B4EF6EB6FFA9B3A79156DBA119537C35EF4FB6404A`

### 无渲染器物品的修复与实测

IsDestroyed 的实际实现只判断 handler 的 Unity Object 是否为 null。
完整业务图会触达一些无渲染器的回调数据，因此不能对每个 GameItemElement 一律要求活渲染器。

现在区分全部业务物品和可恢复画面的物品，保留两者的业务图；仅对活渲染器执行
视图捕获、Validate、延迟销毁。无渲染器对象若仍是 parentInventory.childItems 的成员，
则拒绝捕获，不掩盖真实库存损坏。StateGraph 新增首次可达引用路径用于诊断。

实测唯一无渲染器对象：

```text
item=trashcan uniqueId=-102 inventoryMember=False
root -> EmporiumEntry.<trashcanInvElement>k__BackingField
-> GameInventoryFunc.<onSlotAddItemEndFunc>k__BackingField
-> System.Delegate.m_target -> MiscItemDirectory+<>c__DisplayClass46_0.item
```

成功捕获：

```text
CAPTURE client=lowerInventor objects=16969 slots=97872 bytes=537621
items=168 renderedItems=167 windows=37
```

Unity Random 首次往返探针位于视图检查之前，已通过且捕获成功。
F6 后的随机行为仍未验证。

用户随后执行第一次 F6，日志记录：

```text
[01:14:05] [Customer] RESTORE OK client=lowerInventor diff=0
```

这证明恢复流程已执行且被捕获字段一致，不等于所有游戏状态或画面均已正确恢复。

第二次修复备份：
`<REPO>\out\customer-renderer-fix-20261022-011158`

0.2.0 物品视图修复版 DLL SHA256：
`70BAF72CF98F28730D589AEF4C05803A1DF40989CD7AC60CD37B5F73C481C5BD`

### 0.2.1 议价资格与按钮同步

用户确认多数状态可恢复，但议价恢复初始价格后不能再次议价。
业务图已包含 BargainUIManager 的 trackedClient、各类一次性资格标记及倍率，
但 Unity Button.interactable 不在图中；原生资格检查也依赖 currentDialog，
并非只看 isConversationActiveFlag。

本轮改动：

- 捕获稳定条件增加 currentDialog == null，避免捕获会保留议价锁的对话状态。
- 恢复时用原生 BargainUIManager.Update() 替代单独的分组/声望文本刷新，
  同步游戏自己的资格判断及按钮交互状态，不直接把按钮或资格强制设为可用。
- CAPTURE、BEFORE RESTORE、AFTER RESTORE、OpenUI 增加低频 BARGAIN 诊断，
  记录一次性标记、对话指针、移动/交易锁及按钮状态，不给每帧 Update 挂日志。

当前启动日志确认加载 0.2.1、Harmony patches applied。本轮捕获为
17081 objects、98627 slots、541081 bytes，物品/渲染物品/窗口仍为 168/167/37。
当前日志记录折扣 75、50、25、10 后各一次 F6，四次均 RESTORE OK、diff=0；
每次 discountOffered 恢复 false、discountPercent 恢复 0，折扣按钮从 0/0/4 回到 4/4/4
（interactable/IsInteractable/按钮总数）。前三次恢复后均再次记录 OfferDiscount，
证明折扣路径不再停在一次性资格锁中。

第四次恢复前 currentDialog 非空而 conversation=0，恢复后 currentDialog=0，
说明两个对话字段不能等价处理。尚无足够旧版诊断证据将原故障归结为单一按钮问题。
未额外争抢当前游戏输入，画面/价格与其他议价类型仍需用户确认。

议价修复前备份：
`<REPO>\out\customer-bargain-fix-20261022-012456`

当前已部署 DLL SHA256：
`33FDDC8BC50043FC862FBC8EE4B09928188E448415079AD6976829D7BF9EB2CA`

## 离线验证

插件 Release 构建：0 警告、0 错误。
CheckpointTests：9 项通过，覆盖循环/共享引用、集合 backing array 替换、
删除及新增对象、null 和重复恢复、限额与读取失败释放资源、
预检失败零写入、顾客/日期/场景/store 边界、循环/共享引用来源路径及显式根来源优先级。

上述测试使用 FakeMemory，不证明原生字段布局、类型覆盖或游戏视图恢复正确。

测试前备份：
`<REPO>\out\customer-checkpoint-pretest-20261022-005727`

最近只读核验中，当前文件和该备份一致：

```text
save_0.es3
9A65D156FD3F123038A611A19BB01CED61D25E050D8F3E71B4978D03E395D766

saves_index.es3
8E8F7A58DEAF28D6306E9652822AE66A3C2770A703185A16EAC551ACF3F17B98
```

## 未完成与风险

- 用户确认大部分恢复有效，0.2.1 已有同客多次折扣/F6/再次议价的日志证据；
  仍需画面确认，不将其视为全业务验收。
- 尚未专项验收其他议价类型、不按 F6 时每客一次限制、交易、消耗品、设备及新顾客替换。
- KeyCollection 修复之后仍可能暴露其他真实类型覆盖缺口，必须按日志和元数据处理。
- Unity 原生协程、DOTween、绝对 Time.time 字段等不在完整快照中。
- Validate/RefreshUI 可能改变被捕获字段，需要游戏内 diff 和画面双重确认。
- 所有延迟回调是否能正常移除待处理标记，尚未完整验证。
- 非托管恢复不是原子事务；发生部分写入错误后没有可靠的自动回滚。
- 失败分支保留视图可能产生临时泄漏，应重载而非继续游戏。
- Unity Random 往返探针已通过，仍需验证实际 F6 随机序列和游戏行为。
