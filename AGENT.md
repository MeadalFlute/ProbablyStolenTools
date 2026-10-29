# Probably Stolen — Mod 项目交接文档（给 GPT / 后续 agent）

> 当前源码：Codex **0.6.0**。正式版已部署，使用游戏自带 **Mono / IMod / Harmony**；游戏内验收待用户执行。
> 当前维护者：Codex。DSH 集成已过时，不调用 DSH；用户明确要求不使用 Computer Use。
> 当前实现以本节和 `research/0.6.0-full-release.md` 为准。下方 Demo、IL2CPP、MelonLoader 和 BepInEx 记录均为历史资料，不可直接套用正式版。
> 设计讨论稿见 `research/2026-10-22-snapshot-B-design.md`。
> 实现与验证边界见 `research/customer-checkpoint-implementation.md`，未通过游戏验收前不得称为可用功能。

## 给外部 agent：如何适配其他 Mod

这份文档同时是**兼容性适配的入口**。如果你是被请来把本 Mod 与另一个第三方 Mod 调和的 agent，按下面顺序读：

1. 先读本节的「硬约束」，再看 `当前事实` 里与你改动相关的条目；
2. 需要改哪个功能，就看对应的实现文件（`CustomerCheckpoint.cs` 快照、`Rewind.cs` 文件回退、`ManagedStateAccess.cs` 遍历规则、`DoubleClickInspectionPatch.cs` 双击鉴定、`Panel.cs` UI）；
3. 改完必须跑通 `tests/CheckpointTests` 与 `tests/ReleaseCompatibility`，再用 `scripts/Deploy-FullRelease.ps1` 部署，由人类进游戏验收。

硬约束（改动不得违反）：

- **不主动写存档**：不得调用 `PlayerEngine.SaveGame()` 或任何等价接口；文件回退只允许在用户显式按键时发生，且覆盖前必须保留 `.pre_restore`。
- **快照遍历不得中断**：未知可变类型不得抛异常打断捕获；第三方类型降级为不透明对象（保留身份、不展开字段），只有 `Assembly-CSharp` 与框架类型才允许报错。
- **恢复是原子的**：任一物品放不下或任一校验失败，必须整体回退，不得留下半套布局。
- **不得修改游戏的信任记录、Mod 启用列表或存档来绕过权限确认**。
- 修改 `manifest.xml` 的能力声明时，必须与实际使用的 API 一致（`files` / `reflection-external`）。

适配第三方 Mod 的常见切入点：

| 现象 | 可能原因 | 修改位置 |
| --- | --- | --- |
| 捕获报 `Uncovered mutable type` | 对方类型未被识别 | `ManagedStateAccess.Traversable` 的降级分支 |
| 恢复后对方物品状态异常 | 对方字段未被快照 | 同上，或为其类型增加显式遍历规则 |
| 双击/面板被对方抢走 | Harmony 补丁顺序或 UI 层级 | 对应补丁的 `Priority`、`Panel` 层级 |
| 回退后对方 Mod 的数据没跟上 | 对方有自己的持久化 | 在恢复完成后加刷新钩子，不要改对方的存档 |


## 当前事实（0.6.0，正式版适配）

- 独立工坊上传器已另建于 `<UPLOADER_REPO>`，0.1.0 部署至正式版 `Mods\ProbablyStolenWorkshopUploader`，入口为 Mod 面板或 F9。其交接、构建和验证见该目录 `AGENT.md`；27 项离线测试与实际官方扫描器通过，尚未启动游戏或发布真实条目。自研回溯工具源码、已部署 DLL 和设置未因上传器开发而改变。
- 正式版路径：`<GAME_DIR>`。Steam AppID 4348910、BuildID 26274380，游戏 `VERSION 100-Rev1`，Unity 2021.3.45f2 / **Mono** / x64。`Assembly-CSharp.dll` SHA256：`52F11980B737F10315723FD6EA09AC49C033A7044978DC1CEFAC0137BA93E556`。
- 正式版原生 `ModLoader` 已可用，通过 `IMod` 加载和统一 `Harmony.PatchAll`；不安装 MelonLoader、BepInEx、代理程序集或第三方 Harmony。正式版 DLL 不依赖 MelonLoader、Il2CppInterop 或 Inspector。
- 已部署到 `Mods\ProbablyStolenTools\ProbablyStolenTools.dll` 和 `manifest.xml`。游戏内需用户在 Mod 列表启用、确认声明的文件与反射权限，并按游戏提示重启。未修改游戏信任记录、启用列表或存档来绕过此步骤。
- 构建默认 `GameRuntime=Mono`，目标 netstandard2.1，引用正式版 Managed；输出 `src\ProbablyStolenTools\bin\Release\Mono\ProbablyStolenTools.dll`。`-p:GameRuntime=Il2Cpp` 保留 Demo 编译，Demo 已安装的 0.5.2 DLL 未替换。
- `ManagedStateAccess` 使用真实托管字段、数组元素和结构体字段路径恢复，稳定弱引用身份 ID 不等于 GC 地址；每份快照强持有其业务对象，恢复用反射写屏障。委托身份不变，只遍历每个调用目标的闭包；不执行业务 getter、构造器或回放回调。支持集合扩容/删除、循环引用、引用结构体、可空标量和随机数；遇未知可变类型或含引用的可空结构体拒绝捕获。
- `GameFields` 明确访问私有字段，Demo 代理兼容属性回退。Unity 随机状态在 Mono 使用公开 `Random.state`，不调用 IL2CPP icall。MonoBehaviour 使用正常 AddComponent，不注册 IL2CPP 注入类型。
- 正式版交易详情已改为 Tablet UI：水质从 `NegociationUIManager.BuildExpandedRow` postfix 刷新 `uiObject` 下的 `ItemFeatureUI`，旧 `UpdateItemFeatureGroup` 和 `itemFeatureUIs` 已不存在；议价恢复后调用正式版公开 `RefreshUI()`。
- 正式版配置：共享存档目录下 `PSTools.settings.xml`，独立于 ES3 和 Demo TOML，临时文件替换并保留 `.bak`。已迁移 28 个自研设置（包含强制关闭的三个 Debug 开关）；旧六项设置、窗口尺寸、折叠和鉴定坐标保留。第三方 Inspector 设置不导入。
- F6/F7/F8、最近 10 位顾客、开业前/闭店节点、未来分支保留、本地化、快速鉴定、水质和双击放大镜逻辑继续沿用。**离店、跨日、场景或店铺变化仍释放内存节点，不支持跨场景回溯。**
- 正式版与 Demo 共用 `%LOCALAPPDATA%Low\Questing Goose Studio\Probably Stolen`，不得复制旧 Demo 存档覆盖正式版。继续禁止主动 `SaveGame()`、自测写用户槽位或覆盖 `backup_save`；F7 仍仅由用户主动触发并保留 `.pre_restore`。
- 验证：Mono Release 和 Demo 编译均 0 警告、0 错误；48 项离线测试通过；正式版 51 个 Harmony 目标（含参数名/类型）、68 个字段通过；实际官方 `ModScanner` 无 HardFlags/Error，检测权限为 `files, reflection-external`，与 manifest 一致；正式版配置磁盘往返和真实 PlayerStore/StoreStation/GameItemElement 数据图往返通过。**没有启动游戏，以上不等于 Unity Mono 内运行、完整活体业务图或 UI 已验收。**
- 部署 DLL SHA256：`7A1F981967D820B86D078FEBC52AC71BB47F4AAD60695AC753FDDC6B28730554`。部署备份：`out\full-release-deploy-20261029-001459`，123 个原存档目录文件逐一哈希不变，原游戏 Managed 逐一哈希不变；仅新增本 Mod 目录和独立设置文件。初始源码/测试/Demo 配置/存档备份另在 `out\full-release-20261028-235105`。
- 第三方 ProbablyStolenInspector 的旧 DLL 依赖 IL2CPP，**未搬入正式版**，Demo 副本仍保留。如用户需要，应另找作者的 Mono 正式版或单独研究移植。
- 可复用部署脚本 `scripts\Deploy-FullRelease.ps1`：限定 Mono 正式版、按 EXE 完整路径关闭目标游戏、备份/校验、部署、仅在设置不存在时迁移。不会启动游戏或改原生 Mod 信任状态。

### 正式版待验收

1. 启动正式版，在 Mod 列表找到 Probably Stolen Tools 0.6.0、启用并按游戏提示重启。若加载失败，先读取共享目录的 Player.log / GameLog 和 PSTools.log，不安装旧 IL2CPP 加载器兜底。
2. 载入存档，F8 检查中文、设置、拖动/缩放/收起、位置持久化。
3. 开场对话结束后等顾客节点，做一笔交易或议价再 F6，检查现金/物品/报价/再次议价、贴图与录音机；旧节点和后续节点可往返，新顾客后才截断。
4. 水质详情、双击放大镜、鉴定报价和摘要开关；关闭鉴定页无残留。
5. 闭店仍保留本日顾客，离店/跨日使内存节点失效。F7 是会写回槽位的主动恢复测试，验收时有意触发。

## 历史事实（0.5.2，2026-10-23）

- 已确认第三方 Inspector 使用游戏原生 `CustomUIManager / CustomUIBuilder / CustomUIWindow`，不是它自行绘制的窗框。自研快速鉴定、回溯窗口、节点按钮和设置复选框已改用相同的游戏预制体，不依赖或修改 Inspector DLL。
- 新增 `NativeUi.cs`：统一创建窗口、固定尺寸、按左上角定位、校验顶层射线。原生 `UIDragHandler` 可能把窗口移入 letterbox dock，因此按固定参考区域做世界坐标转换，不能直接把参考区域的位移加到新父级的 anchoredPosition。
- 快速鉴定仍为 300×240、正文可滚动、标题可拖动、松开保存归一化位置；现在可在原生 overlay 可用区域内移动，不再限制于鉴定页本身。关页、禁用、场景或原生管理器失效时关闭并释放窗口。
- 回溯面板保留 F8、列表双击、收起/展开、右下角缩放和持久化。采用原生标题拖动；收起高度 90。设置区改为滚动列表，六项保持正常行高，不再在小窗口压缩复选框。回溯通知仍为不抢焦点的轻量 toast。
- 只改 UI，不改鉴定规则、水质标注、放大镜、自动报价、快照捕获/恢复、分支或跨场景安全边界。原生回调使用 `Il2CppSystem.Action` 并显式保活；不要把第三方 DLL 作为编译依赖。
- 已定位当前启动慢的主要额外耗时：生成器缺少 `Cpp2IL.Plugin.StrippedCodeRegSupport.dll`，而 `force_offline_generation=true`。缺文件触发 Setup，提前置 `AssemblyGenerationNeeded=true`；离线不下载，同路径 Process 又直接成功，导致下次启动仍重建。
- 最新日志 `00:00:30.718` 到 `00:00:48.987` 为约 18.27 秒 IL2CPP 代理生成；`force_regeneration=false`，不是用户主动强制。**本轮没有改加载器、离线配置或补齐插件**，避免重现此前迁移黑屏；启动提速尚未修复或实测。
- Release 编译 0 警告、0 错误，38 项离线回归通过；它们不覆盖原生预制体渲染、IL2CPP 委托、拖动命中和实际场景切换，不能称 UI 已通过游戏验收。
- 已部署 `Mods\ProbablyStolenTools.dll`，SHA256：`BA6C51CAFA115491A6C3A7708000647F30022144ACF93F13BDCB89C387C3CD72`。游戏未运行，未启动游戏。
- 新备份：`out\native-ui-20261023-023826`，含完整 SaveData、UserData、两份旧 Mods DLL、旧 BepInEx 配置、Latest.log、生成器 Config.cfg 和 deployment.json。62 个存档文件、2 个配置文件均与备份一致；部署前后存档、配置、第三方 DLL 和加载器文件哈希不变。
- 存档安全和配置类别规则继续沿用 0.5.1：不主动 SaveGame、不写用户槽位或覆盖 `backup_save`；自研类别为 `ProbablyStolenTools_<section>`，不要改回带点类别 ID。

### 本轮待验收

1. 进入游戏按 F8，确认原生窗框、中文、列表和设置显示正常；拖标题、缩放、收起展开、切设置滚动不重叠。
2. 在鉴定页拖“快速鉴定”标题移开表单，检查正文滚动；切物品、关开页面、关开设置及重启后位置与开关保持；页面关闭后摘要必须消失。
3. 双击顾客节点恢复，再点后续节点，检查既有回溯业务；返回主菜单再读档、离店/换场景，确认没有残留窗框或重复窗口。
4. 本轮未修加载器提速；若另行处理，先查看此前 Cpp2IL probe/no-plugin 记录并单独验证，不能直接恢复缺失插件或关闭离线模式。

## 历史事实（0.5.1，2026-10-23）

- 自研插件部署于 `<GAME_DIR_DEMO>\Mods\ProbablyStolenTools.dll`，使用 MelonLoader 0.7.3 / `version.dll`，已不使用 BepInEx 注入。旧 BepInEx 配置仅作首次配置导入来源，不要向其 plugins 目录部署。
- 第三方 `Mods\ProbablyStolenInspector.dll` 为 kale 的 3.24.3，保持共存。本轮未修改该 DLL、其窗口设置或真伪/估值开关。
- 原生鉴定页的自研“快速鉴定”摘要新增独立标题拖动区，正文仍可滚动。窗口保持 300×240，位置限制于父鉴定页可用范围；以归一化坐标记录，布局或分辨率变化时重新约束。
- 拖动松开时一次性保存坐标；关页、禁用、父布局变化时也结束拖动。切物品、重新开鉴定页与重启后读取同一位置。关闭页面及场景销毁仍回收 UI，不保留原生对象引用。
- F8 检查点面板的“设置”新增“显示快速鉴定摘要 / Show quick inspection summary”，默认开启，切换立即生效。仅控制自研摘要，不关闭水质标注、双击放大镜、自动报价或第三方 Inspector。设置行共六项，最小面板高度时同步收缩复选框。
- 新配置位于 `UserData\MelonPreferences.cfg` 的 `[ProbablyStolenTools_Inspection]`：`ShowQuickInspection`、`SummaryPositionX`、`SummaryPositionY`；X/Y 为 0–1 可移动范围坐标，默认右下角。
- 修复配置持久化根因：MelonLoader 0.7.3 经 Tomlet 写入带点类别 ID 时只留下空表。所有自研节改用 `ProbablyStolenTools_<section>`；保留旧空表和第三方节，仍只读导入旧 BepInEx 值，新类别已保存值优先。不要恢复旧 `dsh.probablystolen.tools.<section>` 类别 ID。
- Release 编译 0 警告、0 错误，38 项离线测试通过，包含真实加载器 TOML/磁盘往返、第三方配置保留、拖动边界和真实 ES3 只读解析。离线测试不证明原生 IL2CPP UI 已验收。
- 已部署，游戏未启动；备份目录 `out\draggable-inspection-20261023-022149`，内含完整存档、UserData、旧配置、两份旧 Mods DLL、日志及 `deployment.json`。部署 DLL SHA256：`3F1D6005EEAD4336A65D6BAB1843FA038C426EA4BAC88CC49809670277D0AFD0`。
- 62 个存档目录文件、2 个 UserData 文件与旧 BepInEx 配置部署前后哈希一致，备份也已验证。第三方 Inspector SHA256 保持 `8919DC6E31006543A07A435639970AEF46212EDDAC2048A8B2115886866C53F9`。
- 安全约束不变：不主动 `SaveGame()`、不调用旧自测写存档、不覆盖项目 `backup_save`；只有用户主动 F7 才通过备份槽位后文件恢复。离店、跨日或换场景后不宣称可恢复旧内存节点。

### 本轮待验收

1. 进入鉴定页，拖“快速鉴定”标题栏移开表单，确认正文滚动不带动窗口、点击其他面板不误拖。
2. 切换物品、关闭重开鉴定页、重启游戏后检查位置保持；如方便再切分辨率，确认不会移出鉴定页。
3. F8 → 设置 → “显示快速鉴定摘要”即时关/开，重启后仍保持；水质、双击鉴定及第三方窗口不受该项影响。

## 历史事实（0.4.2）

- 进入原生鉴定页时，从 `currentInspectingItem` 读取实际属性，覆盖所有有原生 `ItemFeature` 的鉴定物品，包括烟、食品卡、药物。药物按原生状态区分正品、仿品、过期、不可用；不能用“没有 ISSUE 标签”推断正品，也不能用声明与实际相等推断正品。
- 快速鉴定使用 `GetBaseRealDisplay()` 和本地化分类名，过滤禁用属性及临时交易修正；原生 Ribwich 真伪/拆封规则另行调用。摘要常驻原生鉴定页，支持滚动和每 0.25 秒刷新；切物品重新读取，关闭/重置/场景销毁时释放附加 UI。
- 水没有鉴定页。水质信息添加至交易描述的水质属性行及物品悬停说明：标签与实际等级不同，标签红色、括号内实际等级蓝色；相同等级只显示绿色标签。等级由原生 `GetPurityArrayIndex` 判定，不把同等级的纯度差异误判成假标签。
- 标签以 `GetCurrentItemCondition()` 为准：未揭露读取声明，匹配/揭露后跟随原生当前显示，避免已改正标签仍标红。实际水质每次从 `WaterHelper.GetWaterPurity` 读取，不使用最后一次检测缓存。排除酒和空容器；不改变揭露、检测次数、价格或标签业务值。
- 水质悬停说明使用 `RichTextBuilder` 的原生颜色，交易文本使用 TMP 富文本；不要把示例 `[red]` 当作游戏语法。原生“已检测”信息保留。
- 上轮 F7 修复也已保留：文件重载完成后重置 `Rewind` 与 `CustomerCheckpoint` 的旧运行时身份、回调和故障标志，避免 F7 当天只缓存开业前节点。`AppraisalPatch` 强制刷新 TMP 输入标签，自动提交后同步可见数字。估值同时显示在快速鉴定摘要。
- 当前编译、离线测试、部署哈希和待验收项目见 `research/0.4.2-inspection-and-water.md`。离线测试不覆盖 Harmony 安装、IL2CPP 运行或原生渲染。
- 0.4.2 已部署，未启动游戏；Release 0 警告、0 错误，29 项离线测试通过。备份 `out/inspection-water-20261022-202402`；DLL SHA256 `6BD2E16A8AA5A48686085DA1218EDBA3FDC156A179834645CCB502AD6515B50D`。53 个存档目录文件、2 个配置文件与备份哈希一致。当前自然存档第 41 天、cash=164、MORNING=2、slot=0。

## 历史事实（0.4.1）

- 0.3.0 已实现同日最近 10 位普通顾客节点、列表双击恢复、未来分支截断、拖动/缩放窗口及配置持久化、克制的回溯提示。用户认可此前的核心 F6 功能。
- 0.4.0 顾客节点使用蓝图中原生 `displayName`（与对话/录音机相同的已本地化名字），不再显示内部 identifier；日志保留内部 ID。
- 面板增加“设置”，提供自动鉴定报价、自动提交、始终覆盖报价、跳过顾客对话。设置立即保存。新增 `[Dialogue] SkipCustomerDialogue=false`，默认不跳。
- 对话跳过每 0.08 秒尝试原生 `OnAdvancePressed()`；保留原生显示、录音机入队及业务回调。遇选择不代选；电话、SEC、重要优先级、特殊顾客、交火、逮捕不自动推进。不调用 `InterruptDialog()`。
- F7 文件节点现在也在载入已有存档后建立：只读检查并复制自然存档，验证文件中的 day/state/slot/runID。元数据取文件而非运行中现金。F7 恢复后停在开业前，不自动开门。
- 闭店内存节点：`EndAndClose` postfix 标记，等待关门动画、告别对话、跟踪回调清空且持续稳定 0.5 秒后捕获。0.4.1 闭店不再清空顾客历史，保留最近 10 位普通顾客，闭店节点独立于此容量。
- 0.4.1 支持同店、同日、同场景内在营业顾客与闭店节点间恢复：除业务图外记录卷帘门/挡板的位置、遮罩、店内可见对象与顾客视图；不重放开关店业务回调。跨阶段需对话/回调/门动画结束。尚未游戏内验收。
- 回溯只移动历史游标，不删除后续节点；可双击旧节点和“后续”节点来回恢复。原生 `GetNextClient` 确实换成新顾客时才截断游标后的历史，新顾客捕获也有兜底。仅闭店、无顾客的叫客操作不触发截断。
- 物品恢复后调用原生 `ValidatePartialSprite()`，修复快照 `spriteChanged=false` 导致 `Validate()` 不刷新已改变贴图的问题（水质试纸等）。面板明确显示“收起 / 展开”，按钮独立置顶且排除标题拖动命中区。
- **闭店节点仅支持同店内、同日、同场景恢复。`EmporiumEntry.OnLeaveStore` 立即作废并释放所有内存节点，即使离店路径没有真正 LoadScene 也作废。闭店记录可灰显“已离店”；真实场景、日期、store 变化也释放引用。尚未实现跨场景夜间活动回溯。**
- 沿用安全限制：不主动 `SaveGame()`，不写用户槽位，只有用户触发 F7 才覆盖槽位并留 `.pre_restore`；不得覆盖 `backup_save`。
- 新增 `SaveMetadata.cs` 是针对该版本 ES3 的只读结构化元数据扫描器。真实 ES3 含未加引号的数字字典键及 `\“` 等非标准转义，不能直接用严格 JSON 解析或截断前 30 万字符正则读日期。
- 0.4.1 Release 0 警告、0 错误；离线 29 项测试通过（含延迟分支、闭店保留/替换、OPEN/CLOSED 业务图往返与真实 ES3 只读解析）。原生贴图、跨阶段恢复、按钮可见/命中仍需用户游戏内验收。
- 用户要求：命令关闭目标游戏、备份后部署，部署后通知，不必启动游戏或抢占输入。
- 0.4.1 已部署，命令关闭游戏后未重启。备份：`out/history-rendering-20261022-023937`（完整存档目录、配置、旧 DLL、日志和 `deployment.json`）。DLL SHA256：`E2F9B69EEB7E9071F9FB62B0DD825186F813FEEF680183FB43B198D1D8748FFB`。29 个存档目录文件与 2 个配置文件均与备份哈希一致。当前自然存档为第 25 天、cash=101、MORNING=2、slot=0；不要用旧文档第 24 天哈希作为当前断言。

### 本轮验收顺序（0.4.1）

1. 载入原存档，检查“收起 / 展开”、设置、拖动缩放与重启后的持久化。
2. 在顾客节点后使用水质试纸等变贴图物品；F6 后检查贴图和实际状态一致。
3. 连续接待数位顾客后回旧节点，再回后续节点，列表不减少；真正接待下一位顾客后才移除被放弃的后续分支。
4. 闭店等待新节点，确认原先顾客仍在列表；双击顾客回营业阶段，检查门、顾客、队列、交易与议价；未招待下一位前也应能返回闭店节点。
5. 离店或过夜后确认内存节点失效；F7 仍只回本日开业前、不自动开门。检查 `RESTORE OK ... diff=0`，无 Harmony/原生恢复错误。

## 历史记录（以下主要为 0.2.1）

---

## 0. 目标（已定稿，无歧义）

在《深空当铺 / Probably Stolen》Next Fest demo 上做一个 mod：

> **每位新顾客到来时，保存一份运行时快照；按 F6 时，回到当前顾客的初始业务状态。**

- 粒度为**每位顾客一份**：同一顾客身上做了 N 个操作，F6 会一次性退回他刚到店的时刻（N 个操作一起撤销）。
- 这是路线 **B（内存快照）**，已明确否决“复制原生存档 + 按 id 重建顾客”的旧做法。
- 现状：**用户确认 F6 可恢复大部分状态；0.2.1 日志已记录 lowerInventor 四次折扣后恢复成功，前三次恢复后均再次触发议价。其他议价类型、未按 F6 时的一次限制及完整业务流程仍待验收**。
- 首版捕获时点是普通顾客到场、开场对话结束且延迟回调清空后，不是入场动画中途。
- 当前只接受 BUY、SELL、SELLNBUY、BARTER、APPRAISAL_SERVICE，特殊顾客拒绝。
- 当前是选定业务根对象图、部分视图状态与随机源快照，并非整个 Unity 进程快照；协程、动画及绝对时间等仍有覆盖风险。

另一项功能（鉴定自动报价）已编码但**从未在游戏内验证过**。

---

## 1. 环境

| 项 | 值 |
|---|---|
| 安装目录 | `<GAME_DIR_DEMO>` |
| AppID | 4349200（Demo）；正式版 4348910 |
| 引擎 | Unity **2021.3.45f2**，**IL2CPP**，x64 |
| 存档目录 | `%LOCALAPPDATA%Low\Questing Goose Studio\Probably Stolen` |
| 工程目录 | `<REPO>` |
| 加载器 | **BepInEx 6.0.0-be.790**（IL2CPP）+ CoreCLR 6.0 + Il2CppInterop |

安装方式（全部为新增文件，原游戏文件零改动）：

```
<游戏目录>\winhttp.dll
            doorstop_config.ini
            .doorstop_version
            dotnet\
            BepInEx\core\        BepInEx\interop\（首次启动自动生成）
            BepInEx\plugins\ProbablyStolenTools.dll
            BepInEx\config\dsh.probablystolen.tools.cfg
```

卸载 = 删除以上新增项。`BepInEx\config\BepInEx.cfg` 的 `[Logging.Console] Enabled` 已改为 `false`（否则每次启动弹控制台）。

**完整性**：写入游戏目录的 426 个文件均已核查无低完整性标签，Steam 启动不受影响。

---

## 2. 官方 ModLoader 为什么不可用（已定论，别再试）

游戏自带完整第一方框架（`IMod`、`ModLoader`、`ModConfig`、`manifest.xml`、Steam 工坊、`Mods/` 目录），
但这是 IL2CPP 构建，**进程内没有 CLR**，`Assembly.Load` 不成立；demo 还直接打印
`[ModLoader] Limited build: mod loading disabled.`，并用 `LimitedBuildConsoleStripper` 拆掉开发者控制台。

→ 只能走 BepInEx。

---

## 3. 当前插件能力

| 功能 | 状态 |
|---|---|
| 游戏内检查点面板（uGUI + TMP，深色遮罩、四列对齐、收起/展开） | ✅ 可用 |
| 每天开业前自动存档快照（复制游戏自己写的槽位文件） | ✅ 可用 |
| F7 或旧面板行的文件检查点回退（写回槽位 → 回主菜单 → 自动载入） | ✅ 旧功能，保留 |
| 业务事件观测（只读日志，用于研究） | ✅ 可用 |
| 鉴定自动报价 | ⚠️ 已编码，**未验证** |
| 顾客级内存快照 + F6 覆盖（本次目标） | ⚠️ 用户确认大部分恢复有效；0.2.1 已有同客多次折扣/F6/再次议价日志，其他场景待验 |

配置：`BepInEx\config\dsh.probablystolen.tools.cfg`
关键项：`Rewind.Enabled`、`Rewind.RestoreMode=menu`、`Rewind.AllowOpenRestore=false`、`Rewind.QueueRebuild=false`、
`Rewind.CaptureMidDay=false`、`History.Enabled=true`、`Debug.SelfTest=false`、`Debug.PreviewPanel=false`。
新增 `CustomerCheckpoint.Enabled=true`（实验功能）。F6 只走内存快照，无可用快照时不回退到文件恢复。

---

## 4. 已确认的技术事实

### 4.1 存档与恢复

- 存档是**明文 JSON**（EasySave3），无加密、无校验。
- 存档**有意排除**运行时状态（`[ES3NonSerializable]`）：`currentClientInstance`、`isClientArrived`、
  `clientStack`、交互锁等；`futurStoreClientIdQueue` 是**跨日预约**，不是当天队列。
- **`PlayerStore.LoadGame()` 有完整实现**（ES3 载入 + 库存解码 + UI 初始化），但它是场景初始化路径；
  在局内直接调用**不会**按预期覆盖当前状态（实测：卡一下、无变化）。
- **唯一被验证有效的恢复路径**：把存档文件写回槽位 → `SceneManager.LoadScene(0)` 回主菜单 →
  `MainMenuUIController.OnLoadGameClick()` → 找 `SaveSlotElement` → `OnLoadClick()` 载入。
- `SaveFiles.Slot(id)` 返回**相对文件名**（如 `save_0.es3`），需拼 `Application.persistentDataPath`。

### 4.2 业务入口观测结果（`PSTools_history.log`）

已命中：

| 入口 | 方法 |
|---|---|
| 叫客/送客 | `PlayerStore.GetNextClient` / `DismissCurrentClient` |
| 卖出 | `PlayerStore.SellItem`（批量 4 件在 5ms 内，需归一为一步） |
| 买入 | `PlayerStore.BuyItemFromTable`（`Transaction.Norm()` 归一到 `BuyItem`） |
| 议价 | `BargainUIManager.OfferDiscount/OfferMarkup/Blackmail/OfferBuyingMarkup/DemandBuyingDiscount/ThreatenAug` |
| 电话 | `StorePhoneObject.OnClick`、`PhoneUIManager.StartPhoneDialog` |

**未命中（重要）**：

- `PlayerStore.PayMoney` 与 `ModCash` **都不是**电话购物的扣钱路径（`[CASH:unattributed] 374->203` 仍出现）；
- 设备“取出/放入/运行”：`GameInventory.UncheckedAcceptAll/ExpelAll` 未触发；
- 道具使用：`InspectionUIManager.HandleCigarette/Plasma/Stamp/Injector` 未触发。

→ 这三类操作的真正入口**仍未找到**，必须靠 ISIL 定位，不能靠猜。

### 4.3 关键 API（interop 可用的）

```csharp
PlayerStore.IsInstanceExist()          // 菜单期不要碰 PlayerStore.Instance，getter 会惰性 new
PlayerStore.playerCash / storeState / saveSlotId   // storeState: 0=CLOSED 1=OPEN 2=MORNING
PlayerStore.storeClientManager.clientStack         // List<StoreClient>，存档不保存
PlayerStore.currentClientInstance.GetClientBlueprint().identifier
PlayerStore.SetupWaitingClient(id)     // ❌ 只处理 mob/sec/hitman/cartel 四类晨间来客，不是通用入队
PlayerStore.HandleFutureClientQueue()
PlayerStore.PayMoney / ModCash / SaveGame / LoadGame
SaveFiles.Slot(int) / DeleteSlot(int) / NextFreeSlotId()
MainMenuUIController.Instance.OnLoadGameClick()
SaveSlotElement.OnLoadClick() / slotId
AppraisalHelper.GetCorrectBaseValue(GameItem)
InspectionUIManager.currentInspectingItem / apprisalServiceInputField / UpdateAppraisalPanel()
```

---

## 5. 踩过的坑（务必别重复）

1. **`PlayerStore.Instance` 的 getter 会 new 实例**：菜单期每帧访问会疯狂创建（日志刷 `A new PlayerStore has been created`）。
   判断“是否在局内”必须用 `PlayerStore.IsInstanceExist()`（已封装为 `Plugin.Store()`）。
2. **插件绝不能主动调 `SaveGame()`**：会把 CLOSED/OPEN 中间态写进用户槽位 → 读档后“门关着、0 顾客、按钮显示停止营业”。
   曾经的损坏副本 `storeState` 分别是 1 和 0，由此定位。现在唯一写盘时机是用户点“回退”。
3. **高频方法不能挂同步写文件的钩子**：`PlayerStore.OnMainInvAreaChange` 在一次 UI 初始化里被调用 **31 万次**，
   直接卡死游戏、日志涨到 14.3 MB。现已移除该钩子，并给日志加了限流（120 条/秒、8 MB 上限）。
4. **Harmony 补丁参数必须按名字匹配**：`HandleCigarette(GameItem gameItem)` 写成 `item` 会导致
   `PatchAll` **整体中断、所有补丁都不生效**。改参数后必须看日志有没有 `HarmonyX` 报错。
5. **IMGUI 被裁剪**：`Texture2D.SetPixel` / `GUI.*` 抛 `Method unstripping failed`，UI 必须用 uGUI + TMP。
6. **TMP 默认字体没有中文**：要用 `FontLoader.Instance.simplifiedChineseFont`（`NotoSansSC-VariableFont_wght`）。
7. **`UnityEngine.Random.State` 在托管 interop 里不存在**：首版改用 `il2cpp_resolve_icall` 解析
   `UnityEngine.Random::get/set_state_Injected`，用四个 int 存储并在首次捕获执行往返验证。
   最新实测已通过首次往返探针并完成捕获；尚未验证 F6 后的游戏随机行为。
8. **`SetupWaitingClient` 用错会导致坏顾客**（无贴图、挤占第一位、其余顺延）——已确认是分支只认四类特殊来客。
9. **Cpp2IL 的 IL 恢复失败**（方法体全是 `throw null`），但 **ISIL 输出可用**
   （`cpp2il_isil/IsilDump/Assembly-CSharp/*.txt`），能还原真实调用链。
10. **IL2CPP 嵌套类的 namespace 可能为空**：游戏内捕获报 `Uncovered mutable type: .KeyCollection`。
    当前源码已沿 `il2cpp_class_get_declaring_type` 拼接完整类型名，包含 Dictionary 外层命名空间。
    修复版已部署并启动，最新实测已越过该错误并成功捕获。
11. **`InterruptDialog()` 会执行业务 endAction**：内存恢复只停止跟踪中的对话协程、
    作废旧延迟回调并隐藏对话 UI，不调用该方法。
12. **业务图中的物品不一定有活渲染器**：实际发现 `trashcan`（uniqueId=-102）由
    `EmporiumEntry.trashcanInvElement -> onSlotAddItemEndFunc -> Delegate.m_target ->
    MiscItemDirectory.<>c__DisplayClass46_0.item` 持有，但不是 parentInventory.childItems 的成员。
    当前保留其业务数据，不做视图捕获/Validate/延迟销毁；仍拒绝缺渲染器的实际库存成员。
13. **议价恢复不能只看价格与一次性标记**：Unity Button.interactable 不在业务图中，
    原生资格检查还会看 currentDialog，而不仅是 isConversationActiveFlag。
    0.2.1 捕获前要求 currentDialog 为 null，恢复后调用原生 BargainUIManager.Update()
    同步分组、资格和按钮，未强行解锁。不能仅凭目前证据断言旧故障只有按钮这一处根因。

---

## 6. 下一步（游戏验证）

- 0.2.1 已部署并启动，日志确认版本及 Harmony patches applied，无本轮补丁错误。
- 用户确认 F6 能回退大部分状态，但报告议价回退后无法再次议价，故增加上述资格/视图同步修复。
- 本轮 CAPTURE：17081 objects、98627 slots、541081 bytes、168 items、167 renderedItems、37 windows。
- 当前日志记录四次折扣（75、50、25、10）后 F6，均为 RESTORE OK、diff=0；
  每次 discountOffered 从 true 恢复 false、discountPercent 恢复 0，折扣按钮从 0/0/4 恢复 4/4/4。
- 前三次 F6 后均再次触发 OfferDiscount；第四次 F6 前 currentDialog 非空、conversation=0，
  恢复后 currentDialog=0 且按钮可交互。该日志证明两种对话状态不能混为一谈。
- 不争抢用户输入，当前未追加游戏操作。画面/价格表现由用户确认，其他类型议价尚未覆盖。
- 继续验证不按 F6 时每客一次限制仍有效，以及买卖、消耗品、设备库存、新顾客替换和跨场景失效。
- 非托管内存写入不具备事务回滚；恢复报错应停止实验并重载，不能继续宣称状态完整。
- 检查存档及索引哈希，禁止调用 `SaveGame()`。
- 当前离线 9 项测试通过，插件编译 0 警告 0 错误，均不替代游戏验收。

---

## 7. 文件与工具

```
<REPO>\
├── src\ProbablyStolenTools\      插件源码（net6.0）
│   ├── Plugin.cs                 入口/配置
│   ├── Panel.cs                  检查点面板（uGUI+TMP）
│   ├── Rewind.cs                 检查点/回退/自测/热键
│   ├── HistoryObserver.cs        只读业务观测 + 事务分组 + 现金看门狗
│   ├── StateGraph.cs             可测试的对象图与存储槽快照、diff、顾客边界
│   ├── NativeStateAccess.cs      IL2CPP 字段/数组/结构体访问与对象保活
│   ├── CustomerCheckpoint.cs     顾客捕获/F6恢复、回调作废、视图和随机源
│   ├── AppraisalPatch.cs         鉴定自动报价（未验证）
│   ├── Overlay.cs                面板懒创建
│   └── Diagnostics.cs            一次性 API 探针
├── dump\                         Il2CppDumper 输出（dump.cs 19.8MB、DummyDll）
├── decompiled\                   ILSpy 反编译 Assembly-CSharp（678 个 .cs）
├── cpp2il_isil\                  Cpp2IL ISIL 反汇编（**可用**）
├── research\                     调研与设计文档
├── tests\CheckpointTests\         离线对象图测试（不运行游戏）
├── out\                          截图与历史日志归档（history_run1..5.log）
├── backup_save\                  用户原始存档备份（17:16 / 1,007,319 B / storeState=2）
└── tools\                        Il2CppDumper / Cpp2IL / BepInEx 安装包
```

编译与部署：

```powershell
cd <REPO>\src\ProbablyStolenTools
dotnet build -c Release
# 必须先关闭游戏，否则 DLL 被占用
copy bin\Release\ProbablyStolenTools.dll "<游戏目录>\BepInEx\plugins\"
```

离线测试（项目根）：`dotnet run -c Release --no-restore --project tests\CheckpointTests\CheckpointTests.csproj`

> 本机依赖已还原，当前可用 `dotnet build -c Release --no-restore` 编译，不需再次安装。
> 本轮测试前备份位于 `out\customer-checkpoint-pretest-20261022-005727`，包含存档、索引、旧 DLL 和配置。
> 本次重新部署前备份位于 `out\customer-checkpoint-redeploy-20261022-010716`。
> 物品视图修复前备份位于 `out\customer-renderer-fix-20261022-011158`。
> 议价修复前备份位于 `out\customer-bargain-fix-20261022-012456`。
> 当前已部署 DLL SHA256：`33FDDC8BC50043FC862FBC8EE4B09928188E448415079AD6976829D7BF9EB2CA`。
> 最近一次只读核验中，`save_0.es3` 与 `saves_index.es3` 均和该备份哈希一致。

日志：

- 插件：`<游戏目录>\BepInEx\LogOutput.log`
- 业务观测：`%LOCALAPPDATA%Low\Questing Goose Studio\Probably Stolen\PSTools_history.log`
- 检查点/回退事件：同目录 `PSTools.log`

---

## 8. 安全约束（硬性）

1. **永不主动写 `save_0.es3`**，唯一例外是用户点“回退”（覆盖前自动备份为 `.pre_restore`）。
2. 任何自动测试前先备份 `save_0.es3` + `saves_index.es3`；`backup_save\` 是用户原始进度，不要覆盖。
3. 不要给高频方法挂文件 I/O 钩子；新钩子先看 `Harmony patches applied` 与有无 `HarmonyX` 报错。
4. 恢复类实验优先在“读档后的当天”做，失败时用 F7（回开业前）或退出重进兜底。
