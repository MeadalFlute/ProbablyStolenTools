# 2026-11-26 化学辅助与灯牌兼容 DS 审查

审查对象：dirty 工作区（基线 `817af11`，含灯牌适配、咖啡搅拌、试剂/平板、智能挤出及本轮修复、公告改写）。依据 `research/2026-11-26-chemistry-compat-review-handoff.md`；本轮未启动游戏。

## 结论

**没有发布阻断项，可以继续实机验证；我按你的要求把智能挤出的 99% 阈值下调到 70%。** 静态边界（105 挂点 / 76 字段 / 仅 `files`）与运行时夹具都与交接一致，真实 VBE 与真实灯牌 1.0.48 DLL 同时加载的离线回归通过。

需要在升版前处理的是 **P2-1**：灯牌捕获阶段的异常会连整份检查点一起失败，与"未知构建不阻断捕获"的承诺不对称。其余为记录级建议。

## 本轮我做的改动（阈值 99% → 70%）

| 位置 | 改动 |
| --- | --- |
| `ChemistryRules.cs` | 新增 `ExtrusionPercent = 70` 常量，`ExtrusionIngredient` 用它比较（原来写死 `99`） |
| `ChemistryRulesTests.cs` | 边界改为 70%：`{30,70,0}` 命中、`{31,69,0}` 不命中；杂质用例改为 `{0,7000,3001}` |
| `Plugin.cs` | `Chemistry.SmartExtrusion` 描述改成 70% |
| `UpdateNoticeSchedule.cs` | 公告中英两版都改成 70% |
| `README.md` / `README_EN.md` | 功能说明改成 70%（含"不足 70% 保留原选择"） |
| `UpdateNoticeTests.cs` | 断言 `70%`（原断言 `99%`，改文本后会失败——已复现并修正） |

70% 的语义没有变：仍是"最大单一成分占比 ≥ 阈值且存在唯一配方"才自动选品，只放宽阈值；空容器、低纯、无匹配、配方歧义、未开启开关都保持原选择，也不自动生产。

## 复核记录

| 项目 | 结果 |
| --- | --- |
| `dotnet build` Release | 0 警告 / 0 错误 |
| `CheckpointTests` | **474 项**通过（含更新后的 70% 边界与公告断言） |
| `OptionalModCompatibilityTests` | 22 项通过 |
| 官方能力扫描 | **105 挂点 / 76 字段**、`Capabilities: files`、`HardFlags` 空 |
| 游戏 Mono 夹具（原版） | 通过 |
| 游戏 Mono 夹具 + 真实 VBE + 真实灯牌 1.0.48 | 通过；新增 `smart extrusion admission`（module=0/container=1/output=2）与 `Proper Signboards 1.0.48`（三轮回溯保留原记录身份、busy/fault 前置拒绝）两行 |
| 灯牌本机实际版本 | `PawnshopBillboards.Mod.dll` 1.0.48，MVID `6a02d0ff-61d7-49db-9895-fe09c04b5741`，与 `SignboardCompatibility.Supported` 一致 |
| 当前构建 SHA256 | `15097DCB…`（含 70% 改动） |
| 交接记录的部署 | `E2D9FA41…`（060 版，不含本次阈值改动），helper `6129A2D3…`/808448 |

注意：`src\…\bin\Release\Mono\ProbablyStolenTools.StaticLayouts.dll` 是本地 `dotnet build` 产出的单 profile 版（`509D8A98`，670208），打包时必须用 universal manifest + 合并流程，不能直接拿它部署。

## 发现

### P2-1 灯牌捕获失败会整份检查点失败（建议升版前处理）

`CustomerCheckpoint.Access()` 在装配根集合时直接调用 `SignboardCompatibility.Root()` 并把结果加根；`Root()` 在三种情况下抛异常：灯牌仓未初始化（`InvalidOperationException`）、`Capture` 构造时 `Validate()` 发现 `busy`/`fault`/`dragged` 非空、身份不一致。

这些异常一路冒到捕获外层（`CAPTURE FAILED phase=…`，`CustomerCheckpoint.cs:1105`），结果是**整份顾客检查点不生成**，而不只是"跳过灯牌段"。这与交接里"未知构建仅提示、不阻断普通快照"的承诺不一致：未知构建走的是返回 null，但**受支持构建恰好处于繁忙/拖动/未初始化**时是抛异常，属于同一类"灯牌不可用"，处理却相反。

建议：捕获期把灯牌段做成可失败的独立步骤（内部 `try/catch`，失败就 `Signboards = null` 并写一次会话日志），恢复期保持现在的硬失败（`Validate` 拒绝 + "enable after checkpoint" 守卫）。这样不会因为顾客到来那一帧刚好在拖灯牌而丢检查点。

### P2-2 `RefreshSaveCache` 在写入之后才调第三方 Commit，且无异常边界

`SignboardCompatibility.RefreshSaveCache()`（`SignboardCompatibility.cs:159`）在业务图已写、语义校验已通过之后调用第三方 `Commit`，`finally` 里恢复原 `TagState` 身份。两个问题：

1. 没有 `try/catch`：`Commit` 抛异常会让整次恢复以失败结束，但此时状态已经写进去了，日志会把"已恢复但缓存过期"报成恢复失败，玩家只会看到回溯失败。
2. `item.state.dict.TryGetValue(...)` 没判 `state` 为空；虽然游戏内通常非空，但这里一旦 NRE 就是同一类后果。

建议：这一阶段失败降级为"恢复成功但灯牌缓存未刷新"的显式警告（或先探测再决定是否允许恢复），并补 `state == null` 跳过。

### P3-1 `Allow()` 在 null ingredient 上多走了一次读取

去掉了 `Compatible()` 后，`Allow()` 先算 `ChemistryData.Parts(ingredient)` 再判 `ingredient != null`。`ChemistryData.Parts(null)` 会读取 `item` 标签，若真为 null 会抛异常并被外层 `catch` 吞成"拒绝放置"；旧行为是允许手动放置。原生接受回调传入的 item 基本不会为空，建议还是先判 null 再读，保持语义。

### P3-2 咖啡合并的等价性（已核对，成立）

反编译 `ChemicalHelper.BlendInto`：先按 `Round(part*(1-loss), AwayFromZero)` 取整一次，再按 `value/10 + (i < value%10 ? 1 : 0)` 拆十轮 —— 十轮之和恰好等于一次取整，所以合并成一次 `AddLiquid` 的最终组成等价。温度侧 `HandleReceiveLiquidTemp` 默认 `sourceTemp = 26`，白名单要求容器恒 26°C 且未加热，分轮与否结果相同。

唯一的差异面是容量不足时的逐轮截断顺序：`Allow`/`Pass` 已提前用 `output + total > Capacity` 拒绝，实际走不到；如果以后放宽准入，这里需要重新核对。

### P3-3 平板附加组件的每帧开销

`NativeWindowGestures.Tick()` 每帧对交易平板做一次 `GetComponent<NegotiationPinInput>()`（没有才 `AddComponent`），置顶时每帧 `DecorateTablet` 比对并可能写入 TMP 文本。可把组件查找缓存到 Entry，或改为只在 `RefreshClientHeader` 之后做一次装饰。

### P3-4 `ClearViews` 的调用形态建议加断言

`Api.ClearViews` 取的是按名字查到的 `BillboardManager.SceneUnloaded`，恢复时以 `Invoke(null, null)` 调用。若该构建上它是带参数的重载，会在首次恢复时才抛 `TargetParameterCountException` —— 正好落在"恢复中途"。建议在 `ValidateBuild()` 里连同参数个数一起校验（`GetParameters().Length == 0`），不满足就直接判为不支持构建。

### 记录

- 新增挂点 3 个（`GameSlotInventory.UncheckedAccept` postfix、`NegociationUIManager.RefreshClientHeader`、`UIDragHandler.MoveFront`），与 102 → 105 对得上。
- 灯牌只绑定 MVID、未知构建仅提示、不加入禁用名单、不改第三方存档——符合你给的约束；`CompatibilityNotice` 的"同消息才算已展示"改法也正确（不同消息等窗口关闭再弹）。
- 本批是 dirty 候选，`E2D9FA41` 不等于当前 `15097DCB`；正式发布仍需清洁源码打包 + 重新部署，不能把这次部署当 0.9.9。

## 逐项对照交接声明

| 交接声明 | 复核 |
| --- | --- |
| 灯牌仅 1.0.48/MVID 匹配才启用，未知构建提示不阻断 | 代码与 MVID 核对通过；**未知构建确实返回 null**，但 P2-1 指出的 busy/dragged/未初始化会抛异常 |
| 灯牌记录/亮度原身份还原、Commit 派生缓存 | 仓储与还原逻辑正确；离线夹具覆盖三轮记录往返，未执行真实 `Commit`（交接已说明） |
| 咖啡快路径仅 26°C、非加热、白名单成分 | 通过，且等价性经反编译确认（P3-2） |
| 试剂 `*` 前缀、详情显式关闭 compact | 通过；普通悬停仍三行 |
| 平板 Shift+中键置顶、`RefreshClientHeader` 补装饰 | 通过；闪烁根因处理正确 |
| 智能挤出原生第 1 仓 + 唯一配方 | 通过；真实夹具验证 module=0/container=1/output=2 |
| 公告五节、r1 重播一次、6px/5 秒 | 通过；测试同步更新 |
| 474 参考 / 22 可选 / 105 挂点 / 76 字段 / 仅 files | 本轮全部复跑通过 |

## 实机建议顺序

1. **灯牌**：摘一面牌再回溯、挂新牌再回溯、回溯后保存并重载，确认亮度与挂位还原、不出现重复/幽灵灯牌。
2. **智能挤出**：70% 边界（70/30 命中、69/31 不命中）、纯水、低纯、配方歧义保持原选择；换另一种配方与另一台挤出机。
3. **咖啡搅拌**：大批量常温咖啡一次拖入，确认全部合并、含杂质时不走快路径。
4. **平板**：置顶/取消、刷新客户时不闪烁、关闭还原。
5. **公告**：新修订只展示一次、窄屏排版与 5 秒进度条。

上述 1、2 通过后即可准备清洁源码打包、升版（下一小版本）与部署。
