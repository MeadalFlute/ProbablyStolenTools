# 快照优化第一步：校验不再登记新标量

日期：2026-11-10。基于 `c243a96`，保持版本 0.8.13。用户要求分步优化，本步只修改标量差异校验，不混入数组预算、描述压缩、恢复普查或分帧。

## 改动

原先 `StateGraph.Differences()` 调用 `ManagedStateAccess.ReadValue()`，后者会将新观察到的实时值登记到当前访问器的 `_values` / `_savedValues` 中。校验旧节点时，这些值并不属于节点，却会被保留至节点释放。

新增可选接口 `IStateValueComparer`。ManagedStateAccess 用捕获令牌找到保存值，与实时读取的字段比较：

- float/double 比较原始位模式，区分正负零及不同 NaN。
- 其他标量维持原先值登记的相等语义，包括枚举、可空值和等值但 scale 不同的 decimal。
- 不登记新令牌，也不改快照字节、对象集合、槽位和恢复写入。
- 未实现该接口的后端仍按原来的字节路径校验，无需更改旧后端或测试 FakeMemory。

字段读取仍可能更新读取器缓存，引用读取仍沿用原有身份注册。因此这里只承诺“不扩充节点标量值表”，不宣称整个校验完全没有内部缓存副作用。

完整图扫描、身份预检和错误传播均保留。差异消息上限不影响扫描范围。未改 CustomerCheckpoint、VisualState、几何恢复顺序、节点触发条件或存档逻辑。

## 验证

新增四项回归，CheckpointTests 从 76 项增至 **80 项**：

1. 强制反射和启用编译读取两种模式，单字段经历 1000 个新值及完整校验，值表/令牌计数保持不变，原值可恢复。
2. 与原字节校验路径逐项对照：float/double 正负零、不同 NaN、无穷、极小值、可空浮点均正确检测和恢复。
3. 枚举、decimal、可空整数及多个同时保留的节点反复往返，不登记校验期间的新值。
4. 差异消息达到 1 条后仍扫描全部 100 槽；最后一个槽的读取异常继续向外传播。

执行：

```powershell
dotnet build src/ProbablyStolenTools/ProbablyStolenTools.csproj -c Release
dotnet run -c Release --project tests/CheckpointTests/CheckpointTests.csproj -- --value-comparison-benchmark
dotnet run -c Release --project tests/ReleaseCompatibility/ReleaseCompatibility.csproj -- "E:\SteamLibrary\steamapps\common\Probably Stolen" src/ProbablyStolenTools/bin/Release/Mono/ProbablyStolenTools.dll release/manifest.xml
dotnet run -c Release --no-build --project tests/CheckpointTests/CheckpointTests.csproj -- --benchmark
```

结果：

- Mono Release：0 警告、0 错误。
- 80 项离线回归通过，包括既有物品布局、集合、闭包、RNG、历史节点和释放测试。
- 55 个挂点、73 个私有/快照字段通过；官方 scanner 能力仍为 files / reflection-external / dynamic-code，HardFlags 和 Error 为空。
- 实际程序集构造的 PlayerStore / StoreStation / GameItemElement 图往返通过；四个不同数组物品与字典变化恢复通过，布局缓存仍被排除。
- 测试使用独立托管对象和测试配置，不读取或修改玩家存档，不执行 Unity 活体回调。

## 隔离基准

十万个不同 double 的数组，捕获及首次校验不计时；同一代码的两种访问器包装，分别启用原字节比较路径和新比较路径。交替执行顺序，预热一轮，随后六轮每轮五次完整校验，报告每轮平均耗时的中位数。

| 路径 | 完整校验中位数 | 每次当前线程分配中位数 |
| --- | ---: | ---: |
| 原字节路径 | 15.84ms | 5,600,040 字节 |
| 新标量比较 | 5.01ms | 2,400,040 字节 |

减少的分配主要来自省掉浮点值查表用的键装箱；字段读取本身仍有装箱。这是 .NET 6 合成浮点数组，不是 Unity Mono 或实际物品图提速保证。此基准不改变字段值，因此没有把“旧路径遇到新值会增长”的额外代价计入。

另执行既有合成图基准：9,010 对象、176,001 槽，对象/槽覆盖一致。当前热读取路径捕获加完整校验中位数 65.2ms、当前线程分配 102.67MiB。没有同机同轮的旧提交对照，不能将此数字与历史基准直接相减作为本步整体收益。

## 交付边界

本步独立本地提交；不包含此前未提交的玩家附件分析或流程调研。未推送、未修改版本号、未部署、未启动或操作游戏，已安装 DLL 保持原状。

下一步建议为数组预算提前检查，独立提交。恢复前对象普查、描述压缩和任何分帧仍未实施。游戏内验收应先检查成交、移动/旋转、前柜台、跨顾客和多历史节点往返，确认没有贴图与碰撞位置分离。
