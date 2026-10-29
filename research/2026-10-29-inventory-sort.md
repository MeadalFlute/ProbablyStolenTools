# 智能整理（Inventory Auto-Arrange）调研

日期：2026-10-29
状态：**仅调研，未开始开发**
目标：独立新 mod，替换游戏「整理」的摆放策略。

---

## 1. 结论摘要

| 问题 | 结论 |
|---|---|
| 游戏有没有排序框架？ | **有，而且相当完整**：`InventorySortHelper` 提供了占位、形状、方向、提交、撤销 |
| 能不能只换算法？ | **可以**。`Sort()` 只负责选物品/方向，真正摆放是 `PackShelves` / `PackFirstFit`，替换其一即可 |
| 现成 mod 能不能直接用？ | **不能**。见 §3：都是闭源 DLL，且多数面向 Demo + MelonLoader |
| 有没有现成的通用库？ | 有矩形装箱库（MaxRects / Skyline / Guillotine），但**本游戏物品是不规则形状**，矩形近似会浪费空间，只能借鉴思想 |
| 推荐路线 | 复用游戏框架 + 自研「格级 Bottom-Left + 评分 + 压实」，分组与邻接用软约束 |

---

## 2. 游戏侧事实（逆向所得）

### 2.1 排序流水线

```
ItemContextHandler.TriggerButton          // 右键菜单四个排序按钮 + 撤销
  └─ InventorySortHelper.Sort(inv, bigFirst, fromEnd)
       ├─ CanSort()                        // 前置校验
       ├─ EmporiumOption.SortAutoRotate    // 玩家设置：整理时是否允许旋转
       ├─ preferredOrientation[item] = autoRotate ? WideOrientation(shape) : shape.orientation
       ├─ movingItems = childItems.Where(可移动)
       ├─ Context { inventory, inventoryShape, movingItems, preferredOrientation, autoRotate, fromEnd, itemLayers }
       ├─ NewOccupancy(context)
       ├─ PackShelves(context)             // 主策略：按行（货架）扫描
       └─ 失败则 NewOccupancy + PackFirstFit(context)
            └─ Commit(inventory, placements)
```

关键结论：

- **`PackShelves` 是“货架式”**：先算一排的高度 `Shelf.height`，再用 `ScanRow` 在这一排里从左（或从右）扫描。这正是“横向排列、长条空位”的来源。
- **`WideOrientation`** 会把物品转成“横向更宽”的朝向，所以原生整理偏爱横放。
- `Commit` 的行为是：先 `Expel` 所有目标物品，再对每个 `(item, GridShape)` 调用 `GridShapeBuilder.SetTransform` + `set_modifiedShape` + `UncheckedAccept`。**提交是整体性的**，不会只放一半。
- `Undo` 依赖 `undoSnapshots`，`Sort` 成功后自动记录，可直接复用。

### 2.2 数据结构

| 类型 | 作用 |
|---|---|
| `GameGridInventory` | 容器：`items`、`inventoryShape`（容器自身也可以是异形）、`machineryStyle` |
| `GameInventory.childItems` | 容器内物品列表 |
| `GameItem.modifiedShape` | 物品当前占地形状（`GridShape`） |
| `GridShape` | 逐格 `byte`、`orientation`、`flipped`、`minX/minY`、`Intersects/FullyIntersects/Get/GetLocal` |
| `Context` | `inventory / inventoryShape / movingItems / preferredOrientation / autoRotate / fromEnd / itemLayers(byte[])` |
| `InventorySortHelper.Commit` | 应用布局 |
| `InventorySortHelper.IsAnchored` | `ItemPinHelper.IsPinned(item) \|\| DebrisHelper.IsDebris(item)` |

### 2.3 已经存在的“免整理”能力

**物品级**：`ItemPinHelper` 已有完整实现——`IsPinned / SetPinned / TogglePinned / EnterPinMode`，并且 `IsAnchored` 已经把它接进排序流程。

也就是说「钉住这个物品，整理时别动它」**原生就有**，我们只需要在自己的界面里暴露/复用，不必重造。

**区域级**：原生**没有**。需要自己做（见 §4.5）。

---

## 3. 现成方案调研

### 3.1 同游戏的现成 mod（都不能直接用）

| 名称 | 平台 | 说明 |
|---|---|---|
| [Smart Inventory Arrange](https://patch.ali213.net/showpatch/317475.html) | Nexus 691 / 游侠 | 面向 **Demo 0.46D + MelonLoader**，闭源 68K DLL。功能与需求高度重合：同类集中、方向统一、减少碎空、结果稳定、提交前校验、失败回滚 |
| [Inventory Sorter](https://www.nexusmods.com/probablystolen/mods/15) | Nexus | 早期排序 mod，闭源 |
| [Duoji OneClick Sort](https://www.nexusmods.com/probablystolen/mods/805) | Nexus | 一键排序，闭源 |
| [ProbablyStolenTweaks](https://www.nexusmods.com/probablystolen/mods/1107) | Nexus | 综合 tweak，含排序相关功能 |

**不可复用的原因**：

1. 运行时不兼容——Demo 用 MelonLoader + IL2CPP 代理，正式版用自带 ModLoader + Mono + `IMod`；
2. **闭源**：只有 DLL，没有算法实现，也没有许可证可依；
3. 无法确认它们是否处理了正式版新增的物品形状/机器槽位。

> 但它们的功能清单反向验证了需求合理：同类集中、统一朝向、减少碎空、结果稳定、失败回滚——这几条应当直接写进我们的验收标准。

### 3.2 通用 2D 装箱方案

| 算法 | 适用性 | 备注 |
|---|---|---|
| Shelf / NFDH（货架） | ❌ | 就是原生做法，产生长条空位 |
| Guillotine | ❌ | 切分式，空位仍是矩形条带 |
| **Skyline（天际线）** | ⚠️ | 适合矩形；异形需扩展成“逐格高度图” |
| **MaxRects** | ⚠️ | 矩形最优之一；异形只能取包围盒，浪费大 |
| **Bottom-Left Fill** | ✅ | 逐格扫描 + 掩码判定，天然支持不规则形状，就是 `ScanAll` 的泛化 |
| 精确求解（CP-SAT / ILP） | ⚠️ | 小规模可求最优，但运行时太重；可作为离线基准生成启发式参数 |
| 局部搜索 / 模拟退火 | ✅ | 放在压实阶段，代价可控 |

参考：[Skyline 与装箱启发式综述（HAL 论文）](https://theses.hal.science/tel-03236078v1/file/2020TOU30261b.pdf)、[Tetris 背包实现（Godot）](https://github.com/Cholopol/Cholopol-Tetris-Inventory-System)。

**结论**：没有能直接吃“异形 + 分组 + 区域豁免”的成熟库。业界常见做法（塔科夫类背包整理的 mod）也都是「逐格扫描 + 评分 + 多轮压实」，属于自研范畴，但算法思想是公开的。

---

## 4. 推荐设计

### 4.1 总体结构

```
[入口] 右键菜单 / 面板按钮 / 热键
   ↓
[预处理] 掩码合并（区域豁免 + 钉住物品 + 碎片）→ 物品分组 → 排序 → 目标朝向
   ↓
[摆放] 逐组 Bottom-Left Fill + 位置评分
   ↓
[压实] 多方向重力 + 组内微调（保持确定性）
   ↓
[验收] 计算指标；不优于原布局则放弃（保留原样）
   ↓
[提交] InventorySortHelper.Commit
```

对 hook 的建议：

- **不替换 `Sort()`**：它内部还要处理 `SortAutoRotate`、`preferredOrientation`、`movingItems` 过滤，重写容易漏。
- **替换 `PackShelves`（prefix）**：返回我们的 `List<(GameItem, GridShape)>`；返回空/null 时让原生逻辑继续跑，天然带回退。
- 若某些容器不适用（例如机器专用槽位），按 `inventory.machineryStyle` 等标志直接跳过。

### 4.2 摆放：带评分的 Bottom-Left

```
for group in groups (按面积降序，能量矩阵组优先):
    for item in group (面积降序, 同 identifier 连续):
        candidates = 所有 可放置的 (x, y, orientation)
        chosen = argmin cost(candidate)
        place(item)
```

`cost` 由三部分构成（权重可配置）：

1. **贴边/紧凑**：距离已占区域越近越好（减少悬空块）；
2. **邻接加成**：同 identifier、同类别 +N；与能量矩阵 4 邻接 +M（需求 4）；
3. **空腔惩罚**：放下去之后，统计周围“被围住的 1~2 格空洞”，每个 -P。这是让空位成团的关键项——原生算法完全没有这一项。

### 4.3 压实：让空位成“团块”

摆放完成后做 2~4 轮四方向重力：

```
for dir in [Up, Left, Down, Right]:
    for item in 按 dir 排序:
        沿 dir 反复平移直到碰撞
    若包围盒变大或空块指标变差 → 回滚这一轮
```

这一步成本极低（每物品 O(格数)），但对“长条空位 → 团块空位”的改善最明显。

### 4.4 分组与朝向

- 分组键：`item.identifier`（同类集中）；类别由 identifier 前缀/标签映射（机器、电池、能量矩阵、消耗品、杂物）。
- 朝向：**组内统一**。默认竖放（`orientation` 保持），只有当整组竖放放不下时才整组换横放——这直接对应用户「不喜欢乱转」的诉求。
- 能量矩阵组**先放**，再放机器/电池组，使邻接评分有目标可依。

### 4.5 区域豁免（框选）

原生只有物品级钉住，区域需要自建：

- 数据结构：`byte[] mask`（与 `Context.itemLayers` 同尺寸），按 `GameGridInventory` 实例（或用容器的持久 id）保存。
- 生效方式：`NewOccupancy` 之后把 mask 叠加为“永久占用”，我们的 packer 自然避开。
- 交互：
  - 进入「编辑整理区域」模式 → 在容器上拖拽框选 → 选中的格子写 1；再次拖拽取消；
  - Esc/右键退出；
  - 可参考游戏已有的高亮格 `GameGridInventory.highlightSquares` 做选中反馈，或自绘一层半透明遮罩。
- 持久化：写进 mod 自己的配置文件（按容器 id），不碰存档。

### 4.6 验收指标（用来判断“改进了没有”）

| 指标 | 方向 | 说明 |
|---|---|---|
| 空位连通块数量 | 越少越好 | 空位成团的核心指标 |
| 最大空位块面积占比 | 越大越好 | 能否再塞一件大货 |
| 物品包围盒面积 / 物品总面积 | 越接近 1 越好 | 紧凑度 |
| 同 identifier 邻接率 | 越高越好 | 同类集中 |
| 能量矩阵邻接满足数 | 越高越好 | 需求 4 |
| 布局稳定性 | 同输入同输出 | 用固定排序与固定 tie-break 保证 |

低于阈值（例如空块数量没有减少）就**不提交**，保持原布局——与现成 mod 的“找不到更好方案就保留”一致。

---

## 5. 交互与入口

原生右键菜单已有 4 个排序按钮（`buttonSortBigStart / SmallStart / BigEnd / SmallEnd`）+ 撤销。

建议**不动**原按钮，新增独立入口：

1. 本 mod 面板中的一个「智能整理」按钮（作用于当前打开的容器）；
2. 可选热键（默认不占用，避免冲突）；
3. 若后续确认 `ItemContextHandler` 的菜单是可扩展的，再考虑追加一个菜单项。

「编辑整理区域」作为面板里的一个模式开关。

---

## 6. 风险与待验证

| 风险 | 说明 | 验证方式 |
|---|---|---|
| `UncheckedAccept` 的落位语义 | 是否严格按 `modifiedShape` 落位、失败会不会半途 | 小规模实测 + 对比整理前后 `childItems` 的 shape |
| 旋转与翻转 | `GridShape.orientation/flipped` 的取值与 `SetTransform` 配合 | 实测 0/90/180/270 与 flip |
| 容器异形 | `inventoryShape` 本身可能不规则 | 找异形容器（背包/保险箱）测试 |
| 能量矩阵的“邻接”定义 | 未在 `PowerHelper` 找到邻接/范围判定，可能由物品数据或网络逻辑实现 | 游戏内实测：把机器贴在矩阵四邻/斜角，看是否供电 |
| 钉住物品与区域掩码的冲突 | 钉住物品可能落在掩码区 | 掩码优先，钉住物品视为已占 |
| 性能 | 大容器 + 高占用率时候选爆炸 | 单次预算 16ms，超时降级为原生 `PackFirstFit` |

---

## 7. 工作量估算（供排期）

| 阶段 | 内容 | 预估 |
|---|---|---|
| P1 | 打包器骨架 + 掩码 + Commit/Undo 接入 + 指标 | ~600 行 C#，含离线测试 |
| P2 | 分组、统一朝向、能量矩阵邻接 | ~300 行 |
| P3 | 框选 UI（编辑模式、持久化） | ~300 行 + UI 调试 |
| P4 | 现场验收与参数调优 | 取决于实测 |

复用面很大：形状/占位/提交/撤销/钉住全部使用游戏自带实现，我们只写「决策」部分。

---

## 8. 参考

- [Smart Inventory Arrange（Nexus 691 / 游侠转载）](https://patch.ali213.net/showpatch/317475.html)
- [Inventory Sorter（Nexus 15）](https://www.nexusmods.com/probablystolen/mods/15)
- [Duoji OneClick Sort（Nexus 805）](https://www.nexusmods.com/probablystolen/mods/805)
- [ProbablyStolenTweaks（Nexus 1107）](https://www.nexusmods.com/probablystolen/mods/1107)
- [Skyline 装箱启发式（HAL 论文）](https://theses.hal.science/tel-03236078v1/file/2020TOU30261b.pdf)
- [Tetris 背包系统实现（Godot）](https://github.com/Cholopol/Cholopol-Tetris-Inventory-System)
