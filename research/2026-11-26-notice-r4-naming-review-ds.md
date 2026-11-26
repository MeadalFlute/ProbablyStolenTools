# 2026-11-26 公告 r4 与命名/检索 DS 审查

本轮对象：回执 `2026-11-26-hydro-tooltip-final-review-receipt.md` 所列 r4 增量（`UpdateNotice.cs`、`UpdateNoticeSchedule.cs`、`UpdateNoticeTests.cs`、`AGENT.md`）以及你关于"拆成两个模组但保留快速SL 名字"的讨论。只读审查，未改代码、未提交/升版/推送。

## 结论

- **r4 增量无阻断项**，可部署；我独立重建的主 DLL 与 19:06 部署、以及游戏目录中的已安装副本三者哈希一致。
- **"保留快速SL/QuickSL" 站得住**：今天实测工坊检索，中文搜 `SL`、`快速SL`，英文搜 `rewind`、`checkpoint`、`snapshot`、`rollback`、`SL` 都能命中我们。
- 但 **`quicksave` / `回溯` / `检查点` / `快速存档` / `快速读档` 目前搜不到**，原因已定位（见第三节），可以在**不改名**的前提下补齐。

## 一、独立复核

| 项目 | 我的结果 |
| --- | --- |
| Release 构建 | 0 警告 / 0 错误 |
| 主 DLL | `DC98C74D4117CBF4145D14C395F8BA1A25409E23A5B62F658DC8904B20BE4623`，与 `deployment.json` 及 `Mods\ProbablyStolenTools\ProbablyStolenTools.dll`（572928 B）一致 |
| 测试 | **480 项参考测试通过**、22 项可选兼容通过 |
| 官方扫描 | 105 挂点 / 76 字段 / 仅 `files`，`HardFlags`、`Error` 空 |
| Mono 夹具 | 原版 28 PASS、真实灯牌+VBE 28 PASS，0 FAIL |
| helper | `6129A2D3…` / 808448 B 未变 |
| 部署记录 `out/full-release-deploy-20261126-190633/` | 95 个存档目录文件、`ExistingGameAssembliesUnchanged=true`、未导设置、未启动游戏 |

你写的 AGENT.md 19:06 条目与实测一致（"480 参考/22 可选/105 挂点 76 字段/`DC98C74D`"）。

## 二、r4 增量逻辑审查

**通过的关键点：**

- 已读确认从"创建即写"改为"活跃层级 + `Ready()` 连续 2 秒"，或"点击详情且详情窗创建成功"（`UpdateNotice.cs:32-46、142-155、210-237`）。创建时只写 `created` 日志，不再冒充 displayed。
- 中断路径正确：窗口销毁、`Ready()` 转假、黑屏/晨报重开都会 `CloseBanner()` 并把展示资格留在未确认状态，`_retryAt` 0.5 秒后重试（`UpdateNotice.cs:33-39`）；`Schedule.Due(...)/ConfirmVisible(false)` 复位后重新计时（`UpdateNoticeSchedule.cs:73-86`），不会把"被遮挡"误记成已读。
- 8 秒时长与进度条同源 `UpdateNoticeContent.Duration`，`Progress` 对 NaN/越界都有钳制（`UpdateNoticeSchedule.cs:42-44`）。
- r4 订阅器独立：`Revision = :20261126-chemistry-compat-r4`，r3→r4 会重播一次；`Acknowledged` 三态（空、持久化、会话内 `_shown`）语义正确，保存失败只在会话内重试，不重复弹出（`UpdateNoticeSchedule.cs:70-88`）。
- 九节双语结构、`---` 分隔、内联 `---` 不误切分都有用例（`UpdateNoticeTests.cs:18-33、82-93`）。

**可选改进（P3，不影响发布）：**

1. **日志措辞失真**：窗口在已确认之后被中断时，仍会写 `[Updates] banner interrupted notice=…; retry if unacknowledged`（`UpdateNotice.cs:38`）。已确认状态下这句会误导排障。建议加 `acknowledged=` 字段或仅在未确认时打。
2. **遮挡抖动会导致反复创建/中断**：若出现未知第三方全屏窗口使 `Ready()` 周期性地真假跳变，会出现"创建→0.5 秒→中断→再创建"的循环，且每次都写一行日志。建议加退避（2/4/8/30 秒封顶）并每个原因只记一次。
3. **`OpenDetails` 早退跳过确认**：`_details` 已存在时直接 `Show(); return;`（`UpdateNotice.cs:213`），不调用 `Acknowledge("details-clicked")`。只有在首次创建详情时 `CfgUpdateSeen` 为空才可能有影响，实际可忽略。
4. **第 9 节措辞与你已定的方向**：公告写"名称及迁移方式尚未最终确定"是准确的，但既然你已决定不改名，正式拆分公告里应明确"回溯模组沿用 **快速SL/QuickSL** 名称、旧工坊条目与 Mod ID"。r4 已部署，不建议为这句话再改一次版本（会多一次重播）。
5. 可选：`README.md`/`README_EN.md` 未提拆分预告（只有游戏内公告提），不属于必须同步。

## 三、命名与检索实测（回应你的问题）

线上现状（我刚抓取的工坊页）：英文标题 `QuickSL`、中文标题 `快速SL`——说明**工坊条目的中英标题可以分别本地化并已生效**；英文简介含 `regret / rewind / SL / pawnshop`，英文完整描述含 `checkpoint / snapshot`；**中文描述还是 0.9.7 内容**（搜不到"智能挤出"，说明尚未发布 0.9.8 中文描述）。

实测（`appid=4348910` 工坊检索）：

| 关键词 | 是否命中我们 | 结果数 |
| --- | --- | --- |
| `rewind` | 是（第 5 条） | 5 |
| `checkpoint` | 是 | 13 |
| `snapshot` | 是 | 30 |
| `rollback` | 是 | 10 |
| `SL` | 是（排名靠后） | 30 |
| `regret` | 是 | 4 |
| `QuickSL` / `快速SL` | 是 | — |
| `quicksave` | **否** | 7（全是别人的存档类模组） |
| `undo` | **否** | 30 |
| `quick` | **否** | 30 |
| `回溯` | **否** | 30 |
| `检查点` | **否** | 30 |
| `快速存档` / `快速读档` | **否** | 30 |
| `逆转` | **否** | 30 |
| 仅存在于中文描述正文的词（`磁吸窗口`/`模组槽`/`酿酒拓展`） | **否** | 30 |

**结论：**

1. **英文（标题 + 简介 + 完整描述）进检索索引；中文只有标题进索引，中文描述正文不进索引。** 所以"中文关键词"只能放进中文标题，写进中文描述没用。
2. `SL` 能命中是因为英文简介里的整词 `way faster than SL`；而 `quicksave` 不会因为标题里有 `QuickSL` 就命中——复合词必须整词写进英文简介/描述。
3. 两字母查询（`SL`）相关性很弱、排名靠后；真正有区分度的是 `rewind`/`checkpoint`/`quicksave`。

**建议（不改名也能拿到曝光）：**

1. **名称保留 `快速SL` / `QuickSL`**——你的判断成立，国内搜 `SL`、`快速SL` 已能命中。
2. **英文简介第一段补整词**：`quicksave`、`quick save/load`、`save scumming`、`undo`、`rollback`、`checkpoint`；保留现有 `rewind` 和 `SL`。同类模组（`F5 Quick Save`、`Save Anytime + Quicksave/Load`）正是靠这些词被搜到。
3. **中文只想被"回溯/检查点"搜到，唯一位置是中文标题**（如 `快速SL｜回溯存档`）。Steam 卡片标题约显示 30 字符，后缀可行；不加也合理，靠 `SL` + 英文关键词即可。
4. **拆分后的身份**：回溯模组沿用旧工坊条目、`dsh.probablystolen.tools`、程序集、`PSTools` 检查点/配置入口（订阅者无感，与你回执的方向一致）；工具箱新条目单独写 `toolbox / utility / hotkeys / QoL`，两个条目描述互链。
5. **发布时记得上传 0.9.8 中英描述**（`out/workshop_description_0.9.8_zh.bbcode` / `_en.bbcode` 已备好）；线上目前仍是 0.9.7。
6. 注意：社区站检索与客户端检索共用后端，但不保证完全一致；以上为社区站实测结果。

## 四、未覆盖 / 待实机

- r4 横幅的真实可见性、8 秒时长、点击详情后九节显示、确认后重进存档不再提示（Unity 渲染与点击离线不测）。
- 中文关键词是否也影响游戏内模组列表搜索：未验证（本轮未启动游戏）。
- 强制提示/水培等前轮已验收功能本轮未回归。

本轮仅新增本文件，未改代码、未提交、未升版、未推送，版本仍 **0.9.8**。
