# 普通字段写回减重与剩余热点计时

日期：2026-11-11，版本保持 0.8.14，不推送。上一轮 TagState 混合载荷实机记录见 `2026-11-11-semantic-tag-blocks.md`。

## 依据和本轮范围

16:33 会话的常规捕获为 421 / 475 / 443 ms，首节点为 1018 ms（walk=707、Shadow=143、verify=71）。四次恢复为 1253 / 1193 / 1251 / 1232 ms，reconcile 为 488 / 482 / 519 / 523 ms。

不能仅据 reconcile 总计认定视觉几何慢：它还包含完整差异扫描及按需第二次业务图恢复。首个 walk 也混合了元数据、逐字段工作、首次读取委托编译及可能的 GC，当前没有证据许可删除这些工作或把捕获分帧。

本轮做一个普通字段写回减重，并增加低频计时。没有扩大 TagState 子类载荷，没有字典裁剪或 Clear/Add 重建，没有调用原生保存、改存档或调整恢复顺序。

## 写回改动

旧 ManagedStateAccess.Write 对每个标量字段先 GetValue，再递归到叶子返回保存值，再因 FieldType.IsValueType 无条件 SetValue。这个最末旧值不会影响结果。

新路径：

- 非数组的单字段直接 FieldInfo.SetValue；引用字段仍先读取旧引用并用 ReferenceEquals 决定是否写入，包含 readonly 引用字段。
- 标量/引用数组元素的 Path.Length=0，直接 Array.SetValue，不读取一个随后被整个替换的旧元素。
- 嵌套结构体及结构体数组继续使用原递归复制回写；只在最末字段为值类型时直接 SetValue，仍保留所有父层读、父层写及数组元素复制回写。
- 不调用属性 setter，不生成写入委托、不绑定业务对象到静态缓存，不做引用内容比较，不将标量写入变成“相等就跳过”。
- 委托 Anchor 分支保持不变，不调用回调。
- 不改变 FieldInfo/Array 的 GC 屏障、全字段写入、对象身份、预算、捕获/校验及标量先于引用的顺序。二次业务图恢复仍按原条件执行。
- 为独立对照增加五参数内部构造器 directWrites=false，默认生产路径为 true。没有增加玩家设置，已有 TagState 强类型载荷保持原实现。

## 新计时

1. SnapshotFieldReader 只在实际 Compile() 时计时并累计尝试数，成功与回退都包括；32 次读取阈值和失败后不重试策略不变。ClearCache 同时清零计数；已经保留的 reader 仍可使用。
2. CAPTURE 追加 walkReaderCompileMs，取本次 walk 前后的累计 ticks 差；这是 walk 的子集，不是独立加项。readers 摘要给 compileAttemptsTotal/compileMsTotal，便于判断编译发生于哪个节点。单位为整数毫秒，不足 1 ms 截断。
3. RESTORE 同一行追加 reconcileDetail：
   - scan：原刷新后完整 Differences。
   - write：条件满足时原有第二次 Graph.Restore；rewrite 明确是否执行。
   - views：原第二轮完整 VisualState.Restore。
   - geometry：原 RestoreItemGeometry。
   - transforms：原最终 RestoreTransforms。
4. 上述五项合计等于原 reconcile；reportedDiff 为有上限的诊断条数（最多 12），不是总差异数，完整扫描没有截断。错误 phase 给具体子阶段。

不新增每帧/每字段日志，不增加额外业务扫描，不增加完整恢复次数。计时目的为区分下轮工作，不能把计时本身称作提速。

## 验证

- Mono Release：0 警告、0 错误。
- 150 项离线测试通过，新增十项：递归/直接写回的标量、private/readonly、nullable、字符串/相等对象引用身份、各类数组及位模式、嵌套结构体、装箱身份、筛选根、委托、GC 下过去/未来节点以及编译成功/失败/清零计时。
- 真实游戏程序集：55 挂点 / 73 字段 / HardFlags 为空；无新能力声明。
- 真实 PlayerStore/StoreStation/GameItemElement 夹具追加递归/直接写回交替对照，实际修改字段、四个不同数组元素、字典扩容后两份完整图均零差异；私有布局缓存仍排除。TagState 混合载荷、子类回退、独立标签、空串/null/位模式、对象/集合身份及完整审计测试继续通过。
- 没有重新构建已弃用的旧 Demo/IL2CPP；上一轮已记录其现有兼容失败。

### 隔离基准及限制

```powershell
dotnet run -c Release --project tests/ReleaseCompatibility/ReleaseCompatibility.csproj -- `
  "E:\SteamLibrary\steamapps\common\Probably Stolen" `
  src/ProbablyStolenTools/bin/Release/Mono/ProbablyStolenTools.dll release/manifest.xml --benchmark-writes
```

沿用原生业务对象标签夹具，两个路径都启用相同 TagState 混合载荷，仅切换普通字段写回。每组先预热，再交替五次并取中位数；只在 .NET 6 中运行，不是 Unity Mono 测量。每份为 53253 对象 / 349232 槽 / 2793856 逻辑字节，逐次修改标签并在写回后完整验证。

最终实现做了两次独立运行：

| 写回指标 | 运行 1 递归 / 新路径 | 运行 2 递归 / 新路径 |
| --- | ---: | ---: |
| 耗时中位数 | 51.40 / 41.38 ms | 41.20 / 42.04 ms |
| 分配字节 | 8916352 / 6062080 B | 8916352 / 6062080 B |

分配量两次均减少约 32.0%。时间一次下降约 19.5%、一次增加约 2.0%，**不能宣称已证明稳定提速**，也不选择最好的一次作为实机收益。捕获路径未修改，基准捕获/校验时间差视为运行波动，不归因于写回改动。早期仅简单字段路径的版本收益也不明显，未据此宣称性能通过。

## 实机操作

部署后先验证本轮，不继续扩大改动：

1. 重启游戏，从相同存档开店等首个节点，保留第一次捕获的 walkReaderCompileMs。
2. 等两三个普通顾客节点，移动、旋转和买卖物品，然后跨节点恢复。
3. 通过面板按“过去节点 -> 未来节点 -> 同一个过去节点”至少重复一次，包含手动节点；检查数量/金钱、贴图位置/朝向与碰撞。
4. 完成后读取本地 CAPTURE、RESTORE 的原阶段和 reconcileDetail，对比 16:33 会话；只有实机才能判断实际卡顿和编译占比。

不要求用户开启 Verbose；正常摘要已包含所需数据。如有错误，停止进一步操作，保留日志及出错节点，不能通过关闭校验绕过。

## 部署

标准脚本已部署：

- 备份：`out/full-release-deploy-20261111-164907`。
- DLL SHA256：`71CC73B5CF6544B18728BC06F139AA0B246CC41E5D23F0D5DB8EE652B2234D16`，与构建一致。
- 已校验 78 个已有存档目录文件、游戏程序集及其他 Mod 不变；未导入设置。
- 没有关闭游戏进程，未自动启动游戏。
- 部署时 Unity 实机验收待用户操作；随后完成的日志检查见下节，不能以隔离基准替代。

## 首轮实机日志检查

用户完成操作后，检查 2026-11-11 16:56:10 启动的会话，来源为本机 PSTools.log、PSTools_history.log、Player.log 及 GameLog_2026-11-11_16-57-56.txt。

四次捕获均使用 hybrid-exact-tag，约 10.1 万至 10.7 万对象、56.3 万至 62.2 万逻辑槽：

| 节点 | captureMs | walk | walkReaderCompileMs | Shadow | verify |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1 waterStockpiler | 1010 | 658 | 68 | 189 | 65 |
| 2 clinicNurse | 354 | 261 | 15 | 0 | 65 |
| 3 retired_farmer | 517 | 413 | 3 | 0 | 74 |
| 4 retired_winemaker | 364 | 255 | 8 | 0 | 76 |

首节点编译占 walk 约 10.3%、总捕获约 6.7%，不是首节点耗时的主要来源。首节点总耗时与上一会话的 1018 ms 接近。后续三次捕获中位数为 364 ms（上一会话同三个顾客为 443 ms），但本轮没有修改捕获路径，不能归因于写回优化。

六次恢复顺序为 1 -> 3 -> 4 -> 2 -> 1 -> 4，全部 RESTORE OK、diff=0、releaseOk=True；首节点两次独立标签对照均 RESTORE MATCH。完整扫描与最后校验均保留。

| 目标节点 | total | reconcile | scan | 二次 write | views | geometry | transforms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 1151 | 399 | 75 | 244 | 45 | 6 | 29 |
| 3 | 1108 | 399 | 70 | 251 | 44 | 5 | 29 |
| 4 | 1146 | 408 | 79 | 249 | 45 | 4 | 31 |
| 2 | 1028 | 356 | 64 | 221 | 41 | 5 | 25 |
| 1（重复） | 1018 | 365 | 62 | 229 | 44 | 4 | 26 |
| 4（重复） | 1346 | 495 | 97 | 320 | 45 | 4 | 29 |

单位均为 ms。逐行核对五项细分之和等于 reconcile。六次恢复均 rewrite=True、reportedDiff=4；该字段仍是有上限的诊断条数，不应泛化成完整差异计数接口。

与上一会话均出现的节点 1/2/4 的首次恢复相比：

- 总耗时中位数 1251 -> 1146 ms，下降约 8.4%。
- 首次业务图写回中位数 305 -> 256 ms，下降约 16.1%。
- reconcile 中位数 488 -> 399 ms，下降约 18.2%。

这是不同会话的小样本，不是严格 A/B；本次六次恢复范围仍为 1018-1346 ms，最后一次 finalVerify 达 151 ms，不能宣称稳定收益或已经消除卡顿。

### 异常和覆盖边界

- 本会话 PSTools 未出现 FAILED、MISMATCH、Exception 或 RESTORE BLOCKED；PSTools_errors.log 最后写入为 15:32:05，不是本轮新增。
- GameLog 在 16:57:58 报读档场景 InventoryScene 停顿，16:58:03 恢复，合计 10.2 秒；发生在首个捕获之前（首个 CAPTURE 完成为 16:58:10），不能归为本轮快照耗时。启动另有其他 Mod 挂点失败、sprite/重复键和原生 UI 警告，不据此认定本 Mod 恢复回归。
- 历史日志包含实际批量买入和六次恢复对应的现金/顾客切换。没有本轮手动节点记录，不宣称覆盖手动节点或全部物品操作；日志零差异也不能独立证明所有画面和碰撞均正确。

### 后续方向

reconcile 的主要成本是完整第二次业务图写回（221-320 ms），不是几何同步（4-6 ms）。下一阶段先做低频、有限条数的差异归属诊断，明确刷新后变化的对象类型和字段；不增加额外全图扫描，仍须保持扫描完整性。现有计时没有保留这四条差异的字段归属，代码中关于 Validate/标签字典的注释不是本次四条差异来源的证据。

禁止仅据 reportedDiff=4 跳过二次恢复或只写四个槽位：尚未证明引用拓扑、别名和原生刷新依赖允许选择性恢复。集合重建、容量裁剪和分帧仍不在本轮许可范围。本轮仅记录验收证据，不修改 DLL，不重新部署或推送。
