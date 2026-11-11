# 加载器弃用能力警告：原因、官方依据与迁移方案

调研日期：2026-11-12。当前生产代码未改动、未重新部署、未上传。

## 结论

这是有效的前向兼容性警告，不是翻译问题，也不是 manifest 少声明能力。

当前 Mod 实际使用并声明 `dynamic-code` 与 `reflection-external`。官方当前指南明确：2026-12-10 的游戏更新将拒绝这两项能力，以及 native/threads。当前版本仍接受，所以旧兼容测试显示 HardFlags 为空并不矛盾。

因此应在后续上传前将此项列为兼容迁移任务，不能把当前发布说明写成“已兼容下一版加载器”。不修改玩家的扫描器、信任列表或安全设置来消除警告。

## 官方文档

1. 官方开发仓库：
   https://github.com/dragonpan2/probably-stolen-modding
2. 当前英文开发指南，重点是 Capabilities and the scanner、Scoped reflection (ModReflect)、Testing your declaration：
   https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.md
3. 当前中文指南：
   https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.zh-CN.md
4. 官方 API 变更清单，重点是 1.0.0.13（100-Rev13）和 1.0.0.14 beta：
   https://github.com/dragonpan2/probably-stolen-modding/blob/main/APIChanges.md

Steam 的该游戏开发者发布公告亦直接指向此 GitHub 仓库作为 Mod 开发资源。应以线上当前指南及目标游戏版本为准，不继续使用旧缓存中“dynamic-code 无替代，请声明”的结论。

本地旧归档 `research/modding-guide/ModdingGuide.zh-CN.md` 的能力表仍有上述旧说法，不能据此判断未来兼容性。它是历史副本，不是当前政策的唯一依据。

官方明确保留可静态分析的字面类型/成员访问；动态选择游戏类型或成员时，推荐 ModReflect。不是所有反射调用都一律禁止，也不是所有 System/引擎成员访问都需要彻底删除。

## 本机证据

重新运行了既有 ReleaseCompatibility 检查，当前 DLL 的结果：

```text
Capabilities: files, reflection-external, dynamic-code
HardFlags:
Error:
Pending: files: ProbablyStolenTools.Diagnostics.RunOnce -> SaveFiles.Slot ...
```

57 个挂点、76 个字段和原生回归夹具仍通过。这只验证当前加载器及当前规则，不保证弃用期结束后的规则。

只读检查本机 Assembly-CSharp.dll，确认：

- ModCapability.Retiring 含 reflection-external、native、dynamic-code、threads。
- ModScanner.VERSION 为 4，REFUSE_UNSCANNABLE_CAPABILITIES 当前为 false。
- Retiring 处理在当前模式记录能力；开关为拒绝模式时，转为 Hard 标记。
- 因此 `Refused=False` / `HardFlags` 为空，不能当作下一版可发布的验收条件。

没有改动这些类、开关或游戏程序集。

## 源码命中点

| 位置 | 命中原因 | 迁移方向 |
| --- | --- | --- |
| `SnapshotFieldReader.Compile` | Expression.Compile，运行时生成字段读取代码 | 构建时生成静态读取器，或使用合规的预声明字段访问 |
| `SnapshotFieldReader.CompileComparer` | Expression.Compile，运行时生成比较代码 | 静态类型比较器，保持浮点按位/Nullable 等语义 |
| `SnapshotFieldReader` 的 Equal 局部函数 | 由条件表达式选择方法名后 GetMethod | 直接调用 BitConverter，或查找处使用固定 typeof 与固定成员名 |
| `GameFields.Field` | AccessTools.Field/Property 接收运行时计算的类型 | 对游戏对象采用 ModReflect.Get/Set/Has，或生成固定成员访问 |
| `ManagedStateAccess.ObjectSchema` | 对 current 运行时类型调用 GetFields | 构建时生成游戏/运行态描述表，运行时只选择已准入的表 |
| `ManagedStateAccess.Flatten` / `HasReferences` | 对运行时结构体类型枚举字段 | 构建时分析和生成递归字段路径/引用分类 |
| `SemanticEntitySnapshot.Schema` | 通用 Type 参数 GetFields/GetField | 各精确类型采用静态可分析的完整 schema 定义及检查 |
| `SemanticTagShadow.CheckSchema` | 旧开发诊断对运行时类型枚举字段 | 同样迁移，或从发布编译中排除旧诊断实现 |

当前扫描 findings 中，这些合计 9 条 reflection-external 与 2 条 dynamic-code；files 是另一组合法声明，不是本次两个警告的原因。

注意扫描器检查编译进 DLL 的方法，不按玩家本次是否调用判断。关闭 DevelopmentChecks、TypedScalarComparisons 或详细日志，不会让对应方法从程序集消失。只删除 manifest 的两个声明，会变成“代码使用未声明能力”，并不是修复。

把 owner.GetType() 改成一个通用 Type 参数，再由调用者传 typeof，也不保证静态扫描能追踪；应在实际查找指令处使用可静态分析的固定类型/成员，或使用官方接口。

## 已完成的独立扫描探针

探针位于忽略目录 `out/capability-migration-probe`，未加入发布程序集，未访问玩家存档或运行中的游戏对象。

正例包含：

- `typeof(List<int>).GetField("_items", ...)`
- `typeof(List<T>).GetField("_items", ...)`
- `typeof(Dictionary<string, int>).GetField("_entries", ...)`
- `typeof(BitConverter).GetMethod(nameof(BitConverter.SingleToInt32Bits))`
- `typeof(GameItemElement).GetField("validateLock", ...)`
- `ModReflect.Get(target, "validateLock")`

仅编译这些正例后，官方本机扫描器结果：

```text
Capabilities:
Findings:
HardFlags:
Error:
Pending:
Refused: False
```

负对照加入运行时类型 GetFields 和 Expression.Compile 后，恰好出现 reflection-external / dynamic-code 两条对应命中，正例未被标记。

复现命令：

```powershell
dotnet build out/capability-migration-probe/Probe/Probe.csproj -c Release
dotnet run -c Release --project out/capability-migration-probe/Scanner/Scanner.csproj -- `
  "<GAME_DIR>" `
  out/capability-migration-probe/Probe/bin/Release/netstandard2.1/Probe.dll
# 负对照：构建 Probe 时追加 -p:DefineConstants=NEGATIVE_CONTROLS，再扫描。
```

这验证了静态访问方案在当前真实扫描器上可用，不是完整迁移或未来未知扫描器的兼容承诺。它没有验证这些集合字段在目标 Mono 版本的完整布局，也没有进行游戏内捕获/恢复。

## 建议迁移方案

### 第一阶段：停止运行时代码生成

移除发布 DLL 中全部 Expression.Compile 路径，包括关闭设置后仍留在 DLL 的旧实现。

由构建工具读取目标游戏/程序集元数据，生成普通 C# 字段读取/比较代码；最终随 Mod 编译，不在游戏运行中下载、加载新 DLL 或编译代码。构建工具的生成逻辑不能一并装进 Mod。

不能只替换比较器：普通读取器也会编译。保留精确浮点位比较、引用身份及框架标量免写规则，先接受必要的性能变化，不借此删除校验。

### 第二阶段：游戏字段访问改为明确可审计路径

GameFields 中的普通游戏字段访问优先使用官方 ModReflect；其范围覆盖游戏、自身 Mod、已声明前置 Mod 及特定 UI 命名空间，不是任意 System/Unity 内部。

精确实体 schema、字段数组及直接访问器采用构建时生成，或查找处直接写固定类型/成员。不要把通用动态枚举隐藏进另一个辅助函数。

ModReflect.Set 可能设置属性并触发业务逻辑。快照必须明确使用原字段/原 backing field，不能把当前直接字段写回悄悄改成业务 setter。校验阶段也不得无意调用有副作用的 getter。

### 第三阶段：通用运行态图迁移，这是主要工作量

当前实体“语义块”仍保留完整 Runtime/集合/闭包物理存储，剩余大图依赖通用字段枚举。仅迁移五种实体或 GameFields 后，整个 DLL 仍不合规。

建议先保留当前精确物理恢复语义，由构建时工具生成已知游戏类型、闭包/结构体和已准入集合布局的静态描述/适配表。运行时只选择表，字段访问必须保持可静态分析。集合的原身份、共享引用、字典容量/版本、原数组和 RNG 状态必须保留。

固定集合成员的扫描探针已证明这种方向存在可行入口，但不保证所有私有嵌套泛型/平台 RNG 布局都能无改造迁移；要逐类证明，不能泛化为“所有集合均已解决”。

ModReflect 没有直接替代整个通用 GetFields 递归图的接口，且不允许借它无范围访问 System.Random/集合内部。未知类型不能继续回退到被弃用的通用反射，也不能静默少存字段；应拒绝新节点并保留已有节点/给出明确原因。

如果有限静态适配无法保持原物理存储，需要向官方请求受限字段描述或快照接口，再评估完整逻辑集合重建。逻辑重建是另一项高风险设计，不能为了去掉 warning 顺便替换并跳过原有精度测试。

### 第四阶段：发布编译及验证收口

- 迁移或从发布编译中排除旧 shadow/pilot/调试枚举；仅运行时禁用不够。离线测试源码仍保留。
- 将下一版兼容门槛加入 ReleaseCompatibility：退役能力不得出现在 detected、declared、HardFlags 或相关 Pending 中；不要再只检查当前 Refused=False。
- 用实际目标游戏的扫描器再次运行 `mod-scan <id>`，并核对所有 shipped DLL；不要把探针/构建工具打包进去。
- 确认代码不再需要两个能力后才同步清理 manifest。files 可以暂时继续合法声明；ModData 文件接口迁移是可分开的后续改进。
- 跑完整离线/原生回归，再分阶段部署并实测首/普通/手动节点往返、集合容量/成员、交易、RNG、群拖及顾客视觉。
- 迁移后才能依据实测讨论速度；不能承诺移除动态编译后一定提速。

## 对上一份 DS 交接的修正

已通过的 0.8.14 实机证明当前后端在当前游戏可用，不代表满足已公告的下一版加载限制。原交接中的 code review、版本递增和上传步骤，应加入本次退役能力门槛；不能只做一次普通审查就宣称前向兼容。

当前调研结论是“明确需要更新，已有可验证的迁移方向”，不是“已修复”。尚未进行生产代码修改、版本递增或新部署。此轮只新增调研文档、独立忽略目录探针，并提示 DS 交接中的新增风险。
