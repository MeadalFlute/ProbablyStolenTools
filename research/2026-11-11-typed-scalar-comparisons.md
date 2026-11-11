# 普通标量比较减装箱

## 定位

21:10 实机会话普通标量免写生效，但整次恢复中位数仍约 704 ms。完整预检、二次差异扫描、最终校验及免写判断都会读取普通标量；原读取委托返回 object，因此仍装箱。本轮只减少这些比较中的临时装箱，不扩大业务语义覆盖、不减少读取字段、不修改捕获载荷、Census、恢复顺序或视觉处理。

版本保持 0.8.14，不修改游戏 DLL、其他 Mod、加载器或原版存档。

## 实现边界

- `Snapshot.TypedScalarComparisons=true` 绑定到每份访问器。关闭并重启后新节点使用旧装箱比较；已捕获节点保持自身访问器配置，缓存里存在比较器也不能绕过配置禁用。
- SnapshotFieldReader 在同一元数据路径累计比较 32 次后尝试生成 `Func<object,int,object,bool>`，与原读取委托的 32 次读取阈值独立。不在每次槽读取打 Stopwatch，只在实际编译时计时。
- 只许可 CLR primitive（不含 IntPtr/UIntPtr）、enum、decimal 及它们的 Nullable。表达式按原 FieldInfo 路径/数组索引读真实存储一次，expected 由调用参数传入，不捕获业务对象、节点、数组、令牌或 live 值。
- float/double 用 BitConverter 的位模式比较，保留符号零与 NaN payload 差异；可空值先比较 HasValue，再比较非空值。decimal 保留原 Equals 的数值语义，不扩大到物理位相等。
- 自定义 nullable 结构体、未知路径、无 reader（反射模式或缓存已满）、不受支持的代码生成继续原装箱比较。生成/委托不可用异常沿现有 reader 的窄异常类别回退，不重复编译；真实读错误（越界、空 owner 等）继续传播，不能视为匹配或自动重试恢复。
- ValueEquals 和已许可的普通标量免写判断共用该路径。完整逐槽扫描、有限诊断之外的尾部扫描、值令牌表不新增值、身份/来源/预算、owner 计划和全图回退均不变。标签/int[] 块继续原比较与写回，引用不走标量比较器。
- 比较器依附原有最多 4096 条元数据缓存，每条最多一个，ClearCache 清空查找缓存与编译统计；存活节点引用的旧 reader 仍有效，缓存不绑定节点值。

## 日志

- Session 追加 typedScalarComparisons。
- CAPTURE 追加 typedComparisons（该节点已成功走强类型比较的调用数）、verifyCompareCompileMs（本次捕获 verify 中的比较器编译时间）。
- RESTORE OK 追加 typedComparisons（该次操作内目标访问器成功的比较调用数，含预检/写回判断/二次扫描/最终校验）、compareCompileMs（本次操作内比较器编译时间）。
- readers 摘要追加 comparers、compareCompileAttemptsTotal、compareCompileMsTotal。与原读取委托统计分开；walkReaderCompileMs 仍只计 walk 中旧读取委托的编译时间。
- 编译时间是已有阶段耗时的子集，不能再次相加；命中数不是不同字段数，不代表减少图覆盖。没有逐字段日志。

## 验证

- 正式 Mono Release：0 警告 / 0 错误；235 项离线测试通过，新增九项。
- 新回归覆盖阈值和统计独立性、全部基础整数/布尔/字符/枚举/decimal/nullable、数组不同索引、可空浮点位模式、私有/readonly/嵌套字段、过去/未来节点和 GC、期望值不绑定节点、custom nullable 回退、真实读异常传播、动态委托不可用回退且不重编译、禁用/纯反射模式、诊断达到上限仍完整扫描、有界缓存及旧 reader 存活。
- 原生 PlayerStore/StoreStation/GameItemElement 夹具增加强类型/显式关闭强类型的装箱 oracle 完整差异列表对照，实际命中及数组/字典变化后恢复一致。既有标签/int[] 块、owner/full 修复、完整递归 oracle 仍通过。
- 57 个 Harmony 挂点 / 74 个字段，官方扫描 HardFlags 为空，既有 files/reflection-external/dynamic-code 能力声明不变。新增编译和 BitConverter 方法元数据查询由现有能力覆盖。不声明已弃用 Demo/IL2CPP 构建通过。

## 原生类型隔离基准

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet run -c Release --project tests/ReleaseCompatibility/ReleaseCompatibility.csproj -- `
  "E:\SteamLibrary\steamapps\common\Probably Stolen" `
  src/ProbablyStolenTools/bin/Release/Mono/ProbablyStolenTools.dll release/manifest.xml `
  --benchmark-typed-comparisons
```

同进程开关交替，三轮预热、九轮中位数；两侧都开标签/int[] 块及普通标量免写。原生未初始化业务类型夹具：53253 对象、349232 槽、2793856 逻辑字节。每轮约 162080 次比较命中（计至写回结束，不含随后最终基准校验），关闭侧为零。不是 Unity/Mono 活体帧。

| 指标（关闭 / 开启） | 最后两轮运行 1 | 运行 2 |
| --- | ---: | ---: |
| Capture ms | 27.49 / 27.62 | 28.07 / 28.51 |
| Verify ms | 7.64 / 7.13 | 7.85 / 6.72 |
| Restore ms | 19.37 / 18.80 | 18.76 / 18.80 |
| Capture 分配 B | 48910264 / 48910264 | 48910264 / 48910264 |
| Verify 分配 B | 1945072 / 112 | 1945072 / 112 |
| Restore 分配 B | 4976000 / 3031040 | 4976000 / 3031040 |

常规校验改善约 6.7% / 14.4%；写回约快 2.9% / 基本持平，没有稳定写回提速。校验和写回各少分配 1944960 B（约 1.85 MiB），不等于节点持久内存减少。Capture walk 未改，计时波动不视为捕获改善。

追加 `--cold-comparisons` 每个样本清空 reader 缓存。关闭/开启 Capture=29.25/29.46、Verify=9.03/8.80、Restore=19.05/19.11 ms；最后一次开启侧 Verify 编译约 1.01 ms，已包含在 Verify。Verify 分配=2003344/117984 B，Restore 分配=4976000/3099632 B。冷样本中首校验和写回基本接近；该夹具不能证明完整玩家图的首次编译成本可忽略，实机须单看 verifyCompareCompileMs 与首节点总时长。

## 实机停止边界

标准部署后停止，等待用户：

1. 重启，保留首节点日志，再生成几个顾客节点和一个手动节点。
2. 买卖、旋转、单件/批量移动后，在过去/未来/手动节点之间往返至少六次；恢复后再次批量拖拽，检查贴图、碰撞、数量、金钱。方便时增加非默认议价。
3. 不需 Verbose。检查 typedComparisons、comparers、verifyCompareCompileMs/compareCompileMs 及 verify/preflight/write/reconcile/finalVerify/total、diff/releaseOk 和失败日志。
4. 若异常停止继续回溯，保留日志。不得用离线校验改善比例推算整次恢复收益，也不据此开放集合重建或跨帧捕获。

## 部署记录

2026-11-11 21:31 标准脚本部署完成，版本保持 0.8.14。备份：`out/full-release-deploy-20261111-213121`。

DLL SHA256：`FBD9242BC9EE679C094D85543CEED7FEF8A132ED49A10AF0EFF6123305382D38`。

78 个存档文件校验未变，SettingsImported=False，GameStarted=False。独立提交本轮代码与回归后停止，等待用户实机；不继续扩大到其他后端。

## 21:39 实机检查

运行时代码 3c6693e，Session 21:39:07，所有已部署优化开关开启。7 次捕获（含三个手动节点）、5 次内存恢复；全部最终 diff=0、releaseOk=True、owners/eligible、finalFallback=False。首节点 CAPTURE MATCH，恢复至该节点 RESTORE MATCH（独立语义日志 verifyMs=3）；五次二次差异均为 cassettePlayer.modifiedState.dict 的 buckets/entries/count/version 四槽，完整 owner 修复十槽。

| 捕获节点 | captureMs | walk | verify | verifyCompareCompileMs | typedComparisons |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1 手动 | 1006 | 660 | 99 | 36 | 161878 |
| 2 waterStockpiler | 316 | 217 | 62 | 2 | 170041 |
| 3 clinicNurse | 344 | 249 | 66 | 12 | 171523 |
| 4 retired_farmer | 412 | 278 | 67 | 2 | 187856 |
| 5 手动 | 311 | 220 | 62 | 2 | 188268 |
| 6 retired_winemaker | 305 | 221 | 55 | 3 | 184926 |
| 7 手动 | 305 | 219 | 58 | 7 | 185345 |

首节点比较器编译实际 36 ms，明显高于隔离夹具的约 1 ms，已包含 verify=99，不另加到总耗时。至节点七累计 426 个比较器、编译总计 67 ms；不能宣称启动成本可忽略。

| 恢复节点 | typedComparisons | compareCompileMs | Census | preflight | write | refresh | reconcile | finalVerify | total |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 684660 | 4 | 132 | 85 | 138 | 65 | 148 | 54 | 634 |
| 2 | 688476 | 0 | 169 | 75 | 169 | 92 | 161 | 49 | 721 |
| 4 | 754461 | 4 | 122 | 82 | 144 | 85 | 147 | 55 | 638 |
| 5 | 755592 | 0 | 131 | 80 | 199 | 71 | 173 | 55 | 713 |
| 7 | 742480 | 0 | 128 | 108 | 144 | 71 | 144 | 51 | 651 |

恢复中位数：Census=131、preflight=82、write=144、refresh=71、reconcile=148、finalVerify=54、total=651 ms。21:10 对应 total=704、preflight=94、finalVerify=58；这是不同图/节点的非 A/B 样本，仅说明实际命中且未见恢复差异，不把约 7.5% 的总耗时变化全归因于此开关。

八对正常结束的原生群拖均发生在首次恢复前；本轮没有恢复后群拖日志。恢复仅五次，未达到原建议六次；非默认议价及实机全图回退仍未覆盖。PSTools 未见捕获/恢复失败；Player.log 中有既有信任列表 TLS、其他 Mod TryEquipItem 挂点报错，以及初始化 InventoryScene 的一次 watchdog（约 8.1 秒后恢复），不将其归为快照回调卡顿。本轮没有文件回溯，不据此关闭旧音频卸载异常记录。日志零差异不能替代实际贴图/碰撞检查。
