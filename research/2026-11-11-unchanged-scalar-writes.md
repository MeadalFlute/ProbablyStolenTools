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
  "E:\SteamLibrary\steamapps\common\Probably Stolen" `
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
