# 快照边界复查与 Code Review 交接

## 当前范围

当前版本 **0.9.1**，未再次升版，未推送或上传。本轮已完成源码边界复查、可复现问题修复、完整离线验证和最终双 DLL 部署；没有代替玩家进行 Unity 实机验收。

增量审查范围：

```powershell
git diff c4e2b1e..79cab6e -- src tools tests
git show 9a2a484
git show 79cab6e
```

- `c4e2b1e`：此前 0.9.1 发布基线。
- `9a2a484`：PaperBookRegistry 闭包建点失败修复，共享受保护 owner 清单。
- `79cab6e`：统一完整游戏 owner 排除规则，修复 nullable/装箱标量边界，新增元数据与实际图回归。

如审查方尚未覆盖完整加载器迁移，可用 `git diff 5eeaf82..79cab6e`，结合 `2026-11-12-loader-migration-review-handoff.md` 的架构、历史性能及高风险清单。旧文档中的版本/表数量/部署哈希是历史，不作为当前发行包信息。

## 生成器与运行时是否共用规则

**现在共用同一份边界源码与同一个游戏 owner 排除函数。**

`src/ProbablyStolenTools/SnapshotTraversalBoundary.cs`：

- `IsProtectedOwner`：注册、加载器、信任等组件名称清单。
- `IsExcludedGameOwner`：上述清单加最外层 owner 的 `ES3` 前缀。

`tools/StaticSnapshotLayouts/StaticSnapshotLayouts.csproj` 用 Compile Link 直接编译同一源文件；不是手工复制两份名单。

生成器用 Cecil TypeDefinition，运行时用 System.Type。两端分别提取最外层声明类型名称，然后调用共享函数。运行时额外确认来自目标游戏程序集，避免凭名称排除无关程序集对象。字段元数据读取不能直接共用：构建时离线分析和运行时固定 FieldInfo 存储是不同操作，不能把 Cecil 或运行时字段枚举引入发布包。

## 确认的问题及处理

### 1. 原版注册组件闭包被误准入

玩家日志 `logs/Probably Stolen/4/PSTools (1).log` 有 165 次相同失败：`Uncovered static snapshot layout: PaperBookRegistry+<>c`。生成侧刻意排除、运行侧按游戏程序集接收，导致 walk 拒绝整次建点。

现在这类引用保留原身份、零槽，不读写内部字段；显式注册为业务根则拒绝。委托仍保留原身份/调用链，并只追踪目标引用，不调用回调。未知布局不开放通用反射回退。

详见 `2026-11-12-protected-registry-capture-fix.md`。失败摘要新增 manual/closed；另一份没有日志的自动节点问题仍不能确定同因。

### 2. 剩余 ES3 排除规则未共享

原先 ES3 前缀只在生成器排除，现在并入共享函数。当前目标 Assembly-CSharp 内此类类型数为 **0**，因此这是消除潜在规则分叉，不是已证实的玩家触发原因，也没有借机改变实际序列化业务对象。

新增游戏元数据普查：**111 个**被排除 owner/嵌套类型均未获运行时展开准入；其余游戏类型均在静态表中。这里验证类型级覆盖，不声称所有字段路径、外部基类、泛型参数组合或第三方类型都能恢复。

### 3. Nullable Decimal 的引用检查误入缺失表

`Flatten` 本来把 decimal/nullable 当标量叶子，但 `HasReferences(decimal)` 仍递归查询 Decimal 字段。静态后端在 decimal? 数组触发 `Uncovered static snapshot layout: System.Decimal`；旧动态参考测试无法发现此静态表缺口。

已将 decimal 明确按不含托管引用处理，与标量叶子契约一致。实际 Mono 验证 decimal?、DateTime?、Guid? 的值/空值转换恢复；包含引用的 nullable 结构体仍拒绝，不放宽该边界。

### 4. 装箱标量进入 object 集合后缺表

普通 int[] 等数组以标量槽保存，但 `object[]`/集合中的 boxed scalar 是独立 owner。运行时接受值类型，旧表却缺少 Boolean/Int32 等布局。实际 Mono 用例首先复现 `Uncovered static snapshot layout: System.Boolean`。

生成器现补基本数值/字符/decimal owner，以及 System 顶层枚举（如 DayOfWeek）；不将它们当作“不透明标量”忽略。恢复仍写原箱内字段、保留共享身份与浮点按位值。

曾试验性包含全部框架枚举，官方能力门槛立即检出反射/Emit/序列化内部类型的退役能力；该候选没有部署，已收窄范围。最终合并扫描仅 files，没有降低门槛或改动官方扫描器。**不要把白名单重新改成无条件接收全部框架枚举。**

当前静态表：**2861 类型 / 106 泛型模板 / 8108 字段 / 4 模块**，严格 MVID 门控。

## 审查重点

1. 共享边界函数是否覆盖最外层声明类型、嵌套闭包与显式根；是否可能绕过根拒绝、误共享部分根 schema。字段值/对象不进入全局元数据缓存。
2. 受保护 owner 只能保留身份，不获取其字段，不执行构造器/方法/回调；测试夹具仅在离线宿主中生成未初始化身份令牌。
3. 新增字段表仅扩展框架标量布局，不扩大第三方程序集准入，不更改实体块及普通槽的选择。IntPtr/指针等原有不支持路径不得借此获准。
4. decimal 的 HasReferences 特例与现有令牌/比较语义是否一致；含引用 nullable 仍拒绝，不能误判所有结构体均无引用。
5. boxed scalar 必须保留原箱身份与别名，不能以新装箱替换；测试通过实际字段变更、恢复与 NaN/负零位比较验证，而非只比较数值相等。
6. 新 helper 与主 DLL 必须成对构建/扫描/分发；主 DLL 没有动态生成和枚举回退。错误 MVID/未知布局仍拒绝。
7. 参考后端测试与真正静态后端测试不可混淆：380 项参考测试之外，新增覆盖必须在游戏 Mono 的发布双 DLL 上运行。

## 验证证据

完整命令：

```powershell
.\scripts\Test-StaticLayouts.ps1 -GameDir '<GAME_DIR>'
```

最终输出：本地 `out/boundary-audit-static-tests.log`。完整套件通过：

- Release 0 警告/0 错误；380 项参考测试。
- 57 Harmony 挂点 / 76 字段检查。
- 两个正式 DLL 合并官方扫描仅 files，HardFlags/Error 为空。
- 2861 类型/8108 字段的实际 Mono 精确元数据对照。
- 原完整图、实体/Tag/int[]、六轮双向恢复、预算、未知根、释放/身份错误、视觉相关离线契约、确定性生成及错误 MVID 拒绝。
- 新增排除边界普查、实际注册闭包/委托目标、nullable 标量、装箱标量原字段/身份/别名/浮点位模式恢复。

新增 fixture：`tests/CapabilityMigrationProbe/MonoFixture/BoundaryCoverageFixture.cs`。

修改前故障证据：`out/boundary-nullable-before.log`（Decimal），`out/boundary-boxed-before.log`（Boolean）。这些 out 文件是本地审查证据，不是发布附件。

离线通过不保证原玩家存档或任意 Mod 组合恢复正确。此次没有新的性能实测，不因补表宣称提速；首节点初始化卡顿仍未解决。

## 保留限制与搁置项

- 未知游戏/框架布局、未知自定义结构体仍严格拒绝；第三方普通引用对象通常只保留身份，不承诺其内部状态可回溯。
- Unity 视觉/外部资源不靠通用托管图完整复制；现有视觉收尾顺序及最终全图校验保留。
- 被排除组件的内部状态不属于快照。没有扩展到加载器、信任、存档系统的状态恢复。
- 不主动调用 SaveGame，不改存档，不启动游戏，不修改加载器或其他 Mod。
- 用户侧自动生成/第三方自动适配工具，按用户要求搁置，不纳入当前发行。
- 当前官方扫描通过不等于保证未来游戏/加载器接受，也不意味着所有未来版本只重建 helper 即可兼容。

## 最终部署与后续验收

部署记录：`out/full-release-deploy-20261113-000133/deployment.json`，已确认游戏关闭后执行。

- 主 DLL SHA256：`FDC17F0BDE3FF13FF7B842A01E81A89F3383F6C6F7CAD566C18CDD0932785F75`
- helper SHA256：`CBB8F36AECB701BD46BEB87EAB53B63F4782E222056B7FEE0C452D7A2C8E1386`
- 79 个既有存档目录文件、游戏程序集和其他 Mod 校验未变；未导入设置，未终止或启动游戏进程。

此部署替代前一轮仅修复注册闭包的包，必须发正式双 DLL。不要将生成器、Cecil、net8 宿主表、原生离线宿主或负例打包。

待原问题玩家复测手动及普通顾客自动建点，再回溯；如继续失败，需新 CAPTURE FAILED 的 manual/closed、类型及堆栈。其余完整场景矩阵（售出、旋转、柜台、嵌套、群拖、顾客贴图、交易送客）不能仅靠离线结果勾选通过。

请 DS 以缺陷/风险/缺失测试为主，按严重度给出文件/行号。审查通过后再决定发布与版本递增；本轮不自动上传、推送或调用旧插件。
