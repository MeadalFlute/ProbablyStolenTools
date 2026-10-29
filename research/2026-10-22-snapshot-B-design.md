# B 方案设计：按顾客粒度的“全量运行时快照 + 覆盖式恢复”

日期：2026-10-22，Asia/Hong_Kong。
作者：DSH agent（deepseek）。
状态：**设计讨论稿，未落地**。本文用于和 GPT 对齐，不作为实现承诺。

---

## 0. 用户定义的目标

> 每个**新顾客到来**时存一份快照，包含**当前游戏运行时的全部数据信息**，用于 F6 覆盖（即撤销到那一刻）。

与上一版（每天一份存档）的区别：
- 粒度从“天”变成“每位顾客”；
- 数据源从“游戏存档文件”变成“**运行时内存**”；
- 恢复方式从“写回存档 + 重载”变成“**直接覆盖内存**”。

---

## 1. 已确认的事实（本轮观测）

### 1.1 能被钩住的业务入口

| 入口 | 触及方法 | 结果 |
|---|---|---|
| 叫客 | `PlayerStore.GetNextClient` | ✅ 命中，能拿到 `clientStack` 人数与当前顾客 id |
| 送客 | `PlayerStore.DismissCurrentClient` | ✅ |
| 卖出 | `PlayerStore.SellItem` / `SellItemFromTable` | ✅ 批量 4 件在 5ms 内，需归并为 1 步 |
| 买入 | `PlayerStore.BuyItemFromTable`（归一到 BuyItem） | ✅ |
| 议价 | `BargainUIManager.OfferDiscount/OfferMarkup/Blackmail/...` | ✅ 6 种全部命中 |
| 电话 | `StorePhoneObject.OnClick`、`PhoneUIManager.StartPhoneDialog` | ✅ |
| 扣钱（部分） | `PlayerStore.PayMoney`、`ModCash` | ⚠️ 电话购物**仍未命中** |

### 1.2 完全没有事件的两类操作（本轮新增结论）

用户实际执行了“从设备取出 / 放入 / 运行”和“使用道具”，日志中 **`Inv:*` 与 `Tool:*` 均为 0 条**。

说明：
- `GameInventory.UncheckedAcceptAll` / `ExpelAll` **不是**设备取放的入口；
- `InspectionUIManager.HandleCigarette/Plasma/Stamp/Injector` **不是**道具使用的入口（至少不是这条路径）。

> 这意味着“全部数据”不能靠穷举钩子来保证覆盖；**B 方案的价值正在于：不依赖钩子，直接对整个运行时状态做快照。**

### 1.3 现金看门狗的价值

`[CASH:unattributed] 374->203` 证明：即使某条路径没被钩住，只要它改了 `playerCash`，我们就能发现。
→ 快照方案应当保留这个“兜底探测”，用于验收“是否还有未被覆盖的状态域”。

---

## 2. “全部运行时数据”实际包含什么

按 `research/2026-10-21-daily-undo.md` 的划分，目标状态域如下（B 方案要全部覆盖）：

| 域 | 具体内容 | 现有可访问性 |
|---|---|---|
| 经济/剧情 | `PlayerStore` 的标量字段（cash、reputation、case、rent…） | 高（公共字段，interop 暴露为属性） |
| 物品图 | 所有 `GameItem` 实例、父子容器、`GridShape`、标签 `TagSystem`、特征 `ItemFeature` | 中（可遍历，但图很复杂） |
| 顾客 | `storeClientManager.clientStack`（`List<StoreClient>`）、`currentClientInstance`（`StoreClientInstance`） | 中（引用可得，深拷贝难） |
| 顾客运行时 | 预算、耐心、业务进度、`Dialogue` 图、`Action` 委托/闭包 | **低**（委托闭包无法序列化） |
| 对话/议价 | 当前对话节点、已执行标记、报价、`BargainUIManager` 私有状态 | **低**（多为私有字段） |
| 时间/异步 | 协程、DOTween、`MetaHelper.WaitAndDo` 延迟回调、交互锁 | **极低**（无法枚举） |
| 随机数 | `UnityEngine.Random` 内部状态 + 静态 `System.Random` | **未知**（托管层无 `Random.state`） |

**结论**：`PlayerStore` 的公共字段可以拿到；`GameItem` 图可以遍历；
**委托闭包、协程、Unity 动画、私有字段是“全量”的真正边界**。

---

## 3. B 方案的技术路线（候选）

### 路线 B1：反射式字段快照（推荐先验证）

**捕获**：
1. 以 `PlayerStore.Instance` 为根，反射遍历公共属性（interop 把字段生成为属性）；
2. 遇到 `GameItem` / `List<T>` / `Dictionary<K,V>` 递归，用 `ObjectIdentityMap` 记录引用（处理环）；
3. 值类型、string、enum 直接写；Il2Cpp 对象写引用 id；
4. 输出自定义 JSON（我们自己的格式）。

**恢复**：
1. 反序列化得到“字段 → 值”的树；
2. 对每个对象按 id 定位到**当前**对象（必须保证对象身份稳定，不能重建）；
3. 逆序写回字段；
4. 对集合：先清空再按顺序 add（注意 `TrackClient` 之类会记账的方法不能用，要用 `List` 底层方法）。

**风险**：
- `List<T>.Add` 在 IL2CPP interop 下可用，但如果游戏在 setter/add 里有副作用，会污染；
- 委托字段（`Action`）无法写回 → 顾客对话行为会断；
- Unity 对象（Sprite/MonoBehaviour）只能保存引用，不能保存状态。

### 路线 B2：借 `SaveManager.EncodeNodes/DecodeNodes`

报告已指出：这套编解码器只覆盖物品，且 `EncodeNode` 会往 `savedItemFeatureList` 追加（有副作用），`DecodeNodes` 会走工厂重建**新对象**。
→ 与“保持对象身份”冲突，只能用于“物品内容”，不能用于整体覆盖。

### 路线 B3：原生内存快照

把 IL2CPP 托管对象的字段内存按类型布局 dump/restore。
- 报告明确不建议（GC 句柄、引用重定位、Unity 对象生命周期）。
- 我同意：不作为首选。

### 路线 B4：进程级快照（保存整个 Unity 进程镜像）

技术上等价于“虚拟机快照”，工程量和风险都远超本 mod 的合理范围。

---

## 4. 我倾向的方案（供讨论）

**分层快照 + 分层恢复**：

| 层 | 内容 | 捕获 | 恢复 |
|---|---|---|---|
| L1 标量 | `PlayerStore` 全部公共标量字段 | 反射直接读 | 直接写 |
| L2 物品 | 物品 id → (父容器 id, 槽位, 形状, 标签, 特征) | 遍历 + 反射 | 移动物品对象（保持身份） |
| L3 顾客 | 队列顺序（`clientStack` 的 id 序列）+ `currentClientInstance` 引用 | 读引用 | 重建列表顺序（不调用 `AddClient`） |
| L4 会话 | 对话节点索引、议价状态、交互锁 | 反射读私有字段 | 写回 |
| L5 随机 | Unity `Random` + `System.Random` 字段 | **待验证** | 同上 |

**先做 L1+L2+L3 的“捕获 + 校验”**（不恢复），用 `STEP` 前后 diff 验证捕获完整性；
恢复先只在“同一顾客内”做，成功后再扩展。

---

## 5. 需要 GPT 确认/补全的问题（具体）

1. **对象身份**：`PlayerStore` 与 `GameItem` 在 IL2CPP 下有没有稳定的 native pointer 可作为身份键？
   若有，快照里存指针、恢复时按指针找回对象，是否可行（会不会被 GC 移动 / 对象已销毁）？
2. **委托闭包**：`StoreClient` 的 `mainDialogue` / `acceptDealDialogue` 等包含 `Action`，
   这些委托在 ISIL 中挂在哪些字段偏移？闭包捕获的 `this`/局部变量存在哪个对象里？
   撤销时**是否必须重建委托**，还是只要恢复对话图就能继续跑？
3. **私有字段**：`BargainUIManager`、`DialogueUIManager` 的关键状态字段名与偏移（ISIL 里可读），
   哪些必须进快照？
4. **恢复的写入顺序**：报告 6.3 给了协议（暂停输入→作废旧回调→两阶段恢复→修引用→刷新 UI）。
   其中“作废旧时间线回调”在 IL2CPP 下具体怎么做？（`timeScale=0` 不够，`MetaHelper.WaitAndDo` 的队列在哪？）
5. **随机源**：`UnityEngine.Random::get_state_Injected`（`UnityPlayer.dll` 偏移 23914568）是否已由
   `il2cpp_resolve_icall` 解析成功过？失败的典型原因是什么？
6. **协程/动画**：顾客入场动画（`QueueClientArriveAnimation`）正在跑时做快照，恢复后是否需要重播？
   若不重播，`isClientArrived` 与 `StoreClientMono` 的位置如何对齐？

---

## 6. 建议的验收标准（与报告一致，但更具体）

- 捕获：每位顾客到来时生成一份快照，包含 L1–L3；
- 校验：`STEP` 前后 diff 能解释**每一个**变化字段，且 `CASH:unattributed` 为 0；
- 恢复：同一顾客内做“移动物品 → F6 → 物品回到原位且引用未断”；
- 回归：连续 5 位顾客、每人 2 次撤销，无重复计数、无孤儿物品、无丢顾客。

---

## 7. 当前阻碍（不落地的原因）

1. 道具使用与设备取放两条路径**完全没有钩子**，说明还有未知的状态修改入口；
   B 方案本身不依赖钩子，但如果“快照点”只在新顾客到来，**顾客内的操作无法被切成步**——
   需要确认：用户要的是“每位顾客一份”，还是“每个业务操作一份”？
2. 委托/协程/私有字段的恢复能力尚未验证，直接影响“覆盖式恢复”能否成立。
