# 快速SL | QuickSL

[中文]() | [English](README_EN.md)

《深空当铺 / Probably Stolen》的实时回溯模组：把当铺状态切回任意快照节点，随时回到刚进店、刚闭店或手动记录的时机。

## 功能

- 顾客开场对话结束后自动建立节点，另有手动节点、闭店节点和同场景返店节点。
- 保留当天与昨天；每天最多 20 个普通节点，另有一个开业前锚点。
- 双击节点或用键盘选择回溯；只浏览旧节点不会删除未来，出现新分支后才丢弃。
- 被丢弃的节点折叠显示、不能回溯；文件读档会让旧的记忆节点失效。
- 可关闭自动建点、禁用记忆节点，或让整日回退改走原版读档。
- 快速重载当前槽位最近的磁盘存档、重掷后续随机数（默认不绑定按键）。
- 不主动保存、不覆盖你的存档；游戏保存时保留有总量限制的备份。

## 安装

- **创意工坊**：订阅 [Probably Stolen Tools / QuickSL](https://steamcommunity.com/sharedfiles/filedetails/?id=3829188627)。工坊当前为 0.9.x 完整版，自带工具功能。
- **手动安装**：将压缩包里的 `ProbablyStolenTools.dll`、`ProbablyStolenTools.StaticLayouts.dll`、`manifest.xml` 放入 `<游戏目录>\Mods\ProbablyStolenTools\`，在游戏模组列表启用并重启。
- **GitHub 版本**：1.0.0 起工具功能已拆到 [Shift工具箱](https://github.com/MeadalFlute/ShiftToolbox)。想要鉴定、标注、化学辅助、窗口磁吸等，需要另外安装工具箱；两者可单独使用，也可同时安装。

旧版 0.9.x 包仍自带工具功能，与新拆分版不要混用。

## 快捷键

| 默认按键 | 作用 |
|---|---|
| F5 | 回到上一天开业前 |
| F6 | 选择上一个节点；↑/W、↓/S 切换，Enter 确认 |
| F7 | 回到本日开业前 |
| F8 | 显示或隐藏面板 |
| F9 | 建立手动节点 |
| 未绑定 | 快速重载存档、重掷随机数 |

快捷键可在设置中更改，支持鼠标中键和侧键。Esc 取消捕获，Backspace 恢复该项默认，Delete 停用。与工具箱的业务键冲突时拒绝占用。

## 面板

面板可拖动、缩放和收起。主菜单 → 模组 → QuickSL → 齿轮 → **打开QuickSL功能面板**，进档前也能调整设置。
与工具箱同时安装时，面板显示“回溯 / QuickSL设置 / Shift工具箱”三个页签，两边各存各的设置。

## 兼容说明

- **Proper Signboards 1.0.48**：已适配保存挂位与亮度；未安装时不做处理。未知版本只提示并跳过专项适配。
- **Claims & Uprisings**：仅文件读档安全共存。首次检测到它时会打开“禁用记忆节点”。
- 未知游戏布局会拒绝建点并给出原因，不会为了表面兼容漏存字段。
- 极端规模的第三方对象可能触及快照上限；上限可在模组设置里调整。

## 反馈

请附日志：`PSTools.log`、`PSTools_errors.log`、`PSTools_history.log`，以及 `Player.log`、游戏与模组版本、其他模组列表。设置面板可直接查看末尾日志并复制路径。
工具类问题请附 `ShiftToolbox.log`；双装交互问题最好两边日志都附。

## 开发

需要 .NET 8 SDK 与游戏程序集；游戏路径不进入 Git。配置 `src/ProbablyStolenTools/local.props` 或设置 `PROBABLY_STOLEN_FULL_DIR`。

```powershell
dotnet build src\ProbablyStolenTools -c Release
dotnet run --project tests\CheckpointTests -c Release
dotnet run --project tests\OptionalModCompatibilityTests -c Release
.\scripts\Test-SplitMono.ps1 -GameDir "<游戏目录>" -QuickOnly
.\scripts\Test-SplitMono.ps1 -GameDir "<游戏目录>" -ToolboxDll "<ShiftToolbox.dll>"
```

正式包必须从未修改的源码构建，并合并两个游戏版本的布局表：

```powershell
.\scripts\Package-FullRelease.ps1 -GameDir "<游戏目录>" -ManifestPath release/universal/manifest.xml `
  -AdditionalLayoutPaths "<稳定版布局helper>" -OutputRoot "<输出目录>"
.\scripts\Deploy-FullRelease.ps1 -GameDir "<游戏目录>" -PackageDirectory "<输出目录>\ProbablyStolenTools"
```

发布版本号以 `release/manifest.xml` 为准，三份 manifest 与 `Plugin.Version` 必须一致。
不新增公共前置 DLL、不分发构建工具或测试宿主；离线通过不等于实机验收。
维护边界、协议与更详细的约定见 `AGENT.md`。

## 许可证

本项目以 [AGPL-3.0](LICENSE) 发布。可以自由使用、修改和再分发；如果分发修改版，或以网络服务形式提供，需要按同一许可公开对应的完整源码。
