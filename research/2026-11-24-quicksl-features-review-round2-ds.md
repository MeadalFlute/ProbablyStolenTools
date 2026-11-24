# 2026-11-24 QuickSL 回执审查与 0.9.7 推送（DSH）

审查对象：`research/2026-11-24-quicksl-features-review-receipt.md`（对我上轮 `…-features-review-ds.md` 的处理回执），以及该回执之后新增的 `简介排版`、`21 MB 大存档初始文件节点修复` 与两项小修。本轮同时按用户授权完成升版、打包、部署与推送。

## 一、结论

**回执属实，可以发布。** 我独立重跑了构建、454 参考、20 可选、14 原生 IO 与最终 92/76 扫描，数据与回执一致；P1/P3 的实现正确，P2 的保留理由成立。新增的大存档上限修复有效，但**上限从 16 MiB 直接放到 256 MiB，对本机主线程整读开销过宽**（建议后续收紧到 64 MiB），属非阻塞建议。

## 二、独立复核（未引用回执日志）

| 项目 | 结果 |
|---|---|
| Release 构建 | 0 警告 / 0 错误 |
| 主 DLL | `9F6EFACD…`，回执声明、`deployment.json`、安装目录三方一致 |
| 测试 | **454** 参考、**20** 可选兼容、**14** 原生历史/齿轮 IO |
| 最终扫描 | `PASS 92 hook targets and 76 snapshot/private fields`；`Capabilities: files`；`HardFlags` 空；原生 `ParseManifestForTools` 通过 |

## 三、逐项审查

**P1（已采纳，实现正确）**：`UpdateNoticeSchedule.Acknowledged(version, persisted)` 统一"空版本 / 已持久 / 本会话已展示"，`UpdateNotice.Tick` 在 `Ready()` 之前短路，同时把 `SavePending()` 与"存活简报到期关闭"保留在短路之前——保存重试和 3 秒关闭不会被跳过，语义完整。`Due()` 内部复用 `Acknowledged`，`Shown()` 复位 `_readySince`，因此换到新版本后会重新等待 0.5 秒稳定期。新增测试覆盖持久已读、仅会话已读、空版本、新版本重新计时，逻辑与代码一致。

**P3（已采纳，实现正确）**：`NativeQuickSLPanelPatch` 以 `异常类型名: 消息` 为键去重，成功后清零 `_lastError`，可以继续记录"之后"的新故障，符合我要求的"不因潜在风险改变窗口结构、但别刷屏"。

**P2（保留实机验证，理由成立）**：我上轮担心的 `targetGraphic == null` 只影响 Button 的转场渲染；Unity 的 `EventSystem` 是从命中的 Graphic **向上冒泡**到第一个 `IPointerClickHandler` 的，因此只要窗口里有任一 `raycastTarget = true` 的子节点（TMP 文本默认如此），挂在 root 上的 Button 就能收到点击。所以"不改尺寸已调好的窗口结构、留给实机确认"是合理的取舍，不算遗留缺陷。实机项 5 仍要确认一次（点空白处也能打开详情）。

**新增：大存档初始节点修复**：`CheckpointFileCatalog` 与 `SaveMetadata.Reader` 的 16 MiB 硬编码统一为 `SaveMetadata.MaxTextLength`，并新增 21 MB ASCII 档的索引/复用/去重测试与超 16 MiB UTF-8 档的元数据测试——修复方向正确（旧上限确实会让大档在内存禁用时缺初始节点）。

> **建议（非阻塞，下轮可做）**：字节门槛现在允许 256 MiB，`Refresh`/晨间索引路径用 `File.ReadAllText` 整读，主线程峰值约为 256 MB 字节缓冲 + 最多 512 MB UTF-16 字符串；玩家真实大档仅 21 MB，建议把上限收到 64 MiB（仍有 3 倍余量），或改成只读文件头若干 KB 提取 ES3 元数据。当前实现不会越权读改写，只是内存/卡顿余量偏大。

**新增：简报排版**：宽 380、按 `GetPreferredValues` 实测正文与底部提示高度收紧、`---` 独立行转原生分割线并去掉空段。测试覆盖分割与行内短横线保留；`FitLabel` 的测量不改标签文本，未引入上轮 R3 的 TMP 缓冲副作用。

## 四、升版、打包、部署与推送记录

| 步骤 | 结果 |
|---|---|
| 版本 | 0.9.6 → **0.9.7**，三份 `manifest.xml`、`Plugin.Version`、`MelonInfo` 同步；README 中英、AGENT 当前版本同步 |
| 发布提交 | `15826e2` release: 0.9.7 QuickSL item details, rapid boiling and update notice（44 文件，+1843/−85） |
| 记录提交 | `ea8f371` docs: QuickSL feature batch handoff, review and receipt records |
| 通用包 | `out/quicksl-0.9.7-universal`，`BuildCommit=ea8f371`、`BuildDirty=false`，ZIP `486B1E9F…` / 584933 bytes |
| 主 DLL | `3B5E163741A8FAFAA633C71687A0C7B5F7F8173B413F5EDFCBF7BCFDFE02A54B` |
| 双 profile helper | `6129A2D3…` / 808448 bytes（额外布局来源 `CBB8F36A…` / 683008 bytes） |
| 最终扫描（合并产物） | 92 挂点 / 76 字段、仅 `files`、`HardFlags/Error` 空、原生 manifest 解析 QuickSL + 双语 + 0.46D/1.0.0.16 |
| 部署 | `out/full-release-deploy-20261124-133646/deployment.json`；安装哈希与包一致，90 个存档目录文件不变，设置未导入，未启动游戏 |
| 推送 | `04df653..20a816d  main -> main`；远端 `main` = `20a816d120c1f7653136dd9b82fe928fa86f493d` |

顺带记录：本机直连 GitHub 的 TLS 握手当前被重置（`OpenSSL ... unexpected eof`），本次推送经用户本地 Clash 核心 `127.0.0.1:7890` 完成；推送结束后已关闭该临时核心进程，未改系统代理设置。

## 五、仍待实机（不阻塞发布）

1. 简报：3 秒自动关闭、点击空白处展开、详情滚动/Escape、跨重启不重复（0.9.7 首启会向每位玩家提示一次）。
2. 设置入口：原生配置窗口里「打开QuickSL功能面板」按钮的可见性与点击（对应 P2 的实机确认）。
3. 详情键左右 Shift、容器长名三列对齐、无 renderer 时的估宽。
4. 双击鉴定页开合顺序。
5. 快速烧开水阈值/混合液只去酒精/空瓶刷新，以及化学总开关关闭即停。
6. 大存档（第 96 天 21 MB 档）晨间初始文件节点是否恢复，同时留意主线程卡顿——这也是上面 256 MiB 建议的现实依据。
