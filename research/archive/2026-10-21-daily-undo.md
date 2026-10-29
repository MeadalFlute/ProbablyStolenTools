# Probably Stolen Demo：每日流程与逐步撤销可行性调研

日期：2026-10-21，Asia/Hong_Kong。

范围：静态、只读调研。未启动游戏，未注入新代码，未编译或部署插件，未改动游戏文件、配置、现用存档和备份。本文是调研产物，不是功能实现或运行验证结论。

## 1. 结论

**维护“历史记录”，实现同一天内的业务操作逐步撤销，有技术可行性，但现有检查点实现不能直接承担这个目标。**

建议路线是：**业务操作日志 + 有明确覆盖范围的内存状态快照 + 撤销时的回调隔离与视图恢复**。先支持普通顾客的移动物品、鉴定、议价、交易，再扩展到顾客切换。不要继续用“复制原生存档 + 按顾客 ID 再生成”冒充精确撤销。

以下三件事需要区分：

1. **记录发生过什么**：相对容易，可以在业务入口和实际状态变化处采集事件。
2. **恢复受支持操作之前的完整业务状态**：可以设计，但必须补足原生存档故意不保存的运行时对象、私有字段和跨对象引用。
3. **任意时刻、任意玩法、跨场景的全状态倒流**：本轮没有证据能保证做到，不能据此承诺。动画播放帧、音频、Steam 已发送成就和已发送分析事件也不应承诺一并撤回。

本文确认的是静态调用与字段归属，不是运行时每种分支都已执行验证。哪些功能在 Demo 的具体进度中可触达，仍需事件追踪确认。

## 2. 证据来源与可信度

- `dump/dump.cs`：字段偏移、序列化属性、方法 RVA。
- `cpp2il_isil/IsilDump/Assembly-CSharp/*.txt`：原生反汇编、调用目标和 ISIL。
- `decompiled/*.cs`：类型与字段结构。大量方法体是 `throw null`，不能当作实际逻辑。
- `src/ProbablyStolenTools/*.cs`：当前插件行为，而不是游戏本体行为。
- 直接对安装目录的 `GameAssembly.dll` 做 `objdump`：抽查了开门调用链、`SetupWaitingClient` 的分支、`StoreClientManager.OnNewDay`、`Dialogue.ExecuteEndAction`，与既有 ISIL 对应。
- 只读扫描安装目录的 `UnityPlayer.dll`：核对随机状态入口字符串；检查 `GameAssembly.dll` 导出表。

**重要修正**：Cpp2IL 没有恢复出可读 C#，不等于所有 Cpp2IL 产物无用。现有 ISIL 已可用于还原实际调用链。但是 ISIL 中的 `Call 0`、寄存器间接调用、`NotImplemented` 等不能按表面文字解释，需要回看原生指令、字段偏移和委托结构。本文没有把调用列表当成无条件执行列表。

本轮安装文件指纹：

| 文件 | SHA-256 |
| --- | --- |
| `GameAssembly.dll` | `A2CAA0CECBF197CCD6B12DDEC7BB9E6FA0BC2CD2A5C52ABEF627AA919E2B9BFD` |
| `global-metadata.dat` | `8067B2F4EE069FDBA6FB2E051ACF7C465C9A275CDDC8D8CA15B313D9FC54B92C` |

地址只对应本次安装版本；运行时模块基址可能变化，应使用 RVA 或元数据定位，不硬编码绝对 VA。

## 3. 一天实际如何推进

### 3.1 次日初始化不等于点击“开始一天”

正常的跨日路径发生在前一天结算按钮触发的黑屏序列内：

```text
EndOfDayUIManager.OnEndDayButtonClick
  -> 发送睡眠分析事件、隐藏相关界面
  -> StoreUIManager.StartBlackscreenSequence
     -> 黑屏业务回调 b__0
        -> ForceImmediateClose
        -> 设置 MORNING，重置部分门锁
        -> CloseAllUI
        -> PlayerStore.EndNight
        -> StoreEventManager.OnDayEnd
        -> 清理部分界面/记录
        -> PlayerStore.BeginDay
        -> TutorialUIManager.OnDayBegin
        -> 条件分支：游戏结束，或 SaveGame
     -> 黑屏结束回调 b__1
        -> 未游戏结束时打开 StartOfDayUIManager
```

`BeginDay()` 再调用：

```text
StoreStation.StartDay
  -> dayCounter += 1
  -> StoreEventManager.OnDayStart
  -> 条件性更新长期数值
  -> 随机化当天两类水的纯度
  -> HandleFaction
  -> 更新/移除到期店铺请求
  -> 刷新商店 PC 状态
  -> 各 NetworkUpgrade.OnDayStart
dayUntilRent -= 1
StoreClientManager.OnNewDay
  -> daySinceWantedWasQueued += 1
  -> isTodayWantedQueued = false
SecData.OnNewDay
CommissaryData.OnNewDay
```

`StoreEventManager.OnDayStart` 内含未来事件处理、追踪器更新、新事件排队及断电相关分支。

**当天普通顾客队列不是在 `StoreClientManager.OnNewDay()` 中生成的。** 该方法在当前原生二进制中只有递增计数、清除布尔值两项操作。

晨报中的 `StartOfDayUIManager.OnStartDayButtonClicked()` 主要是隐藏面板和播放音效；该方法内没有 `BeginDay()` 或顾客生成调用。因此不能把这个按钮当作新一天所有状态变化之前的起点。

证据：[跨日回调](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/EndOfDayUIManager_NestedType___c__DisplayClass21_0.txt:214)、[BeginDay](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/PlayerStore.txt:8964)、[StartDay](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/StoreStation.txt:609)、[OnNewDay](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/StoreClientManager.txt:1293)、[晨报按钮](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/StartOfDayUIManager.txt:2127)。

### 3.2 真正开门：多步业务流程

晨间卷帘门点击经条件判断后进入 `StoreShutterButton.OpenShutter()`，当前 RVA 为 `0x6FA170`：

```text
PlayerStore.OnShutterOpened
  -> 按天数更新解锁标记
  -> CheckCrime
  -> SoldItem.HandleSoldItem
  -> 重置部分自动叫客/门锁状态
  -> StoreClientManager.GenerateClient
  -> HandleFutureClientQueue
  -> 移除空顾客
  -> LogQueuedClient
  -> RefreshCounterItem
PlayerStore.OpenShutter
  -> storeState = OPEN
  -> 条件性检查/提交 Steam 成就
ToggleShutterDoor
lockShutterButtonFlag += 1
OnShutterDoorOpenMorning
  -> 晨间物品、许可、教程/剧情相关分支
EmporiumEntry.ShowFrontArea
PlayerStore.OnLateShutterOpen
  -> 声望特权、传闻、晨间特殊来客等条件分支
启动延迟回调
  -> 解锁门按钮，处理相应界面
EmporiumEntry.Validate
```

这解释了为什么**单独调用 `PlayerStore.OpenShutter()` 并不等于正常开业**：它没有调用 `GenerateClient()`。

`OnShutterDoorOpenMorning()` 还可能根据进度把报纸、教程物品、剧情便条等送上柜台，不能重复调用而不恢复其副作用。

证据：[完整开门](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/StoreShutterButton.txt:801)、[开门生成队列](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/PlayerStore.txt:10141)、[仅切换 OPEN](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/PlayerStore.txt:8517)、[晨间物品](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/StoreShutterButton.txt:1496)。

### 3.3 顾客生成不是简单的 ID 队列

`StoreClientManager.GenerateClient()` 会按顺序检查多类来源，其中许多是条件分支：

1. 很早期 ModHook、展柜严重违禁/赃物检查、许可撤销来客。
2. 救济、展柜、化学反馈、跳过/常驻、事件、介绍、检查员等来源。
3. 早期 ModHook、供应商、内容解锁、剧情线、贷款返还等。
4. 远征、情报、发明家、仓储、次要顾客等。
5. 中期 ModHook、Aug 与普通顾客。
6. 后期 ModHook、特权、市场、服务、租金相关来客。
7. 去重/空值检查及最晚期 ModHook。

`AddClient()` 不只把对象放进列表，还会调用 `TrackClient()`。后者会更新顾客冷却追踪及永久访问计数。恢复时重新走 `AddClient()`，可能再次记账，不能默认它是纯容器插入。

顾客工厂会创建随机外观、对话图、行为委托等。例如 `CreateScavGeneral()` 选择随机拾荒者外观，创建对话并绑定结束动作。因此同一个 `identifier` 只表示一种生成入口，不能唯一标识已生成的那一个顾客及其完整状态。

`futurStoreClientIdQueue` / `futurStoreClientDayQueue` 是**跨日预约**：`HandleFutureClientQueue()` 检查等待天数，到期后通过 `StoreClientListDict.CreateStoreClient()` 创建对象，加入当天队列并移除预约项。它们不是当天 `clientStack` 的副本。

证据：[GenerateClient](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/StoreClientManager.txt:354)、[TrackClient](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/StoreClientManager.txt:9662)、[预约转入](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/PlayerStore.txt:11374)、[随机顾客工厂](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/StoreClientList.txt:18790)。

### 3.4 叫客、入场与对话不是同一个时刻

```text
StoreBell -> TryCallNextClient
  -> 检查队列、忙碌状态、逮捕/交火、当前顾客
  -> GetNextClient
     -> 取 clientStack[0]
     -> GetSprite + 设置显示精灵
     -> 从 clientStack 移除第 0 项
     -> 清除议价界面的当前物品
     -> CreateClientInstance(原 StoreClient 对象)
     -> 设置 currentClientInstance
  -> QueueClientArriveAnimation
     -> 动画到达后 StoreClientMono.OnArrived
        -> isClientArrived = true
        -> 释放部分交互锁
        -> StoreUIManager.OnNextClientArrived
           -> 顾客 OnArrival 委托
           -> OnCriminalVisit
           -> RefreshCounterItem
           -> 给相关物品附加顾客购买特征
           -> 延迟开始主对话
```

`CreateClientInstance()` 保存的是传入顾客引用，并创建一个网格容器等；不是按 ID 完整重建顾客，也没有复制整个顾客对象。

因此，“从队列取出”“已经走到柜台”“到场委托已执行”“主对话已开始”“对话结束并生成货物”可能是不同历史节点。对异步过程过早拍照，会得到尚有旧回调待执行的中间态。

证据：[叫客](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/PlayerStore.txt:10971)、[取队首](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/PlayerStore.txt:10646)、[实例创建](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/StoreClientInstance.txt:72)、[动画到场](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/StoreClientMono.txt:2745)、[到场业务](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/StoreUIManager.txt:9176)、[延迟主对话](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/StoreUIManager_NestedType___c__DisplayClass110_0.txt:14)。

### 3.5 营业中的操作会改变哪些状态

| 操作 | 已看到的实际变化/调用 | 撤销需要覆盖 |
| --- | --- | --- |
| 移动物品到柜台/库存 | 容器与父子关系、形状状态校验、柜台变化回调、事件/购买特征 | 物品引用、位置、嵌套容器、标签和特征；不能只把图标移回 |
| 揭露物品特征 | `ItemFeature.SetExposed`、刷新议价 UI | 私有的揭露/发现标记、价格相关派生状态 |
| 议价/压价/威胁 | 尝试次数标记、随机结果、物品议价特征、顾客预算、交易声望倍率；部分分支改声望并安排离开 | `BargainUIManager` 私有状态、顾客和物品状态、声望、随机状态、待执行对话 |
| 玩家买入 | `OnItemBought`、扣现金、设置所有权、记录买价、声望、顾客回调、移除临时特征、移动容器 | 现金之外的全部关联状态 |
| 玩家卖出 | 检查违禁/脏水/危害物品/弹药、现金、顾客预算与购买种类、顾客回调、声望、所有权、`SoldItem.TrackItem` | 犯罪与证据、已售追踪、预算、声望、物品和对话 |
| 批量/批发 | 循环交易、整批价格、交换容器、完成业务、接受交易对话 | 必须作为一个外层事务，不能每个内部调用单独生成撤销步 |
| 易货 | 完成业务、`BarterOffer.OnBarterAccepted`、前后柜台所有权转换与交换、特征清理 | 双方完整物品图、易货进度、顾客业务标记 |
| 鉴定报价 | 正确值与精度计算、分析事件、成就检查、耐心/对话分支、费用、业务完成 | 不能只还原报价输入框；外部已发送事件另计 |
| 选择/推进对话 | 分支 Action、结束 Action、下一对话、已执行标记、调度回调 | 对话图及捕获状态；一些剧情副作用藏在委托里 |

证据：[买入](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/PlayerStore.txt:25395)、[卖出](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/PlayerStore.txt:25873)、[批发](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/NegociationUIManager.txt:908)、[易货](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/PlayerStore.txt:29603)、[议价](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/BargainUIManager.txt:4676)、[鉴定](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/AppraisalServiceHelper.txt:3222)、[移动容器](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/GameGridInventory.txt:7152)。

### 3.6 没有点击，也可能改变业务状态

`EmporiumEntry.Update()` 使用 `oneSecondTimer` 累积时间，到阈值后遍历 `GetAllItems()`，调用物品的 `onOneSecondItemFunc` 委托。对应委托字段位于 `GameItemFunc + 0x180`，ISIL 中是读取 `[item+384]` 后的间接调用。

这意味着只在按钮点击前后拍照还不够。还要识别正在运行的机器、扫描和其他定时物品行为；至少要定义撤销是否连同这段时间内的自动变化一起还原。具体每种机器的计时语义本轮未穷尽。

对话和人物移动另有协程、DOTween、延迟 `MetaHelper.WaitAndDo` 回调。暂停鼠标输入不等于停止这些逻辑；单独依赖 `timeScale` 也尚未证明能隔离全部来源。

证据：[物品定时逻辑](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/EmporiumEntry.txt:9484)、[委托字段](/D:/Program/GameProject/ProbablyStolenMod/dump/dump.cs:63895)。

### 3.7 顾客离开、关店、睡觉

`DismissCurrentClient()` 可能先触发 Aug 相关处理和顾客 `OnDismiss`，然后清掉当前顾客引用、议价增益、非自有柜台物品及临时顾客特征，安排人物离场，并按参数/配置安排自动叫下一位。离场动画结束后还会释放锁；相关延迟回调可能继续 `TryCallNextClient()`。

关门的 `StoreShutterButton.EndAndClose()` 包含界面/物品清理、对话中断、`PlayerStore.EndDay()`、门动画及多个延迟收尾。`EndDay()` 自身设置 `CLOSED`、清空剩余队列、更新服务费用。

`EndNight()` 的影响范围显著更大，含服务收费、清洁/电力/保险、破坏/检查/安全提醒、经营行动、远征、健康、电话顾客、化学模拟、物品周期处理、冷藏标记与过期特征等，最后进入 `MORNING`。随后外层结算回调再更新事件并执行 `BeginDay()`、保存。

**建议第一版历史记录在“确认关店/睡觉”处设明确边界，不默默允许未验证的跨夜撤销。** 若以后支持跨日，必须把夜间模拟和所有参与的管理器一并纳入。

证据：[送客](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/PlayerStore.txt:13268)、[自动下一位](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/StoreClientMono_NestedType___c.txt:73)、[关店](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/StoreShutterButton.txt:2690)、[EndDay](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/PlayerStore.txt:14174)、[EndNight](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/PlayerStore.txt:14241)。

## 4. 为什么现有回退无法精确撤销

### 4.1 原生存档有意遗漏运行时状态

`dump.cs` 中明确标注 `[ES3NonSerializable]` 的包括：

- `PlayerStore.currentClientInstance`、`currentNegociatedItem`、`lastVisitedClient`、`nightlyAction`。
- `isClientArrived`、逮捕/交火状态、自动下一位标记、多类交互锁。
- 当日部分举报/通缉来客状态。
- `StoreClientManager.clientStack`、`chemicalFeedbackStack`。

相反，现金、声望、案件数据、预约队列、各库存 JSON 等会进入原生存档。于是它不是“完全不支持保存物品交易进度”，而是**可持久化经济状态与未持久化的营业会话不完整地混在一起**。

证据：[PlayerStore 序列化边界](/D:/Program/GameProject/ProbablyStolenMod/dump/dump.cs:35091)、[顾客管理器序列化边界](/D:/Program/GameProject/ProbablyStolenMod/dump/dump.cs:42817)。

### 4.2 `SetupWaitingClient(id)` 用错了接口

当前 RVA `0x5C2A30`。先让人物立即到位，然后只识别 `"mob"`、`"sec"`、`"hitman"`、`"cartel"` 四类晨间特殊来客，设置相应精灵/随从，生成对应特殊顾客实例并启动晨间对话。

**它没有通用的 `clientStack.Add` 逻辑，不是“按任意顾客 ID 补回等待队列”。** 普通 ID 走不到那些初始化分支，却已经执行了前置人物操作；这提供了之前无贴图/状态异常的一条代码级解释，但没有在本轮运行复现具体症状。

证据：[特殊来客分支](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/PlayerStore.txt:9715)。

### 4.3 `LoadGame()` 不是原生空函数

它有完整的 ES3 载入、库存解码和界面初始化流程。开头还会从 `NewGameData.Instance` 取得槽位，回调使用 ES3 的加载入口还原 `playerStore` 和 `storeStation`。

所以旧文档中的“实测调用没生效”是特定调用环境的观察，不能提升成“该方法本身没有实现”。确切失败原因仍需核对运行槽位、加载前提、ES3 调用及之后发生的回调，不在本轮静态证据中强行归因。

同时，这个加载流程是场景初始化路径，不是已验证可重入、无副作用的内存恢复接口。

证据：[LoadGame 开头](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/PlayerStore.txt:6250)、[加载回调](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/PlayerStore.txt:39257)。

### 4.4 当前插件保存的是文件检查点，不是历史状态

`Rewind.Point` 主要记录存档路径、现金、槽位、店铺状态及顾客 ID 列表；没有完整顾客/对话/议价/待执行任务快照。

`Capture()` 会调用 `SaveGame()`，不是被动观察。`SaveGame()` 还更新游戏时间、槽位、存档索引和预览缓存。状态轮询每隔几帧执行，不能保证捕获每次业务操作前的状态，也不能区分一次点击内的多层副作用。

此外，当前源码已经默认关闭 `QueueRebuild`、`CaptureMidDay` 和 OPEN 恢复，与 `AGENT.md` 的部分近期描述不一致。本报告不把旧的“已修复”视作现行可用功能。

“旧 Harmony 钩子没有打印”也不能证明游戏不调用这些方法：当前原生开门链明确调用 `PlayerStore.OpenShutter()`，叫客链明确调用 `GetNextClient()`。钩子是否正确安装、命中实际路径，需要独立验证。

证据：[现有快照结构](/D:/Program/GameProject/ProbablyStolenMod/src/ProbablyStolenTools/Rewind.cs:17)、[当前配置默认值](/D:/Program/GameProject/ProbablyStolenMod/src/ProbablyStolenTools/Plugin.cs:74)。

## 5. 随机状态：不是已被证明无解

本体中至少有两套需要区分的来源：

- `RNG.Roll(int/float)`、`GetRandomInt`、`GetRandomElement` 等使用 Unity 随机数；整数方法的原生目标 `0x181CF4B70` 对应 `UnityEngine.Random.Range(int,int)`。
- `RNG.GetRandomDouble()` 使用静态 `System.Random random`。本体 `System.Random` 有 `_inext`、`_inextp`、`_seedArray` 等字段。若要快照，需要保留原数组内容而不是只存引用；还需检查其他实例和调用方。

议价会消费随机数；人物闲置表现的 `PlayIdleForCurrentClient()` 也可能调用 `RNG.Roll`。因此重播时只记录业务输入，不控制装饰逻辑的随机消费顺序，不足以保证同一结果。

本体托管元数据中确实没有 `UnityEngine.Random.state`。但本轮直接在 `UnityPlayer.dll` 找到：

```text
UnityEngine.Random::get_state_Injected    文件偏移 23914568
UnityEngine.Random::set_state_Injected    文件偏移 23914608
```

`GameAssembly.dll` 导出表存在 `il2cpp_resolve_icall`。这使“解析被裁掉托管包装的内部调用”成为值得测试的候选路线。

**仅有字符串和解析函数不证明目标已注册可调用，也不证明 ABI/状态布局正确。** 本轮没有加载 DLL 或调用这些入口。下一阶段必须在隔离测试中验证非空函数指针、正确布局、保存/恢复后的随机序列一致性，以及实际所有业务随机源的覆盖。

若该路线失败，还可以评估业务层随机结果记录/重放，但不能仅用 `InitState(seed)` 冒充任意时刻的完整随机状态恢复。

证据：[RNG 实际调用](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/RNG.txt:3)、[System.Random 字段](/D:/Program/GameProject/ProbablyStolenMod/dump/dump.cs:91303)、[议价随机数](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/BargainUIManager.txt:5237)、[闲置随机数](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/StoreClientMono.txt:1815)。

## 6. 建议的历史记录设计

### 6.1 “一步”按业务事务定义

建议把以下操作看作历史步：一次物品移动/拆分/合并，一次鉴定或工具使用，一次议价，一次接受交易（含整批），一次有效对话选择，一次送客/叫客。

普通窗口开关、悬停、打字机逐字显示不单独占业务历史。推进对话若会执行 Action，则不能当成纯显示操作。

对涉及延迟动作的一步，记录“操作前快照”，把属于该动作的同步与延迟副作用归入同一事务，在确认到达受支持稳定状态后提交。不能只在 Postfix 随便等待三秒；时长不是稳定性的证明。可能持续运行的生产/计时系统必须另定自动事件或事务内时间策略。

### 6.2 快照的最低覆盖范围

| 状态域 | 最低内容 |
| --- | --- |
| 玩家与世界 | `PlayerStore` 的经济/剧情/案件/声望/服务/预约等数据，`StoreStation`、事件和经营管理器相关状态 |
| 顾客 | 队列顺序、当前/上一位实例、每个已生成对象的完整可变字段、预算、耐心、业务进度、外观、委托引用及可变捕获对象 |
| 物品 | 全部相关库存与柜台的对象图、数量、所有权、价格、标签、嵌套关系、位置/旋转/形状、特征及私有揭露标记 |
| 对话与议价 | 当前对话节点、分支/已执行标记、捕获状态、私有议价标记与倍率、报价和当前物品引用 |
| 时间与异步 | 定时器、一步中产生的业务回调、相关协程/动画调度、交互锁与到场状态 |
| 随机数 | 所有相关随机源的实际状态，或经过验证的等价结果重放机制 |

对象必须用一致的快照内部身份关联，不能只按顾客蓝图 ID 或物品类型 ID 对接。`uniqueId` 等现有标识可作为辅助，但需核查唯一性和重建后含义。

同一进程内优先评估**保留对象身份，恢复其可变字段与集合**，避免重新执行顾客工厂和重绑到不同对象的委托。仅保存旧对象引用不是快照：后续操作会继续修改同一对象。必须对可变字段、集合及闭包进行覆盖审核，并验证 IL2CPP 原生对象生命周期保活。

不建议把整个 Unity 对象/进程内存做 `memcpy`。引用关系、原生资源、GC 和渲染对象的恢复需要显式规则，不能仅靠复制裸指针。

### 6.3 撤销的执行顺序

以下是待实现并验证的协议，不是现有功能：

1. 进入恢复模式，阻止新业务输入、历史采集和不应继续执行的业务定时回调。
2. 作废旧时间线的待执行动作；给受控延迟业务回调加时间线代号校验，隔离未受控协程/动画回调。
3. 不触发旧业务结束动作地停止呈现与调度。不能直接把 `InterruptDialog()` 当作无副作用清理。
4. 两阶段恢复对象字段和集合，再统一修复引用、库存归属和容器图。
5. 恢复对话、议价、顾客、计时器和必要的交互状态。
6. 用验证过不会重新执行业务的刷新路径更新界面。`Validate`、`Refresh` 等名字不保证纯显示，需逐项审核。
7. 所有可能消费随机数的重建/刷新完成后，再按定义恢复随机状态；若重建本身会改变业务结果，需要消除该副作用，而不是只在最后重设随机数。
8. 做一致性检查，成功后才移动历史游标并解除恢复模式；失败则保持输入受控并回到恢复前的完整安全快照，不留下半恢复状态。

关键证据：`InterruptDialog()` 会在条件满足时执行 `Dialogue.ExecuteEndAction()`，而 `ExecuteEndAction()` 通过委托执行实际业务并设置已执行标记。撤销必须丢弃旧动作，而不是“正常结束”它们。[证据](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/DialogUIManager.txt:6420)、[结束动作](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/Dialogue.txt:1650)。

### 6.4 历史数据结构与边界

建议记录：序号、业务类型、显示摘要、相关对象身份、前/后状态或差分、随机状态、所属日期/场景、完整性摘要及是否可恢复。

维护独立历史游标；连续撤销应向前移动，重做向后移动。在旧节点执行新操作时截断未来分支，释放相应快照及原生保活资源。批量购买的内部 `BuyItem()`、声望变化等应归并到外层操作，防止重复入栈。

先用受限全量快照验证正确性，再考虑差分、结构共享和容量上限。内存和捕获耗时都需要实际测量，不能根据存档文件大小估算原生对象图成本。

第一阶段历史限定在当前进程、当前一天和受支持玩法。退出/跨场景后如何持久化顾客对话与委托图，是独立目标，不应隐含承诺。

原生存档只作为备份/显式保存，不作为每步操作的写入目标。还要定义撤销后自动保存策略，避免磁盘处于“未来”、内存处于“过去”却没有清楚的提交边界。

### 6.5 可复用的物品编码器也不是纯快照工具

`SaveManager.EncodeNodes/DecodeNodes`、`SaveItemNode` 和 `TagSystem.Clone()` 提供可复用线索，但不能直接认为可无损覆盖营业会话：

- `SaveItemNode` 保存物品状态、形状、值和子物品关系，不是完整顾客/对话快照。
- `EncodeNode()` 会向 `PlayerStore.savedItemFeatureList` 追加特征，有全局副作用。
- `DecodeNodes()` 会走 `DirectoryMaster.Item/CharacterItem` 工厂，并进行标签、形状等恢复；重建过程中的初始化及引用关系必须审核。
- 现有 UI、顾客回调和对话可能仍指向旧物品。重建一个内容相同的新物品，不代表旧引用自动正确。

证据：[编码副作用](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/SaveManager.txt:1110)、[解码工厂](/D:/Program/GameProject/ProbablyStolenMod/cpp2il_isil/IsilDump/Assembly-CSharp/SaveManager.txt:3079)、[物品保存结构](/D:/Program/GameProject/ProbablyStolenMod/decompiled/SaveItemNode.cs:6)。

## 7. 路线取舍

| 路线 | 评价 |
| --- | --- |
| 继续复制原生存档 | 不能提供精确营业中撤销；遗漏状态是明确的结构问题 |
| 按顾客 ID 重建 + 存档 | 会丢失已生成的随机内容、对话/闭包、预算/耐心、货物与引用，还可能重复更新追踪器 |
| 为每种操作编写反向命令 | 小范围可用，但交易、剧情和异步副作用多，新增玩法时易遗漏 |
| 业务日志 + 内存快照 | 推荐先验证；可以明确声明覆盖范围，避免依赖所有操作都有逆函数 |
| 日初完整基线 + 确定性重放 | 候选后备方案；必须控制业务与表现随机数、定时器、对象身份及外部副作用，否则不确定 |
| 整个进程/Unity 内存回滚 | 不作为本项目首选，远超目前插件需要且难以保证一致性 |

## 8. 下一阶段验证顺序

本节是后续实验建议，本轮没有执行：

1. **隔离测试环境**：备份全部相关存档及索引，最好使用独立测试存档路径/配置；确认云同步不会覆盖用户进度。先处理现有插件 `Capture()` 自动调用 `SaveGame()` 的副作用，不能一边说“只记录”一边继续自动写用户存档。
2. **观测模式**：在已确认的原生业务入口验证钩子命中，记录顺序、日期、帧号、顾客对象身份、队列、物品/声望/案件摘要；不用 `SaveGame()` 采样。验证开业、叫客、对话动作和每秒物品回调的实际先后。
3. **随机状态最小实验**：解析候选状态入口，执行“保存状态 -> 抽样 -> 恢复 -> 同样抽样”的对比；分别覆盖 Unity 与 System.Random，并检查实际游戏调用和表现逻辑干扰。
4. **同一顾客内最小撤销**：先验证物品移动/特征揭露，再验证议价，再验证单件与整批交易。每次撤销逐字段比较前状态，不只看现金或界面正常。
5. **顾客切换**：完整保留已生成顾客、货物、对话与待客队列；验证撤销送客后不会发生旧的自动下一位回调、重复 OnArrival/OnDismiss、重复记账。
6. **特殊玩法扩展**：鉴定、易货、剧情/举报、机器定时行为分别验收。交火、逮捕、关店和跨夜先标成不支持的历史边界，覆盖完成后再开放。

最低验收场景应包含：连续撤销/重做多步、撤销后改走另一分支、同一顾客多次买卖、批量操作只占一步、物品嵌套/拆分合并、议价随机结果一致、等待十秒后没有旧回调复活、无重复顾客/物品/声望/犯罪计数，以及当前节点保存并重载的明确语义。

## 9. 仍未解决的事项

- 具体运行路径中的 Harmony/原生钩子可靠性，及已有未触发钩子的原因。
- Unity 随机状态内部调用是否可解析、ABI 是否匹配；所有随机源是否完整覆盖。
- 任意顾客和物品委托捕获的可变对象、私有字段和原生保活需求。
- 每种机器/物品定时逻辑的状态与恢复边界。
- 对话/视图的无业务副作用刷新路径，跨顾客恢复是否能保持一致。
- 完整快照的实际成本、容量上限和恢复失败后的原子性。
- Demo 中各特殊分支的可触达性与跨日范围。

**最终判断：目标值得继续做最小可行性验证；不能继续以“文件已写回、现金一样、队列人数非零”作为精确撤销的成功标准。**
