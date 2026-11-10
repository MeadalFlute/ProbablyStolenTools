# 保守快照优化：2026-10-31

用户授权优化快照代码降低延迟。基线为 main `48c2114`，上一轮 DS 的性能改动已回退。本轮保持捕获时机、遍历边界、主线程执行、完整捕获校验与恢复规则，未改存档、配置、第三方 Mod、通用兼容测试分支。

## 实现

`ManagedStateAccess` 每个实例缓存完整类型描述、数组元素字段模板和遍历判定，避免同类型对象重复做反射展开。缓存不跨快照；选定字段的根单独构建描述，委托目标仍逐实例处理。整个数组仍逐元素记录，包括集合 Count 之外的容量。

数组模板只保存相对字段路径；每个元素的槽位使用 `i * stride + j`，存储索引使用 i。已回退的 `943a02b` 将偏移固定在元素循环之外，是业务图骤减与缺物品的具体原因；共享错误读取器的捕获后校验不能独立识别此错误。

存储路径描述和 StateGraph.Entry 使用只读值类型，减少临时小对象。相同标量的令牌字节在同一访问器内只分配一次，保持字节只读与浮点位模式规则。没有剪枝、跳过校验、异步读取 Unity 或新增反射 getter。

CAPTURE 日志增加 access / walk / items / verify / total 各阶段毫秒数，便于实际定位剩余卡顿。items 阶段包含物品与相关视图处理；walk 是完整 StateGraph 捕获；verify 是完整 Differences 校验。total 还含节点入列等开销。

## 离线验证

```powershell
dotnet build src/ProbablyStolenTools/ProbablyStolenTools.csproj -c Release
dotnet run -c Release --project tests/CheckpointTests/CheckpointTests.csproj -- "%LOCALAPPDATA%Low\Questing Goose Studio\Probably Stolen\save_0.es3" --benchmark
dotnet run -c Release --project tests/ReleaseCompatibility/ReleaseCompatibility.csproj -- "<GAME_DIR>" src/ProbablyStolenTools/bin/Release/Mono/ProbablyStolenTools.dll release/manifest.xml
```

- Mono Release：0 警告、0 错误。
- 59 项测试通过，真实 ES3 仅读取，未写入。
- 新增冻结旧后端作独立对照，逐一比较槽位元数据、值、引用、对象集合与来源路径。多元素数组的不同对象全部必须被记录和还原，不能仅断言 diff=0。
- 回归覆盖 0/1/2/9/137 元素、嵌套结构体、List/稀疏 Dictionary、继承私有 readonly 字段、不同闭包、选定根隔离、多历史节点往返、可空/基本类型/交错数组及原有非法数组拒绝行为。
- 53 个 Harmony 目标、68 个字段通过；实际官方扫描器没有 HardFlags 或 Error，权限仍为 files / reflection-external。
- 配置磁盘往返、实际游戏程序集的 PlayerStore / StoreStation / GameItemElement 数据图恢复通过。后者额外验证四个不同数组物品及多项字典的修改/删除/新增后还原，不调用 Unity 活体回调。

## 基准

合成图 9,010 对象、176,001 槽。新旧后端交替顺序运行，各一次预热后取五次中位数，每次重新创建访问器，不依赖跨快照缓存。两个后端使用相同当前 StateGraph，计时包括捕获与完整校验，不含图释放及游戏物品视图处理。

| 后端 | 捕获加校验中位数 | 当前线程分配 |
| --- | ---: | ---: |
| 冻结旧访问器 | 222.8ms | 158.32MiB |
| 优化访问器 | 114.3ms | 111.91MiB |

此轮约减少 49% 时间、29% 分配，但只是 .NET 6 合成图；不等于游戏 Mono 的帧停顿或完整节点内存。Entry 值类型相对旧类实现的独立收益未在此基准计入。实际游戏提速仍待用户验收。

## 游戏验收

自然招待两三位顾客，观察新节点建立时停顿。交易、移动物品、议价后 F6 或双击旧节点，并在已有后续节点间往返，检查现金、物品数量/位置/可用性、议价及贴图。尤其检查是否出现可重叠、不能交互的幽灵物品。

出现异常时保留 PSTools.log 和 Player.log。比较 CAPTURE 的阶段时间与 objects / slots / items / renderedItems；状态不同的节点数量不必完全相等，不能把无故数量暴跌认作优化。常规验收无需 F7。

不自动启动游戏，不改权限信任列表、现有配置或存档。

## 部署记录

已通过 `scripts/Deploy-FullRelease.ps1` 部署至正式版的 `Mods\ProbablyStolenTools`。备份目录为 `out\full-release-deploy-20261031-134640`，含 SaveData、原 Mods 与 deployment.json。

DLL SHA256：`3CC042F1E4B20AE6D13C89BF013E42BE80F16D85C66342C0BAB69D6F06A7ED33`。部署文件与构建产物一致；原存档目录 229 个文件、游戏 Managed 和其他 Mod 文件逐一校验不变，SettingsImported=False，GameStarted=False。未提交或发布工坊更新，游戏实测仍待用户执行。

## 第二轮：视图收集、读取委托与重复遍历

第一轮用户测试无幽灵物品，但仍明显卡顿。实际最新两次 CAPTURE：

| 阶段 | 顾客 1 | 顾客 2 |
| --- | ---: | ---: |
| walk | 397ms | 300ms |
| items | 89ms | 84ms |
| verify | 148ms | 157ms |
| total | 637ms | 541ms |

对象 46,379 / 46,776，槽位 262,639 / 266,614，物品 436 / 439，渲染物品 435 / 438。旧 922ms 日志未启用完整校验，不能据此推断同条件优化比例。

用户授权顺序继续优化，暂不缓解卡顿观感。实际改动：

- 视图从线性扫描判重改为临时 ID 集合判重，保留首次出现顺序、原有 VisualState 与全部视图。对象分类每个 ID 只解析一次。
- 生成缓存的字段路径读取委托，数组通过传入索引读取。只访问字段、不调用 getter；写入保持原实现。代码缓存不捕获业务实例、快照或数组索引，累计 32 次使用才生成，每条路径只尝试一次，不支持时回退原反射，上限 4096 条，Shutdown 清理。
- 队列在入队前判重，每个对象只入队一次，每个引用槽照常保存。显式根先注册，保持根路径优先和旧 BFS 的首次来源。
- 令牌字节直接按值查回；完整校验仍逐槽读取，同一不可变字节实例无需再次逐字节比较。
- 不改捕获时机、UI、时间比例、线程、跨场景边界或恢复流程。

生成委托需要实际 `dynamic-code` 权限，manifest 已增加。官方扫描器实测检测到该能力，无拒绝/HardFlags/Error。用户在游戏内正常确认权限，部署不代改信任记录。

日志新增 `readers(compiled=...,fallback=...,cached=...)`，分别统计当前代码缓存可用委托、尝试生成但不可用的路径和全部缓存路径。冷路径尚未尝试生成不算 fallback；总耗时包括惰性编译，因此应同时观察首次节点和后续节点。

第二轮最终验证：61 项测试通过；Mono Release 0 警告/0 错误；53 个目标和 68 个字段通过，官方扫描器声明匹配。真实游戏数据测试在修改前反复完整校验，实际生成 326 条字段路径，再验证四个不同物品数组元素与字典往返。离线验证使用 .NET 6，尚未替代 Unity Mono 实测。

同轮最终合成基准如下，交替顺序、各次重新建立快照，五次中位数。冷代码缓存每次清空；热代码缓存在计时外先捕获一次。所有模式使用同一当前 StateGraph，因此只能隔离后端读取收益，不能据此量化队列/视图优化相对部署版的总收益。

| 模式 | 捕获加完整校验 | 当前线程分配 |
| --- | ---: | ---: |
| 冻结旧后端 | 242.3ms | 157.06MiB |
| 当前缓存后端，强制反射 | 114.5ms | 111.18MiB |
| 读取委托，冷代码缓存 | 84.8ms | 102.76MiB |
| 读取委托，热代码缓存 | 89.3ms | 102.67MiB |

约少 22–26% 时间、约 8% 分配（相对同轮强制反射）。冷/热结果有噪声，不保证热态更快，也不承诺游戏内同等收益。生成代码本身及运行时保留的内存不等于线程分配计数。

额外检查旧 Demo 构建失败，10 个错误在本轮未改动的 DoubleClickInspectionPatch / Panel / Rewind：IL2CPP 集合与托管集合不兼容、GetLayerRoot API 缺失、Action 转换不兼容。未修复或部署 Demo；正式版验证不受此旧目标检查结果替代。

第二轮已部署正式版，备份 `out\full-release-deploy-20261031-140156`；DLL SHA256：`E61742FA25C1B854DB1240C9662AE382CE14A8F99786F98222A043D2D8EEF6AA`。脚本校验原存档目录 230 个文件、游戏 Managed 及其他 Mod 不变，SettingsImported=False，GameStarted=False；未修改信任/启用记录，未提交或发布工坊更新。

用户验收仍为按原生流程确认新增动态代码权限后，自然接待两三位顾客，比较首次/后续节点卡顿，交易、物品移动和议价后往返恢复，重点确认没有幽灵物品及读取异常。

## 用户反馈与收尾

第二轮部署后，用户游戏内反馈：“表现效果良好，虽然还是有一小段卡顿，但在接受范围内”。本轮性能目标据此收尾，当前实现作为后续维护基线；不追加分帧、线程或观感改动。

未收到新的分项计时或逐项回溯验收结果，不据此宣称所有物品、Mod 组合或场景均已通过验证。本次收尾只更新文档，没有重新构建、部署或修改游戏文件。
