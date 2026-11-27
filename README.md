# 快速SL | QuickSL

[English](README_EN.md) | 中文

《深空当铺 / Probably Stolen》的实时回溯模组。原名 Probably Stolen Tools，保留
Mod ID `dsh.probablystolen.tools`、DLL、安装目录、配置和现有工坊身份。当前版本 **1.0.0**：
快速鉴定、水质与酿酒标注、Shift 详情、化学辅助、窗口磁吸和置顶已迁至独立的
**Shift工具箱 | Shift Toolbox**（`shift.toolbox`），两者可分别单装，双装时共享面板、各自保存设置。
游戏使用 Unity 2021.3.45f2 / Mono / x64，自带 **IMod / Harmony** 加载器，不需要 MelonLoader 或 BepInEx。
通用包含 **0.46D 稳定版**与 **1.0.0.16 beta** 两套兼容数据，启动时按游戏版本自动选择。

完整版停在 **0.9.10**（分支 `Full`，含 hotfix：未启用灯牌模组时 F9/自动建点失效、
原生无父节点视图被误判而无法回溯）。拆分线自 1.0.0 起；工坊推送后中间位 +1、小版本归零，
本地每轮小版本 +1，GitHub release 与工坊推送同步发布，便于回退。已发布的旧 0.9.9 包仍
包含工具功能，不要把旧包当作拆分线。
设置页顶部支持中英名称、分类及配置键的模糊搜索，清空后恢复完整分组。

使用游戏自带 Mono / IMod / Harmony 加载器，不需要 MelonLoader 或 BepInEx。
通用包声明支持 **0.46D 稳定版**与 **1.0.0.16 beta**，按程序集指纹选择布局。
本轮仅有 beta 原始程序集可重跑，不能替代稳定版实机验收。

## 回溯功能

- 顾客开场对话结束后建立节点；手动节点、闭店节点及同场景返店节点。
- 同会话保留当天和昨天，每天独立最多 20 个普通节点，另有开业前锚点。
- 双击节点或使用键盘选择回溯；只浏览旧节点不删除未来，发生新分支后才丢弃。
- 被丢弃节点折叠显示，不能回溯；文件读档会使旧内存节点失效。
- F5 回到上一天、F7 回到今天开业前，默认优先使用内存晨间锚点。
- 可关闭自动建点、禁用内存节点，或仅让整日回退走原版文件读档。
- 快速重载当前槽位最近磁盘存档，或重掷后续随机数；两项默认不绑定按键。
- 内存后端为完整对象图与精确实体载荷，保留原对象身份，不是原版存档序列化。

## 面板和快捷键

| 默认按键 | 作用 |
|---|---|
| F5 | 回到上一天开业前 |
| F6 | 选择上一个节点；↑/W、↓/S 切换，Enter 确认 |
| F7 | 回到本日开业前 |
| F8 | 显示或隐藏面板 |
| F9 | 建立手动节点 |
| 未绑定 | 快速重载存档、重掷随机数 |

快捷键可在设置中更改，支持鼠标中键和侧键。Esc 取消捕获，Backspace 恢复该项默认，
Delete 停用；同一模组内冲突时交换，与工具箱业务键冲突时拒绝占用。
面板键可共用默认 F8，双装时仅由 QuickSL 消费一次，主菜单也可直接打开。
“恢复默认快捷键”遇到工具箱已占用的默认业务键时留空，不改变对方设置。
手工 XML 中的重复业务绑定仍需自行处理。

面板可拖动、缩放和收起，双装时显示“回溯 / QuickSL设置 / Shift工具箱”页签。
主菜单 -> 模组 -> QuickSL 齿轮 -> **打开QuickSL功能面板**，可在进档前调整设置。
工具箱也有自己的原生设置入口；单装时打开独立窗口，无强制前置。

存在 `dev.txt` 时，F8/F9 会与游戏开发工具冲突，请另行改键。
更新简报在黑屏加载结束、晨间/夜间报告关闭并稳定后展示，持续 8 秒；
点击查看详情，正常可见连续 2 秒或打开详情后才持久确认。双装只展示一份公告。

## 存档和兼容

- 不主动调用游戏保存，也不覆盖活动档。原版保存发生时保留有总量限制的备份；
  文件回退通过临时副本走原生加载，保留恢复前备份。
- 内存节点不跨游戏进程持久化。退出、实际重新加载或场景重建会使其失效。
- **Proper Signboards 1.0.48**：已核对构建保存挂位/亮度及独立挂位仓，恢复后刷新缓存；
  未安装不生效。未知构建只提示并跳过专项适配，仍可能存在灯牌回溯风险。
- **Claims & Uprisings**：仅文件读档安全共存，未完成其独立世界状态的内存适配。
  首次检测启用时一次性打开“禁用内存节点”；可手动强行开启，相关丢物风险不受理。
  冲突提示手动关闭，同一模组仅提示一次。禁用时仍列出匹配的“开业前 / 存档”节点。
- VBE 的回溯状态仍属 QuickSL；防止原料入桶溶解、工具标注避让等业务保护属工具箱。
- 未知布局严格拒绝建点，不以漏存字段换取表面兼容；大量第三方对象也可能触及限额。

## 快照限额和布局表

游戏模组设置可调整对象、字段槽和字节预算，Apply 同步 `PSTools.settings.xml`。
退出游戏后也可手动编辑，例如：

```xml
<Entry key="Snapshot.MaxObjects" value="300000" />
```

默认 200000 对象 / 3000000 字段槽 / 256 MiB 单份逻辑载荷。
提高上限不会增加低于原上限节点的遍历或按上限预分配；允许更大节点后，
实际内存和耗时可能增加。失败提示给出具体原因，可连同日志提交。

通用 helper 以 MVID 区分两个已收录游戏构建，不生成或动态加载运行时代码。
未知游戏构建需等待更新，或使用独立进程的 Static Layout Generator。
该工具不是通用第三方 Mod 适配器，生成成功不代表游戏 API 或未来规则已兼容。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Generate-StaticLayouts.ps1 -GameDir "<游戏目录>"
```

先关闭游戏。只替换生成的 `ProbablyStolenTools.StaticLayouts.dll`，备份放在 Mods 外；
默认本地安装可加 `-Install`。详见 [玩家工具说明](tools/StaticSnapshotLayouts/PLAYER-README.md)。
不要把生成器、Cecil 或测试 DLL 放进 Mods，工具附件需单独发布。

## 安装

当前正式工坊身份不变：[Probably Stolen Tools / QuickSL](https://steamcommunity.com/sharedfiles/filedetails/?id=3829188627)。
拆分候选尚未上传；手动安装放入 `<游戏目录>\Mods\ProbablyStolenTools\`，
保留主 DLL、通用 helper 和 manifest。仅声明 `files` 能力，无网络权限。
Shift Toolbox 需另外安装，不随 QuickSL 自动下载或动态加载。

## 开发

需要 .NET 8 SDK。配置 `src/ProbablyStolenTools/local.props`（参照 example），
或设置 `PROBABLY_STOLEN_FULL_DIR`。两仓库不互相引用工程或 DLL。

```powershell
dotnet build src\ProbablyStolenTools -c Release
dotnet run --project tests\CheckpointTests -c Release
dotnet run --project tests\OptionalModCompatibilityTests -c Release
.\scripts\Test-SplitMono.ps1 -GameDir "<游戏目录>" -QuickOnly
.\scripts\Test-SplitMono.ps1 -GameDir "<游戏目录>" -ToolboxDll "<ShiftToolbox.dll>"
```

先构建 `tests/CapabilityMigrationProbe/MonoFixture` 与 `MonoHost` 才可运行 Mono 脚本。
`CheckpointTests` 的 480 项包含拆分前工具规则的历史参考 fixture；
工具箱现行源码由其自己的 Rules/Contracts/Mono 套件验证，不把参考 fixture 当作现行工具测试。

主项目每次构建生成**当前游戏单版本表**，通用包必须重新合并稳定版表再扫描/部署：

```powershell
.\scripts\Package-FullRelease.ps1 -GameDir "<游戏目录>" -ManifestPath release/universal/manifest.xml `
  -AdditionalLayoutPaths "<稳定版原始helper>" -OutputRoot "<新输出目录>"
.\scripts\Deploy-FullRelease.ps1 -GameDir "<游戏目录>" -PackageDirectory "<输出目录>\ProbablyStolenTools"
```

正式打包拒绝 dirty 源码；开发部署不等同正式发布。不得分发构建工具、宿主表或测试宿主。
`Snapshot.SemanticDevelopmentChecks` 默认关闭，额外对照不应混入玩家发布配置。
唯一版本真源为 `release/manifest.xml`，三份 manifest 和 Plugin 版本须同步；
按既有规则正式推送前递增补丁版本，纯文档不单独升版。

详见 `AGENT.md` 和 `research/2026-11-26-feature-split-handoff.md`。
协作协议/迁移改动须联合验收，常规开发可每仓库一个对话；不新增公共前置 DLL。
离线通过不等于 Unity 界面、输入或实际回溯验收。

## 反馈

请附 `PSTools.log` 及轮转文件、`PSTools_errors.log`、`PSTools_history.log`、
设置文件、`Player.log`、游戏/模组版本及模组列表。设置中可查看末尾日志并复制路径。
工具问题附 `ShiftToolbox.log`，双装交互问题最好同时附两边日志。

其他模组适配前请先读 `AGENT.md`，保留未知布局拒绝、存档安全和官方权限约束，
完成构建、离线回归及实际组合验收后再提交 PR。
