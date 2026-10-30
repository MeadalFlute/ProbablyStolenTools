# Probably Stolen Tools

《深空当铺 / Probably Stolen》的自研辅助 mod，正式版适配版本 **0.6.0**。
正式版为 Unity 2021.3.45f2 / Mono / x64，使用游戏自带的 **IMod / Harmony**，无需额外加载器。

## 当前部署

- 游戏目录：`<GAME_DIR>`
- 自研插件：`Mods\ProbablyStolenTools\ProbablyStolenTools.dll`，同目录有 `manifest.xml`。
- 适配版本：游戏 `VERSION 100-Rev1`，Steam BuildID 26274380。
- 配置：`%LOCALAPPDATA%Low\Questing Goose Studio\Probably Stolen\PSTools.settings.xml`。
- 首次启动需在游戏 Mod 列表启用 **Probably Stolen Tools**，确认文件与反射权限，按游戏提示重启。
- 旧 Demo 安装与配置保留；旧 Inspector IL2CPP DLL 未移入正式版。

正式版不要安装旧 Demo 的 IL2CPP 代理 DLL、MelonLoader 或 BepInEx 来加载本 Mod。

## 检查点

| 默认按键 | 作用 |
|---|---|
| F6 | 将当前顾客恢复至开场对话结束后的内存检查点，无文件恢复兜底 |
| F7 | 回到本日开业前的文件节点，经主菜单重新载入，不自动开店 |
| F5 | 回到上一天的开业前文件节点 |
| F8 | 显示或隐藏检查点面板 |

**键位可改**：F8 → 设置 → 快捷键，点任意一行后按下新键即可；Esc 取消，Backspace 恢复该条默认值，底部「恢复默认快捷键」一键还原全部。若新键与其他动作冲突，两者会自动交换，避免两个功能共用一个键。

- 列表保留同日最近 10 位普通顾客，并有独立的开业前、闭店节点；**双击节点**恢复。
- 回溯不立即删除后续节点；真正接待下一位顾客形成新分支后才截断。
- 闭店保留顾客节点；同日、同店、同场景内可在营业与闭店节点间恢复。
- **离店、过夜、场景或店铺变化时释放内存检查点**，尚不支持跨场景夜间活动回溯。
- 检查点窗口使用游戏原生窗框与控件，可拖动、缩放、收起，设置保存于配置文件。设置区支持滚动。

## 鉴定与设置

F8 → 检查点面板 → 设置：

- 自动鉴定报价、自动提交、始终覆盖报价。
- 跳过普通顾客对话，保留原生录音机记录与业务回调；不代选选项、不跳特殊事件。
- 双击可鉴定物品时使用已拥有的放大镜；其他物品保留原生双击行为。
- **显示快速鉴定摘要**：即时开关自研鉴定摘要，跨重启保存。

快速鉴定摘要显示本地化的实际属性、可用检查项目及估值。窗口沿用第三方 Inspector 所用的**游戏原生 UI 框架**，不依赖其 DLL。**拖动“快速鉴定”标题栏**调整位置，正文区域仍可滚动；松开后保存位置。关闭重开鉴定页和重启游戏时读取已保存位置，分辨率变化时约束在原生 overlay 可用区域内。

水没有原生鉴定页：声明与实际水质不同，在水质标签后追加实际等级；相同时标签绿色，不改变原生检测业务。

摘要开关不影响水质标注、双击鉴定和自动报价。第三方 Inspector 不属于正式版安装包。

正式版使用独立 XML 配置；设置即时保存，保留 `.bak`，例如：

```xml
<ProbablyStolenTools>
  <Entry key="Inspection.ShowQuickInspection" value="True" />
  <Entry key="Inspection.SummaryPositionX" value="1" />
  <Entry key="Inspection.SummaryPositionY" value="1" />
  <Entry key="Inspection.DoubleClickToInspect" value="True" />
</ProbablyStolenTools>
```

鉴定坐标是可移动范围内的归一化比例，0 为左/上，1 为右/下。首次部署在新配置不存在时迁移 Demo 的自研设置和窗口位置，保留源 TOML。Debug 自动测试默认关闭。

## 存档安全

- 不主动调用 `SaveGame()`，只复制并验证游戏自然写出的开业前存档。
- F6 为内存恢复，不覆盖槽位。F7 是用户主动触发的文件恢复，会覆盖当前槽位，但先留 `.pre_restore` 备份。
- 不覆盖工作区 `backup_save`。部署前另建时间戳备份，校验已有存档与原游戏文件不变。
- 存档目录：`%LOCALAPPDATA%Low\Questing Goose Studio\Probably Stolen`。
- Demo 和正式版共用此目录；不要用旧 Demo 备份覆盖正在使用的正式版存档。

## 项目结构

```
src/ProbablyStolenTools/         Mod 本体（Harmony 补丁、快照引擎、原生 UI、配置）
tests/CheckpointTests/           离线测试：快照/状态图往返，47 项
tests/ReleaseCompatibility/      对着正式版 DLL 校验补丁目标与私有字段（51 入口 / 68 字段）
scripts/Deploy-FullRelease.ps1   备份 + 部署到游戏 Mods 目录
release/manifest.xml             Mod 清单（ID、版本、能力声明）
docs/workshop/                   Steam 创意工坊介绍文本（中/英）
research/                        逆向与设计记录，按版本归档
tools/                           自用逆向工具源码（MetaDump / ILDump）
AGENT.md                         交接文档：架构、约束、验收顺序
```

游戏侧的关键挂点集中在这几个文件：

| 文件 | 负责 |
| --- | --- |
| `CustomerCheckpoint.cs` | 顾客内存快照的捕获、恢复、分支截断 |
| `Rewind.cs` | 文件节点（开业前/上一天）、主菜单重载、按键入口 |
| `ManagedStateAccess.cs` | 托管对象图的遍历与字段规则（第三方类型降级为不透明） |
| `StateGraph.cs` | 槽位/对象/引用的序列化与差异校验 |
| `DoubleClickInspectionPatch.cs` | 双击鉴定 |
| `Panel.cs` | F8 面板与设置项 |

## 用你自己的 AI 适配其他 Mod

本项目把源码和交接文档一起公开，就是为了让适配工作可以由**任何人**完成，而不必等我逐个处理。

```
1. clone 整个仓库（不要只下载 DLL）
2. 把“这个仓库 + 目标 mod + 冲突现象的日志”交给你的 AI 助手，提示它：
   “阅读 AGENT.md，按 <目标 mod> 的改动把 <功能> 改为兼容实现，不要破坏现有快照约束”
3. 本地验证：
   dotnet build src\ProbablyStolenTools\ProbablyStolenTools.csproj -c Release
   dotnet run -c Release --project tests\CheckpointTests\CheckpointTests.csproj
   dotnet run -c Release --project tests\ReleaseCompatibility\ReleaseCompatibility.csproj -- "<游戏目录>" src\ProbablyStolenTools\bin\Release\Mono\ProbablyStolenTools.dll release\manifest.xml
4. .\scripts\Deploy-FullRelease.ps1 部署，进游戏验收
5. 提 PR，附上改动说明与日志
```

`AGENT.md` 里写了补丁挂点、快照字段的可遍历规则、禁止事项（不主动 `SaveGame()`、恢复前必须留 `.pre_restore`）以及验收顺序，足以让不了解本项目的 agent 独立完成一次适配。

需要注意的现实情况：这个游戏的 Mod 生态没有统一框架，各家实现差异很大，兼容问题往往不是"改一行"能解决的。作者本人尚未完整体验原版内容，不会主动为第三方 Mod 做适配，这类工作更适合由实际使用该组合的玩家完成。

## 构建与测试

工作区：`<REPO>`。
默认正式版目标为 netstandard2.1，引用本地正式版 `Probably Stolen_Data\Managed`。

游戏路径不写进仓库。首次构建前把 `src\ProbablyStolenTools\local.props.example` 复制为 `local.props`，填上自己的游戏目录（该文件已在 `.gitignore` 中）：

```xml
<Project>
  <PropertyGroup>
    <GameDir>C:\Program Files (x86)\Steam\steamapps\common\Probably Stolen</GameDir>
  </PropertyGroup>
</Project>
```

也可以用环境变量 `PROBABLY_STOLEN_FULL_DIR`（正式版）与 `PROBABLY_STOLEN_DEMO_DIR`（Demo）代替 `local.props`。

```powershell
dotnet build src\ProbablyStolenTools\ProbablyStolenTools.csproj -c Release
dotnet run -c Release --project tests\CheckpointTests\CheckpointTests.csproj -- "%LOCALAPPDATA%Low\Questing Goose Studio\Probably Stolen\save_0.es3"
dotnet run -c Release --project tests\ReleaseCompatibility\ReleaseCompatibility.csproj -- "<GAME_DIR>" src\ProbablyStolenTools\bin\Release\Mono\ProbablyStolenTools.dll release\manifest.xml
.\scripts\Deploy-FullRelease.ps1
```

Release 输出：`src\ProbablyStolenTools\bin\Release\Mono\ProbablyStolenTools.dll`。部署脚本备份后复制 DLL 和 manifest，仅在新配置不存在时迁移设置，不启动游戏。源码仍支持 `-p:GameRuntime=Il2Cpp` 编译 Demo 目标，输出旧 `bin\Release`，不得向正式版部署此输出。

0.6.0 编译 0 警告、0 错误，48 项离线测试通过。正式版 51 个 Harmony 入口与参数类型、68 个字段、实际官方 Mod 扫描器、原生 XML 配置往返及真实游戏数据类型的快照往返均通过；这些不能代替 Unity Mono 运行和 UI 验收。
日志位于存档目录 `Player.log`、`GameLog_*.txt`、`PSTools.log` 和 `PSTools_history.log`。

## 版本管理

- **唯一真源是 `release/manifest.xml` 里的 `ModVersion`**，同时对应游戏 Mod 列表里显示给玩家的版本号；`README`、`AGENT.md` 里的版本描述次之。
- 每次发布打 **annotated tag**，格式 `vX.Y.Z`（例如 `v0.6.0`），标签说明写该版本的一句话主题。
- 版本号规则：`X` 只在出现跨大版本适配（如 Demo → 正式版）时递增；`Y` 为功能/行为变更；`Z` 为修复与文案（不改变行为）。
- 发布流程：改 `manifest.xml` 的 `ModVersion` → 构建 → 跑两个测试 → 部署验收 → 提交 → `git tag -a vX.Y.Z` → 推送。

## Demo 启动耗时（历史）

这份 MelonLoader 安装的日志显示，每次启动约有 18 秒在重复生成 IL2CPP 代理。当前生成器缺少 StrippedCodeRegSupport 插件，同时启用了离线生成，包检测因此每次将缓存标记为需要重建；并非据此就能认定 MelonLoader 固有地比 BepInEx 慢。

0.5.2 没有修复此问题。正式版采用原生 Mono Mod 加载，不经历这段 IL2CPP 代理生成；尚未实测正式版启动耗时。历史证据见 `research/archive/0.5.2-native-ui.md`。

## 交接

- 当前实现、安全约束及验收顺序：`AGENT.md`。
- 本轮设计、测试和部署记录：`research/0.6.0-full-release.md`。
- 0.4.x / 0.5.x 等测试版时期的记录已归档到 `research/archive/`，**不能**作为当前加载器或部署指南。


## Reporting issues

Please open an issue with **logs attached** (drag & drop the files, don't paste screenshots):

| File | Location |
| --- | --- |
| `Player.log` | `%LOCALAPPDATA%Low\Questing Goose Studio\Probably Stolen\Player.log` |
| `PSTools.log` | same folder |
| `PSTools_history.log` | same folder |

Include the mod version (in-game Mod list), your game build (full release / demo), and whether other mods are installed.
