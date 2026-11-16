# Probably Stolen Tools

[English](README_EN.md) | 中文

《深空当铺 / Probably Stolen》的自研辅助 Mod，当前版本 **0.9.5**。
游戏使用 Unity 2021.3.45f2 / Mono / x64，自带 **IMod / Harmony** 加载器，不需要 MelonLoader 或 BepInEx。
0.9.5 同一包包含 **0.46D 稳定版**与 **1.0.0.14 beta** 的兼容数据，启动时按游戏版本自动选择。

### 游戏 Beta 分支

游戏 **1.0.0.14 是 beta**，不是 0.46D 稳定分支。0.9.5 的主 DLL + 合并 helper 会按程序集指纹自动选择 0.46D / 1.0.0.14 的布局；两个已收录版本之间切换不需要手动换表，未来版本仍严格拒绝未知指纹。
离线合并只复用相同字段访问代码，版本专用类型令牌保持隔离；运行时不生成、解压或动态加载代码，也不漏存游戏闭包状态。
维护者使用 `release/universal/manifest.xml` 打包；单分支入口保留。当前只在 beta Mono 上重跑了完整套件，正式版原始程序集不可用，**通用包的稳定分支仍需单独实机复验**，不代表未来 beta 或任意第三方 Mod 已兼容。详见 `research/2026-11-16-universal-static-layouts.md`。

---

## 功能一览

### 回溯

- **顾客节点**：每位普通顾客的开场对话结束后自动建立完整快照；双击节点回到那一刻。
- **手动节点**：默认 `F9` 随时建立，列表中显示为「手动」，不需要有顾客在场。
- **回到本日开业前**（`F7`）与**回到上一天**（`F5`）；优先使用内存晨间节点，回溯后仍可浏览相邻两天。
- 同会话保留当天和昨天：每天独立最多 **20 个**顾客/手动/闭店节点，另有 1 个不占名额的开业前锚点，第三天释放更早节点。
- **F6 节点选择**：↑/W、↓/S 切换，Enter 确认，Esc 取消，双击节点也可回溯；鼠标中键/侧键可改键。
- **跨顾客回溯**：第 3 位顾客在场时也可以回到第 2 位顾客的节点。
- 只浏览旧节点不会删除未来；真正接了下一位顾客后才截断被放弃的分支，并保留最多 80 条只读摘要。
- 闭店（含开门前的早市状态）下建立的手动节点可以正常回溯。
- 同场景离店会暂停回溯，返店稳定约 0.5 秒后新增节点；跨天保留相邻两天。
- **0.9.0 起**：顾客/手动节点使用「完整对象图 + 精确实体载荷」的混合后端，保留原对象身份、完整校验与失败回退；这是内存回溯后端，不是用原版存档替换回溯。

### 多语言

- 水质、纯度、评级、酿酒等标注直接读取游戏原生语言键，随游戏语言切换，不再只支持中/英。
- 原生条目缺失时依次回退到英语表与英文文本；中文环境缺键回退中文文本；第三方液体保留其自带名称。
- 设置页可分别关闭「水质标注」「化学辅助」「酿酒标注」。

### 鉴定与信息

- **快速鉴定**：鉴定界面列出真伪、问题与建议报价，可选自动提交。
- **双击鉴定**：双击可鉴定物品时使用背包中的放大镜，省一次拖拽。
- **水质标注**：实时标注水质，标签与实际不一致时标红。
- **化学辅助**：水质栏显示主要成分与「占比% | 纯度% | 评级」；空瓶显示尺寸/名称，水培种子显示对应物品名；容器标注从 150mL 起，不再设最大容量。
- **自动搅拌**：可混合物品拖入搅拌机后按原版混合规则处理，补回原版音效；不同产物不会混在一起，容量不足会提前阻止。
- **快速萃取**：向含正量酒精的容器加入液体时，按原版比例把三类菇的糊糊转为对应萃取物；比例、取整和条件不变。
- **快速化学反应**：独立开关，只处理九种明确的非热反应，每次最多额外推进 512 步，没有变化就停止。
- **设施预测**：净水器、蒸发器、水培、鱼池/RAS 的悬浮说明显示今夜预计产出/去除量，缺水、缺容器、电力不足会提示。
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
| `F6` | 选择上一个节点，↑/W、↓/S 切换，Enter 确认 |
| `F5` | 回到上一天的开业前节点 |
| `F7` | 回到本日开业前 |
| `F8` | 显示 / 隐藏检查点面板 |
| `F9` | 立即建立手动节点 |
| 未绑定 | 快速重载存档 / 重掷随机数（可在设置中绑定） |

全部可改：`F8` → 设置 → 快捷键。点一行后按新键；`Esc` 取消、`Backspace` 恢复该条默认、`Delete` 停用该快捷键；底部「恢复默认快捷键」一键还原。新键与其他动作冲突时会自动交换。

> 如果游戏目录存在 `dev.txt`（开发者控制台），`F8` / `F9` 会与游戏自带工具冲突，启动日志会给出提示。玩家通常不会开启它。

---

## 面板

- 拖动标题移动，右下角缩放，标题按钮收起；位置、尺寸与收起状态持久化。
- 双击节点回溯；被拒绝时会**逐条列出具体原因**（顾客移动、拖拽、通话、状态不符等），并提示查看日志。
- 建立失败的节点以「损坏」显示，鼠标悬停查看原因。
- 建点失败直接显示具体异常、当前上限与处理方法，截图可直接用于 bug report。
- 设置区可滚动，包含功能开关、快捷键与「查看日志」。
- **自动建立节点**默认开启；关闭后不再自动创建晨间/顾客/闭店/返店节点，手动 F9、节点选择与已有节点回溯仍可用，F5/F7 改为读取文件存档。
- 设置页新增 **快速重载存档** 与 **重掷随机数** 两个操作，默认不绑定快捷键，可在快捷键区设置。快速重载会读取当前槽位最近保存的磁盘存档，未保存进度和全部内存节点会丢失，执行前会确认，且不会先保存或覆盖活动档。重掷只影响后续随机结果，不重建已有顾客/物品，也不读档；新序列不保证抽到不同结果。

---

## 快照限额与手动生成布局表

### 调整快照限额

主菜单 → 模组 → Probably Stolen Tools 的齿轮，可调整对象数、字段槽数与单份字节上限。
按「应用」后同步写入 `PSTools.settings.xml`，也可以退出游戏后手动编辑：

```xml
<Entry key="Snapshot.MaxObjects" value="300000" />
```

默认对象上限 200000，支持 1000–1000000；字段槽默认 3000000，上限 8000000；
字节预算默认 256 MiB，上限 2048 MiB。这是逻辑载荷预算，不是整个 Mod 的实际内存上限。
提高限额不会让未超限节点多遍历对象，也不会按新上限预分配完整快照；
它会允许原先被拒绝的大节点继续捕获，因而可能占用更多内存并出现更长卡顿。
XML 是重启时的真源，齿轮面板不是独立覆盖 XML 的另一套偏好。

### 游戏更新后布局表不匹配

若建点提示 `TypeLoadException`、`Snapshot layout build does not match loaded module`
或未知静态布局，请等待 Mod 更新；另行发布的独立 **Static Layout Generator** 可在获得后使用。
玩家版工具不需要安装 .NET；它在独立进程读取游戏程序集，只生成 helper。
**不要把工具程序或 Mono.Cecil 放进 Mods。**

解压工具后在其目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Generate-StaticLayouts.ps1 -GameDir "E:\SteamLibrary\steamapps\common\Probably Stolen"
```

先关闭游戏，替换为自己的游戏目录。输出位于工具的 `generated-日期` 文件夹。
只把 `ProbablyStolenTools.StaticLayouts.dll` 替换到实际 Mod 文件夹；
旧 helper 备份必须放在 Mods 外面。默认 `Mods\ProbablyStolenTools` 安装可加 `-Install`
自动备份和替换，回滚方法见 [玩家工具说明](tools/StaticSnapshotLayouts/PLAYER-README.md)。
工具压缩包由 `scripts/Publish-StaticLayoutTool.ps1` 打包，发布时需单独上传为附件，
不随游戏加载的 Mod DLL 分发。

生成成功**不等于**新版本已兼容：它不能自动适配任意第三方 Mod、修复已变化的游戏 API、
不支持的字段或未来加载器政策。仍失败时请提交具体异常、`PSTools.log`、游戏版本与 Mod 列表。

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

当前发布包只声明 `files` 能力，无网络访问；`reflection-external` / `dynamic-code` 不再需要。必须同时安装 `ProbablyStolenTools.dll` 与 `ProbablyStolenTools.StaticLayouts.dll`，不能只替换主 DLL。

---

## 兼容性

- 当前包同时包含 **0.46D 稳定版**与 **1.0.0.14 beta** 的兼容数据；同一包按游戏版本自动选择，未来游戏版本仍需 Mod 更新，不会自动适配任意版本。
- 1.0.0.14 仍是 beta，首次使用建议备份存档；从 beta 切回 0.46D 后若建点失败，请附游戏版本、日志和 Mod 列表。
- 正式版（0.46D）与 beta（1.0.0.14）的原始程序集不是同一套，通用包不能替代各分支自己的实机验收。
- 与不修改同一界面、不新增大量游戏对象的 Mod 一般可共存；快照会遍历游戏业务对象图，**向游戏内新增大量对象或自定义数据结构的 Mod** 可能导致建节点失败（面板会显示「损坏」及原因）。
- 发布包只声明 `files`；构建时静态字段表替代运行时类型枚举/表达式编译，字段表严格绑定目标游戏/Mono 程序集版本，未知布局拒绝建点而不漏存。

---

## 项目结构

```
src/ProbablyStolenTools/         Mod 本体（Harmony 补丁、快照引擎、原生 UI、配置）
tests/CheckpointTests/           快照 / 状态图往返与布局回归
tests/ReleaseCompatibility/      针对正式版 DLL 校验补丁目标与私有字段
release/manifest.xml             Mod 清单（稳定 0.46D）
release/beta/manifest.xml        Beta 1.0.0.14 清单
release/universal/manifest.xml   双版本通用清单
scripts/Package-FullRelease.ps1  干净源码打包（单版本/通用）
scripts/Deploy-FullRelease.ps1   备份并部署到游戏 Mods 目录
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

当前本机为 beta 时，套件需指定 beta manifest；通用包从干净源码打包并传入另一版本原始 helper：

```powershell
.\scripts\Test-StaticLayouts.ps1 -GameDir "<游戏目录>" -ManifestPath release/beta/manifest.xml
.\scripts\Package-FullRelease.ps1 -GameDir "<游戏目录>" -ManifestPath release/universal/manifest.xml `
  -AdditionalLayoutPaths "<稳定版原始helper>" -OutputRoot "<新输出目录>"
.\scripts\Deploy-FullRelease.ps1 -GameDir "<游戏目录>" -PackageDirectory "<输出目录>\ProbablyStolenTools"
```

当前源码基线：编译 0 警告 / 0 错误；`CheckpointTests` **430 项**通过；正式版 **84 个** Harmony 入口、**76 个**字段与官方 Mod 扫描器通过，16 项官方保护 owner 覆盖、13 项存档动作测试、14 项原生历史/齿轮 IO 夹具、11 项玩家工具断言通过。单次遍历实体后端已随 0.9.0 发布，离线测试不能代替游戏内验收。

开发期重复快照对照由 `Snapshot.SemanticDevelopmentChecks` 统一控制，发布默认关闭；开启会增加首节点及额外手动采样的耗时和内存。关闭后完整预检、状态校验、修复及必要诊断仍保留。新后端的开关为 `Snapshot.SemanticEntityBlocks`，修改后需重启游戏。

构建会生成目标 Mono 字段表，以及隔离在 `out/static-layouts-net8` 的测试宿主字段表。后者、Cecil 构建工具、探针与原生离线测试宿主均不得随 Mod 发货。完整迁移回归可运行：

```powershell
.\scripts\Test-StaticLayouts.ps1 -GameDir "<游戏目录>"
```

该脚本包含游戏自带 Mono 下的字段表全量对照、完整图往返及版本拒绝测试，不会启动游戏或访问存档；仍不能代替 UI/视觉/实际游玩性能验收。

---

## 版本管理

- **唯一真源是 `release/manifest.xml` 的 `ModVersion`**。
- 版本规则：**大版本、中间版本手动决定；小版本每次 `git push` 前 +1**（例如 0.9.4 → 0.9.5）。
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
