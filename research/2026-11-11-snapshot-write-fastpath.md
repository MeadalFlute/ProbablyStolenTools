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
- 本轮 Unity 实机验收待用户操作，不能以隔离基准替代。
