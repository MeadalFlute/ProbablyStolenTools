# 普通引用直接身份比较

## 动机与边界

21:39 实机完整预检/二次扫描/最终校验仍占明显成本。普通引用原先每次 ReadReference 都通过 ManagedObjects.Id 查询 ConditionalWeakTable，再比较稳定 ID。完整成功图已经在访问器中强引用保留所有非空捕获目标，因此可以直接比较真实读取值与该节点保留对象的 ReferenceEquals；不是内容相等，不改变对象身份规则。

- 可选接口 IStateReferenceComparer 只用于 StateGraph 的普通引用完整差异扫描。未知后端继续 ReadReference/ID 路径。
- Snapshot.DirectReferenceComparisons=true 绑定到节点访问器，关闭并重启让新节点走原 ID 比较；保留节点不受配置切换影响。
- 仍调用原 Read，逐槽读取真实字段/数组元素或不可变 delegate anchor；支持原编译 reader 和纯反射路径，不增加编译或业务 getter/Equals/运算符调用。
- 捕获、Census、引用发现、来源、预算、全图 Validate、预检、诊断截断后的尾部扫描、owner/full 修复、全部写回、块比较和视觉保持不变。TagState 块的引用比较没有扩大到此路径。
- 新 live 引用在差异扫描中不再为比较而分配全局 ID，但仍被识别为变化；捕获与 Census 仍正常登记身份。目标引用由 _retained 的捕获 ID 解析，不使用变化后的 live 图寻找目标。
- 真实读错误/缺失身份仍传播，不吞异常、不自动重试。命中计数在成功读取并解析目标之后增加；Dispose 清零。
- Session 追加 directReferenceComparisons，CAPTURE 追加节点 referenceComparisons，RESTORE OK 追加本次增量（普通引用槽，不含块）；不逐槽计时或写日志。

## 验证

Mono Release 0 警告 / 0 错误，243 项离线回归；新增八项覆盖 null、同文字不同字符串、同值不同装箱、业务 Equals/hash/运算符不得调用、私有/readonly/嵌套/筛选根、纯反射、结构体数组不同索引、multicast anchor、扫描不登记新 live ID、诊断到上限后仍完整读取、owner/full 修复、字典增长替换存储、多个节点/GC/缓存清理、真实读取异常传播及释放统计。既有非比较接口后端仍通过旧路径回归。

原生 PlayerStore/StoreStation/GameItemElement 夹具显式关闭 oracle 的直接引用开关；开启侧命中、关闭侧为零，数组及字典变化的完整差异列表一致，多次写回和 owner/full 修复均由双路径验证。57 挂点 / 74 字段，官方扫描 HardFlags 为空，能力声明不变。

## 隔离基准

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet run -c Release --project tests/ReleaseCompatibility/ReleaseCompatibility.csproj -- `
  "E:\SteamLibrary\steamapps\common\Probably Stolen" `
  src/ProbablyStolenTools/bin/Release/Mono/ProbablyStolenTools.dll release/manifest.xml `
  --benchmark-reference-comparisons
```

原生类型 .NET 6 夹具，不是 Unity/Mono 帧；53253 对象、349232 槽、2793856 逻辑字节。两侧都开 Tag/int[] 块、普通免写和强类型标量比较，只切普通引用比较。三轮预热、九轮交替中位数。

| 关闭 / 开启 | 运行 1 | 运行 2 |
| --- | ---: | ---: |
| Capture ms | 30.08 / 29.71 | 27.71 / 28.23 |
| Verify ms | 7.98 / 5.39 | 7.32 / 5.01 |
| Restore ms | 19.61 / 18.96 | 18.01 / 19.10 |
| Capture 分配 B | 48910264 / 48910264 | 48910264 / 48910264 |
| Verify 分配 B | 112 / 112 | 112 / 112 |
| Restore 分配 B | 3031040 / 3031040 | 3031040 / 3031040 |

开启侧每次首次完整校验 82992 个普通引用比较，关闭侧为零。Verify 约快 32.5% / 31.6%；Capture/Restore 未改、无稳定收益，不归因于此优化；内存分配相同。旧基准入口显式关闭该新开关，保留其原比较条件。

## 实机停止边界

标准脚本部署后等待用户重启测试，不启动游戏：

1. 生成首节点、几个顾客节点和手动节点。
2. 买卖/旋转/单件及群组移动后，在过去/未来/手动节点间往返至少六次；至少在恢复后再次批量拖拽，核对贴图/碰撞、数量、金钱。方便时覆盖非默认议价。
3. 无需 Verbose；检查 referenceComparisons 命中、diff=0、releaseOk=True，以及 verify/preflight/reconcile scan/finalVerify/total。不以隔离比例推算整次恢复收益。
4. 异常时保留日志并停止回溯；不进一步开放集合重建或跨帧捕获。

## 部署记录

运行时代码独立提交 fcf4c74，2026-11-11 21:50 标准脚本部署，版本保持 0.8.14。

- 备份：`out/full-release-deploy-20261111-215050`。
- DLL SHA256：`23288AA846A888964EE7961DB1127727F351335C019C3E04365B81F91A71E590`，构建与部署一致。
- 78 个存档文件校验未变，SettingsImported=False，GameStarted=False；没有启动游戏。
- 待上述实机操作，不把离线零差异或约 32% 校验收益当作 Unity 验收结论。

## 21:54 实机结果

Session 21:54:43，directReferenceComparisons=True；4 次捕获（含一个手动节点）、6 次内存恢复，全部 diff=0/releaseOk=True、owners/eligible、finalFallback=False。采样节点 CAPTURE MATCH，恢复至该节点两次 RESTORE MATCH。恢复后的两对四件群拖正常结束；本次共九对群拖（其余七对在首次恢复前）。

捕获 id=1..4 的 captureMs=894/283/448/312，verify=74/46/57/57，referenceComparisons=206333/206615/226394/226661。首节点比较器编译 30 ms，已包含 verify。没有把首节点自动顾客与上轮手动首节点作严格对照。

| 恢复目标 | referenceComparisons | census | preflight | write | refresh | reconcile | finalVerify | total |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 618999 | 135 | 69 | 131 | 84 | 153 | 44 | 626 |
| 4 | 679983 | 122 | 65 | 153 | 84 | 159 | 48 | 635 |
| 3 | 679182 | 139 | 70 | 151 | 72 | 154 | 50 | 639 |
| 2 | 619845 | 174 | 64 | 125 | 80 | 152 | 39 | 641 |
| 1 | 618999 | 126 | 61 | 123 | 75 | 183 | 40 | 616 |
| 4 | 679983 | 120 | 64 | 133 | 78 | 131 | 73 | 603 |

中位数 preflight=64.5、finalVerify=46、total=630.5 ms；上轮分别 82/54/651 ms。引用直接比较确实启用，完整读取阶段有下降，但不是严格 A/B，不归因全部收益、不推算离线约 32% 为整次提速。每次二次修复仍为 cassettePlayer.modifiedState.dict 四个差异槽、完整十槽 owner。

本会话 PSTools 未见失败/异常；Player.log 仍为既有信任列表 TLS 和第三方 TryEquipItem 挂点错误，另一次初始 InventoryScene watchdog 约 7.7 秒后恢复，不归为快照回调卡顿。没有文件回溯/音频卸载路径，非默认议价与实机全图回退仍未覆盖。
