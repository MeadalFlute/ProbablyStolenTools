# 恢复性能第三步：缓存标量比较准入

日期：2026-11-12。版本保持 0.9.0，未推送。

## 上一轮实机结果

用户完成数组读取候选 `4480418` 测试后，15:17 会话有三次 RESTORE OK，均 diff=0、releaseOk=True；未见快照失败、退役能力错误或实体 schema fallback。

| 平均 ms | 布局优化版本，7 次 | 数组读取版本，3 次 |
|---|---:|---:|
| total | 1259.6 | 1311.7 |
| census | 278.9 | 305.3 |
| preflight | 207.6 | 212.0 |
| write | 209.0 | 214.7 |
| refresh | 89.7 | 76.7 |
| reconcile | 277.7 | 312.0 |
| finalVerify | 194.0 | 187.3 |
| geometry | 3.4 | 3.7 |

新样本为 1274/1361/1300 ms，对象图来自不同节点。三次恢复涉及约 10.9–11.6 万普查对象、60.7–67.8 万槽，不能与前一轮作严格 A/B。**目前没有证据表明数组优化显著降低了整次回溯耗时**，不得用上一轮数组微基准替代这个结论。

布局修复的 geometry 毫秒级效果仍保持。普通字段反射读取/比较仍是后续关注方向；不能宣称已恢复旧表达式读取器的性能。

## 本轮变更

静态 SnapshotFieldReader 原先每次 TryEquals 都检查字段类型是否为 primitive/enum/decimal，并排除 IntPtr/UIntPtr。读取器构造后字段路径和类型不再变化，因此将完全相同的准入条件预计算为 readonly bool。

- Nullable 仍按其底层类型判定，null 语义不变。
- 引用、指针和自定义结构体仍拒绝类型化比较，不调用自定义 Equals。
- float/double 仍逐位比较，区分正负零及不同 NaN payload。
- 读取、写回、预算、完整普查、预检、最终全量校验均不变。
- 不添加运行时代码生成或新的反射入口，不添加生产基准/冗余日志。

这是一个小范围元数据缓存优化，不是完整反射读取替代。

## 验证与基准

scripts/Test-StaticLayouts.ps1 全套通过，输出为仓库 out/static-scalar-gate-validation.log：

- 正式 Mono Release 构建 0 警告/0 错误；373 项参考测试、57 挂点/76 字段、原有原生实体/图夹具通过。
- 双 DLL 官方扫描仅 files，HardFlags/Error 为空，没有退役能力。
- 游戏 Mono 新增 scalar gate 测试：primitive/enum/decimal/Nullable 相同/不同值、null 双向转换、浮点位模式；引用/IntPtr/UIntPtr/自定义结构体仍拒绝，其中自定义 Equals 一旦被调用即抛错。
- 数组直接读取/回退、两种访问方式的预算一致及六轮变更恢复、完整图六轮往返、字段表对照、可重复构建和错误 MVID 拒绝继续通过。

先用同一份新增夹具运行已部署的上一轮 DLL，再构建候选。另做三组新旧 DLL 交替进程测量：每种字段每进程 262144 次比较、5 个样本取中位数，下表再取三个进程中位数的中位数。

| 字段 | 旧 DLL ms | 新 DLL ms |
|---|---:|---:|
| int | 53.91 | 49.78 |
| float | 48.83 | 44.84 |
| Nullable int | 49.94 | 45.48 |

日志位于 out/static-scalar-gate-before-{1,2,3}.log、out/static-scalar-gate-after-{1,2,3}.log，首次旧版对照为 out/static-scalar-gate-before.log。

这是游戏自带 Mono 的离线单字段微基准，不含 Unity 渲染，也不代表真实对象图各路径占比。单个样本存在波动，约 8% 的局部差异不等于整次恢复提升 8%。本轮收益预期有限。

## 后续

新候选的整次实机性能仍待确认。对比恢复总耗时及 census/preflight/write/reconcileDetail.scan/finalVerify，不用降低校验强度或删除物理集合字段掩盖剩余回退。
