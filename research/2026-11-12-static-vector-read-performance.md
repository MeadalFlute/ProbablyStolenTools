# 恢复性能第二步：静态数组下标读取

日期：2026-11-12。版本保持 0.9.0，未推送。

## 第一轮实机反馈

上一轮固定物品布局字段访问优化提交为 `cd40aea`。
用户完成测试后，本地 PSTools.log 中 14:30 启动的会话记录了 7 次恢复，均为 diff=0、releaseOk=True，未出现新的快照失败或实体 schema fallback。

| 平均 ms | 布局修复前，6 次 | 布局修复后，7 次 |
|---|---:|---:|
| total | 2313.0 | 1259.6 |
| refresh | 424.0 | 89.7 |
| reconcile（包含 geometry） | 853.7 | 277.7 |
| geometry | 552.3 | 3.4 |
| census | 291.7 | 278.9 |
| preflight | 268.0 | 207.6 |
| write | 273.5 | 209.0 |
| finalVerify | 198.8 | 194.0 |

样本来自不同节点，非受控 A/B；总耗时约降低 45.5% 只能描述这两批记录，不能视为任意存档的保证。布局热路径已恢复到毫秒级，普通图的读取/比较仍明显慢于旧的运行时编译读取器。

## 本轮范围

只修改 STATIC_SNAPSHOT_LAYOUTS 分支的 SnapshotFieldReader：

- 为 SZ 引用数组及 byte/bool/int/float/double/long/uint/ulong/short/ushort/sbyte/char/decimal 数组选择编译时已有的泛型下标读取方法。
- 每个 metadata 读取器只选择一次；不保存数组实例或业务对象。
- 引用数组按协变 object[] 读取；仍保留每个元素的真实引用身份。
- 枚举、Nullable、结构体数组及非 SZ 数组继续原来的反射读取路径；嵌套字段逐级读取逻辑未改。
- 不新增运行时代码生成/加载、计算类型字段枚举、私有字段直接 IL 读写或 unsafe。
- 写回、字节预算、槽数量、原集合存储、完整普查、预检、owner/full 修复、最终全量校验均未修改。

本轮是常见数组的局部读取优化，不是把全部字段读取恢复成旧表达式实现，也没有宣称已解决所有性能回退。

## 离线验证

完整脚本 scripts/Test-StaticLayouts.ps1 通过，输出保留在仓库 out/static-vector-read-validation.log：

- Mono Release 构建 0 警告/0 错误；373 项参考测试、57 挂点/76 字段及原有原生图/实体夹具通过。
- 双 DLL 官方扫描仅 files，HardFlags/Error 为空，没有退役能力命中。
- 游戏 Mono 新增直接读取/比较测试：全部受支持基础类型、极值、正负零、不同 NaN payload、null、共享/协变/锯齿引用数组，及 Nullable/枚举/嵌套 KeyValuePair/非零下界数组回退。
- 普通反射访问与新数组访问分别捕获同一批数组，校验对象/槽/字节预算一致；三轮逐元素清空、两种后端各自恢复，共六轮完整还原，检查原始位模式及引用身份。
- 既有游戏 Mono 完整图六轮往返、字段表精确对照、可重复生成与错误 MVID 拒绝测试全部通过。

游戏自带 Mono 离线微基准（每项 262144 次读取，5 个样本取中位数）：

| 数组 | Array.GetValue ms | 新读取器 ms |
|---|---:|---:|
| byte[] | 31.16 | 10.54 |
| object[] | 24.93 | 1.97 |

该测试包括读取器调用和 byte 值装箱，不含 Unity 渲染或真实玩家对象图，不能据此推算整次回溯提速比例。没有添加生产基准或冗余日志。

## 后续验收

重新测试多次回溯，比较 census/preflight/write/reconcileDetail.scan/finalVerify/total；同时确认物品布局、顾客贴图、群拖及交易无回归。若总耗时仍偏高，继续按实测热点推进，不能通过删减校验或集合物理字段换速度。
