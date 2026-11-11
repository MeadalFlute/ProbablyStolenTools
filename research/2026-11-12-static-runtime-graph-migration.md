# 静态运行态图迁移实现与离线验收

日期：2026-11-12。当前版本号保持 0.9.0，未推送/发布本轮迁移。
承接 `2026-11-12-static-type-token-proof.md`，已从窄探针推进到生产源码候选。

## 已实现

1. 构建时生成 `ProbablyStolenTools.StaticLayouts.dll`，用明确类型令牌和固定字段名返回完整 DeclaredOnly 字段表。包含本机 2807 类型、106 个泛型模板、8027 个实例字段，来自四个目标模块。
2. 游戏闭包、普通对象/嵌套结构体、Dictionary.Entry、HashSet.Slot、集合视图/Enumerator、Tuple、Random 等通过同一静态表准入。集合仍保存原物理存储，不 Clear/Add，不重建或裁剪容量。
3. `SnapshotStaticLayouts` 使用惰性、只读工厂注册和最多 8192 个关闭泛型 metadata 缓存。校验目标模块 MVID；未知类型/版本不符直接拒绝，没有动态 GetFields 回退。
4. ManagedStateAccess 的计算类型字段枚举及递归结构体描述改为静态表。实体 schema、标签 shadow 的动态入口也改为静态表；精确游戏类型的字面 schema 检查仍可依法保留。
5. Mono 发布编译排除 SnapshotFieldReader 的 Expression.Compile/动态查找实现，改用纯字段 metadata 读取与标量比较。浮点位、Nullable、引用身份及自定义 Equals 边界保持。
6. GameFields 的动态 AccessTools 字段/属性选择改为官方 ModReflect，保留其原有字段/属性访问用途。图写回仍使用明确 FieldInfo，不将原字段恢复换成业务 setter，也不调用 ModReflect.Set 写 readonly。
7. manifest 列出两个真正发布 DLL，只声明 files。发布兼容检查扫描清单中所有 DLL，检测/声明/相关 Pending 中出现任意退役能力即失败，不再只看当前 Refused。
8. 部署脚本同时复制/核验主 DLL 和静态表；不复制 Cecil、生成器、宿主表或测试原生 bootstrap。Session 新增 layoutBackend=static-token-v1。

旧动态实现仍可留在离线参考测试/未发布平台分支中，但不编译进 Mono 发布 DLL。开发 oracle/shadow/pilot 也使用合规字段描述，开关是否打开不再影响退役能力扫描；不能将其 schema 独立性宣传成仍有旧动态枚举实现。

## 未做功能阉割

保留完整字段、原对象/数组身份、共享引用、版本/容量/空闲存储、随机状态、预算、完整普查、预检、owner/full 修复与最终校验。
物品/顾客视觉恢复和群拖门控顺序未改。文件回溯、鉴定、标注、多语言与 UI 功能未删除。

兼容范围明确收紧：未知可变原生布局不自动反射适配；需要基于目标版本重新生成/构建。旧第三方 opaque 对象仍只保留身份，不新增“恢复任意第三方内部状态”的承诺。

## 已执行验证

统一命令：

```powershell
.\scripts\Test-StaticLayouts.ps1 -GameDir "<GAME_DIR>"
```

全程未启动游戏、未访问玩家存档，运行时样本只在离线进程内新建。

### 构建与扫描

- 正式 Mono Release 构建 0 警告/0 错误。
- 主 DLL + 目标静态表合并扫描：MethodsScanned=4357；detected 只有 files；HardFlags/Error 为空。
- 原有合法文件能力 Pending 保留：Diagnostics.RunOnce -> SaveFiles.Slot；没有退役能力 Pending。
- 声明中没有 reflection-external/dynamic-code/native/threads。不是只删声明，实际编译方法也已清除其命中。
- 重新生成的目标表 SHA256 与构建表完全相同。

### 既有回归

- 373 项 CheckpointTests 通过。这组仍编译旧动态参考路径，不能把它单独当作新静态后端验收。
- ReleaseCompatibility 改为 net8，使用隔离的宿主静态表执行现有真实游戏类型夹具；57 挂点/76 字段以及全部既有实体、运行态、双节点、预算、owner/full 等夹具通过。
- 宿主表不参与发布扫描或部署。相关基准输出标为 net8-static-host-not-Unity，不能与旧 net6 或实际 Unity 直接作 A/B 结论。

### 游戏自带 Mono 的实际执行

原生离线宿主加载游戏自己的 Mono 与 BCL，不是启动游戏进程：

- 2807 类型、106 泛型模板、8027 声明字段逐一对照旧反射：字段顺序、类型、属性、声明类型、模块 MVID 全部一致。
- 实际启用实体/Tag/int 块，与普通字段图覆盖/预算一致。
- 精确对象/槽/字节预算成功，各减少 1 的三种预算分别正确拒绝。
- 两个独立节点六轮前后往返，验证 Dictionary/HashSet/List/形状原存储身份、readonly Tuple 引用及嵌套 KeyValuePair 结构体。
- 验证共享引用、游戏闭包、NaN payload、正负零、Random 序列、Census、owner/full 修复；恢复未执行业务回调。
- 未知根明确拒绝，保留的两个节点随后仍可恢复。
- 独立负例进程加载错误模块 MVID 表，确实在返回任何字段前拒绝，不回退或部分恢复。

## 风险与剩余边界

1. **Unity 活体验收未完成**：用户离开，无法协助；没有用离线字段或图测试冒充顾客/物品贴图、群拖、交易、窗口、文件回溯的实际游玩验收。
2. **性能未实测**：原来的运行时编译读取器已移除，metadata 读取可能让热路径变慢。缓存/实体块仍在，但不能承诺本轮提速。不得为性能删除完整校验。
3. **未来游戏实际版本未验收**：当前官方扫描器已无退役能力命中，不代表已测试尚未发布的目标加载器。严格 MVID 绑定意味着游戏更新需要重新构建并重跑检查。
4. **版本/布局安全失败**：不匹配时影响的是内存建点，不应妨碍其他辅助功能或写入存档；失败不触发原生 SaveGame。
5. **分发必须包含两个 DLL**：只上传主 DLL 将缺依赖；上传前必须使用目标 Mono 表，不能误取 out 中的 host/mismatch 表。

本轮实现已达到可部署等待游戏实测的阶段；不以“扫描通过”宣布整体发布验收完成。
