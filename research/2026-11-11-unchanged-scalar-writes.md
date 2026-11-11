# 普通标量相同值免写

## 依据与撤除的候选

19:27 实机会话 Census 中位数 127 ms，未证明上轮快路径提速；首次全图写回仍为 197-211 ms。继续保留全部对象覆盖、预算、预检与最终校验，不改原版存档机制。

先测量嵌套写回的局部递归函数改为静态递归函数。对照旧 DLL，两者直接写回分别为 36.98/36.92 ms，分配均为 6062080 B，没有可用收益。不能仅凭捕获局部变量就认定存在堆闭包分配；该候选已完整撤除，未部署。保留原递归路径和父层复制写回。

## 实现

- `Snapshot.SkipUnchangedScalars=true`，绑定到节点的 ManagedStateAccess。关闭并重启后，新节点使用无条件普通标量写入；原节点由其原访问器解释。五参数构造器的 directWrites=false 默认同时禁用免写，以保留递归对照。
- StateGraph 仍访问全部捕获槽位，全部普通标量阶段仍先于块标量、普通引用及块引用。不生成写委托，不使用游戏属性 setter，不重建集合，不依赖有限诊断来决定扫描范围。
- 每次普通 WriteValue 先解析原值令牌；仅捕获值为 null、CLR primitive、enum 或 decimal 时许可比较。使用原 Read 读取真实存储，并复用 ValueEquals 的标量比较。float/double 按位比较，不能把不同符号零或 NaN payload 当作相同。
- 完全相同时省略实际存储写入；不同时沿原 Write 路径逐层装箱、写叶子和复制父层/数组元素。引用保持原身份守卫，不改此轮引用流程。自定义可空结构体不增加 Equals 调用，直接走原无条件写入。
- 精确标签块和 int[] 块不受本轮免写影响，继续完整写回。Capture、Census、Differences、PrepareReconciliation 的完整扫描、计划上限、全图回退及最终验证保留。
- 新增读取异常不静默成功、不切回旧写法重试，沿现有恢复失败流程处理。读取是额外成本，改变非常密集时可能增加时间或分配，不能声称所有负载都改善。

## 诊断

Session 追加 `skipUnchangedScalars`；RESTORE OK 追加 `ordinaryScalarWrites(checked=N,skipped=M)`。

只计本次恢复期间许可比较的普通标量调用及实际免写调用，包含首次写回、owner 二次修复及发生时的最终全图回退；不计块字段和引用，不是不同槽位总数，不是整图大小。按目标访问器累计计数的前后差输出，重复恢复不累计到下一次摘要。没有逐槽日志。普查临时访问器释放不影响目标计数。

## 验证

- 正式 Mono Release 构建 0 警告 / 0 错误，版本保持 0.8.14。
- 226 项离线回归：新增七项覆盖相同/全变化框架标量、nullable 空/非空、枚举/decimal、浮点位、嵌套结构体数组、共享引用、GC 与过去/未来节点、自定义 nullable 不调用 Equals、读异常传播和旧节点保留、禁用开关与筛选根边界。
- 原生 57 个挂点 / 74 个字段、能力扫描 HardFlags 为空；现有真实 PlayerStore / StoreStation / GameItemElement、字典 entries/buckets 增长和递归写入对照、owner 修复/全图回退、精确标签与 int 块组合验证通过。
- 不测试或声明已弃用 Demo / IL2CPP 兼容，不启动游戏。

## 隔离基准

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet run -c Release --project tests/ReleaseCompatibility/ReleaseCompatibility.csproj -- `
  "<GAME_DIR>" `
  src/ProbablyStolenTools/bin/Release/Mono/ProbablyStolenTools.dll release/manifest.xml `
  --benchmark-scalar-writes
```

同进程交替开关，三轮预热、九轮取中位数；使用相同捕获后端（标签块及 int[] 块均开启），只改变 SkipUnchangedScalars。真实原生类型的未初始化业务夹具：53253 对象、349232 槽、2793856 逻辑字节；不是 Unity 游戏帧。

| 指标（关闭 / 开启） | 第一次 | 第二次 |
| --- | ---: | ---: |
| 写回 ms | 27.83 / 18.92 | 26.74 / 18.73 |
| 写回分配 B | 6062080 / 4976000 | 6062080 / 4976000 |
| 捕获分配 B | 48910264 / 48910264 | 48910264 / 48910264 |

写回改善约 32.0% / 30.0%；临时分配少 1086080 B（约 1.04 MiB）。每次检查 81040 个普通标量，其中 81024 次免写。该夹具字典底层多数不变，因此不能外推所有玩法，更不等于整个恢复同比例提速。捕获路径未改。

追加 `--dense-scalars` 禁用两侧标签块，改变全部标签的六个标量，保留同一对象/槽/字节覆盖：关闭/开启写回 36.26/30.60 ms（约快 15.6%），分配 6062080/7332992 B（反而多约 1.21 MiB）。检查 179248 次、免写 80944 次；此样本仍有不变字典槽，不代表所有标量均变化的最坏耗时保证。全变化正确性另有回归测试。

## 实机停止边界

标准脚本部署后停止，不继续改变其他后端：

1. 重启，建立几个顾客节点和一个手动节点，操作买卖、旋转、单件及批量移动。
2. 过去/未来/手动节点往返至少六次，并在恢复之后再做一次批量拖拽；确认贴图、碰撞、数量、金钱正常。方便时覆盖非默认议价状态。
3. 不需 Verbose。检查 Session 开关、ordinaryScalarWrites、write/reconcile/finalVerify/total、diff、releaseOk 及失败记录。免写命中不等于实机提速；优先同目标、相近规模比较。
4. 遇异常停止继续回溯并保留日志。此前“开业前”文件回溯卸载音频异常属于独立待排查项，本轮未改文件回溯或音频。

## 部署记录

2026-11-11 19:40 标准脚本部署完成，版本保持 0.8.14；备份为 `out/full-release-deploy-20261111-194024`。

DLL SHA256：`180BAFC3B025AE91688CE7DFB36D7B4A4AD849A64AEC0CC69D3CE4F51FC97247`。

78 个存档文件校验未变，SettingsImported=False，GameStarted=False。等待用户实机验证；本轮到此停止扩大运行时改动。

## 21:10 会话实机检查

检查本机 PSTools.log 的 2026-11-11 21:10:06 Session 及同次 Player.log，用户反馈已完成操作。

- Session.skipUnchangedScalars=True；共 5 次捕获。中途场景重载清空一次历史，两个上下文首节点分别 1092/1186 ms，后续普通/手动节点 362/445/364 ms。本轮未改捕获，不能将这些波动归因于免写。
- 5 次内存恢复，目标 id=2/4/5/3/2，含手动与过去/未来；全部 diff=0、releaseOk=True，无最终全图回退。采样节点两次 RESTORE MATCH。
- 每次普通标量检查 172407-189151 次，免写比例 99.831%-99.967%；不包括标签/int[] 块或引用，也不代表全图 99.8% 无变化。
- 首次写回 140/144/144/137/131 ms，中位数 140 ms。上次 19:27 会话中位数 207 ms，下降约 32.4%，但不是严格同节点开关 A/B。
- 整次恢复 737/702/709/704/660 ms，中位数 704 ms（上次 697 ms）；不能宣称整次恢复提速。Census 中位数 138 ms、预检 94 ms、reconcile 156 ms、finalVerify 58 ms，其他阶段波动抵消了写回改善。
- 二次修复均 owners/eligible，四个差异槽集中于 cassettePlayer.modifiedState.dict，一个 owner、十个计划槽；未覆盖非默认议价或实机全图回退。
- 8 对群组拖拽 START/END，最后两对发生在恢复之后（各 4 件），全部正常结束。没有本 Mod 捕获/恢复失败、异常几何诊断；日志不独立证明所有贴图、碰撞、数量和金钱正确。实际恢复五次，未达到计划六次。
- Player.log 的两次 StallWatchdog 位于 InventoryScene 加载阶段（7.8/6.7 s），没有本轮内存恢复期间的五秒卡顿。仍有其他 Mod 的 TryEquipItem 参数补丁失败及信任列表 TLS 请求失败；未见上次 AudioSource 空 source 异常，但本次也没有同样的“开业前”文件回溯记录，不能宣称该问题已修复。

结论：免写路径实际工作，样本中首次写回下降，整次恢复未证明加速；现有完整校验及恢复后群拖日志通过。保持当前部署，不继续扩大改动。
