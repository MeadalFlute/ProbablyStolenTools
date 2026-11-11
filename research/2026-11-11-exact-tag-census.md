# 精确标签只读普查

## 本轮选择过程

批量拖拽修复已由用户确认可用；恢复主要成本仍为首次全图写回、Census、预检及最终完整读取。没有依据删除完整校验、减少图覆盖或重建集合。

本轮先实现并离线测量两个候选，均已从源码、测试及配置中完全撤除，没有部署：

1. 完整引用数组载荷：完整 Clone + 身份令牌、原数组 Array.Copy 写回，含原生 GameItemElement[] / List 容量、别名、枚举器与 GC 对照。引用丰富夹具 56326 对象 / 419889 槽 / 1026 引用数组块，九轮交替样本两次测量：普通/块捕获 33.28/31.95、34.53/31.93 ms；校验 12.35/8.42、12.48/8.40 ms；写回 29.55/33.00、29.75/32.19 ms；捕获分配约 49.23/50.41 MB。校验改善但写回退化且分配略增，不部署。普通标签夹具仅 1024 个引用槽，整体收益也很小。
2. 连续值令牌索引表：令牌字节、去重、比较、恢复不变，只改 token -> 值查询，含五项专门回归及原生旧字典对照。65 个令牌夹具几乎无捕获/写回收益；32812 个令牌夹具两次后续测量，普通/索引捕获 30.33/34.40、30.74/34.91 ms，写回 33.64/33.28、33.73/34.26 ms，分配约少 0.91 MB。校验有时明显改善、有时接近，捕获有退化，不部署。

以上 .NET 6 隔离夹具不是 Unity 的真实帧耗时；失败候选只保留这份结果，不留下未启用的运行时原型。当前正式改动只有下述只读普查。

## 实现边界

- StateGraph 增加可选 IStateCensusValueProbe：仅 Census 查询，八字节逻辑标量令牌访问器才可许可；返回 true 必须已消费对象描述的全部标量。不为 Capture / Differences / Restore 增加快捷判断。
- ManagedStateAccess 仅在安装 CensusValuesProbe 时尝试；筛选根直接回退，未支持类型继续逐槽 ProbeValue。计数 CensusValueObjectCount 只统计实际成功对象，释放时清零。
- 原生适配复用 SemanticTagBlock 的精确类型和完整九字段描述校验，子类、描述缺失/不符、原生字段布局不符均不进入快路径。每个访问器独立描述校验，不复用 live 值。
- 一次直接读取 valueEnabled / internalValueString（原生 valueString getter）/ valueInt / valueFloat / valueLong / valueDouble / valueBool / identifier / identifierName，构造临时 SemanticTagValue 并 GC.KeepAlive 消费，确保读取结果有使用点；临时装箱不保留到 Census，也不建立恢复块。精确类型已验证，无须重复执行 Shadow 的继承链检查。
- StateGraph 对成功对象的六个标量逐槽计原八字节预算，但不再逐槽调用通用读取器。三个引用仍在原位置通过 ReadReference 读取、发现、入队、计预算，引用读取没有省略；它们在临时值中也被读过。
- 没有改对象/槽/字节预算、发现顺序、来源路径、数组容量、第三方 opaque 政策、捕获载荷、恢复写回、预检和完整最终校验；不修改或调用原版存档接口。
- 读异常照常传播；失败不静默重试或变成成功；旧节点不受影响。

## 开关与诊断

`Snapshot.SemanticTagCensus=true` 独立于 SemanticTagBlocks；仅在恢复前创建 Census 访问器时安装。

Session 追加 semanticTagCensus；RESTORE 原 census 摘要追加 `exactTagProbes=N`，以实际命中确认生效。没有逐标签日志，不改 CAPTURE 耗时定义。

关闭开关并重启可回到旧普查路径，无需改变节点载荷；部署不预改 settings。

## 验证

- 正式 Mono Release 构建 0 警告 / 0 错误；版本维持 0.8.14。
- 219 项离线测试通过，新增六项覆盖身份/别名/循环/来源/预算一致、Capture 不调用、子类/筛选根/不匹配回退、精确槽/字节/对象预算、晚期读取异常和部分 Census 释放、旧节点仍可恢复及无探测器旧行为。
- 真实 TagState / ItemStateModifier / GameGridInventory 夹具：普通/直接 Census 对象/槽/字节/身份/来源完全一致；精确标签与独立标签共两次命中，子类不命中；描述不符和筛选根回退；无值令牌、捕获 Entry 或块。现有标签/桶数组、owner/full 回退及递归对照仍通过。
- 57 个 Harmony 挂点、74 个快照/私有字段；HardFlags 为空，能力声明不变。弃用 Demo / IL2CPP 未重建，不能据此宣称通过。

## 原生类型隔离基准

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet run -c Release --project tests/ReleaseCompatibility/ReleaseCompatibility.csproj -- `
  "E:\SteamLibrary\steamapps\common\Probably Stolen" `
  src/ProbablyStolenTools/bin/Release/Mono/ProbablyStolenTools.dll release/manifest.xml --benchmark-census-tags
```

原生程序集的未初始化业务对象，不启动 Unity；53253 对象、349232 槽、2793856 逻辑字节。精确标签 16368 次命中，另外 16 个子类走旧字段路径。三轮预热、交替次序九轮取中位数，两种模式使用相同捕获载荷；直接变化仅 Census 的 scalar probe。

| 指标（普通 / 直接） | 运行 1 | 运行 2 |
| --- | ---: | ---: |
| Census ms | 32.73 / 30.70 | 37.03 / 33.93 |
| Census 分配 B | 23495184 / 22319816 | 23495184 / 22319816 |

普查阶段改善约 6.2% / 8.4%，分配少 1175368 B（约 1.12 MiB / 5.0%）。不是整个恢复的改善比例，也不证明 Unity 收益。夹具不含 Unity 视觉、完整玩家存档、其他 Mod 或帧调度；首次捕获、首次完整写回及最终校验本轮没有优化。

## 实机停止边界

标准脚本部署后停止，等待用户：

1. 重启，生成数个顾客节点及一个手动节点，进行物品买卖、旋转/移动、批量移动。
2. 在过去/未来/手动节点之间往返至少 6 次，确认贴图、碰撞、数量、金钱和批量移动仍正常；方便时增加非默认议价状态。
3. 不需 Verbose。检查 Session.semanticTagCensus、RESTORE.census.exactTagProbes、census/preflight/write/finalVerify/total 和最终 diff；不能只用总时间波动判定普查收益。
4. 若有异常停止继续回溯并保留日志。本轮不继续扩大到结构体/引用数组载荷、集合重建或分帧捕获。

## 部署记录

2026-11-11 19:10 标准脚本部署完成，版本保持 0.8.14；219 项离线测试及原生兼容检查再次通过。

- 备份：`out/full-release-deploy-20261111-191036`。
- 部署 DLL SHA256：`8D871871D61EFC9EBA5D581BC314A88C093CBFB80FCDFCB7CCD787AD136253A8`。
- 校验 78 个存档文件未变；SettingsImported=False，GameStarted=False。
- 待用户实机操作；本轮不据离线基准宣称真实游戏提速或 UI 验收通过。

## 19:27 会话实机日志检查

用户反馈已完成操作。检查本机 PSTools.log 的 2026-11-11 19:27:37 Session 至 19:29:31，以及同次 Player.log。

- Session.semanticTagCensus=True；5 次捕获（4 个顾客、1 个手动），首节点 927 ms，其余 344/449/308/292 ms。捕获路径未改，不将其波动归因于本轮。
- 实际记录 3 次内存恢复，目标 id=2/4/5（含手动），均 diff=0、releaseOk=True；exactTagProbes=16779/16212/16782，确认原生精确标签快路径实际命中。
- Census=127/116/141 ms，中位数 127 ms；整次恢复=696/697/753 ms，中位数 697 ms。首次写回=197/207/211 ms；预检中位数 88 ms，最终校验中位数 61 ms。
- 对照 18:31 会话六次恢复：Census 中位数 127.5 ms，整次恢复中位数 732.5 ms。样本、图和目标不完全相同，没有开关 A/B；Census 实机未见显著改善，不能将总时间差归因于快路径，也不能直接证实隔离基准的 6-8%。
- 三次二次修复均 owners/eligible，4 个差异槽、一个字典 owner、10 个计划槽，全部来自 cassettePlayer.modifiedState.dict；无最终全图回退。无本 Mod 捕获/内存恢复失败或异常几何诊断。
- 记录 3 对群组拖拽 START/END（2/4/4 件），全部在本次首次恢复前。没有恢复后的群组拖拽记录，不能据本会话验证该边界。也未采样恢复 id=1，因此只有 CAPTURE MATCH，没有新增 RESTORE MATCH。
- 此次内存恢复只有 3 次，未覆盖原计划至少 6 次往返、非默认议价或实机全图回退；日志也不能独立证明画面/碰撞/数量/金钱正确。

### 文件回溯另记

19:29:19 用户随后触发“开业前”文件回溯。Player.log 在卸载场景期间记录 NegotiationTablet sibling 警告和一次 ArgumentNullException(source)：AudioSource.Play -> EmporiumAudioPlayer.PlayUnMatched -> NegociationUIManager.CloseUI -> StoreUIManager.CloseUI -> StoreUI.OnDisable。之后菜单加载、存档加载和 post-load 收尾完成，19:29:31 回到 state=2。

调用栈位于原生场景卸载关闭 UI 的音频路径；目前无法证明由本轮只读 Census 引起，也不能断言与 Mod 文件回溯无关。不吞异常、不擅自增加原版音频补丁，作为独立待排查问题保留。

两次 StallWatchdog 均为 InventoryScene 加载阶段（初次载入 7.6 s、文件回溯再载入 6.5 s），不是本次 696-753 ms 的内存恢复回调。另有其他 Mod 的 TryEquipItem 参数不匹配和信任列表 TLS 请求失败，不作为本 Mod 快照失败。

本轮结论：路径已命中，现有样本内存恢复校验通过，实机提速未获证明；保留完整图覆盖、预算和最终校验，不据此扩大语义后端范围。
