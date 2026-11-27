# QuickSL / Shift工具箱 功能拆分审查交接

日期：2026-11-26，Asia/Hong_Kong。交给 DS 的统一入口。
本次请求是审查拆分候选，不是发布授权。本轮准备审查没有修改生产代码、重新部署、
升版、提交、推送或上传，也没有再次操作游戏。工具箱离线测试重建的DLL哈希未变。

## 1. 必须审查两个仓库

| 项目 | 工作区 | 基线 | 当前候选 |
|---|---|---|---|
| QuickSL | `D:\Program\GameProject\ProbablyStolenMod` | `57265389669478f91474a8a074fcce6c7772ce7e` | 仍标0.9.9，基线后的整个dirty工作区及未跟踪文件 |
| Shift工具箱 | `D:\Program\GameProject\ShiftToolbox` | `5cc313146423981c0d56a9f05295a6ea18aecd18` | `shift.toolbox` / 0.0.1 / 米德；整个独立仓库及后续dirty修复 |

**不能只看两边的 `git diff`：工具箱初始提交已经包含迁入的全部工具代码。**
QuickSL的未跟踪 `ToolboxBridge.cs`、`Test-SplitMono.ps1`、`FileNodeIndexFixture.cs` 和
`tests/PreSplitToolRules/` 同样属于审查范围。两仓库均未为本次审查新建提交。

QuickSL的 `AGENT.md` 21:05清理记录是用户此前保留的工作，不是拆分回滚内容，不要删除。
较早的开发说明：QuickSL `research/2026-11-26-feature-split-handoff.md`；工具箱
`research/2026-11-26-initial-split-handoff.md`。若其中历史状态与本文件冲突，以本文件为准。

## 2. 功能归属与非目标

- QuickSL保留回溯、对象图/语义块、静态布局表、顾客/手动/跨天节点、原生存档历史、
  文件回退、快速重载、重掷随机数、回溯的第三方兼容及安全门控。
- 其余原有功能归工具箱：鉴定/放大镜、对话快进、水质/酿酒、化学自动化/预测、
  Shift详情/强制提示/固定提示、窗口磁吸/置顶、运行中机器模组槽解锁。
- QuickSL继承旧ID `dsh.probablystolen.tools`、DLL/安装目录、PSTools配置/日志/备份及工坊身份。
  工具箱单独目录、Git、ID、DLL、设置、日志、版本；尚无远程或工坊发布。
- 两者均可单装。不增加公共前置DLL，不硬引用对方或第三方DLL，不动态加载/生成代码。
  小型UI/语言/配置辅助源码各自持有，以换取独立安装和构建。
- 这不是新的快照后端重写，静态helper未改。QuickSL旧工具源文件保留但被Mono csproj明确排除；
  不能按源文件仍在目录里就推断工具仍会执行。反之也要检查编译产物确无业务挂点残留。
- 已发布旧0.9.9仍包含工具，与本候选同号但不是同一包；必须按协议和哈希识别。
  正式发布前需另行决定QuickSL版本并同步版本声明、重建/重测，不可直接覆盖同号发布。

## 3. 协作协议和面板

入口：QuickSL `ToolboxBridge.cs`；工具箱 `ToolboxIntegration.cs`。
通过公开 `ModLoader.Mods` 找启用实例，将 `LoadedMod.Instance` 作为 `IServiceProvider`，
请求 `typeof(IDictionary<string, object>)` 并验证 `protocol=1`、`modId`。
只传BCL和公开游戏类型；常规1Hz发现，恢复前强制刷新。

| 服务 | 提供方与类型 |
|---|---|
| `protocol` / `modId` | 两边，int / string |
| `capturing` / `keys` | 两边，Func<bool> / Func<KeyCode[]> |
| `panelKey` / `actionKeys` | 两边可选，Func<KeyCode> / Func<KeyCode[]> |
| `busy` / `loading` / `openToolbox` | QuickSL，Func<bool> / Func<bool> / Action |
| `legacySettings` | QuickSL可选，Func<IDictionary<string,string>>，仅工具白名单 |
| `reset` / `mount` / `unmount` | 工具箱，Action / Action<CustomUIWindow,RectTransform> / Action |

双装由QuickSL提供“回溯 / QuickSL设置 / Shift工具箱”外壳，工具箱维护自己的页面。
单装工具箱使用独立原生滚动窗口；默认F8，主菜单/进档均可打开；原版Mod设置齿轮也有入口。
两边面板键可共用F8，相同绑定仅QuickSL消费一次；业务键不享有这个例外。
改键当帧互相屏蔽输入，左右Shift视为同一详情键。

工具箱在QuickSL回溯/载入/settling期间暂停自动化；恢复写图前清空自动化队列、鉴定和临时详情。
reset异常不能吞掉后继续恢复；unmount只是UI清理，异常限频记录，不应卡死QuickSL面板释放。
工具箱单装也有原生载入prefix/finalizer门控。旧QuickSL无协议时暂停工具箱重复业务，
保留设置访问，不修改/禁用对方模组。

## 4. 设置、权限、日志

- QuickSL仍需 `files`，工具箱manifest为 `<Capabilities />`。后者只用官方
  `ModHelper.Read/WriteModDataFileText`、`DeleteModDataFile`、`GetModDataFolder`。
- 工具箱数据：`<persistentDataPath>/ModData/shift_toolbox/`；文件
  `ShiftToolbox.settings.xml`、`ShiftToolbox.log`。不能在运行时逃出该目录读根PSTools文件。
- 兼容QuickSL启用后，通过已验证协议导入工具白名单；不导入回溯、面板键/位置、公告/日志偏好。
  当前已有/显式修改的工具箱值优先，不写回QuickSL。
- 同一会话内延迟发现QuickSL可更新已有绑定；`Migration.QuickSL099`标记一次性完成。
  **先单装保存默认值、退出、再启用QuickSL的语义见第7节，不能声称已完整覆盖。**
- `Migration.PanelDefaultF8`一次性修正早期None默认，保留自定义键和迁移后主动停用。
- 工具箱原生注册两个布尔设置使齿轮可用；XML启动时为真源，原生项双向镜像，disable取消订阅。
- 配置保存先写最近一次有效XML到 `.bak`，再写主文件，不是原子替换。
  主文件缺失/损坏时读备份；损坏原件另存 `.unreadable-UTCtimestamp`，均无效则警告并用默认。
- 本地Deploy脚本仅在专属目标缺失时验证/备份/等hash复制早期本地候选XML，原文件不改。
  这是开发部署迁移，不是玩家运行包获得根目录文件访问权限。
- 专属API无append，工具箱日志使用有界整文件读写：256KiB当前文件加3份归档。
  日常日志稀疏，异常保留，游戏ModLog也接收关键日志；不要夸大为零IO开销。
- 中英文说明/设置均保留。公告为双语拆分说明、8秒、报告退出后才展示/确认；双装只QuickSL显示。

## 5. 回溯失败修复

初轮拆分包捕获成功，但恢复写前因 `A checkpoint view or its parent was destroyed` 全部拒绝。
23:25诊断定位到原生Element Node：node/rect/image存活，捕获时本就没有Transform父对象。
`CustomerCheckpoint.VisualState`新增 `_hadParent` 区分“原本无父”和“捕获的父对象后来销毁”；
后者及真正丢失的视图/贴图仍严格拒绝，未用跳过全部视觉校验的方式规避。
恢复Transform原有 `SetParent(null, false)` 路径保留。

模型测试用托管Unity wrapper验证四个分支，不调用Unity原生Transform，不能视为真实销毁证明。
玩家随后单装/双装基本验收回报“看起来没什么问题”。本地日志在23:28候选后共8次
RESTORE OK：510/557/474/578/612/555/568/573ms，全部 `diff=0`、`releaseOk=True`、
`finalFallback=False`，包含顾客、跨顾客、晨间和手动往返；未发现新失败。
普通捕获约290-364ms、晨间约985-1150ms，非同操作A/B，不据此宣传额外提速。

## 6. 验证结果和可复跑命令

本轮准备审查再次通过：QuickSL 480参考、13存档动作、14历史/设置IO；
ReleaseCompatibility 56挂点/76字段、无工具业务挂点残留、视觉预检模型、真实图往返及官方扫描；
工具箱完整 `Test.ps1`：56现行生产规则、54挂点、原生manifest/配置/协议/化学契约、独立游戏Mono。
拆分修复阶段还通过22可选规则、QuickSL单装/双装Mono、真实VBE/Proper Signboards隔离夹具。
500组比例耗水、408组确定品质分配、八种化学开关组合包含在相应夹具中，不另冒充独立测试套数。

480参考中包含 `PreSplitToolRules` 冻结历史源，不等于现行工具箱覆盖率；新工具规则必须测工具箱源码。
离线Mono没有Unity引擎，已知icall/out-of-sync输出不代表游戏Mono损坏，也不证明真实输入/渲染正确。
本轮只有1.0.0.16 beta原始程序集，0.46D原生扫描/实机未重验。

扫描实际状态：工具箱773方法，Capabilities/Findings/Pending/HardFlags/Error均空；
QuickSL3997方法，Capabilities仅files、HardFlags/Error空，**存在files类Pending记录**
（SaveFiles及历史挂点等）。无退役能力Pending，扫描未拒绝，files在manifest声明。
不能继续写“两包Pending都为空”；详看 `ReleaseCompatibility/Program.cs` 对退役能力的门槛。

```powershell
# QuickSL仓库；下列测试不会部署，也不使用玩家真实存档作IO夹具。
$game = 'E:\SteamLibrary\steamapps\common\Probably Stolen'
$quick = 'D:\Program\GameProject\ProbablyStolenMod\src\ProbablyStolenTools\bin\Release\Mono\ProbablyStolenTools.dll'
$tools = 'D:\Program\GameProject\ShiftToolbox\src\ShiftToolbox\bin\Release\Mono\ShiftToolbox.dll'
dotnet build src/ProbablyStolenTools/ProbablyStolenTools.csproj -c Release "-p:GameDir=$game"
# 主项目构建会覆盖为单beta helper，最终扫描/打包必须恢复双profile表。
Copy-Item out/20261126-fixes-merged/ProbablyStolenTools.StaticLayouts.dll src/ProbablyStolenTools/bin/Release/Mono/ProbablyStolenTools.StaticLayouts.dll -Force
dotnet run --project tests/CheckpointTests -c Release
dotnet run --project tests/OptionalModCompatibilityTests -c Release
dotnet run --project tests/GameSaveActionTests -c Release
dotnet run --project tests/NativeSaveHistoryTests -c Release
dotnet run --project tests/ReleaseCompatibility -c Release -- $game $quick release/universal/manifest.xml
dotnet build tests/CapabilityMigrationProbe/MonoFixture -c Release
dotnet build tests/CapabilityMigrationProbe/MonoHost -c Release
.\scripts\Test-SplitMono.ps1 -GameDir $game -QuickOnly
.\scripts\Test-SplitMono.ps1 -GameDir $game -ToolboxDll $tools

# ShiftToolbox仓库；独立验证时省略可选QuickSLDll即可。
.\scripts\Test.ps1 -GameDir $game -QuickSLDll $quick
```

两个仓库 `git diff --check` 通过；QuickSL仍有Git行尾转换提示，不是内容错误。
审查必须逐步检查命令退出码；不要将整个片段当作自动遇错终止脚本。

## 7. 自检发现的待审边界

这部分不是“已修复”清单，不能因基本实机验收通过就删除。准备审查未追加生产改动。

1. **改键互换只验证了新键，未验证被交换到另一动作的旧键。** 两边Plugin.SetHotkey
   （QuickSL约118行、工具箱约83行）仍保留同模组冲突交换。例：双装共享F8，工具箱将面板键
   改为自己的详情键Shift，可能把详情反向换到QuickSL面板F8。请验证完整交换后的跨Mod冲突，
   不能只测PeerKeyUsed单次查询；现有契约没覆盖该事务路径。
2. **QuickSL改键拒绝提示可能被成功提示覆盖。** `Panel.cs` EndCapture约560行调用void
   SetHotkey后仍无条件显示“已改为”。新跨Mod拒绝分支可导致配置未变但反馈成功。
   工具箱没有同一段成功toast，但也需要审查改键失败时标签/输入门控。
3. **迁移优先级跨重启需要确认产品语义。** 工具箱Load把所有XML条目标为显式；Bind的默认值
   也会在首次Save落盘。因此“先单装、默认保存、退出，再双装”会保留这些已保存默认值，
   不再导入同名旧偏好。这符合“已有文件优先”的保守策略，但不等于“所有未手动改项以后都能迁移”。
   当前延迟导入测试只覆盖同一会话，没有跨重启来源标记；若需要后者，应补持久化来源和测试。
4. 专属接口非原子保存、IO失败时启用行为、日志整文件写入、异常去重是否足以满足容错，
   请按本模组实际稀疏写入频率评估，不为了原子rename重新申请files。
5. 协议缺字段/异常、同帧卸载、共享页销毁/语言切换、不同面板键和重置默认的组合仍需审查。
   此轮验证不是所有极端加载顺序/所有语言/所有第三方Mod的穷举。

## 8. 产物来源与当前安装状态

验收候选及当前源码构建：

| 文件 | 字节 | SHA256 |
|---|---:|---|
| ProbablyStolenTools.dll | 459264 | 977E9E6DD102CB55D7AC4951284674514031C9280BA6029F2DAFA28E797C2F57 |
| ProbablyStolenTools.StaticLayouts.dll | 808448 | 6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23 |
| ShiftToolbox.dll | 192000 | 1EA02591ACB9600F5D849CF2342E5CFECD6502432FB2428E970212B89A656B68 |

候选部署回执：QuickSL `out/full-release-deploy-20261126-232753/deployment.json`，
工具箱 `out/deploy-20261126-232816/deployment.json`。当时102个既有玩家文件校验未变，
不表示后续玩家测试整个数据目录仍不变。

**准备审查期间，安装目录QuickSL已恢复成旧发布0.9.9，未覆盖它：**
`E:\SteamLibrary\steamapps\common\Probably Stolen\Mods\ProbablyStolenTools\ProbablyStolenTools.dll`
为572928字节 / `DF901AB77E4F9DCB947AFAD90A533411E97E4563D39DB7245A9AA1E3C0E5562D`，
manifest也为旧带工具说明；哈希与 `out/quicksl-0.9.9-universal/ProbablyStolenTools/` 发布包一致。
不能确定是玩家操作还是其他同步，不擅自回滚。工具箱安装文件仍与上表一致，helper也未变。
**不能拿当前游戏目录的QuickSL DLL代替本次候选做code review或双装协议验收。**

本轮复跑始终对准仓库bin中的候选。若重建哈希变化，先核对源码/编译元数据和通用helper，
不要仅以时间戳或相同0.9.9标号断定等价。今后部署仍须先关游戏、按原脚本备份与核验，
不得为审查修改玩家存档、设置、信任记录或其他Mod。

## 9. 请审查者返回

优先按严重度报告数据损坏、回溯/原生载入回归、重复执行、热键/生命周期/迁移问题，附文件和行号。
区分确定缺陷、产品语义待定、可选重构；特别回应第7节，而非把已知限制当成已测通过。
先给审查结论，不自动提交/推送/上传/部署；涉及两个仓库的修改应各自记录并联合回归。
常规后续功能可每仓库独立对话，协议和迁移仍由协调任务一起维护。
