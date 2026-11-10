# 原生存档替代内存快照的可行性调研

日期：2026-11-10。范围：当前正式版 Mono；本轮只调研，不替换后端、不部署、不启动或操作游戏、不调用 SaveGame / LoadGame，不读取或修改用户 ES3 存档。

## 结论

**不能把现有顾客级 F6 直接改成“原版 SaveGame + LoadGame”，但可以复用原生持久化格式和库存编解码，另外实现运行态扩展。** 后者属于开发一个“支持营业中恢复的扩展存档后端”，不是换两个 API。

需要区分三个目标：

| 目标 | 判断 | 代价与边界 |
| --- | --- | --- |
| 回到原生支持的开业前存档 | 现有文件回退已实现这一路径 | 不需要取代顾客快照；不支持恢复当前顾客 |
| 去掉大对象图快照，允许回溯时重新加载场景 | 有条件可行，值得做受限原型 | 原生存档主体 + 自定义运行态数据 + 顾客工厂适配 + 加载流程控制 |
| 保留当前无重载、同一顾客精确回溯，完全不用运行时快照 | 不能仅靠原生接口实现；重写恢复器的成本高 | 需要保留对象身份或完成引用重绑定，原生 DecodeNodes 不能直接代替 |

建议：**先做独立实验后端，不移除正式版快照。** 若接受加载画面，优先验证“原生存档主体 + 运行态扩展 + 重载”；若要求即时 F6，则验证“原生物品记录 + 保留身份的原地恢复 + 小范围运行态快照”，不要把它称为完全摆脱内存快照。

## 1. 本次证据

直接读取本机正式版原始程序集，未执行游戏代码：

- `PlayerStore.VERSION_NUMBER = VERSION 100-Rev13`。
- `Assembly-CSharp.dll` SHA256：`725CB999D019DFF1FF31BED353C7F9BB8D81A7605A15DE1F80F5A3A6CF7B1D05`。
- `Assembly-CSharp-firstpass.dll` SHA256：`DC41D5435316EA11D8F40770DB6218F1F7BB22BE64913DAFDA110CC44BC5FA60`。
- 与交接文档 Rev1 的哈希不同，因此重新核查，未把旧 Demo 的运行结果当成 Rev13 实测。

反编译证据位于项目根的 `out/native-save-research-20261110/`；下面 `文件:行号` 均指该目录内的文件，除非特别说明。

另用 Mono.Cecil 只读检查元数据和 IL，**30 项静态检查通过**。程序和结果：

- `out/native-save-research-20261110/Audit/Program.cs`
- `out/native-save-research-20261110/audit.json`

这不是存读档往返测试、Unity 活体验证或性能测试。审计不创建 PlayerStore、不执行程序集初始化器、构造器、Unity 方法或 ES3 I/O。

## 2. 原版存档到底保存了什么

### 2.1 保存主体有用，但运行态有意缺失

`PlayerStore.WriteSlotFile()` 只写两个顶层键：

```text
playerStore  -> PlayerStore
storeStation -> StoreStation.Instance
```

证据：`PlayerStore.decompiled.cs:945`。

现金、日期、声望、任务、网络升级、模组 `modData` 等可以复用原生持久化。注意：ES3 的默认规则也可能保存未标注但符合条件的 public 字段，不能只根据 `[ES3Serializable]` 手抄一份字段清单。

但当前正式版仍明确标记 `[ES3NonSerializable]`：

- `PlayerStore.currentClientInstance`、`lastVisitedClient`、`currentNegociatedItem`。
- `isClientArrived`、逮捕/交火状态、六个交互锁。
- `nightlyAction`。
- `StoreClientManager.clientStack`、`chemicalFeedbackStack`。

证据：`PlayerStore.decompiled.cs:161`、`:363`、`:547`；`StoreClientManager.decompiled.cs:7`；逐字段属性检查见 `audit.json`。

因此，存下 `storeState=OPEN` 不等于保存了营业中的完整局面。原生存档不是现有 StateGraph 的完整替代品。

### 2.2 前柜台甚至不在原版保存清单内

`SaveGame()` 编码主库存、后柜台、隐藏仓、展示柜、已售仓、雇员仓、售货机/饮料机、垃圾桶、文件包和录音机库存，但**没有编码 `frontInvinvElement`**。

证据：`PlayerStore.decompiled.cs:875`；静态审计同时检查实际字段读取和属性 getter 调用，确认主库存、后柜台存在，前柜台不存在。

当前顾客出售的商品、正在使用的前柜台物品及其嵌套内容，必须补充保存；不能只额外存一个顾客 ID。

### 2.3 不能只调用 ES3.Serialize(store)

物品主体不是把整个 GameItem 运行对象直接塞进 PlayerStore：

1. `SaveGame()` 先刷新 `mainInvJSON` 等库存字符串。
2. `SaveManager.EncodeNodes()` 生成 `SaveItemNode`，记录物品状态、形状、价值、贴图和子库存链接。
3. `EncodeNode()` 另把物品特征加入 `PlayerStore.savedItemFeatureList`。
4. 然后才把 PlayerStore / StoreStation 写入 ES3。

证据：`PlayerStore.decompiled.cs:875`、`:964`；`SaveManager.decompiled.cs:49`、`:110`。

跳过第一步直接序列化 PlayerStore，可能保存的是上一次原生保存时的库存字符串，而不是节点时刻的库存。只保存 SaveItemNode、遗漏特征列表也不完整。

## 3. 哪些原生接口值得复用

### 3.1 ES3 支持纯内存序列化

当前自带的 ES3 确实提供：

```csharp
ES3.Serialize<T>(value, settings);                 // byte[]
ES3.DeserializeInto<T>(bytes, existing, settings);
new ES3File(settings, syncWithFile: false);
```

`Serialize` 和 `DeserializeInto` 的实现使用 MemoryStream；非同步 ES3File 构造器不读取文件，其 `Save` 写入内存缓存。**不调用 `Sync`，就不需要借用用户存档槽。**

证据：`ES3.decompiled.cs:555`；`ES3File.decompiled.cs:56`、`:134`、`:216`。

因此，“使用游戏存档机制”不必等于“每个顾客都写一次磁盘”。节点可以保存序列化后的不可变字节；这仍占用内存，但不再必须保留整个通用对象图的逐槽记录。

注意两个格式层次：

- `ES3.Serialize(store)` 是一个对象载荷，不是含 `playerStore` / `storeStation` 顶层键的整份槽位文件。
- 若要交给原版 `LoadGame()`，需要构建带这些键的标准 ES3 容器，例如非同步 ES3File，再导出整份字节。不能直接把对象载荷改名 `.es3` 交给原版。

### 3.2 原生物品记录比全图反射更贴近业务

`SaveManager.EncodeNodes(GraphNodeStorage)` 和 `DecodeNodes(List<SaveItemNode>)` 可复用物品的持久化表示，包括容器中的物品。

应保留原生 `JsonUtility.ToJson(new SaveState { saveItems = ... })` 这层编码契约：`SaveItemNode` 实现 `ISerializationCallbackReceiver`，用 `OnBeforeSerialize` / `OnAfterDeserialize` 把标签字典展平/还原；另有仅供解码链接使用的 `tempLink`。不能假定把节点直接交给另一套序列化器，或保留含活引用的节点列表，就与原生库存 JSON 等价。证据：`SaveItemNode.decompiled.cs:5`、`:54`。

可探索直接对真实库存根编码，额外记录各库存的顶层物品列表和库存标识，避免 `EncodeSaveItem()` 的临时窗口挂接。但原生解码入口返回单个根物品，不能假定“对库存根编码”天然符合其单根解码契约；多根布局必须额外实现和验证。

编码也不是严格无副作用：`EncodeNode()` 会追加全局特征列表，SaveManager 使用共享静态堆表。需要主线程、禁止重入、独立的特征收集作用域，并在 `finally` 恢复借用状态。不能直接挪到后台线程。

## 4. 为什么简单改法会出问题

### 4.1 不要调用 SaveGame 再拦截最后的文件写入

Rev13 的 `SaveGame()` 除写文件外，还会：

- 临时挂接库存窗口并编码。
- 更新海报数据、累计游玩时间、记忆内容窗口位置。
- 轮换原生历史文件，失败时可能隔离文件、从历史恢复。
- 更新 `saves_index.es3` 和存档列表缓存。

证据：`PlayerStore.decompiled.cs:875` 至 `WriteSlotFile()`；`EncodeSaveItem()` 实际调用两次 `PixelWindow.Attach()`。

仅替换 `WriteSlotFile()` 不能隔离此前的历史轮换和其他副作用；改 `saveSlotId` 为“临时槽”同样会影响原生历史/索引。研究后端应在更低层组装数据，不调用该方法。

### 4.2 顾客 ID 不是完整的恢复配方

`StoreClient` 包含预算、交易限制、对话完成状态，以及大量 `Action` / `Func`。`Dialogue` 又有 startAction、endAction、选择回调和已执行标记。

证据：`StoreClient.decompiled.cs:67`、`:153`；`Dialogue.decompiled.cs:23`。

工厂会随机生成预算/外观，部分售货商品是在开场对话 endAction 中随机生成，例如 `CreateShadyPharmacist()`。按 ID 重新调用工厂、再播放开场，可能重新抽取商品或重复执行业务。

证据：`StoreClientList.decompiled.cs:93`、`:1323`；`StoreUIManager.decompiled.cs:641`。

`GetNextClient()` 还会消费队列、更新来访追踪和 RunStats，不应拿来当“只把旧顾客显示出来”的函数。`SetupWaitingClient()` 在 Rev13 仍只有 mob / sec / hitman / cartel / lou 专用分支，并不是通用恢复入口。

证据：`PlayerStore.decompiled.cs:1250`、`:1331`。

更改 `safeReflection=false` 也不会自动取消 `[ES3NonSerializable]` 的排除规则，更不会提供正确的回调和闭包重建协议。

证据：`ES3Internal.ES3Reflection.decompiled.cs:233`；`:255` 的条件仍检查排除属性。

### 4.3 原生解码会换掉对象身份

`SaveManager.DecodeNodesInternal()` 调用 `DirectoryMaster.Item()` / `CharacterItem()` 新建物品，再调用 `UncheckedAccept()` 装配库存；`DecodeSaveItem()` 还执行升级、机器、锁和物品 `onLoaded` 等加载逻辑。

证据：`SaveManager.decompiled.cs:145`；`PlayerStore.decompiled.cs:1109`。

原有 StateGraph 则持有原对象，恢复原内容和原引用，见源码 `src/ProbablyStolenTools/StateGraph.cs:101`。

这意味着：

- 不能把原生 `LoadGame()` 当作原地覆盖整个场景的 API。其解码路径没有通用的“清空全部旧库存、解除全部旧 UI/回调引用”的步骤；直接局内调用有残留、重复和引用错配风险。Rev13 本轮没有运行复现旧 Demo 的“现金不变”现象。
- 保留旧顾客、委托和物品引用，却加载一套新物品，也不安全。静态审计发现 149 个直接持有 GameItem 字段的编译器闭包类型，其中有 3 个位于 StoreClientUniqueList。这个数量只说明风险真实存在，不代表它们都可在当前允许的普通顾客节点触发。
- `uniqueId` 相同不代表对象身份相同。必须建立引用重绑定协议，或者保留旧物品并原地恢复。
- 当前议价管理器、检查/交易窗口、卷帘门、顾客外观、RNG 等也不全在两份原生存档主体中。

加载方法还会触发原生加载修复和多个 ModHook；其他模组可能在加载期执行逻辑。`modData` 被保存不等于所有模组运行态都被保存。

### 4.4 重放开门不是恢复营业状态

`StoreShutterButton.OpenShutter()` 会生成队列、处理报纸/许可证/奖励、刷新柜台、处理特殊晨间来客。不能为了让 OPEN 存档“看起来开门”而重放整条开门链。

证据：`StoreShutterButton.decompiled.cs:84`；`PlayerStore.decompiled.cs:1289`。

应恢复状态和视图，不重放这些业务。原生 `LoadGame()`、`GetNextClient()`、`OnNextClientArrived()` 的各阶段都需要明确恢复协议。

## 5. 两条实际可做的路线

### 路线 A：扩展原生存档，回溯时重载

适合优先减少通用快照维护成本、接受回溯时加载场景的目标。

节点内容：

```text
标准 ES3 主体
  playerStore / storeStation
运行态扩展（独立、带 schemaVersion）
  当前顾客 + 当天剩余队列 + 化学反馈队列
  顾客创建配方、随机结果、可变状态、对话执行状态
  前柜台与其他未纳入原生存档的物品/布局/特征
  当日未持久化业务字段、闭店待执行动作的恢复协议
  议价、选择/检查引用、交互锁、门与顾客视图
  System.Random + UnityEngine.Random 状态
  runID / slot / day / 游戏与模组版本
```

流程：

1. 沿用现有稳定门控，在开场对话及已跟踪回调完成后编码。独立构建载荷，不调用 SaveGame、不轮换原生历史、不写用户槽位。
2. 用户回溯时，把标准容器写到模组专属暂存文件，用现有 `NewGameData.saveFile -> SaveFiles.ResolveLoadFile` 路由重载。
3. 先加载原生主体，再于受控加载阶段接上运行态扩展。必须在依赖顾客的物品回调和允许交互之前完成相应重绑定，不能假定仅在 OnGameLoadedLate 加一段代码就足够。
4. 通过带版本和适配器的顾客工厂重建行为，覆盖保存的随机结果与可变状态，**不重放已执行的到店/开场业务**。
5. 校验现金、物品、队列、预算、议价与引用，最后恢复随机源和交互；失败则拒绝继续使用半恢复状态。

难点集中在第 3、4 步。游戏没有现成的“任意顾客行为完整导出/导入”协议，需要为受支持类型开发适配器。自定义 Mod 顾客和未知闭包应先拒绝，不应静默丢失回调。

**此路线有机会完全删除现有大图快照，但只在覆盖范围明确且验证通过之后。** 不能承诺任意顾客/任意模组/夜间场景通用恢复。跨场景失效限制在原型期不放宽。

### 路线 B：原生物品记录 + 小范围运行态快照，保持即时 F6

适合希望降低捕获开销但保留当前无加载回溯体验的目标。

1. 用 SaveItemNode 等原生表示承载大部分物品持久状态。
2. 保留原物品/必要运行对象，按节点 ID 和物品 ID 原地恢复状态和拓扑；对已销毁、被拆分、合并和新增的物品继续有明确保活/清理策略。
3. 顾客、对话、委托闭包、议价和原生表示未覆盖的业务另做小范围捕获。
4. 不直接使用会创建新身份的 DecodeNodes 作为恢复器，除非已经完成所有引用重绑定。
5. 以原生语义状态和独立预期值校验，不以新对象图的引用相等判断恢复正确。

这有望**替换大量逐字段库存快照**，但不是纯原生读档。若闭包和回调仍使“小范围图”遍历回全部库存，必须调整边界，不能宣称已经缩小快照。

工程风险：原地恢复器会承担一部分游戏原生解码器的职责，游戏更新后也要维护；并非天然比当前后端简单。

## 6. 最小验证顺序与止损条件

先验证可恢复性，再比较性能。建议分两阶段，不在第一版同时覆盖所有顾客和所有手动节点。

### 第一阶段：受限的重载原型

仅普通 BUY / SELL 顾客、开场结束的稳定节点；先禁用跨顾客和未知 Mod 顾客。保留正式版后端可切换，不自动替换。

必须通过：

1. 仅编码/放弃节点后，现金、库存归属、窗口父级、下一次原生保存均未改变。
2. BUY：卖出物品、议价、改变预算后恢复，预算和一次性议价资格正确。
3. SELL：前柜台商品数量、类型、标签、随机属性、位置和嵌套物品完全一致；不再随机生成一批商品。
4. 加载时不重复 OnArrival、开场 endAction、来访统计、报告奖励、物品 OnLoaded 副作用。
5. 同一节点恢复两次结果一致；以后扩展跨顾客时，过去/未来节点往返和新分支截断正确。
6. 主槽位/索引哈希不因模组的捕获与恢复暂存写入改变；**随后游戏自然存档**另行观测，不能误认为回溯保证永久不改变进度。
7. 核对 System.Random / Unity 随机序列、队列顺序和未来顾客的生成结果。

通过后，再增加 SELLNBUY、BARTER、APPRAISAL、闭店、设备库存、拆分合并和第三方模组。闭店 `nightlyAction` 等状态需要单独解决，不能由普通顾客原型推定覆盖。

### 第二阶段：是否值得替换

在 Unity 自带 Mono、同一实际存档和业务状态下，交替比较：

- 捕获主线程耗时、GC 分配、不可变载荷大小与实际保留内存。
- 恢复总耗时，包含场景切换、UI 重建和校验。
- 物品/库存/特征数量与独立语义摘要，不能只看现金或 `diff=0`。
- 20 节点历史、重复回溯、离店清理和长时间运行的内存。

目前没有原生序列化后端的游戏内基准。JSON/ES3 也要遍历、克隆和分配，不能从“原生”推导“更快”或“不卡”。若需要大量通用闭包反射才能补齐数据，或捕获更慢且恢复明显更慢，就不值得替换现有后端。

## 7. 本轮改动与验证边界

- 新增本报告；反编译结果、只读审计程序与 JSON 在忽略目录 `out/`。
- 没有修改 `src`、配置、版本、发布清单或部署内容。
- 没有执行原生保存/加载、读取用户 ES3 或启动/关闭游戏。
- 30 项审计仅确认上述代码结构；没有宣称原型可用或完整往返通过。

复现本机静态审计：

```powershell
dotnet run -c Release --project out\native-save-research-20261110\Audit\Audit.csproj -- "<GAME_DIR>\Probably Stolen_Data\Managed" "<REPO>\out\native-save-research-20261110\audit.json"
```

本报告的可行性判断基于本地程序集的一手证据。外部搜索未返回可用结果，未依赖其他 ES3 版本的文档来推定本机实现。
