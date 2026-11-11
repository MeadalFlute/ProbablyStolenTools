# 库存与窗口字段覆盖

## 上一阶段实机检查

检查 5d69429 的 2026-11-11 23:01 会话：

- 7 次捕获、6 次恢复（包括手动、跨顾客、后续分支），全部 diff=0/releaseOk=True，均 owners/eligible，无最终全图回退。
- 首节点 RECORD CAPTURE MATCH；两次回到首节点均 RECORD RESTORE MATCH，独立检查 15/16 ms。标签独立对照也匹配。
- 首节点独立记录 4205（953 物品、107 格子、179 单槽、2966 形状），unsupported=0；5932 序列、89179 逻辑槽、713432 逻辑字节。
- 首节点捕获 1337 ms，其中 entityRecords=209 ms、entityAudit=61 ms、标签 shadow=181 ms；原 walk=678 ms。新记录确实增加诊断成本。其余捕获 232–348 ms、中位数 328 ms。
- 恢复总耗时 568–658 ms、中位数 638.5 ms，非 A/B；其中首节点恢复包含 20/22 ms 的标签加实体观察。不能宣称提速。
- 恢复前群拖覆盖 2/6/8/20 件；恢复后有三次四件群拖正常结束。非默认议价、实机全图回退未覆盖，日志本身不证明全部贴图/窗口状态。
- Player.log 无快照相关错误；启动信任列表下载 SecureChannelFailure 独立存在。

## 本轮目的

迁移路线仍处于“只读实体记录 + 字段覆盖清单”。补齐精确库存/窗口的字段清单和关系，不改变恢复权威，不裁剪旧图。

上轮的库存只记录 items/currentItem，本轮扩展为：

| 声明类型 | Entity：独立记录 | Runtime：明确保留在完整旧图 | Visual：沿用旧描述器排除 |
| --- | --- | --- | --- |
| GameInventory | identifier、两种锁覆盖开关、parent | 无 | 无 |
| GameGridInventory | 成员、inventoryShape、尺寸、透明度、renderDynamicBackground、hideBorder、machineryStyle | 四个 `_last...` 刷新缓存 | TreeNodeRender 及列表、Sprite/贴图字典等 |
| GameSlotInventory | currentItem、stealUI/isUIDown、stolenUI、默认方向/尺寸、tooltip、尺寸/透明度/isHidden | validateLock | 原生渲染节点 |
| GameInventoryFunc | 无 | 全部 33 个回调字段；只分类、不调用 | 无 |
| TagSystem（库存继承部分） | 无新增实体值 | dict 引用及深层仍旧图/既有标签混合路径负责 | 无 |
| PixelWindow（精确类型） | 父物品列表、linkedContainerStorage、childElement、锁、样式、可移动标志、尺寸/标题/内边距/透明度等原字段 | headerButtons、三种回调、focusStamp | 原生渲染节点、preDockParent |

这些 primitive 布局元数据仍原本就在业务图中；归入独立只读记录不等于用它们代替 RectTransform 或现有 VisualState。也没有新增排除任何旧字段。PixelWindow 的 style 是 enum，记录核心新增框架枚举值比较，保留完整底层值（包括未命名枚举值）；不调用自定义业务 Equals。

原版 `children/childItems/child` 会生成列表或组合其他对象，`UncheckedAccept/Expel/Validate` 有副作用，因此读取真实存储字段，不调用这些 API。借用关系不通过重放接收动作恢复。

证据为安装版 Assembly-CSharp 的 GameInventory、GameGridInventory、GameSlotInventory、GameInventoryFunc、TagSystem、PixelWindow 的 ilspycmd 只读结果；全部 manifest 已在安装版原生夹具实际验证。

## 覆盖协议

新增 `SemanticEntityFieldPlan`：按声明类型提供全部实例字段的名称、精确类型及 Entity/Runtime/Visual 角色，schema 数量和每项类型都严格匹配。没有“未知字段自动算 runtime”的默认分支。缺失/新增/重命名/变型/重复字段、非法角色直接拒绝诊断。

精确格子和单槽库存组合它们自己的完整清单与 GameInventory -> GameInventoryFunc -> TagSystem 清单；精确 PixelWindow 使用自身完整清单。schema 元数据仅在这次采样内存在，按原 schema 索引共享。旧 Item 基类及精确形状记录继续保留。

一次完整旧槽扫描同时校验：

- 每个 Entity 和 Runtime 字段都必须作为旧图中恰好一个、引用种类相符的槽出现。
- Visual 字段必须没有进入业务图；若描述器规则变化使其出现，拒绝诊断，不自动扩大或删除覆盖。
- 精确库存/窗口/形状 owner 出现未分类槽即拒绝；只给原生 Item 派生类保留明确的“额外字段全部 legacy-only”范围，未宣称其派生完整清单已完成。
- 索引包括视觉元数据也按 maxSlots 约束，required 位掩码有 63 项上限；超限只影响本次只读诊断，旧图继续有效。逻辑 payload 预算保持原规则。

Entity 字段才进入独立记录。Runtime 字段继续由旧图捕获、普查、完整预检/恢复/最终校验，不能因 Entity MATCH 就删掉它们。所有原对象和集合身份、有效顺序、null/重复成员均保留；集合容量、版本、物理数组及别名仍旧图负责。

不适配库存派生类、PixelWindow 派生类或第三方深层实现；unsupported 计数并保留旧图。未知字段 schema 失败是整个采样诊断失败，不会返回一份伪完整记录。

## 日志与验证

沿用 `Snapshot.SemanticEntityRecords`、每上下文一次、失败隔离及原恢复序列，没有新生产写回开关或新挂点。scope 扩为 `item-base+inventory-fields+window-links+exact-shapes`。

摘要新增：

- windows：精确窗口记录数量。
- classifiedRuntimeSlots：本次完整清单明确分类且确实存在于旧图的 runtime 槽（仅已适配库存/窗口 owner）。
- excludedVisualFields：这些 owner 明确列出的视觉字段元数据数量，不是节省的槽数。

projectedOwnerSlots/legacyOwnerSlots 仍只统计被投影 owner 的旧槽，不是全图分类数。未分类 Item 派生字段继续包含于 legacyOwnerSlots，不能从该统计推断完整实体迁移百分比。

验证：

- 269 项离线回归，新增 7 项：完整分区、未知/重复/变型字段拒绝、继承声明边界、只读记录不代替旧图保存其余值、枚举全宽值、metadata 不依赖调用者规则数组、无写回 API。既有多节点与释放回归继续通过。
- 安装版原生夹具保留此前六轮旧图恢复，另构建共享窗口：父物品重复引用、链接格子/单槽、子库存、借用窗口与物品内容窗口共享身份。
- 改变 parent/锁/库存形状/stolenUI/默认方向/窗口子控件/窗口锁与样式/两个列表成员等十处，独立记录检出全部；旧图恢复后双方零差异。
- 单独改动 grid 刷新缓存、slot.validateLock、窗口回调时，独立实体记录零差异但旧图报告三处；旧图恢复原回调身份，未调用任何业务回调。
- 用包含全部 Entity 字段、但删除 Runtime 字段的筛选库存根构建旧图，新覆盖检查明确拒绝 `not fully covered`。此前 schema/预算失败仍不破坏原节点。
- Mono Release 零警告/错误；57 挂点、原 74 字段及新完整 schema 原生检查通过，官方扫描 HardFlags 为空，能力不变。

## 下一边界

先验收真实窗口/借用关系及新增记录成本，之后补物品派生层/回调完整字段清单。外部实体句柄、运行态图切分、实体写回、生产图替换均未启用；当前仍是完整 StateGraph 权威的混合后端。

实机建议：首节点建立时让一个容器窗口处于打开状态；随后开关窗口、在容器与柜台之间移动/旋转物品、买卖并建后续节点，然后往返最早节点，恢复后测试群拖。若方便，再覆盖带容器的设备单槽借用窗口。保持默认日志即可，无需开启 Verbose。

## 部署记录

2026-11-11 23:12，独立运行时提交 `e46e7d4`，标准脚本部署，版本保持 0.8.14。

- 备份：`out/full-release-deploy-20261111-231247`。
- 部署 DLL SHA256：`CAB30EEA870288DA312908D97E8D7A251668EE6968F3405FFAC03EC171E6436B`，与构建一致。
- 78 个存档文件校验未变，SettingsImported=False，GameStarted=False。
- 未启动游戏，等待库存/窗口扩展后的 RECORD CAPTURE/RESTORE MATCH、windows/分类计数及 entityRecords 耗时；本轮不叠加物品派生层或运行态切分。
