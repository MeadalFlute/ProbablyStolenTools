# 完整实体语义化：路线与首步边界审计

## 决策

推进，但目标是 **保留原对象的物品/库存实体记录 + 有边界的剩余运行态图**，不是用 SaveGame/DecodeNodes 重载，也不是给现有访问器换名。

当前生产恢复仍是完整物理 StateGraph 与强类型块的混合后端。此前的标签块、免写、普通比较优化不能算作物品/库存实体已完整迁移。新实体后端先作为独立实验路径与旧图对照，未通过覆盖和往返验证前不替换默认后端。

## 原版机制能借什么，不能直接借什么

本轮复核本机 Rev13 反编译结果：

- SaveManager.EncodeNode 的 SaveItemNode 可参考物品数量、价值、两种形状、标签、贴图及子库存链接的业务表示。
- EncodeNode 会向 PlayerStore.savedItemFeatureList 追加内容，EncodeNodes 使用全局 heap，因此不作为无副作用的只读捕获函数调用。
- SaveItemNode.OnBeforeSerialize 会过滤全零/禁用/空字符串标签，不能用于要求保留全部标签和字符串身份的精确即时回溯。
- DecodeNodesInternal 新建物品、克隆状态和形状，调用 SetSprite、StorageExpansionHelper、UncheckedAccept 等加载/接收逻辑。GameSlotInventory.UncheckedAccept 又会触发物品/库存回调、修改渲染父级及借用窗口，不能当作原地恢复协议。
- GameGridInventory.items 保存真实成员列表；childItems 是复制列表的 getter。GameSlotInventory.currentItem 才是实际单槽成员；children 还包括 stolenUI，不能把“子物品列表”等同完整窗口/借用关系。
- GridShape 是接口，GridShapeBuilder 才是一个具体实现，包含 data/outside/位置/旋转/翻转/尺寸。以后不能对未知形状实现盲调接口或只存包围矩形。
- 旧调研已发现大量持有 GameItem 的原版闭包。uniqueId 不是可替代托管对象身份的安全主键；本轮原生夹具已覆盖两个 uniqueId=0 的不同实体。

证据：`out/native-save-research-20261110/SaveManager.decompiled.cs`、`SaveItemNode.decompiled.cs`；`out/rewind-render-fix-20261110/GameItem.decompiled.cs`、`GameGridInventory.decompiled.cs`；本轮只读 ilspycmd 直接检查安装版 GameSlotInventory/GridShape/GridShapeBuilder。未执行原版保存/编码/解码、构造器、库存业务回调或用户存档 I/O。

## 目标模型

| 层 | 负责内容 | 首版边界 |
| --- | --- | --- |
| 节点身份表 | 节点内 EntityRef 与原对象强引用、实体类型/schema；跨记录共享与环 | 不以 uniqueId/identifier 合并实体，不生成替代物品 |
| ItemRecord | 数量/价值/原始与修改状态/双形状和原点/特征/类型/可变描述、贴图、内容及检查窗口关系 | 按完整字段覆盖清单区分实体字段、运行态字段与视觉字段；未知新增字段使该类型降级 |
| InventoryRecord | 成员顺序、归属、库存类型、形状、限制/借用窗口等运行状态 | 格子和单槽分别适配；不调用 Accept/Expel/Validate 充当业务恢复 |
| 共享子状态记录 | TagSystem/TagState、GridShape、特征与容器共享身份 | 不假设独占；别名/版本/枚举器安全未解决前保留对应物理存储 |
| RuntimeIslandGraph | 顾客、队列、闭包捕获变量、对话、议价、RNG、尚未迁移字段 | 遇已注册实体时保存外部实体句柄并停止重复展开；不丢闭包状态 |
| VisualState | 实际视图父级、布局和最终几何 | 沿用已修复序列，业务写回后刷新/几何/最终变换；不把碰撞正确当作贴图正确 |

运行态小图还没有实现。尤其不能只是“少加几个根”：顾客闭包可从小图重新走回整个库存。未来需要显式图切分协议、外部句柄保活/预检及引用覆盖校验，不能靠忽略字段让统计变小。

## 分步迁移

1. **实体边界审计（本轮已实现）**：从完整捕获图识别物品、格子/单槽/其他库存以及原生/外部实体，扫描已捕获槽位，计数直接实体链接、运行态入/出边、数组成员引用及闭包/委托候选。只提供证据，不批准裁剪或写回。
2. **实体只读记录 + 覆盖清单（进行中）**：首批已实现 GameItem 基类完整字段、精确库存成员及精确 GridShapeBuilder 全字段/全部字节的只读记录，schema/旧槽覆盖/独立预算及恢复后对照；其他字段仍 legacy-only，尚未完成库存/窗口/派生运行态的完整清单。详细范围见 `2026-11-11-entity-readonly-records.md`。每个旧字段必须映射到实体记录、明确保留的运行态或既有视觉规则，不允许未归类字段消失。未知类型/字段/第三方扩展明确回退。
3. **外部实体句柄与运行态切分**：先离线构造闭包反向引用、共享库存、根实体、多个过去/未来节点，核对“实体记录 + 剩余图”覆盖旧图的全部必需状态和引用。旧全图继续保留为独立 oracle，尚不减少生产捕获。
4. **受限原地写回**：一次仅开放一个完整、已对照的实体类型；业务标量先于引用/拓扑，集合原存储保留，视觉序列不变。旧完整校验、明确失败流程及独立开关保留；需要多轮实机。
5. **扩大类型并替换捕获**：只有实体记录与运行态句柄覆盖、失败/预算/多节点/视觉测试都通过，才允许新生产路径停止构建相应旧字段图。分别测捕获、普查、写回、视觉和总耗时/分配，不能用减少校验换性能。

不预先承诺第 4/5 步一次会话完成，也不承诺纯语义集合重建。若必须保留集合身份/版本，其物理载荷可以有显式强类型记录；“实体后端”不等于删除所有 backing arrays。

## 本轮实现

配置 `Snapshot.SemanticEntityAudit=true`，独立于标签 Shadow。每次上下文清理/文件重载后最多尝试一次；失败只记录 AUDIT FAILED、保留主快照流程，不逐帧重试。成功在完整主图捕获校验通过后输出一条：

```text
[SemanticEntities] BOUNDARY id=... scope=captured-graph-only readOnly=true
pruningAllowed=false restoreAllowed=false ... auditMs=...
```

CAPTURE 的 `ms(...)` 单列 `entityAudit`，包含类型分类/临时索引/完整拓扑扫描及摘要整理；verify 计时从审计之后开始，不混入比较器编译时间。此诊断增加首个采样节点成本，不是提速。没有增加每次恢复扫描或持久实体副本。

核心 SemanticEntityBoundaryAudit 只使用图内稳定 ID、根标记、VisitCapturedSlots；不读业务字段、不调用 Validate/Restore/Differences。标签等块也按其捕获槽展开。返回仅有计数和不超过六条有界单行来源，没有保留额外实体引用索引。索引有 MaxObjects 上限，不在每个槽上分配诊断字符串。

原生适配器只从已保留对象读取 GetType/类型元数据，类型缓存只在本次调用内存在，最多 4096 项。实体包含所有 GameItem/GameInventory 派生类型，外部继承链单独计数，不据 Native=true 认证字段已覆盖。委托或带 CompilerGeneratedAttribute 的类型只作为 callback 候选；普通类的方法也可以是回调目标，故 callbackIncomingLinks **不是完整回调数**，其余引用仍计入 otherIncomingLinks，摘要明确 metadata-heuristic。

`arrayIncomingLinks` 是来自数组对象的已捕获引用（包括列表 backing arrays 的成员及未用容量），不证明数组归某实体独占；`sharedEntities` 是直接入边多于一条的实体数，不是业务容器成员数。静态字段、图外引用与第三方 opaque 内部仍不可见，任何零计数都不能认证全局独占。

## 验证及停止边界

- 251 项离线回归，新增八项：精确分类计数、只读旧拓扑、块引用、身份去重/显式根/空集合、未知库存、有限样本之外完整计数、非法/冲突/超预算元数据、Census/释放图拒绝、真实块错误传播且旧图可继续使用。
- 原生夹具：两个 uniqueId=0 的 GameItemElement、格子真实 items、单槽 currentItem、原版 MiscItemDirectory 闭包；完整槽覆盖、数组/闭包入边命中，实时清空库存后仍报告捕获拓扑，原 StateGraph 恢复后零差异。
- Mono Release 零警告/错误，57 挂点 / 74 字段，官方扫描 HardFlags 为空。未增加挂点或私有存储字段访问；原有 files/reflection-external/dynamic-code 能力不变。

部署后只需要正常进入游戏建立首节点，并进行买卖/单件和群组移动、手动节点和六次回溯。无需 Verbose；保留 BOUNDARY、CAPTURE entityAudit 和 RESTORE 行。下一轮先核对真实边界/候选闭包来源与采样开销，再推进实体记录，不直接开放实体写回。

## 部署记录

2026-11-11 22:10，独立运行时提交 878cef9，标准脚本部署，版本保持 0.8.14。

- 备份：`out/full-release-deploy-20261111-221025`。
- DLL SHA256：`52D663B369A0CFA9463FF81803CB8312CF42B03FE6848AF35447B947A8223731`，部署与构建一致。
- 78 个存档文件校验未变，SettingsImported=False，GameStarted=False。
- 未启动游戏；等待实体边界首轮实机日志，不把边界审计称为实体语义恢复已经完成。

后续：上述审计已完成 22:43 会话检查（7 捕获/8 恢复最终零差异、首节点审计 42 ms），详细数据及首批只读记录阶段见 `2026-11-11-entity-readonly-records.md`。

首批只读记录已完成 23:01 会话检查（7 捕获/6 恢复最终零差异、两次 RECORD RESTORE MATCH）；第 2 步继续推进精确库存/窗口完整角色清单及关系记录，物品派生字段清单仍未完成。详见 `2026-11-11-inventory-window-records.md`。第 3/4/5 步仍未开放。

最新：23:15 库存/窗口已验证（5 捕获/6 恢复最终零差异）。第 2 步当前精确
GameItemElement/GameItem/GameItemFunc、库存全继承链、PixelWindow、GridShapeBuilder
完整角色清单已补齐，未知子类仍 legacy-only。第 3 步新增节点隔离原对象句柄、
完整槽位分区、运行态入口计划及仅运行态边的全对象发现重放，284 项及原生夹具
通过；这不是独立 RuntimeIslandGraph 值载荷，runtimeCut=false。
完整物品/句柄待实机；第 4/5 步未开放。详见
`2026-11-11-complete-item-records.md`，不再按前述历史停止点判断当前进度。

23:39 完整物品/句柄实机已通过（5 捕获/8 恢复最终零差异）。接续第 3 步现新增
独立 Runtime 分区值载荷：独立访问器/值令牌/保活/块工厂，逐槽/引用和全部对象
对照旧图；只读且拒绝所有普通/块写回。294 项及原生六轮恢复/旧图释放通过，
待实机。第 4/5 步未开启，生产仍完整旧图。
详见 `2026-11-11-independent-runtime-payload.md`。

23:52 独立运行态已实机通过。2026-11-12 开始第 4 步，仅精确形状实体参与
采样节点第一次写回，其余仍旧图；不开放运行态载荷写回或删旧捕获。
302 项及原生六轮八字段实际实体写回通过，等待首次 Unity 写回验收。
见 `2026-11-12-shape-entity-writes.md`，第 5 步仍未开启。

2026-11-12 00:05 形状、00:23:52 窗口写回已通过用户实测及完整日志对照。
第 4 步接续完整 GameItemElement 继承链 Entity 字段实际写回，形状/窗口/
物品可组合且全部标量先于引用；323 项及原生 14 轮组合通过，待物品首次
实机。库存实体/运行态载荷未实际写回，第 5 步仍未开启，生产仍保留完整
旧图和独立诊断，开发期成本按用户要求保留。见 `2026-11-12-item-entity-writes.md`。

2026-11-12 00:39 物品实机已通过（6捕获/9恢复最终零差异，三次实际
953物品写回及双载荷 MATCH）。第4步接续格子/单槽库存同组实体写回，
至此五种已准入精确类型的 Entity 业务字段写回覆盖补齐，但只在采样节点；
库存待首次实机。323项及原生30轮全部15组合/缺失类型/预算/失效预检通过。
独立运行态仍只读，第5步生产捕获替换仍未开启，不能称完整后端完成。
见 `2026-11-12-inventory-entity-writes.md`。

2026-11-12 01:04 库存回溯后群拖补测通过，接续第4步独立运行态联合写回：
完整实体组＋同节点独立 Runtime 必须精确无重叠覆盖旧图全部槽和引用，
仅采样节点首次写回切到两载荷；旧完整捕获/校验/owner-full修复/视觉保留。
336项及原生六轮真实联合写回通过，待首次 Unity 验收。
第5步捕获替换和移除重复诊断仍未开始，不能称完整生产后端完成。
见 `2026-11-12-runtime-partition-writes.md`。

2026-11-12 01:18 联合运行态首次实机通过（4捕获/7恢复零差异，
首节点4次483607槽运行态实际写回、全部独立MATCH、恢复后群拖正常）。
接续同上下文首节点＋首个后续手动节点的有界双采样，341项及原生
两个完整节点六轮往返/释放隔离通过，待双节点Unity验收。
仍不扩大到全部节点或替换生产捕获，见 `2026-11-12-semantic-two-node-sampling.md`。
