# Probably Stolen Tools

[English](README_EN.md) | 中文

《深空当铺 / Probably Stolen》的自研辅助 Mod，适配正式版 **0.9.1**。
正式版为 Unity 2021.3.45f2 / Mono / x64（游戏 0.46D），使用游戏自带的 **IMod / Harmony** 加载器，不需要 MelonLoader 或 BepInEx。

---

## 功能一览

### 回溯

- **顾客节点**：每位普通顾客的开场对话结束后自动建立完整快照；双击节点回到那一刻。
- **手动节点**：默认 `F9` 随时建立，列表中显示为「手动」，不需要有顾客在场。
- **回到本日开业前**（`F7`）与**回到上一天**（`F5`）。
- 最多保留 **20 个**顾客 / 手动节点，另有独立的开业前与闭店节点。
- **跨顾客回溯**：第 3 位顾客在场时也可以回到第 2 位顾客的节点。
- 闭店（含开门前的早市状态）下建立的手动节点可以正常回溯。
- 接了下一位顾客后才截断被放弃的后续分支。
- 离店、跨天、切换场景会释放内存节点；不支持跨场景回溯。
- **0.9.0 起**：顾客/手动节点使用「完整对象图 + 精确实体载荷」的混合后端，保留原对象身份、完整校验与失败回退；这是内存回溯后端，不是用原版存档替换回溯。

### 多语言

- 水质、纯度、评级、酿酒等标注直接读取游戏原生语言键，随游戏语言切换，不再只支持中/英。
- 原生条目缺失时依次回退到英语表与英文文本；中文环境缺键回退中文文本；第三方液体保留其自带名称。
- 设置页可分别关闭「水质标注」「化学辅助」「酿酒标注」。

### 鉴定与信息

- **快速鉴定**：鉴定界面列出真伪、问题与建议报价，可选自动提交。
- **双击鉴定**：双击可鉴定物品时使用背包中的放大镜，省一次拖拽。
- **水质标注**：实时标注水质，标签与实际不一致时标红。
- **化学辅助**：水质栏显示主要成分与「占比% | 纯度% | 评级」；空瓶显示尺寸；滴管、量筒/量杯与 200mL 以下容器保持原名。
- **酿酒标注**：显示荧光莓品相；未发酵酒瓶按内容物给出预期品质，水量不足单独提示，负分为「酿造失败」；违禁酵母使用原版样式的独立「违禁品」标签。
- **对话快进**：普通顾客寒暄自动推进，遇到选择、SEC 与重要对话自动停下。

### 日志

- 设置页「查看日志」可在游戏内滚动查看 `PSTools.log` 末尾 400 行，日志路径自动复制到剪贴板。
- 日志头统一为 `[QuickSL]` / `[QuickSL/子系统]`（如 `[QuickSL/Customer]`）。
- 默认保留捕获/回溯耗时、节点规模、失败原因及真实修复回退；正常贴图、几何、议价、群拖、等待轮询等详细诊断不再刷屏。设置页「详细诊断日志」可临时开启，独立于额外快照对照开关。
- `PSTools_history.log` 保留业务操作记录用于排查，但默认不再把事务摘要重复写入游戏控制台。现有日志不清空，仍按大小轮转。

---

## 快捷键

| 默认按键 | 作用 |
|---|---|
| `F6` | 回到当前顾客的到店节点（内存恢复） |
| `F5` | 回到上一天的开业前节点 |
| `F7` | 回到本日开业前 |
| `F8` | 显示 / 隐藏检查点面板 |
| `F9` | 立即建立手动节点 |

全部可改：`F8` → 设置 → 快捷键。点一行后按新键；`Esc` 取消、`Backspace` 恢复该条默认、`Delete` 停用该快捷键；底部「恢复默认快捷键」一键还原。新键与其他动作冲突时会自动交换。

> 如果游戏目录存在 `dev.txt`（开发者控制台），`F8` / `F9` 会与游戏自带工具冲突，启动日志会给出提示。玩家通常不会开启它。

---

## 面板

- 拖动标题移动，右下角缩放，标题按钮收起；位置、尺寸与收起状态持久化。
- 双击节点回溯；被拒绝时会**逐条列出具体原因**（顾客移动、拖拽、通话、状态不符等），并提示查看日志。
- 建立失败的节点以「损坏」显示，鼠标悬停查看原因。
- 设置区可滚动，包含功能开关、快捷键与「查看日志」。

---

## 化学辅助规则

- 容器容量 **≤ 2999mL** 才显示成分；更大的桶保持原样。
- 水或酒精占比 **> 65%** 时按水 / 酒精为主成分；否则显示「水、酒精之外占比最高」的成分。
- 水质栏格式：`成分（占比% | 纯度% | 评级）`，占比为该成分 ÷ 全液总量。
- 纯度按「预期提纯后的产物」计算：排除水、酒精、脂合剂、脂肪、物理污染物、微生物、植物质、不溶性碎屑、三种菇类糊糊；蛋白 / 酸 / 碱不满 1mL 不扣除；化学污染物按 0.7% 封顶。
- 评级：纯品级 / 药用级 / 近药用级 / 街头标准级 / 掺兑级 / 垃圾级 / 淀粉级，按纯度划分并用颜色区分。
- 酒精为主时显示 `酒精（占比）`，名称显示「酒精」；74–76% 为「医用酒精」，≥99.95% 为「脱水乙醇」。
- 空瓶名称显示为 `空瓶（迷你/小/标准/大）`，不占用「水质」行。
- 名称替换只作用于**悬浮说明**与**拖拽使用 / 倾倒提示**；背包、交易列表、任务文本保持原名。

---

## 存档安全

- Mod **不会**主动调用游戏存档函数，也不会主动改动存档文件。
- 顾客节点是纯内存恢复。
- 「回到开业前 / 上一天」现在**只加载节点、不写回存档**：检查点会复制成临时文件，通过游戏的 `NewGameData.saveFile` 读取；之后的存档完全由游戏自己写入。另会保留一份 `.pre_restore` 备份。
- 首次使用前仍建议手动存一次，尤其是在多 Mod 环境。

---

## 下载与安装

**推荐：Steam 创意工坊** —— [Probably Stolen Tools](https://steamcommunity.com/sharedfiles/filedetails/?id=3829188627)

在 Steam 创意工坊页面点击「订阅」，启动游戏后在「模组」菜单启用即可，更新由 Steam 自动完成。

手动安装：

1. 下载 release 中的 `ProbablyStolenTools` 文件夹，放进 `<游戏目录>\Mods\`。
2. 在游戏「模组」菜单启用 **Probably Stolen Tools**，按提示重启。
3. 读档进店后按 `F8` 打开面板。

当前迁移候选仅需 `files` 能力，无网络访问。尚未发布的本轮迁移需同时安装 `ProbablyStolenTools.dll` 与 `ProbablyStolenTools.StaticLayouts.dll`，不能只替换主 DLL；旧发布构建仍可能显示退役能力警告。

---

## 兼容性

- 适用于**正式版**（Mono）；Demo / MelonLoader 路径不再维护。
- 与不修改同一界面、不新增数据结构到游戏内的 Mod 一般可共存。
- 快照会遍历游戏业务对象图，**向游戏内新增大量对象或自定义数据结构的 Mod** 可能导致建节点失败（面板会显示「损坏」及原因）。
- **兼容迁移候选**：当前源码用构建时静态字段表替代计算类型枚举及运行时表达式编译，两个发布 DLL 合并扫描仅剩 `files`。字段表严格绑定目标游戏/Mono 程序集 MVID；游戏更新后需重新构建和验证，未知布局拒绝建点而不漏存。当前尚待 Unity 实机验收，也不将现有扫描器通过冒充未来游戏版本已验收。

---

## 项目结构

```
src/ProbablyStolenTools/         Mod 本体（Harmony 补丁、快照引擎、原生 UI、配置）
tests/CheckpointTests/           快照 / 状态图往返与布局回归
tests/ReleaseCompatibility/      针对正式版 DLL 校验补丁目标与私有字段
scripts/Deploy-FullRelease.ps1   备份并部署到游戏 Mods 目录
release/manifest.xml             Mod 清单（ID、版本、能力声明）
research/                        逆向、设计与问题记录
docs/workshop/                   创意工坊介绍文本
tools/                           自用逆向工具源码（MetaDump / ILDump）
AGENT.md                         交接文档：架构、约束、验收顺序
```

关键文件：

| 文件 | 负责 |
|---|---|
| `CustomerCheckpoint.cs` | 顾客 / 手动节点快照的捕获、恢复、分支截断 |
| `Rewind.cs` | 文件节点（开业前 / 上一天）、只加载不写回的重载流程 |
| `ManagedStateAccess.cs` | 托管对象图遍历与字段规则 |
| `StateGraph.cs` | 槽位 / 对象 / 引用序列化与差异校验 |
| `LiquidComposition.cs` | 液体成分、纯度与评级 |
| `WineAppraisal.cs` | 荧光莓与未发酵酒瓶标注 |
| `Panel.cs` | `F8` 面板与设置项 |

---

## 构建与测试

需要 .NET 8 SDK。游戏路径不写进仓库。首次构建前把 `src\ProbablyStolenTools\local.props.example` 复制为 `local.props` 并填入游戏目录（已加入 `.gitignore`）：

```xml
<Project>
  <PropertyGroup>
    <GameDir>C:\Program Files (x86)\Steam\steamapps\common\Probably Stolen</GameDir>
  </PropertyGroup>
</Project>
```

也可以用环境变量 `PROBABLY_STOLEN_FULL_DIR`。

```powershell
dotnet build src\ProbablyStolenTools\ProbablyStolenTools.csproj -c Release
dotnet run -c Release --project tests\CheckpointTests\CheckpointTests.csproj
dotnet run -c Release --project tests\ReleaseCompatibility\ReleaseCompatibility.csproj -- "<游戏目录>" src\ProbablyStolenTools\bin\Release\Mono\ProbablyStolenTools.dll release\manifest.xml
.\scripts\Deploy-FullRelease.ps1
```

当前源码基线：编译 0 警告 / 0 错误；`CheckpointTests` **380 项**通过；正式版 **57 个** Harmony 入口、**76 个**字段与官方 Mod 扫描器通过。单次遍历实体后端已随 0.9.0 发布，离线测试不能代替游戏内验收。

开发期重复快照对照由 `Snapshot.SemanticDevelopmentChecks` 统一控制，发布默认关闭；开启会增加首节点及额外手动采样的耗时和内存。关闭后完整预检、状态校验、修复及必要诊断仍保留。新后端的开关为 `Snapshot.SemanticEntityBlocks`，修改后需重启游戏。

构建会生成目标 Mono 字段表，以及隔离在 `out/static-layouts-net8` 的测试宿主字段表。后者、Cecil 构建工具、探针与原生离线测试宿主均不得随 Mod 发货。完整迁移回归可运行：

```powershell
.\scripts\Test-StaticLayouts.ps1 -GameDir "<游戏目录>"
```

该脚本包含游戏自带 Mono 下的字段表全量对照、完整图往返及版本拒绝测试，不会启动游戏或访问存档；仍不能代替 UI/视觉/实际游玩性能验收。

---

## 版本管理

- **唯一真源是 `release/manifest.xml` 的 `ModVersion`**。
- 版本规则：**大版本、中间版本手动决定；小版本每次 `git push` 前 +1**（例如 0.8.15 → 0.9.0）。
- README 文案更新不推进版本号。

---

## 反馈

有日志基本都能定位到具体原因。请附上：

| 文件 | 位置 |
|---|---|
| `PSTools.log` | `%LOCALAPPDATA%Low\Questing Goose Studio\Probably Stolen\PSTools.log` |
| `Player.log` | 同目录 |
| `PSTools_history.log` | 同目录 |

并说明 Mod 版本、游戏版本与是否安装了其他 Mod。也可以在游戏内通过「设置 → 查看日志」临时读取。

---

## 用你自己的 AI 适配其他 Mod

源码与交接文档一起公开，就是为了让适配可以由任何人完成：

1. clone 整个仓库（不要只下 DLL）；
2. 把「仓库 + 目标 Mod + 冲突日志」交给你的 AI 助手，提示它读 `AGENT.md`；
3. 按 `AGENT.md` 的约束修改，跑上面两组测试；
4. 部署验收后提 PR，附改动说明与日志。

这个游戏的 Mod 生态没有统一框架，兼容问题往往不是改一行能解决的；作者不会主动为第三方 Mod 做适配。
