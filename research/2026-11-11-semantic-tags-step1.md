# 标签语义后端第一步：只读对照

日期：2026-11-11。版本保持 0.8.14；本轮独立提交，不推送。

本文记录首版 `442354e`。随后实机发现原版 TagSystem 子类会被过严检查拒绝，已改为原版子类的基类字段投影，见 `2026-11-11-semantic-tags-live-check.md`；以下首版限制及基准保留作历史依据。

## 决策与范围

用户授权尝试语义化后端。先对 TagSystem/TagState 建立紧凑只读表示，旧 StateGraph 捕获、恢复和完整校验保持不变。它是后续后端的验证步骤，不是已切换的恢复后端；不会让本轮实机快照变快，采样节点反而增加少量工作。

依据前轮本机日志，捕获主要成本在 walk 和 verify。语义表示的目的在于减少需要逐字段遍历的集合实现细节，而不只是加快单次反射。当前没有可用于全局提速预测的 Unity Mono 对照测量。

## 与原版保存的区别

安装版 TagSystem 的唯一实例字段为 dict；TagState 有九个实例字段：

- valueEnabled、internalValueString、valueInt、valueFloat、valueLong、valueDouble、valueBool。
- identifier 和 identifierName 的自动属性后备字段。

只直接读取这些数据；valueString getter 不会改值，保留 null 与空字符串区别。float/double 存位模式，区分负零与 NaN 载荷。对类型和字段集合执行白名单核验，拒绝子类或新增字段。

原版 SaveItemNode.OnBeforeSerialize 会过滤未启用且全零的标签，不能照搬到即时回溯：原对象仍可能被外部引用。实验保留全部有效字典条目，包括未启用、全零、null 值和共享 TagState。记录共享字典、实例身份、键文本、枚举顺序与公开 comparer 身份。不得调用 GetTag/IsTag/EnableTag 等可能创建标签或更新名称的业务接口。

## 接入与安全边界

- 从已捕获的完整旧图中发现所有可达 TagSystem，不仅采集可见物品。
- 按引用身份去重 owner/map/state；逐项检查它们及键、标签字符串是否在旧图覆盖内。
- Dictionary.Comparer 可能公开默认或解包后的对象，不一定是内部实际存储引用，因此不要求该公开对象被旧图覆盖。
- 单独执行同配置上限的逻辑载荷预算，超限释放半成品。逻辑字节按引用 IntPtr.Size、六个标量各 8 字节计算，不是实际分配量；不把字符串和 comparer 计成额外语义对象。
- 捕获语义全量比较之后仍执行旧图全量 Differences；恢复只在原最终校验通过之后观察语义差异。
- 观察失败只输出错误，不把旧节点判坏、不重放业务回调、不修改贴图恢复顺序。
- Snapshot.SemanticTagShadow 默认 true，每个上下文最多尝试一个样本。清空、场景/天数边界和文件重载会重置尝试标记；失败也不在每个顾客处反复重试。
- 语义数据随原节点释放；类故意没有 Restore/Write API。可以在设置文件关闭后续采样，不直接修改玩家设置。

## 验证

- Mono Release 构建：0 警告 / 0 错误。
- CheckpointTests：115 项通过，新增 8 项，覆盖别名、空值、全部字段、浮点位模式、枚举顺序、预算边界、失败隔离、完整扫描和重复旧后端恢复。
- ReleaseCompatibility：55 挂点、73 字段通过；原生扫描 HardFlags 为空，能力仍为 files/reflection-external/dynamic-code。
- 额外用安装版真实 TagSystem/TagState（仅无构造的纯数据夹具，不调用 Unity）验证 schema、逐字段变更检测、只读无副作用、共享 map/state、禁用/空标签、扩容及重复恢复后的零差异。

可复跑模型基准：

```powershell
dotnet run -c Release --project tests/CheckpointTests/CheckpointTests.csproj -- --semantic-tag-benchmark
```

首轮输出：1500 个 owner，各先插入 24 个标签再删除一半，剩 18000 个 state。交替运行 7 轮，舍弃首轮，比较捕获加完整校验的中位数：

| 指标 | 旧图模型 | 语义只读模型 |
| --- | ---: | ---: |
| 耗时 | 110.06 ms | 3.93 ms |
| 当前线程分配 | 105018652 B | 6229272 B |
| 逻辑槽 | 457500 | 201000 |
| 逻辑字节 | 3660000 | 1608000 |

这是 .NET 6 的标签子集模型，不是 Unity Mono，不含完整游戏图、视觉处理、语义系统发现/旧图覆盖核验或语义恢复。旧图还覆盖集合实现和独立字符串对象等状态，两者保证范围不同。不能把该倍率当成全局提速，也不能据此直接删除旧图数据。

## 实机测试

每个上下文首个成功匹配的采样节点会记录：

```text
[SemanticTags] CAPTURE MATCH id=... readOnly=true captureVerifyMs=... systems=... maps=... entries=... states=...
```

回溯到这个 id 后应出现 RESTORE MATCH。移动、旋转、售卖、液体变化后回溯，并保留普通 CAPTURE/RESTORE 摘要及 SemanticTags 行。捕获阶段 semanticShadow 包括发现、捕获、覆盖核验与语义比较；恢复 semanticVerify 包括对照日志 I/O。其他节点为零开销或计时粒度内的常数工作，不产生语义恢复日志。

## 下一步门槛

仍需实机样本。在允许语义写入前必须明确集合 backing arrays/entries 的外部别名、迭代器和 version、comparer 自身可变状态、独立可达 TagState、字符串引用身份及其他业务对象的所有权边界。当前语义字段比较使用字符串内容，不声称能替代完整引用拓扑。

不能在未经覆盖和别名分析的情况下切掉整个 Dictionary 子图。后续应独立提交混合后端适配，保留失败前置门控、旧后端回退策略及原完整视觉恢复序列；本轮不继续启用写入。

## 部署

已通过标准脚本部署至 `<GAME_DIR>\Mods\ProbablyStolenTools`。

- 备份：`out/full-release-deploy-20261111-151738`。
- DLL SHA256：`7EFC6ABF8D13A4186FA0AA3E1A6F51A009ADFA7A356BBCD11C6ED37B0172BDA8`，构建与部署一致。
- 77 个已有存档目录文件逐项核验不变，游戏程序集及其他 Mod 哈希核验不变。
- SettingsImported=False；未启动游戏，尚未 Unity 活体验收。
