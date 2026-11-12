# 静态迁移后的恢复性能回退：第一步修复

日期：2026-11-12。版本保持 0.9.0，未推送。

## 实机证据

用户测试静态迁移候选后反馈：回溯慢了很多，暂未发现其他问题。这是初步功能反馈，不等于全部验收项目完成。

来自本地 PSTools.log：

| 阶段（平均 ms） | 02:43 旧后端，4 次恢复 | 14:14–14:16 静态候选，6 次恢复 |
|---|---:|---:|
| census | 136.5 | 291.7 |
| preflight | 64.0 | 268.0 |
| write | 107.8 | 273.5 |
| refresh | 66.0 | 424.0 |
| reconcile（包含下方 geometry） | 132.2 | 853.7 |
| finalVerify | 41.5 | 198.8 |
| geometry | 4.0 | 552.3 |
| total | 549.8 | 2313.0 |

两次样本并非同一节点的受控 A/B；新版节点规模约 10–11 万对象、56–68 万槽，不能将整次性能差异全部归因于单项改动。新样本全部 diff=0、releaseOk=True，无实体 schema fallback，未启用开发 oracle。

## 已确认的布局热点

迁移时 GameFields 改用了官方 ModReflect。反编译当前游戏 ModReflect.ReadMember/WriteMember 与 ModLoader.CallingModId 可见：即使字段元数据已缓存，每次访问仍重新识别调用者并遍历调用栈。

原恢复热路径：

- refresh：每个可见物品用 ModReflect 写 validateLock、appliedGridSize。
- geometry：每个可见物品再通过 ModReflect 读取两个原点并重置两个布局缓存。
- 日志约 950–1060 个可见物品，累计数千次调用；单个低频访问的便利 API 被带入批量循环。

修复：

1. Mono 下新增 GameFields.ResetItemLayout，使用字面 typeof(GameItemElement) + 固定字段名获取两个私有 FieldInfo，只重置 validateLock/appliedGridSize。
2. 两处批量布局循环使用该专用方法；不改 UpdateRectTransform/Validate 调用与最终锚点写回顺序。
3. modifiedXOrigin/modifiedYOrigin 是原生公开自动属性，直接读取，避免通用 ModReflect。详细几何日志同样使用公开属性。
4. 保留其他低频 GameFields 的官方 API；没有重新引入计算类型反射、动态编译、私有 IL 直接访问或信任层修改。

## 验证

完整 scripts/Test-StaticLayouts.ps1 通过：

- 构建 0 警告/0 错误，373 项参考测试、57 挂点/76 字段及现有原生实体/图夹具通过。
- 两个发布 DLL 合并扫描只有 files，HardFlags/Error 为空，无退役能力。
- 游戏 Mono 的新测试确认只重置两个缓存字段，其他缓存与原点保持不变。
- 游戏 Mono 离线微基准：1000 个物品的四个字段操作，ModReflect 路径 208.09 ms，固定访问路径 0.75 ms。测试宿主通过反射调用两侧，被测数据不包含 Unity 视图；这不是实机整次恢复时间，也不是性能保证。
- 原有完整图六轮往返、预算/未知布局拒绝、字段表精确对照、可重复构建和 MVID 负例全部通过。
- 输出保留在仓库 out/static-layouts-layout-perf-validation.log，不随 Mod 发布。

## 仍待确认

运行时表达式编译移除后，普通图的元数据读取仍可能较慢，本轮没有解决所有图遍历/比较开销，也没有减少任何普查、预检或最终全量校验。

下一次实机重点对比 RESTORE 的 refresh、reconcileDetail.geometry 及 total；再根据剩余 census/preflight/write/finalVerify 分布决定后续优化。物品售出/移动/旋转/柜台、顾客贴图和群拖仍应保留回归检查。
