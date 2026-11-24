# 2026-11-25 QoL 修复与原生窗口手势交接

> 已被 `2026-11-25-qol-final-review-handoff.md` 取代。下方为过程记录：当前交互为重复Shift单击取消置顶、棕色中英文字标记，无系统字体加载；新增固定成分明细与普通提示警报。当前部署、测试及待验收范围请读最终交接。

## 04:42 实机回报修正（覆盖下文旧交互与部署）

用户报告Shift+双击打开窗口与原版橱窗转移冲突。现不再在窗口首次添加时置顶；`WindowsHandler.TryAddWindow`只维护已有置顶层级。新增`TreeNodeRender.OnPointerClick` postfix，只接受物理Shift+左键单击、非拖动、可见可移动窗口的直接标题/背景节点；不接管物品或按钮事件，不禁用原版Shift+双击转移。

置顶列表按最后置顶顺序维护；Shift再点已有置顶将其移至末尾，普通ToFront不再强行把焦点窗口压到其他置顶之上。窗口关闭和设置停用的清理保持。

Player.log本次实机1788–1790行显示三种系统字体均`Unable to load font face`，并非已证明图钉字形不存在。保留一次系统字体桥接尝试，失败后`[PIN]`可靠兜底，追加一次原因日志；不打包字体/不新增依赖。字体异常路径补清理。

最终461参考（新增Shift单击/双击/非左键/拖动/非窗口表面规则测试）、原版/真实VBE游戏Mono、101挂点/76字段及官方仅files扫描通过，HardFlags/Error空；22可选兼容沿用本批上一轮通过结果。部署`out/full-release-deploy-20261125-044254/deployment.json`，主DLL `62202F25D4A0DF19B6DC7B9F0E1FEB67411EB9F4CC7F90A4CA8C1E2FD6C14D33`，双表helper仍`6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23`。91个存档目录文件/原设置/其他Mod/游戏程序集不变，未启动游戏/重置公告/提交/升版/推送。请实测Shift单击窗口背景置顶、后置顶顺序、普通点击顺序不变、关闭清除；双击物品保持原版行为。

## 范围

基于正式 0.9.7（`ea8f371` 后的清洁工作区）开发；本轮不提交、不升版、不推送。

本批包括用户先前报告的四项修复，以及随后追加的三项可选窗口/公告功能。不能用上一批实机验收代替本批验收。

## 修复

### VBE 营养果入桶异常

核对本机 Vanilla Brewing Expansion 的 `BucketBrew.SetupWindow` 及原版 `ItemMouseDragHandler.EndDrag`、`ChemicalHelper.InitChemItem`：扩展给桶装水设置原料仓；原版拖放在物品入仓后还会尝试 Target，营养果/梦帽菇的原版 Target 会调用 `DissolveInto`。这不是本 Mod 的搅拌机自动化误识别桶。

新增两个精确保护挂点：`GameItem.CanTarget(GameItem)` 和 `ChemicalHelper.DissolveInto`。仅针对营养果/梦帽菇 + 桶装水/带原生酿酒容器标签的容器，且 VBE 正在运行或容器已有 `VBE_STATE`。第一个原料尚未生成扩展状态也受保护；无扩展、无扩展标记时不拦截原版。无需引用/调用第三方 DLL。原有扩展容器标注/反应排除保持。

### 工作中机器模组仓放入

在现有插入/移除锁查询及新版显式拖拽锁保护之外，补上实际入口 `GameGridInventory.MayHaveValidInventorySlot`。只放行设置已开、机器有工作锁、仓与 `MachineHelper.GetModuleInv(machine)` 引用完全相同的情况。保留祖先关系、所有权、旋转尺寸和双方原生物品过滤；精确碰撞/空位依旧交给原版，不改锁字段/标签，不解锁电池及原料仓。

该入口兼顾 Mono 可能内联简单锁查询的情况；内联是静态分析的风险点，不能声称已实机证明是唯一原因。Postfix 使用 `Priority.First`，不覆盖其他 Mod 后续的否决。

### 标注与小容器

- 荧光莓/酿酒标注恢复直接显示，鉴定摘要同步；仍受原有酿酒标注开关控制。机器预测保持按住详情键。
- `LiquidComposition.AnalyzeDetails` 复用原分析核心，只在明细路径放宽 150mL 下限；支持正容量液体容器，包括量筒、滴管。常规摘要/改名规则不变，VBE 容器排除不变。
- 中英文 README、详情键配置描述及更新详情同步修正。

## 新功能

### 原生窗口磁吸

`Windows.ShiftSnap=true`，设置页独立中英开关。物理左右 Shift + 左键拖动；在 `TreeNodeRender.OnDrag` 前后比较窗口实际位置/尺寸，只对确实移动、未调整尺寸的可见原生可移动窗口生效。只吸附同父层的可移动窗口，10 个父坐标 UI 单位阈值；取最近边缘，禁止吸附引入父区域越界。

不注入原生拖动回调，不接管鼠标输入，不干涉物品拖动或窗口缩放。几何计算为独立纯函数 `WindowSnap.Align`。

### 原生窗口置顶

`Windows.ShiftPin=true`，设置页独立中英开关。在 `WindowsHandler.TryAddWindow` 确认首次添加可见窗口时，按住物理 Shift 则记录 UI-only 置顶。关闭窗口后清除；设置关闭后下一 Tick 清除全部。

`PixelWindow.ToFront` 后只调整同父层兄弟顺序；不调用原生 ToFront 来反复改变 focusStamp，不改 visibleWindows 的 Escape/自动关闭顺序，不抬高菜单/模态层。多个置顶窗口之间允许当前点击的置顶窗口在最前。

标题直接子节点 TMP 文本加图钉前缀，不改 `PixelWindow.titleString`、原生回调或存档字段。游戏字体不含 U+1F4CC 时尝试 Segoe UI Symbol/Segoe UI Emoji/Arial 系统字体，私有副本追加字体回退，不改游戏共享字体的 fallback 列表；系统字体仍不支持则使用 `[PIN]`。关闭/停用清理私有字体及创建的纹理/材质。标题空间不足使用省略，不无限扩展窗口。

本批没有实现任意自定义 Mod UI 窗口的手势；范围为原生 `PixelWindow`。

### 更新简介倒计时

简介底部固定 3px 轨道 + 蓝色填充，按 `Time.unscaledTime` 在三秒内从满到空；在已读短路前继续更新。关闭/展开/禁用时清理引用。保留任意位置点击展开详情、首次显示即记账及失败重试；不重置已读版本、不调整公告文案/版本号。避免原生 `AddSeparator` 的额外上下 padding。

## 离线验证

- Release 构建：0 警告、0 错误。
- `CheckpointTests`：460 项；新增 5 项磁吸几何与 1 项倒计时测试。
- 可选 Mod 兼容：22 项，新增首原料保护及无扩展回退覆盖。
- 实际游戏 Mono 离线夹具：无第三方环境及真实 VBE DLL 均通过；包含配方 Target/Dissolve 拦截、实际原生模组仓准入/过滤/尺寸、小容器资格及原有快照往返。
- 最终合并 helper 按两个原始 profile 做等价验证，仍为 808448 bytes。
- ReleaseCompatibility：100 挂点/76 字段；官方能力仅 `files`，HardFlags/Error 空，原生 manifest 解析通过。

原版/真实 VBE Mono 夹具使用离线 Unity 程序集，退出时会有既有 runtime/class-library out-of-sync 提示；这不是 Unity 活体验收。原始 0.46D 程序集仍不可用，不能声称已在稳定分支重新实测。

日志：`out/20261125-qol-{build,tests,optional,mono,vbe-mono,merge,compatibility}.log`。

## 待实机验收

1. VBE 首次营养果/梦帽菇入桶及酿酒瓶，原料保留，原版其他可溶物仍正常。
2. 已开解锁设置时，运行中机器模组槽可放入/取出；原料/电池仓及不匹配物品仍受限。
3. 荧光莓/原版酒瓶直接标注；量筒/滴管按住详情键显示实际含量；松开恢复。
4. 两个原生窗口按住 Shift 相邻拖动，水平/垂直和不同缩放下对齐；松键自由拖，物品拖拽/缩放不误吸附。
5. Shift 打开窗口出现图钉/字体兜底标记；普通窗口不能盖过它；菜单/警告仍保持原生模态层；关闭再普通打开不置顶，关闭设置立即取消。
6. 三秒公告填充条收缩，尺寸无额外留白，点击任意处仍展开。已有已读记录本轮不重置，如需重播应另行明确操作。
7. 开窗/关窗/置顶后建点和回溯，物品与窗口显示正常，日志无 WindowGestures 新异常。

## 部署

- 记录：`out/full-release-deploy-20261125-043230/deployment.json`。
- 主 DLL SHA256：`780EA25AD7CEB89700D935FE2C3BD780C103E4A4EC938432A46AFE292E4EE001`。
- 双 profile helper SHA256：`6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23`，808448 bytes。
- 安装目录主/helper 与构建一致，91 个存档目录文件、原配置、其他 Mod 及游戏程序集经部署脚本校验不变；未导入设置、未启动游戏。
- 新配置只在下次 Mod 初始化按原生配置流程写入；本轮未修改玩家 XML 或公告已读版本。未提交/升版/推送。
