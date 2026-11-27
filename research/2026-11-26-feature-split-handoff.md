# QuickSL / Shift Toolbox 拆分交接

日期：2026-11-26（Asia/Hong_Kong）。本轮为本地开发候选，未推送、上传工坊或创建远程仓库。
QuickSL 基线 `5726538` / 已发布 0.9.9；QuickSL 暂未升版。
最终审查入口现为 `research/2026-11-26-feature-split-review-handoff.md`；
其中包含玩家基本验收、待审边界和安装目录后来恢复旧发布包的说明，优先于本文阶段记录。
工具箱为独立目录/独立 Git 仓库 `D:\Program\GameProject\ShiftToolbox`，
ID `shift.toolbox`，版本 0.0.1，作者米德。独立仓库首次本地提交 `5cc3131`，
无远程；两仓库后续修复均保留在工作区待审查，不混入用户原有修改一起提交。

## 本轮完成

| 归属 | 内容 |
|---|---|
| QuickSL | 内存捕获/恢复、跨天节点、文件备份/回退、快速重载、重掷随机数、存档安全及回溯兼容适配 |
| Toolbox | 自动报价/鉴定摘要/双击放大镜、对话快进、水质/酿酒/化学标注、全部化学自动化/预测、Shift详情/强制提示/固定详情、窗口磁吸/置顶、机器模组仓解锁 |
| 各自保有 | 配置、日志、公告调度、语言/UI小型辅助源码；不是新的公共前置 DLL |

QuickSL 保留原 Mod ID、DLL、安装目录、PSTools 配置/日志/备份名、工坊身份与通用静态表。
工具箱不包含快照引擎或 static helper，不引用 QuickSL；QuickSL 同样不引用工具箱。
没有增加运行时权限，没有动态程序集加载或外部框架。

旧 0.9.9 发布包与本候选版本暂同号，不能只看版本号判断是否完成拆分。
工具箱检测旧包缺少协议时暂停自己的业务，避免重复自动化；不修改对方状态。
正式发版前需要按既有政策升 QuickSL 补丁版本并同步清单/Plugin、重建/重测/再部署。

## 协作和面板

通过原版公开 `ModLoader.Mods` 查启用实例，用 BCL `IServiceProvider` 请求
`IDictionary<string, object>`，验证 `protocol=1` 和 `modId`。
不反射对方内部实现，不用稳定版缺失的新版 ModHelper API。

| 提供者 | 协议成员 |
|---|---|
| 两者 | protocol、modId、capturing: Func<bool>、keys: Func<KeyCode[]>；可选 panelKey: Func<KeyCode>、actionKeys: Func<KeyCode[]> |
| QuickSL | busy/loading: Func<bool>、openToolbox: Action；可选 legacySettings: Func<IDictionary<string, string>>，仅工具白名单 |
| Toolbox | reset: Action、mount: Action<CustomUIWindow, RectTransform>、unmount: Action |

常规 1Hz 发现，恢复前 Reset 强制刷新。回溯/加载/settling 时工具箱暂停自动化，
回溯前清空排队搅拌、临时详情/鉴定等状态；Reset 失败向上抛，不能忽略后继续写业务图。
Unmount 只是 UI 清理，异常限频记录，不阻断 QuickSL 释放面板。
工具箱单装也通过原生入口/LoadGame 的 prefix/finalizer 清理并暂停加载期间的自动化。

双装面板由 QuickSL 提供外壳：“回溯 / QuickSL设置 / Shift工具箱”三个页签。
工具箱自己构建、更新、保存其设置页；单装时用自己的原生滚动设置窗口。
原生模组设置均有打开入口，工具箱面板热键缺省 F8，详情热键缺省 Shift。
工具箱注册两个原生布尔项启用齿轮，再追加打开完整面板的按钮；XML启动时为真源，双向同步。
改键期间两边输入互斥；拒绝占用对方业务按键，左右 Shift 视为同一详情键。
面板键可共用F8，双装相同时仅QuickSL消费一次；主菜单F8也可打开。
恢复默认若与对方业务键冲突则留空，不重写对方；手工 XML 的已有重复业务键不自动重排。
`Migration.PanelDefaultF8` 一次性将缺失/早期候选None改F8，保留自定义键及迁移后主动停用。

## 配置和公告

- 工具箱只用官方 `ModHelper` 专属ModData读写，manifest `<Capabilities />`，不申请files。
  设置/日志位于 `<persistentDataPath>/ModData/shift_toolbox/`。
- 新配置 `ShiftToolbox.settings.xml`；兼容QuickSL启用后，通过校验后的协议只读白名单导入，
  不直接读取根目录旧文件。新文件已有值/用户已改值优先，延迟导入同步已有绑定；
  不导入回溯/快照、旧面板热键/几何、日志/公告偏好。单装先用自身默认，之后仍可导入未配置项。
- `Migration.QuickSL099=true` 随 Save 记录。保存先备份最近有效XML，再写主文件，非原子替换；
  缺失/损坏主文件尝试.bak，损坏原件另存.unreadable-UTCtimestamp，均无效时警告并用默认。
- QuickSL 暂保留旧 XML 中已不再绑定的工具键，便于初次导入/回滚；工具箱后续改动不写回它。
- 默认快速萃取/快速反应/快速烧水仍关闭，已有玩家设置按迁移值保留。
- 新日志 `ShiftToolbox.log`，256KiB、三份轮转；官方专属接口无append，使用有界整文件读写。
  常规日志稀疏，必要错误仍保留；日志窗口说明已区分归属。
- 两边公告改成拆分说明，8秒、仍等黑屏/两晨报退出，正常可见2秒或打开详情后确认。
  修订 `:20261126-feature-split-preview-1`；双装只由 QuickSL 展示，工具箱单装展示自己的。
- 未手动重置公告。部署脚本仅在专属目标不存在时，验证/备份/等hash复制早期本地工具箱XML，
  原文件不动；这不是运行时读取旧根配置的权限。真实启动后由游戏保存新配置和迁移标记。

## 验证

- 两边 Release 构建均 0警告/0错误；最终 C# 换行按 Git 的 LF 约定统一后重建。
- QuickSL：480参考断言、22可选兼容、13存档动作、14原生历史/设置IO 通过。
- 注意：480套含 `tests/PreSplitToolRules` 的拆分前冻结工具规则，用于旧参考兼容；
  不代表当前工具箱全部由此覆盖，不能继续在这份fixture开发工具功能。
- 工具箱：56条直接链接现行生产规则的断言；54原生挂点、原生manifest解析、官方扫描通过。
- 隔离XML迁移/保存：白名单、显式值/用户修改优先、延迟绑定、一次性标记、F8迁移、
  最近有效备份、主文件缺失/损坏与双损坏恢复通过；原生齿轮注册/双向镜像/取消订阅通过。
- 500组原版比例耗水、8种萃取/反应开关组合、150mL边界、50L/量器/滴管契约通过。
- 双程序集服务类型身份、单装/旧包暂停/忙碌/输入门控和左右Shift冲突判断通过。
- 不加载QuickSL/helper的工具箱独立游戏Mono进程通过；包括408组确定品质分配对照。
- QuickSL独立Mono、两包Mono、两包加真实VBE和Proper Signboards隔离Mono均通过：
  完整图六轮往返、跨天门控、VBE回调身份（只保留不执行）、灯牌三轮记录/挂位仓恢复等。
- QuickSL最终 56挂点/76字段，最终通用helper保留当前beta profile等价验证通过。
- QuickSL官方扫描仅 `files`（3997方法），存在files类Pending，HardFlags/Error空，无退役能力；
  工具箱Capabilities/Findings/Pending/HardFlags/Error为空（773方法），无QuickSL/helper硬依赖。
- QuickSL额外回归：捕获时本无父Transform的视图应允许恢复，有父而缺失则拒绝；
  托管Unity wrapper模型测试通过，不是Unity native销毁验证。

离线Mono未启动Unity，会输出已知的“cant resolve internal call / out of sync”警告；
这些不表示已证明真实UI可用，也不是本轮改动了游戏Mono。0.46D原始程序集仍不可用，
未重新验证其原生扫描/实机；不能用beta通过代替两版验收。

## 最终部署

游戏 `E:\SteamLibrary\steamapps\common\Probably Stolen`，beta 1.0.0.16，
MVID `d927a6e4-ae7a-4e65-a5e2-9f9624c131f3`。

| 产物 | 字节 | SHA256 |
|---|---:|---|
| ProbablyStolenTools.dll | 459264 | 977E9E6DD102CB55D7AC4951284674514031C9280BA6029F2DAFA28E797C2F57 |
| ProbablyStolenTools.StaticLayouts.dll | 808448 | 6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23 |
| ShiftToolbox.dll | 192000 | 1EA02591ACB9600F5D849CF2342E5CFECD6502432FB2428E970212B89A656B68 |

QuickSL回执：`out/full-release-deploy-20261126-232753/deployment.json`。
Toolbox回执：其仓库 `out/deploy-20261126-232816/deployment.json`。
上述部署时两边安装目录与对应构建一致，曾复核hash一致。后来安装目录QuickSL恢复旧发布
DF901AB7...，本轮审查未覆盖它，当前实际状态见最终审查入口。102个既有玩家目录文件部署时校验不变；
QuickSL设置未导入，早期本地工具箱XML按上述脚本复制至专属目录。部署时游戏已关闭，
未修改第三方DLL/游戏程序集。后续已启动游戏进入存档进行测试，不能用部署前hash说明
玩家测试后的整个数据目录未变。更早部署仅作备份，不可代替最终产物。

每次 QuickSL build 会覆盖为单beta helper。正式包/部署前恢复或重新合并双profile表；
当前合并表来源仍为 `out/20261126-fixes-merged`，未对新工具箱复制一份。

## 回溯失败定位及实机证据

22:35-22:42捕获成功，恢复均在写前预检失败。23:25诊断复现为
`Element Node parent=<none>; nodeAlive=True, parentAlive=False, rectAlive=True, imageAlive=True`。
原生部分视图捕获时本就无Transform父对象，旧验证无条件要求非空，误判为已销毁。
VisualState现记录`_hadParent`，仅有父而父对象失效时拒绝，其他视图/贴图严格验证不变。

23:28最终候选启动后，23:32-23:33本地日志四次RESTORE OK（客户节点、跨客户和晨间），
总耗时510/557/474/578ms，均diff=0/releaseOk=True/finalFallback=False。
该会话普通捕获290-364ms、晨间1060ms；非严格性能A/B。双装主菜单F8打开/关闭已观察。
真实物品贴图、工具箱单装F8/齿轮和剩余功能仍需玩家确认，不将日志正确等同视觉验收。
23:35玩家另做晨间/手动往返，两次RESTORE OK为612/555ms，同样diff=0且releaseOk=True。
累计六次成功，无新失败；23:36工具箱日志已显示QuickSL缺席的单装会话，入口效果仍待反馈。

## 需要玩家验收

1. 模组列表启用 Shift工具箱，按原版提示重启。双装进档后检查F8三页切换、滚动、
   收起/展开、缩放、关闭再开；两边原生设置入口应指向同一面板。
2. 检查以前的化学/酿酒/详情/快捷键设置是否导入；工具箱改动只影响新XML。
3. Shift水龙头/小容器/水培详情、强制提示、固定详情和窗口置顶各测试一次；
   双击鉴定切页、普通酿酒标注和自动搅拌/挤出选品做代表性回归。
4. 建立节点，修改物品后回溯，确认详情已清理、工具自动化随后正常恢复；
   快进对话与顾客建点并用，确认没有跳过应有节点或双执行。
5. 单独关闭QuickSL并按提示重启：原生工具箱设置可开、工具正常，无快照报错；
   单独关闭工具箱再重启：QuickSL建点/恢复正常，不残留工具功能。
6. 切中英、两边改键/恢复默认、退出重进、晨报关闭后公告可见和只展示一次。

初轮图形授权超时未绕过；后续QuickSL启用授权被拦时先询问，玩家手动启用后再测试。
玩家返回并按Escape后停止自动UI操作，剩余实机由玩家进行；没有修改信任记录绕过授权。
本轮没有DS审查结果。

## 后续工作流

两个仓库独立Git、独立构建、独立日志/版本。工具箱有自己的README/AGENT/Build/Test/Deploy；
当前暂无远程，不会擅自创建GitHub项目或工坊条目。
常规功能各自开对话；当前协调对话负责协议、设置迁移和联合部署。
审查应看QuickSL整个dirty工作区和工具箱整个初始仓库，而非只看QuickSL删除行；
QuickSL已有的21:05清理AGENT段为用户先前修改，本轮保留，不能视为拆分代码回滚。
正式上线前仍需独立code review和上述实机矩阵，不把本轮离线通过当作发布批准。
