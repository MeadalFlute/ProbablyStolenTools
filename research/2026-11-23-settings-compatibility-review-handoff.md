# 2026-11-23 设置与可选 Mod 兼容审查交接

## 状态与审查范围

- 当前为 0.9.5 开发候选，基于 HEAD `48652b2dcfff8bca45ea4629d72590c01679818b` 的未提交工作区；未升版、未推送、未制作正式发布包。
- 本文覆盖本轮设置调整，并连同此前尚未提交的 beta/VBE/Claims 兼容改动一起交接。上轮背景见 `research/2026-11-23-optional-mod-compatibility.md`；其中 Claims 强制禁用及旧部署哈希已由本文替代。
- `research/2026-11-19-persistent-checkpoint-feasibility.md` 是本轮前已有的独立文件，没有修改，不应误算进本轮实现。
- 不使用审查插件；审查者直接检查当前源码及新增文件。`git diff` 不显示 untracked 文件，尤其不能漏掉 `OptionalModCompatibility.cs`、可选兼容测试项目及测试脚本。

## 设置变化

| XML 键 | 缺省值 | 行为 |
|---|---|---|
| `Chemistry.AutoExtraction` | false | 快速萃取默认关闭；已有 XML 值不覆盖 |
| `Chemistry.RapidReactions` | false | 快速化学反应默认关闭；已有 XML 值不覆盖 |
| `CustomerCheckpoint.DisableMemory` | false | 设置页「禁用内存节点」；true 时禁止全部自动/手动内存捕获与恢复 |
| `Rewind.WholeDayUseMemory` | true | 设置页「整日回退使用内存」；仅影响 F5/F7，不禁用单个内存节点 |
| `Compatibility.HandledMemoryConflicts` | 空字符串 | 内部持久记录已处理的黑名单 Mod ID，逗号分隔，不作为普通开关展示 |

禁用文案（中文）严格为：

```text
存在mod冲突，已禁用内存节点。
可在设置中强行启用，但不受理因此导致的BUG。
```

无黑名单冲突、仅玩家主动禁用时显示「已在设置中禁用内存节点。」，不错误声称发现冲突。英文使用相同语义。

### 一次性处理与覆盖

1. 当前黑名单只有 Claims ID `yexiaoyang.InteriorExpansion`，不把 VBE 或 Perks 加入内存黑名单。
2. `Refresh()` 读取公开 `ModLoader.Mods` 后检查启用状态；只有黑名单 ID 尚未记录时，才将 DisableMemory 置 true、记录 ID，并输出首次处理日志。
3. 玩家把 DisableMemory 关掉后，继续帧更新、再次检测、停用再启用 Mod、配置写盘后重载均不再次覆盖该选择。
4. 移除/停用黑名单 Mod 不自动复位 DisableMemory；恢复内存功能需玩家自己关闭该开关，避免反向覆盖其主动禁用选择。
5. 强行启用只解除策略门控，不绕过对象预算、未知布局/MVID 拒绝或其他安全检查；不代表 Claims 已完成完整适配。
6. 审查后改为暂时关闭 SaveOnConfigSet，一起设置禁用值与已处理 ID，再恢复原值并显式保存；失败就地捕获、同错误去重、每 5 秒重试，会话内不重复覆盖玩家选择，重试保存最新值。未持久化即退出仍可能下次首次检测。最终实现、433/17 项测试与新部署哈希见 `2026-11-23-settings-compatibility-review-receipt.md`；下方旧测试数和部署记录仅为送审时历史。

### 整日回退与单节点

- `UseWholeDayMemory = CustomerCheckpoint.AutoCapture && CfgWholeDayMemory.Value`。AutoCapture 本身包含后端就绪、原节点总开关及 DisableMemory 检查。
- F7：允许内存时沿用今日晨间节点；不允许时走已有 Quick reload 路径，读取当前槽位的最近磁盘保存文件，不先保存。该磁盘文件可能是晨间或闭店保存，不承诺等于内存晨间状态。
- F5：允许内存且保留上一天时优先上一天晨间图；否则沿用槽位/周目匹配的历史磁盘文件选择。
- 关闭 WholeDayUseMemory 不影响 F9、面板单节点选择和恢复，也不停止自动建点；实际执行文件载入后，旧图按已有机制失效。
- 关闭 AutoCapture 仍沿用旧规则：手动节点可用、F5/F7 文件读档。
- 确认文案跟随有效路径；上一天节点保留关闭时，F5 也按文件路径说明风险。需要文件读档但确认 UI 不可用时拒绝执行，除非玩家已明确关闭确认开关。

### 设置排列

按鉴定相关开关、顾客对话、水质/酿酒标注、化学主开关与子开关、内存/自动建点/跨日/整日回退/确认、日志与存档动作、快捷键排列。

化学分割线随新位置移动，子开关仍缩进并受化学主开关约束。鉴定三个首项保持原索引依赖；没有改写窗口布局框架。21 个开关的布局模型已补测。

## 兼容边界（不能夸大）

- beta 当前为 1.0.0.16 / VERSION 100-Rev16，重建静态表与旧正式版 0.46D 精确 profile 合并；不是放宽未知版本检查。
- VBE 的名称/评分/快速反应干预排除沿用上一轮实现；桶装酿造换贴图、销毁/生成物品的 Unity 活体行为仍未由离线测试证明。
- Claims 仍没有独立房间根、静态服务、资源/部队以及视图生命周期的完整内存适配。原审计 DLL 是 1.1.18；本机最新启动日志已显示 1.2.13，本轮仅按稳定 Mod ID 执行冲突策略，没有重新完成 1.2.13 全量业务审计。
- `Rewind.Restore` 强制原生菜单文件载入、`PostLoadResume` 跳过额外顾客/卷帘门修复，均依据 **Claims 是否运行**，不依据 DisableMemory。玩家强行启用内存后，也不会意外失去文件读档保护。
- 仍无第三方程序集硬引用/必装依赖；没有新增权限，扫描只声明/使用 `files`。没有把生成器、反编译源码或第三方 DLL 放入部署包。
- 本次不修 Perks: Probably fun 的主菜单本地化补丁；此前日志已指向其 `FriendlyTalkRenamePatch` 访问尚未就绪的 PlayerStore。实机设置验收前需先排除这个独立菜单故障。

## 审查重点

- `Plugin.cs` 的缺省值、设置绑定顺序，以及旧 XML 值保留。
- `OptionalModCompatibility.cs` 的首次检测持久化、ID 精确匹配、重复刷新/重启不覆盖手动选择，以及实际冲突状态和有效禁用状态的区分。
- `CustomerCheckpoint.cs` 的 Enabled/自动入口/最终 Capture/Restore 门控，`CheckpointPicker.cs` 与 `Panel.cs` 的拒绝提示。
- `Rewind.cs` 两个整日入口、确认路径，以及独立于手动覆盖的 Claims 文件载入保护。
- `Panel.cs` 的 21 行设置分组/化学索引与两个鉴定子开关的禁用规则。
- 上一轮 `WineAppraisal.cs`、`LiquidComposition.cs`、`ChemistryAutomation.cs` 的 VBE 排除范围；不要把标签往返当成 Unity 贴图生命周期验收。

## 已执行验证

- `Test-StaticLayouts.ps1`：最终源码构建，430 项参考测试、14 项可选兼容测试、API 合同与官方能力扫描、实际游戏 Mono 静态表/完整图回归、可复现生成及错误 MVID 拒绝均通过。
- 实际游戏 Mono fixture：原自动门控 8 组合、新整日内存/禁用/自动开关 8 组合；单节点不受整日偏好影响；Claims 首次门控、手动覆盖、真实 XML Save 后重建 ModConfig 均通过。
- `Test-OptionalModCompatibility.ps1`：真实 VBE DLL 的回调包装身份、标签/分数/进度三轮图往返通过，最后再对最终合并 helper 重跑；未执行酿造业务回调。
- GameSaveActionTests：13 项通过。NativeSaveHistoryTests：14 项通过。
- 合并表逐 profile 等价核验通过；最终两个 DLL 扫描 `Capabilities: files`，`HardFlags` / `Error` 空。
- 布局模型覆盖 380/480/900 宽、21 个开关、不同快捷键/动作数量的行间距和滚动可达性。尚无 Unity 实机外观/点击验收。
- `git diff --check` 无空白错误，仅既有行尾格式提示。

可复查日志均在未入库的 `out/`：

```text
settings-20261123-static-suite.log
settings-20261123-optional-native.log
settings-20261123-optional-merged-native.log
settings-20261123-save-actions.log
settings-20261123-native-history.log
settings-20261123-merge.log
settings-20261123-universal-scan.log
settings-20261123-deployment.log
```

## 最终部署及构建来源

- 部署记录：`out/full-release-deploy-20261123-220539/deployment.json`。
- 主 DLL SHA256：`54C4DFD288FD09DCB30C1070777FDF19DBBEB5B391FA9B152C58953B8D985925`。
- helper SHA256：`6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23`，808448 bytes；包含 0.46D 与 1.0.0.16 两个 profile。
- 部署时游戏已关闭，90 个已有存档目录文件、其他本地 Mod 和游戏程序集校验未变；未导入/改写玩家设置，未启动游戏。
- 上述 DLL 来自当前 dirty 源码，不是基础 HEAD 的 clean 构建。审查后提交/推送前应从最终源码重建，并重新合并 helper、扫描实际产物、部署核对哈希。普通主项目构建可能重新产生 beta 单表，不能误把它直接当通用包发布。
- 正式版原始游戏程序集仍不可用，不能将离线旧 profile 保留等价核对写成正式版重新实机验收。

## 待实机验收

1. 无黑名单 Mod/全新配置：快速萃取和快速反应关闭、DisableMemory 关闭、WholeDayUseMemory 开启；旧配置则保留化学开关原值。
2. 首次启用 Claims：出现指定文案、DisableMemory 自动开启，F9/自动建点和内存恢复被拒绝，F5/F7 文件读档可用。
3. 手动关闭 DisableMemory，退回游戏及重启同一配置后仍关闭；不要据此认为房间节点已安全，使用非重要测试存档验证。
4. 原版环境分别切换 WholeDayUseMemory：关闭时 F5/F7 提示并走磁盘，F9/单节点仍可用；文件载入后旧图清空符合预期。
5. 设置页窄窗/滚动/快捷键捕获、化学主从开关，以及鉴定依赖开关没有错位或交互回归。

实机全部通过及审查完成前不要声明正式发布；后续上传不属于本轮已完成动作。
