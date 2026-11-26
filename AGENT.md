# Probably Stolen Tools — 交接文档

> 适用源码：正式版 **0.9.9**（2026-11-26）。平台为游戏自带 **Mono / IMod / Harmony**；MelonLoader / IL2CPP 路径已弃用，`#if !MONO` 分支只保证能编译，不再维护。
> 旧版本文档（0.2.1–0.6.0、Demo、早期性能优化过程）归档在 `research/archive/AGENT-0.6.0-history.md`，**不要**按它描述的环境或版本操作。

> 2026-11-23 兼容候选：本机 beta 已更新至 **1.0.0.16 / VERSION 100-Rev16**，游戏 MVID `d927a6e4-ae7a-4e65-a5e2-9f9624c131f3`；beta/universal manifest 已随之更新。下文 11-16 的 1.0.0.14、已部署哈希与规模是历史记录，不是本轮验收。当前新版单表 2915 类型/8235 字段/670208 bytes。可选 Mod 检测使用两版均有的公开 `ModLoader.Mods` 只读表，不调用旧正式版不存在的 `ModHelper.IsModRunning`，不引用第三方 DLL，也不新增权限/前置依赖。酿酒扩展 1.4.4 避免标注/改名/快速反应干预；领地与革命 1.1.18 暂时仅文件读档安全共存，启用时明确禁止内存建点/恢复并强制完整原生菜单载入，不能宣称完成完整实体回溯。维护范围/后续接入要求见 `research/2026-11-23-optional-mod-compatibility.md`。

本文只写当前仍成立的事实。实现细节以源码为准，阶段记录与实验数据看 `research/`。

2026-11-26 19:52 **0.9.9 发布**（当前正式源码，覆盖下方全部候选与 0.9.8 记录）：玩家验收本批（智能挤出、灯牌适配、强制详情提示、水培预测、营养液改名、平板置顶标记、试剂 `*` 与完整详情、自动搅拌优化、8 秒九节公告）后按惯例升版推送。版本 0.9.9 同步三份 manifest、`Plugin.Version` 与 `MelonInfo`；480 参考、22 可选兼容、原版与真实灯牌+VBE Mono 夹具（各 29 PASS，含 408 组确定品质分配与原生一致）、105 挂点/76 字段/仅 `files` 扫描均通过，HardFlags/Error 空。打包 `out/quicksl-0.9.9-universal`（ZIP `3CCEF4BD…` 605445 bytes、主 DLL `DF901AB7…`、双 profile helper `6129A2D3…`/808448 bytes、通用包额外布局来源 `CBB8F36A…`/683008 bytes、打包提交 `5b49ced`）；部署 `out/full-release-deploy-20261126-195207/deployment.json`（95 个存档目录文件不变、设置未导入、未启动游戏、`InGameValidated=false`）。推送 `main` `817af11..5b49ced`。工坊材料 `out/workshop_changes_0.9.9_zh.bbcode`、`out/workshop_changes_0.9.9_en.bbcode`、`out/workshop_description_0.9.9_zh.bbcode`、`out/workshop_description_0.9.9_en.bbcode`；公告只列相对 0.9.8 的新增与可见变化，开发期间的修复/调整不对外记录。命名维持 **快速SL / QuickSL**（工坊中英标题分别本地化），拆分只在公告预告、本次未实施。r4 横幅可见性与本批新 UI 的实机表现仍以玩家验收为准。

2026-11-26 r4命名审查通过：`research/2026-11-26-notice-r4-naming-review-ds.md`无阻断项，DS复建主DLL与19:06部署DC98C74D…一致。处理回执`research/2026-11-26-notice-r4-naming-review-receipt.md`；本次仅文档，无重建/部署/公告重置/提交/升版/推送。玩家最终确定保留“快速SL / QuickSL”，不采纳旧回执的“快速回溯”改名建议；未来工具箱“Shift工具箱 / Shift Toolbox”，旧身份由回溯部分继承，本次未拆分。搜索观察不等同平台索引规则；不据此擅改标题或声称线上材料已上传。r4可见性仍待明确实机验收。

2026-11-26 19:06 公告后续（覆盖下方部署，仍0.9.8）：已读最终DS审查，无阻断项；按玩家要求公告改8秒/r4/双语九节，新增未来拆分回溯与Shift工具箱预告，本次不拆包/改名/改ID。当前原生大夜报实际为AdvCalendar面板；黑屏/载入/两晨报/闭店面板等全部就绪连续0.5秒才创建。P2-1改为横幅存活且root层级活跃、Ready连续2秒后才确认，或成功点击详情；未确认中断会重试，不再创建即记已读。并非像素可见性证明，未知第三方遮挡仍需实机。480参考/22可选、原版与真实灯牌+VBE Mono、105挂点/76字段/仅files通过；部署`out/full-release-deploy-20261126-190633/deployment.json`，主DLL `DC98C74D4117CBF4145D14C395F8BA1A25409E23A5B62F658DC8904B20BE4623`，通用helper仍6129A2D3…/808448 bytes。95存档目录文件、设置、第三方Mod、游戏程序集不变，未启动游戏/提交/升版/推送。其余DS可选建议未擅自混入，处理与待办详见`research/2026-11-26-hydro-tooltip-final-review-receipt.md`；该增量需实机并不属原DS已审范围。

2026-11-26 本轮最终审查入口：`research/2026-11-26-hydro-tooltip-final-review-handoff.md`。玩家补充评分实机为`-->`回退，未确认字体支持箭头；r3公告可见性仍未明确验收。入口包含前次DS审查后新增功能/处理回执、478项测试与18:28部署哈希，覆盖旧增量交接；准备审查仅改文档，无重建/再部署/提交/升版/推送。

2026-11-26 18:28 验收后微调（覆盖下方部署，仍0.9.8）：水培工作后剩余量单独固定两位小数（6.33L），不改变通用成分表精度；生长评分合并为当前/工作后单行，即使不变也显示两端，按实际tooltip TMP字体及fallback查询箭头，缺字回退ASCII `-->`，阻塞时只显示当前值。玩家设置中已存在`0.9.8:20261126-chemistry-compat-r2`已展示记录，非未升版阻止；旧日志无正常展示时间，不能确定当时是否可见，未发现原生晨报时序的明确错误。不改原触发门控，公告修订到r3重播一次，新增每公告一次已读状态/成功展示标识日志，未手改玩家XML。478参考/22可选、原版和真实灯牌+VBE Mono通过，105挂点/76字段/仅files，HardFlags/Error空。部署`out/full-release-deploy-20261126-182854/deployment.json`，主DLL `41208059DA12F27492D83A3D5111920B7C58C79D4027C35F4450D29B2F5FC754`；双profile helper仍`6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23`/808448 bytes。95个存档目录文件、设置、其他Mod和游戏程序集未改，未启动游戏、提交、升版或推送；新箭头渲染和公告可见性待实机。

2026-11-26 17:58 后续候选（覆盖下方当前部署，仍0.9.8）：已逐项处理DS化学/灯牌review，保留其智能挤出70%修改。灯牌已知构建忙碌/拖动/未初始化/故障时，自动捕获在消费边界前等待，手动给出明确重试提示；不使用会留下缺元数据节点的catch后忽略方案。Commit状态空值防护及“原生图已写、缓存失败”诊断已补，仍严格拒绝不安全恢复；SceneUnloaded/Commit校验static零参数。新功能为详情键（默认Shift）在拖物/倾倒/工具操作时晚帧只读显示目标tooltip，基础提示加棕色实际绑定键提示；水培显示当前/工作后生长评分、剩余营养液及矿物质/(水+矿物质)、分品质产量，随机品质按期望、随机产量按范围，不推进RNG；自动改名水主成分、矿物质>=1.5%、原生六类生长污染物合计<=总液体1%的容器为水培营养液，保留量器/滴管/VBE排除。公告双语八节/r2，按玩家改写，正常进档重播一次。476参考/22可选、原版与真实灯牌+VBE Mono通过，408组确定品质分配与原生一致；105挂点/76字段/仅files，HardFlags/Error空。部署`out/full-release-deploy-20261126-175827/deployment.json`，主DLL `D9B5259FA4F0CA3F48ACFBA416CFE103F5C19AB30768789F02648C6F19C3AB51`，双profile helper `6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23`/808448 bytes。95个存档目录文件、设置、其他Mod和游戏程序集不变，未启动游戏、升版、提交或推送。回执`research/2026-11-26-chemistry-compat-review-receipt.md`，增量交接`research/2026-11-26-hydro-tooltip-handoff.md`；新UI/输入/实际预测与Commit仍需实机，不属此前DS审查通过范围。

2026-11-26 14:57 智能挤出实测回报修复/审查候选（覆盖下方部署）：玩家已验收平板置顶与试剂完整详情；智能挤出未生效根因为手动挤出机无电池仓，原生仓位 module=0/container=1/output=2，前轮错误套用了搅拌机容器索引2。现修正为1，提取精确身份判定并以游戏Mono原生MachineryPanelRow/GetBodyStorage/GetParentItem和旧GridPixelElement两路径回归，未改其他机器仓位。更新简报/详情替换为本批五节双语内容，修订`:20261126-chemistry-compat-r1`使其正常进档重播一次，保留5秒/6px灰底填充进度条及真实分隔线，无手改已读XML。474参考、22可选兼容、原版/真实灯牌+VBE Mono及105挂点/76字段/仅files扫描通过；挤出实际选品和新公告排版仍待实机。部署`out/full-release-deploy-20261126-145703/deployment.json`，主DLL `E2D9FA412879C516ECC29D750D3613F48FEC1D54A150B985AEE246CE5E25381C`，通用helper仍`6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23`/808448 bytes。95个存档目录文件/设置/其他本地Mod/游戏程序集校验不变，未启动游戏、提交、升版或推送，仍0.9.8。综合审查入口`research/2026-11-26-chemistry-compat-review-handoff.md`；当前HEAD `817af11`，需审查整个dirty工作区及未跟踪文件，非仅本轮索引修复。

2026-11-26 续修（当前候选，覆盖下段严格未知灯牌策略与部署）：平板标记改挂实际改名的 RefreshClientHeader postfix，修复 RefreshUI 仅排队导致标记闪烁；Shift/固定试剂详情显示全部非零成分，普通悬停三行不变；新增 `Chemistry.SmartExtrusion=true` 独立中英文设置，插入 chem_finisher 容器槽后按 >=70% 主成分（DS 审查轮按玩家实测要求由 99% 下调，含边界用例）和原版唯一配方选择产品，不自动生产，受化学主开关/所有权/加载回溯门控。用户要求未知 Proper Signboards 构建仅提示：专项 Root/Capture 跳过，不停用内存节点，不拦截常规存档；已适配节点的 fault/身份安全拒绝保留。共用兼容窗口不同消息等待重试，避免互相吞提示。473 参考、22 可选兼容、原版/真实灯牌+VBE Mono 离线及105挂点/76字段/仅files扫描通过，HardFlags/Error空；未知MVID跳过分支已离线测试，真实UI/机器操作待验收。部署 `out/full-release-deploy-20261126-143058/deployment.json`，主 DLL `EB891FB0A124725BF9EC6962E56F54CBEF38E64E8D237CD866AC8854B35D69CB`，双表helper仍 `6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23`/808448 bytes；95个存档目录文件/设置/其他本地Mod/游戏程序集校验不变，未启动游戏、重置公告、提交/升版/推送，版本仍0.9.8。交接 `research/2026-11-26-extrusion-details-followup.md`。

2026-11-26 修复候选（覆盖下方部署，版本仍 0.9.8）：新增 Proper Signboards 1.0.48 精确构建适配，捕获其独立原生挂位仓及记录、回溯时清派生视图并重建存档缓存；未知构建/故障/身份变化严格拒绝，未增加通用第三方图遍历或受限权限。常温纯咖啡搅拌合并十轮加液、减少重复成分与库存列表读取，复杂混合物仍用原版，慢批次仅 >=100ms 记总耗时；性能改善待实机。优质试剂前缀改 `*`；CustomUIWindow 交易平板接入 Shift+中键置顶及中英文棕色标记，原生刷新来客名后补回。471 参考、22 可选兼容、原版/真实灯牌+VBE 游戏 Mono 离线夹具及最终 104 挂点/76 字段/仅 files 扫描通过；灯牌 Commit 与 Unity 输入/渲染未离线执行，需实机验收。部署 `out/full-release-deploy-20261126-013515/deployment.json`，主 DLL `CAF3775F40AE36E382D4BFB96DAD4952B7428B3048D67ABB48F2BA8832C26195`，双 profile helper `6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23`/808448 bytes；94 个存档目录文件/设置/其他 Mod/游戏程序集校验不变，未启动游戏、未重置公告、未提交/升版/推送。交接 `research/2026-11-26-signboards-coffee-tablet-review-handoff.md`，不属于此前 DS 已审范围。

2026-11-25 06:37 **0.9.8 发布**（当前正式源码，覆盖下方全部候选记录）：玩家实机验收通过后按惯例升版推送。本批含 Shift＋中键固定详情（替代会被原版快速搬运干扰的 Shift 左键）、窗口磁吸与置顶、运行中机器模组槽解锁（默认关）、酿酒直接标注、滴管/量筒/小容器明细、VBE 营养果/梦帽菇入桶守卫、普通 tooltip 机器警报、公告七节/r4，以及固定详情在物品栏定位到左上角的修复（改取所属 rootCanvas，尺寸统一为 Canvas 单位）。版本 0.9.8 同步三份 manifest、`Plugin.Version` 与 `MelonInfo`；469 参考、22 可选兼容、原版/真实 Workshop VBE Mono 离线夹具、102 挂点/76 字段/仅 `files` 扫描均通过，HardFlags/Error 空。修复后清洁源码打包 `out/quicksl-0.9.8-universal`（ZIP `8F8EBCF0…` 592498 bytes、主 DLL `2AD3445D9ECD5F383433572A0887A9E31A7CB857FB7975896EB42A2DF2865423`、双 profile helper `6129A2D3…`/808448 bytes、通用包额外布局来源 `CBB8F36A…`/683008 bytes）；部署 `out/full-release-deploy-20261125-063714/deployment.json`（91 个存档目录文件不变、设置未导入、未启动游戏、`InGameValidated=false`）。推送 `main` `0815d69..608c1b6`。工坊材料 `out/workshop_changes_0.9.8_zh.bbcode`、`out/workshop_description_0.9.8_zh.bbcode`、`out/workshop_changes_0.9.8_en.bbcode`、`out/workshop_description_0.9.8_en.bbcode`；公告只列相对 0.9.7 的新增与可见变化，开发期间的修复/调整不对外记录。DS 审查不覆盖 06:11/06:22 两个增量；固定提示的渲染与输入时序仍以玩家验收为准。

2026-11-25 06:22 固定详情物品栏定位修复（覆盖下段部署）：不再把 Owner 的直接父 RectTransform 当作屏幕边界，改取所属 rootCanvas；物品与提示框实际尺寸统一换算到该 Canvas，再以位移修正左上角，保留 Transform 仍属于原窗口及原有关闭清理，不改 Shift 中键输入。469 参考、22 可选兼容、原版/真实 VBE Mono 离线及最终兼容扫描通过，Unity 实际物品栏/容器位置仍待复测。部署 `out/full-release-deploy-20261125-062239/deployment.json`，主 DLL `015C2016E86BA0CD32524B6DFB368874142B5DDFC401C308B33D2CE4FB182FAB`，双表 helper `6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23`；91 个存档目录文件校验不变，未导入设置、未重置公告、未启动游戏/提交/升版/推送。本增量不属于此前 DS 已审范围。

2026-11-25 06:11 Shift中键固定修复（覆盖此前左键手势/部署）：实机复现原版ItemQuickTransferAltHandler在释放左键时独立执行快速搬运，UI点击postfix不能阻止；玩家同意把固定统一改为物理Shift＋中键，窗口置顶和完整物品详情均适用。新增TreeNodeRender.OnPointerDown前缀记录中键是否允许，点击完成时重新检查并消费；左键按住/按下、单件/批量/UI拖拽以及恢复/输入门控会拒绝或取消，无原生输入替换/业务回调注入，保留原版中键翻转和Shift左键搬运。DetailedCompositionTooltip.BuildDetails复用临时/固定两条路径，固定扩展至水龙头、化学成分及机器预览，原开关、跟随位置/层级/外层关闭/恢复清理保留。公告r4重播一次，中英文与设置同步。构建0警告/错误，468参考、22可选兼容、原版/真实VBE Mono及102挂点/76字段/官方仅files扫描通过；真实输入时序和机器固定视图待实机，不声称原DS已审此增量。部署`out/full-release-deploy-20261125-061141/deployment.json`，主DLL `FFCAD220DF7D8FF1CBAFFB197C1ADFC08562C1ECAC664DDCFDE7263F07F731BE`，helper `6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23`/808448 bytes，91个存档目录文件/设置/其他Mod/游戏程序集未变，未启动游戏/提交/升版/推送。增量交接`research/2026-11-25-middle-pin-followup.md`。

2026-11-25 QoL DS审查处理：`2026-11-25-qol-final-review-ds.md`结论无发布阻断代码问题。本轮只统一最终交接为r3/七节/6px灰底/5秒及05:29部署，逐项处理见`research/2026-11-25-qol-final-review-receipt.md`；不改运行时代码、不重复部署、不重置公告、不提交/升版/推送。已复核安装主/helper哈希仍2A8F30EA…/6129A2D3…，并用安装产物隔离副本+Workshop真实VBE DLL额外跑Mono夹具通过（非业务拖放实测）。原生普通/机器Validate均会重算标题宽度；VBE独立原料仓入仓在后续CanTarget之前，暂不据假设移除守卫。净水报警顺序遵守玩家既定判据，不按审查UX建议擅改；固定提示窄屏/布局风险和profile覆盖部署校验建议保留后续专项。实机优先VBE首次入桶及Shift固定详情与置顶并发。

2026-11-25 05:29 简报微调（覆盖下段当前候选）：用户已验收独立进度条可见。本次改从空到满、左向右填充，下层全宽灰色静态底轨，仍6px/5秒；详情末尾新增中英VBE营养果溶解入桶说明，保留真实分隔线且无空白自然段。修订号`:20261125-qol-r3`使其重播一次。构建0警告/错误、467参考、原版Mono离线回归及最终双表扫描通过，101挂点/76字段/仅files。最终部署`out/full-release-deploy-20261125-052936/deployment.json`，主DLL `2A8F30EA4FA6E0208A666451A7E4C52FACAAEFFC256A8B2207AD71BE0533CDDD`，helper `6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23`；先前05:28部署误带单beta helper，已立即用核对后的808448 bytes双profile表重新扫描/部署修正，不使用该中间部署。91个存档目录文件、设置、其他Mod及游戏程序集未变，未启动游戏/提交/升版/推送。

2026-11-25 05:21 简报微调（覆盖下段部署及3秒/3px说明）：改为独立6px蓝色Image填充+深色底轨，位于“点此查看详情”下方，替代分隔线预制件子节点，布局明确预留高度并强制重建；展示5秒。普通tooltip警报移除“警报：/Warning:”前缀，保留红字。公告修订号推进至`:20261125-qol-r2`，使前一修订已读玩家下次正常进档后重播一次，不手改玩家XML。467参考、原版Mono离线回归和最终101挂点/76字段/仅files扫描通过；22可选兼容和真实VBE沿用05:13同批结果，未宣称本轮重跑。部署`out/full-release-deploy-20261125-052130/deployment.json`，主DLL `D1956D3A2C6D99868D76B5202593FF0AD55A6F37850B2029EF05DAF8A64A8A35`，helper不变；91个存档目录文件、设置、其他Mod及游戏程序集不变，未启动游戏。实际进度条可见性待实机确认。

2026-11-25 05:13 QoL 最终候选（覆盖下方04:42/04:32交互及部署说明，正式版仍0.9.7）：物理Shift左键单击窗口标题/背景切换置顶，重复点击取消；按置顶顺序排列，中文棕色`[置顶]`、其他语言棕色`[PIN]`，已移除系统字体尝试。Shift单击液体物品固定成分明细，重复取消，纯Mod UI引用和Transform父子关系，不写入原生窗口业务图；跟随物品和所属窗口层级，外层容器关闭及加载/回溯时清除。机器警报也显示在普通tooltip中，完整预测仍需详情键；净水完成仅依据同一次Purify预测无实际去除量判断（含零预算），不另设污染物存在判定。新公告六节包含机器模组槽解锁，移除空白段行并保留真实分隔线；按`版本:20261125-qol`记账，不手动重置玩家设置。467参考、22可选兼容、原版/真实VBE游戏Mono离线回归及101挂点/76字段/官方仅files扫描通过，HardFlags/Error空；固定明细实际渲染/层级/输入及新警报仍待实机验收，不能把离线通过当Unity验收。部署`out/full-release-deploy-20261125-051339/deployment.json`，主DLL `5CB360AD211F75169B1966C4F8057C626CD4C834EB5305BBCC70C929EE0AD630`，helper `6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23` / 808448 bytes；91个存档目录文件、设置、其他Mod、游戏程序集校验未变，未启动游戏/提交/升版/推送。当前审查入口`research/2026-11-25-qol-final-review-handoff.md`，原始0.46D程序集仍未重验。

2026-11-25 04:42 置顶交互回报修复（覆盖下段部署）：改为物理Shift+左键单击原生窗口标题/空白背景，不再在打开窗口时置顶；不处理物品、按钮、拖动及第二次双击事件。多个置顶严格按后置顶在上，普通聚焦不重排；Shift再点已置顶窗口将其升至最上。原生Shift+双击物品转移不改。Player.log确认TMP加载三种系统Font均失败，仍保留系统字体尝试及可靠`[PIN]`兜底，无新字体文件/权限/依赖；兜底原因每会话仅记一次。461参考、原版/真实VBE Mono、101挂点/76字段及仅files扫描通过，HardFlags/Error空。部署`out/full-release-deploy-20261125-044254/deployment.json`，主DLL `62202F25D4A0DF19B6DC7B9F0E1FEB67411EB9F4CC7F90A4CA8C1E2FD6C14D33`，双表helper不变，91个存档目录文件/原设置/其他Mod/游戏程序集不变，未启动游戏/提交/升版/推送；新交互待实机验收。

2026-11-25 04:32 QoL/窗口手势候选（覆盖下方旧候选部署；正式版仍 0.9.7）：部署 `out/full-release-deploy-20261125-043230/deployment.json`，主 DLL `780EA25AD7CEB89700D935FE2C3BD780C103E4A4EC938432A46AFE292E4EE001`，双 profile helper 仍为 `6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23` / 808448 bytes。VBE 配方原料入桶拦截原生拖放后续 Target/Dissolve，包含第一个原料尚无扩展标签的情况；工作机器模组仓补准入入口，保留尺寸/过滤/所有权；荧光莓/酿酒恢复直接标注；按键成分明细支持量筒/滴管及不足150mL容器。新增 `Windows.ShiftSnap=true` / `Windows.ShiftPin=true` 独立中英设置，使用物理左右 Shift，只处理原生可移动 PixelWindow、同父层吸附/置顶，不跨原生模态层，不把图钉写入 titleString 或快照；关闭取消，字体回退最终 `[PIN]`。公告底部3px倒计时条，不重置已读版本。460参考、22可选兼容、原版/真实VBE游戏Mono离线回归通过；100挂点/76字段、官方能力仅files、HardFlags/Error空。91个存档目录文件/原配置/其他Mod/游戏程序集不变；未启动游戏、未提交/升版/推送。新交互和字体仍待实机验收；稳定版原始程序集未重验。交接 `research/2026-11-25-qol-window-gestures-handoff.md`。

2026-11-24 13:13 DS 审查建议回执（当前部署，覆盖下方记录）：审查无必修项；采纳 P1 已展示更新时在 Ready 前短路（保存重试及存活简报关闭仍继续），P3 设置入口相同异常去重、成功后复位。其余建议/待实机项不据猜测改动。部署 `out/full-release-deploy-20261124-131337/deployment.json`，主 DLL `9F6EFACDBFE3AFBEB6CE7E4FF6C38CD00C64EC54CD5FAF761CCD96FC3F697E39`，双 profile helper 不变。454 固定参考、20 可选兼容、游戏 Mono 回归及最终双表 92 挂点/76 字段扫描通过，仅 files、HardFlags/Error 空。90 个存档目录文件与设置未变，不再重置简介、不启动游戏、不提交/升版/推送；版本仍 0.9.6。见 `research/2026-11-24-quicksl-features-review-receipt.md`；DS 本文所审 03:00 候选不包含后续简介排版、大存档节点修复及本次两项小修，不能称这些后续补丁已获独立审查。

2026-11-24 0.9.7 推送（当前正式源码，覆盖下方全部候选记录）：回执项复核通过后按惯例升版并推送。版本 0.9.7 同步三份 manifest、`Plugin.Version` 与 `MelonInfo`；代码提交 `15826e2`（44 文件，含详情键/快速烧开水/更新简报/存档外设置入口/双击鉴定与化学摘要）、记录提交 `ea8f371`（本轮交接、审查与回执）。清洁源码打包 `out/quicksl-0.9.7-universal`（build commit `ea8f371`、`BuildDirty=false`，ZIP `486B1E9F…`、584933 bytes），主 DLL `3B5E163741A8FAFAA633C71687A0C7B5F7F8173B413F5EDFCBF7BCFDFE02A54B`，双 profile helper `6129A2D3…` / 808448 bytes（通用包额外布局来源 `CBB8F36A…` 683008 bytes）；通用包最终扫描 92 挂点/76 字段、能力仅 `files`、`HardFlags/Error` 空、原生 manifest 解析通过。部署 `out/full-release-deploy-20261124-133646/deployment.json`（安装哈希与包一致，90 个存档目录文件不变，设置未导入，未启动游戏）。454 参考、20 可选兼容、14 原生 IO 夹具通过；本轮含 P1 已展示简报在 `Ready` 前短路、P3 设置入口异常去重，以及 21 MB 大存档索引上限放宽（`SaveMetadata.MaxTextLength` = 256 MiB）。后续审查建议：该上限可再收紧到 64 MiB 以限制主线程整读开销；UI/热处理仍待实机验收。

2026-11-24 13:01 文件初始节点修复（覆盖下方旧部署）：`out/full-release-deploy-20261124-130102/deployment.json`，主 DLL `59F077C1D12FAA07922A7E7B2DAE80EF0A1778801906CC9D8AEA6687974E1C86`，双 profile helper 不变。玩家第 96 天活动档 21607544 bytes / 21409522 字符，旧 16 MiB 字节索引和字符解析上限均会拒绝，导致内存禁用时晨间文件节点缺失；现在分别放宽至 256 MiB 字节 / 256 Mi 字符，保留阶段/日期/槽位/周目/所有权/链接边界。面板同步 PointRevision，避免同数量节点覆盖后不刷新。453 固定参考测试及 1 项实际第 96 天存档只读解析（合计 454）、真实游戏 Mono 大文件晨间索引和现有回归、最终双表兼容/官方能力扫描通过。部署确认 90 个存档目录文件不变；随后按用户要求独立备份配置到 `out/update-notice-settings-backup-20261124-130137.xml`，只清空 `Updates.LastShownVersion`，其他配置经 XML 比对不变。未启动游戏/提交/推送/升版；初始节点与新简介尺寸仍待本次实机复验。

2026-11-24 12:50 简报排版修复（覆盖下方旧部署）：部署 `out/full-release-deploy-20261124-125041/deployment.json`，主 DLL `25A9ABCC6EDB131FDA6F82B849F0E6705D29447A93F9A489C479F17A6B0357BA`，双表 helper 不变。简介宽上限 380、按正文和底部“点此查看详情”实测高度收紧，关闭纵向拉伸；详情独立 `---` 行转原生分割线，移除空白段行。452 参考测试及最终 92 挂点/76 字段/官方仅 files 扫描通过。90 个存档目录文件及配置未改，不重置一次提示版本；新排版待实机复验，完整 Mono/VBE 沿用上批。交接文档顶部有本次补充。

2026-11-24 QuickSL 本轮最终部署：`out/full-release-deploy-20261124-030020/deployment.json`，主 DLL `573D2C33BEB59E346275894C56A6FA5465800508FBE40738C30D6662195B8918`，双 profile helper `6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23` / 808448 bytes；450 参考、20 可选兼容、beta 原生解析/Mono/VBE 回归通过，92 挂点/76 字段，扫描仅 files，HardFlags/Error 空。90 个存档目录文件、其他 Mod、游戏程序集未变；未启动游戏、未导入设置、未提交/推送/升版。上批用户验收不代表本批 UI/加热已验收；交接入口 `research/2026-11-24-quicksl-features-review-handoff.md`。下文旧主 DLL/90 挂点等为历史记录。

2026-11-24 QuickSL 功能候选（覆盖下文旧交互说明，版本仍 0.9.6）：显示名改 QuickSL，技术 ID/DLL/目录/XML 不变。三份 manifest 修正原生版本列表元素为 `<Version>`，通用包声明 0.46D + 1.0.0.16，描述以单个原生 Description 同列中英（加载器无独立语言字段）。原生 ModConfigWindow 仅为本 Mod 追加打开功能面板按钮，未进存档也可设置。`Inspection.KeyItemDetails=LeftShift` 统一按住显示成分/水龙头/机器预测/酿酒，绑定 Shift 时左右等价，改键后不再隐式接受 Shift，None 停用；未按键不追加机器/酿酒信息。`Chemistry.RapidBoiling=false` 独立于非热快速反应，仍受化学总开关/恢复/载入/夜间门控；仅加热板精确容器槽、沿用原生蒸发元数据，微生物 >=70°C 全清，不处理其他热反应或 VBE 容器。双击鉴定仅真实原版 magnifier，合法性检查完成后才关闭旧页并调用真实 Target，不接管不可鉴定物品。`Updates.LastShownVersion` 记录每版首次简报，正常商店且结算窗口关闭后稳定 0.5 秒展示，3 秒关闭/任意处点击展开；按展示记账，不等点击，写盘失败同会话不重复并限频重试。新文本中英/其余英文回退。UI 与实际热处理仍需实机验收，最终测试/部署记录见本轮交接，不得用旧验收冒充本批验收。

2026-11-24 QoL 最终审查候选（覆盖下段部署）：用户已验收四项微调；多语言检查补齐来源标识，中文“快速SL”、其他语言“Quick SL”。原生成分/水质名称跟随游戏语言，Mod 自有 UI 仍中文/英文两套、其他语言英文回退。最终部署 `out/full-release-deploy-20261124-015204/deployment.json`，主 DLL `B764A72A5E4A21CD04071F62DE6C8077339447BD7132B29618C708C37EA0E13B`，helper 仍为 `6129A2D3…`，443 参考、20 可选兼容、真实游戏 Mono/VBE 回归通过，90 挂点/76 字段、扫描仅 files。该英文来源标识补丁尚未单独实机复测，未提交/推送/升版。当前审查入口 `research/2026-11-24-qol-final-review-handoff.md`，旧 QoL 文档仅作过程记录。

2026-11-24 QoL 微调（当前候选，覆盖下方旧行为描述）：用户已实机验收上轮修复。新部署 `out/full-release-deploy-20261124-014319/deployment.json`，主 DLL `913916DC14D43935A1C693E09B9B4911195A96DD0128D2F0CEC801BE2D29D289`，双 profile helper 仍为 `6129A2D3…`（808448 bytes）。冲突窗口追加左下角小号 LightBlue 斜体「快速SL」来源；`Compatibility.NotifiedMemoryConflicts` 单独持久记录已成功显示弹窗的 Mod ID，不复用首次禁用默认值的 HandledMemoryConflicts，UI 未就绪不标记，IO 失败保留本轮记录并限频重试。水龙头明细及隐藏查询覆盖均要求按住任一 Shift，松开保持原版行为；明细保留连接说明。容器/试剂/水龙头明细统一按原始占比稳定降序，相同占比保留注册顺序，主成分颜色独立于排序。441 参考、20 可选兼容、真实游戏 Mono/XML 重载及真实 VBE 回归通过，最终合并扫描仍仅 files、HardFlags/Error 空。90 个存档目录文件、其他 Mod、游戏程序集未变，未启动游戏/导入配置/提交/推送/升版。新增交互与来源标识排版待实机验收，见 QoL 交接文档顶部。

2026-11-24 QoL 实测回报修复（当前候选，取代下段首轮候选）：已部署 `out/full-release-deploy-20261124-012906/deployment.json`，主 DLL `88941921B5D9ECF97442DF9368B4BCA0E78755F518176BB9E3E7BD3F08BF0C63`，helper 仍为 `6129A2D3…`。440 参考、17 可选兼容及真实游戏 Mono 回归通过，90 挂点/76 字段，最终合并表扫描仍仅 `files`、HardFlags/Error 空；未提交/推送/升版。水龙头载入只更新 shortDescription、未清除原生 IS_TOOLTIP_DISABLED_TAG 的路径已核对：仅启用明细且已解锁水源时覆盖 HasTooltip 查询，不改标签；最终 tooltip 追加明细，保留原五层内容和连接说明，Shift 不清空水龙头。水龙头只列占比；容器按实测文字宽度使用 TMP 固定列位置，显示名称/含量/占比，自动 uL/mL/L、两位小数百分比，非零痕量 `<0.01%`。测宽后 SetText 复原 TMP 处理缓冲，防止未变化 tooltip 被原生缓存复用时错显。冲突通知按文字动态测量尺寸，仅保留标题 X。修复与待验收见原交接文档顶部补充。

2026-11-24 QoL 开发候选（覆盖下方旧部署的“最新”表述）：基于 `8169488` dirty 工作区构建，版本仍为 **0.9.6**，未提交/正式打包/推送。已部署 `out/full-release-deploy-20261124-003203/deployment.json`，主 DLL `24BD0F120C4407676C642FFC2778556514E96720D2CA18C3C185D6C66D8B0C33`，双 profile helper 仍为 `6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23`（808448 bytes）。438 参考、17 可选兼容、13 存档动作、14 原版历史测试通过；游戏 Mono 夹具验证 8 种摘要组合、模组仓精确身份及无锁字段写入、晨间文件只读索引；真实 VBE 回调身份往返通过。最终合并 DLL 官方扫描为 89 挂点/76 字段、仅 `files`，HardFlags/Error 空。安装哈希一致；90 个存档目录文件、其他本地 Mod 和游戏程序集未变，未启动游戏/导入配置。稳定分支原始程序集仍不可用，不能声称稳定版重新实测。交接及待验收项见 `research/2026-11-24-qol-review-handoff.md`。

本轮新配置：`WaterQuality.PipeComposition=true`、`Chemistry.ShiftComposition=true`（均不依赖化学辅助主开关）；`Chemistry.ShowCurrentShare=true`、`ShowExpectedPurity=false`、`ShowGrade=false`；`Machines.UnlockWorkingModules=false`。试剂精简/星标仅针对酸、碱、脂合剂、**还原剂 `reducing_agent`**，不包含稳定剂（用户已确认）；固定主成分/化学污染物/重金属三行，零杂质也列出，星标严格 `<0.10%`。细目显示不受摘要三开关裁剪。Shift 在两个最终 tooltip postfix 替换完整结果，松键由原版逐帧重建恢复。水管按原版两种升级共用的 grade=1 分布只读计算，不调用加水/RNG；运行中机器只绕过模组仓查询及新版显式拖拽拦截，不改锁字段/工作标签。冲突通知改为原生手动关闭窗口，UI 未就绪时限频重试；内存禁用时只读索引匹配槽位/周目的晨间备份，正常显示文件初始节点，历史读档后补锚点仅尝试一次，不在每轮 Tick 重读不匹配的活动存档。

2026-11-23 设置调整（覆盖上段 Claims 强制门控的旧说明）：`CustomerCheckpoint.DisableMemory=false` 控制全部内存建点/恢复。首次检测 Claims 时置 true，`Compatibility.HandledMemoryConflicts` 持久记录已处理 ID；玩家手动关掉后不再因检测/重启覆盖，移除 Claims 也不自动复位。强制启用不代表完整支持，相关 BUG 不受理；Claims 存在时文件读档仍强制完整原生载入并跳过顾客/门修复。`Rewind.WholeDayUseMemory=true` 仅控制 F5/F7，关闭走文件、个别节点仍可用。`Chemistry.AutoExtraction/RapidReactions` 缺省改为 false，不迁移覆盖旧值。审查入口见 `research/2026-11-23-settings-compatibility-review-handoff.md`。

最新部署为 `out/full-release-deploy-20261123-232330/deployment.json`（通用包 `D:\Program\GameProject\out\release-0.9.6-universal`，ZIP `609C1D8E…`、573261 bytes，构建提交 `8169488`、clean）：主 DLL `37E9FE8E…`、双 profile helper `6129A2D3…`（808448 bytes，0.46D + beta 1.0.0.16），manifest 声明 0.9.6，安装目录哈希与包一致。打包/部署按正式流程重跑：ReleaseCompatibility（84 挂点/76 字段、官方扫描仅 `files`、HardFlags/Error 空）与 Mono 夹具全部通过；90 个存档目录文件、其他本地 Mod、游戏程序集未变，未导入玩家配置。实机首启（游戏 1.0.0.16）加载 `version=0.9.6 layoutBackend=static-token-v1`，Claims 首次检测一次落盘 `DisableMemory=true` 与 `Compatibility.HandledMemoryConflicts=yexiaoyang.InteriorExpansion`（`PSTools.settings.xml` 23:24:33，无异常），随后 `memoryCheckpoints=False fileRestore=native-menu`；完整玩法矩阵仍待验收。此前 `9BA884F8…` / `BA005FA4…` / `54C4DFD2…` / `9547C8A0…` 均为更早候选。

2026-11-23 审查修复：禁用内存不再连带关闭卷帘门维护；首次冲突两个值批量赋值后显式保存，失败就地捕获/去重/5 秒重试，保存最新选择（未持久化前退出不保证跨进程记忆）。Mod 列表 1Hz 轮询，处理 ID Trim/缓存。F5 文件选择复用元数据，保留旧命名/较大 UTF-8 存档回退且不纳入清理；确认文案按锚点实际可用性判断。强行启用只追加状态提示，不追加确认弹窗，依赖偏好仍可编辑。逐项采纳与不采纳原因见 `research/2026-11-23-settings-compatibility-review-receipt.md`。

2026-11-23 推送：版本升至 **0.9.6**（三处 manifest、`Plugin.Version` 与 `MelonInfo` 同步），源码提交 `8169488` 并推送 `main`；随后按正式流程重新打包、部署并完成首次实机启动核对（见上段）。三轮审查见 `research/2026-11-23-settings-compatibility-review-ds.md`、`…-round2-ds.md`、`…-round3-ds.md`；遗留项：`RestorePreviousDay` 日期读取失败仍是静默返回（HEAD 既有、低危）。

2026-11-23 本轮开发候选已部署，主 DLL `9547C8A0…`、双 profile helper `6129A2D3…`（808448 bytes），记录 `out/full-release-deploy-20261123-141215/deployment.json`。基于 HEAD `48652b2` 的 dirty 工作区构建，未提交/打正式包/推送。430 参考 + 12 可选兼容 + 13 读档 + 14 历史测试、无第三方 DLL 的游戏 Mono 回归、真实 VBE 回调往返及最终合并 DLL 扫描通过；只有 `files`。未启动游戏，89 个存档和其他 Mod 校验未变。Claims 内存节点仍明确停用，组合实机待验收。

---

## 1. 快速开始

```powershell
# 构建（需 src\ProbablyStolenTools\local.props 指向游戏目录）
dotnet build src\ProbablyStolenTools\ProbablyStolenTools.csproj -c Release

# 离线测试
dotnet run -c Release --project tests\CheckpointTests\CheckpointTests.csproj
dotnet run -c Release --project tests\ReleaseCompatibility\ReleaseCompatibility.csproj -- `
  "<游戏目录>" src\ProbablyStolenTools\bin\Release\Mono\ProbablyStolenTools.dll release\manifest.xml

# 部署（必须先关闭游戏）
.\scripts\Deploy-FullRelease.ps1

# 当前本机为游戏 beta 1.0.0.16；单版本验证
.\scripts\Test-StaticLayouts.ps1 -GameDir "<游戏目录>" -ManifestPath release/beta/manifest.xml
.\scripts\Deploy-FullRelease.ps1 -GameDir "<游戏目录>" -ManifestPath release/beta/manifest.xml

# 通用包：干净源码构建当前 beta 表，再合并另一版原始 helper
.\scripts\Package-FullRelease.ps1 -GameDir "<游戏目录>" -ManifestPath release/universal/manifest.xml `
  -AdditionalLayoutPaths "<稳定版原始helper>" -OutputRoot "<新输出目录>"
.\scripts\Deploy-FullRelease.ps1 -GameDir "<游戏目录>" -PackageDirectory "<输出目录>\ProbablyStolenTools"
```

当前 0.9.5 通用候选：**430 项**参考测试、13 项存档动作测试、14 项原版历史/齿轮 IO 测试、11 项玩家工具断言（含随包源码复建）；84 挂点/76 字段、16 项官方保护名单核对；双 DLL 扫描仅 `files`，`HardFlags/Error` 空。当前 beta 单表 2883 类型/8160 字段；合并 helper 783872 bytes，按游戏 MVID 自动选择 0.46D / 1.0.0.14 两套完整注册表。beta Mono 的精确布局、完整图往返、预算/边界/GC/MVID/失效令牌拒绝及 500 组耗水对照通过；正式版原始程序集不可用，**稳定分支未重跑实机/扫描验收**。离线通过不等于 Unity 活体验收。详见 `research/2026-11-16-beta-static-layout-compatibility.md` 与 `research/2026-11-16-universal-static-layouts.md`。

0.9.5 新增：**自动建立节点默认开启**（XML `CustomerCheckpoint.AutoCapture`）；关闭后不自动创建晨间/顾客/闭店/返店节点，手动建点与已有节点回溯仍可用，F5/F7 改走文件读档。设置页增加 **快速重载存档** 与 **重掷随机数** 两个带确认/门控的操作，快捷键默认未绑定；快速重载只读当前槽位磁盘档、不先保存/不覆盖活动档，重掷只影响后续随机源、不重建已有内容。见 `research/2026-11-16-save-actions.md` 与 `research/2026-11-16-automatic-checkpoints.md`。

上一批统一审查入口：`research/2026-11-15-feature-batch-review-handoff.md`，覆盖 `44cf260..48853fb`；回执见 `2026-11-15-feature-batch-review-receipt.md`，0.9.3 已由 DS 推送。本轮补 N1/N2 文件保护、N3 不匹配立即取消、N4 脏源码拒绝打包；beta 适配及构建来源以 `research/2026-11-16-beta-static-layout-compatibility.md` 为准，不把旧 0.46D 实测当作 beta 验收。

0.46D 的 2861 类型/8108 字段属于旧分支表，不能混用本轮 beta 的程序集或验收记录。通用包见 `research/2026-11-16-universal-static-layouts.md`：离线合并旧正式表与当前 beta 表，主 DLL 接口不变；只共享相同访问代码，各版本完整注册表/MVID 仍独立，未知指纹拒绝。当前安装为 `8fd29fd` clean 构建的 0.9.5 通用包（主 DLL `F5E8804A…`、helper `8503FA2D…`、ZIP `F2AA18BA…`，包 `out/universal-release-0.9.5-auto-on`）；正式版原始程序集仍缺，不能宣称正式分支重验通过。

### 版本与发布

玩家静态表工具的独立分发流程：运行 `scripts/Publish-StaticLayoutTool.ps1`，取得 `out/static-layout-tool-日期/ProbablyStolenTools-StaticLayoutGenerator-win-x64.zip`。在 GitHub 单独上传附件，不能把工具 exe/Mono.Cecil 放进 Mod 的 Assemblies 或 Mods。玩家完整用法见 `tools/StaticSnapshotLayouts/PLAYER-README.md` 与 README；先退出游戏，执行 `Generate-StaticLayouts.ps1 -GameDir "<游戏目录>"`，默认只生成。`-Install` 只支持默认 `Mods/ProbablyStolenTools` 安装，会把旧 helper 备份到工具 generated 目录，再只替换 helper。该工具不能自动适配任意第三方类型或修复游戏 API 变化，不代表新版本已验收。工具附件已打包测试，但**尚未上传 GitHub Release**，不要声称已可从 Release 下载。推送使用用户临时 token，不写入文件、日志或命令历史。

- 唯一真源：`release/manifest.xml` 的 `<ModVersion>`；`Plugin.Version` 必须同时改。
- 规则：**大版本、中间版本手动决定；小版本每次 `git push` 前 +1**。当前 **0.9.9**。
- 纯文档提交不单独升版；推送前重新构建 → 两组测试 → 部署 → 正常 `push`（不 force）。
- 推送使用用户临时 token，不写入文件、日志或命令历史。
- `release/manifest.xml` 只声明稳定游戏 0.46D；`release/beta/manifest.xml` 只声明 beta 1.0.0.16；`release/universal/manifest.xml` 声明两版。三个 ModVersion 必须与 Plugin.Version 一起更新。不能仅改 manifest 宣称新版本兼容。
- 用干净源码执行 `scripts/Package-FullRelease.ps1 -GameDir "<游戏目录>" -ManifestPath release/beta/manifest.xml` 构建 beta 包。通用包改用 universal manifest 并传 `-AdditionalLayoutPaths "<另一版原始helper>"`；离线合并和逐版本等价验证后，扫描实际合并产物。每包仍仅主 DLL、一个 helper、manifest、preview；正式包和工具包均拒绝 dirty 源码，保留源码提交/哈希/包体记录。`Deploy-FullRelease.ps1 -GameDir ... -PackageDirectory "<包目录>"` 验证当前分支 profile/扫描后部署。不能以 beta 通过代替两版实机验收。

---

## 2. 回溯架构（0.9.0）

顾客/手动节点采用**完整对象图 + 精确实体载荷**的混合后端，不是原版 SaveGame/LoadGame，也不是所有集合的逻辑重建。

### 2.1 精确实体块

`SemanticEntityBlocks` / `SemanticFieldBlock` 在主 `StateGraph` 遍历中一次捕获 owner 的全部非 Visual 字段：

| 类型 | 覆盖 |
|---|---|
| `GameItemElement` | 物品继承链业务字段、引用、Runtime 字段 |
| `GameGridInventory` | 库存继承链、成员/归属/锁/关联引用及 Runtime |
| `GameSlotInventory` | 单槽库存继承链及 Runtime |
| `PixelWindow` | 窗口业务字段、关系与 Runtime |
| `GridShapeBuilder` | 完整形状标量及原 `data` 数组引用 |

- 集合底层数组、字典容量/版本、闭包等仍由完整图保存；不重建列表/字典。
- 字段清单是**精确 schema**：逐 DeclaredOnly 类型字段数必须完全相等；不匹配时实体块回退普通字段图，不静默漏存。Mono 迁移候选的普通字段图同样使用静态表，未知布局或模块版本变化会拒绝建点，不回退动态枚举。
- 块与 legacy 槽位通过 `slots.Count == Fields.Length`、槽名唯一、`IsReference`、`Size==8` 对齐；不匹配即回退。
- 写回直接写原字段，不调用游戏业务 setter、库存移动、买卖或到店回调；引用按原身份。
- 预算仍按对象/槽/字节逐槽计数，不因块压缩而放宽。

### 2.2 生产默认值

| 配置 | 默认 | 说明 |
|---|---|---|
| `Snapshot.SemanticEntityBlocks` | true | 单次遍历实体块 |
| `Snapshot.SemanticDevelopmentChecks` | false | 关闭额外旧图 oracle/shadow/pilot |
| `Snapshot.TagBlocks` | true | 精确 `TagState` 九字段块 |
| `Snapshot.IntArrayBlocks` | true | 完整 `int[]` 块 |
| `Snapshot.OwnerReconcile` | true | 刷新后有界 owner 二次修复 |
| `Snapshot.SkipUnchangedScalars` | true | 未变化框架标量免写 |
| `Snapshot.TypedScalarComparisons` | true | 强类型标量比较 |
| `Snapshot.DirectReferenceComparisons` | true | 直接身份比较 |
| `Logging.Verbose` | false | 详细诊断/几何/群拖日志 |
| `Debug.Diagnostics/SelfTest/PreviewPanel` | false | 不启动自动化 |

旧单项 shadow/pilot 开关即使曾持久化为 true，也必须受 `SemanticDevelopmentChecks` 总门控；恢复入口在 `CustomerCheckpoint.Restore` 重新检查该值（`b99864b`），关闭后一律走完整 `Graph.Restore()`。

### 2.3 恢复顺序

1. 业务图还原（必要时第二次还原）；
2. `VisualState.Restore()`：父级、锚点/枢轴/尺寸/位置/缩放/旋转、Image sprite/color/enabled/raycast；
3. `RestoreItemGeometry()`：按还原后的 `modifiedShape` / `modifiedXOrigin` 调原生 `UpdateRectTransform()`，写回正常层与拖拽层尺寸、锚点、背景原点；不符即抛错；
4. `VisualState.RestoreTransforms()`：用捕获的原生几何收尾朝向，避免旋转被清零的 `modifiedShape.orientation` 覆盖；
5. `Differences()` 全量校验；owner 修复失败则 full fallback 并再次全量校验。

物品仍排除六个布局缓存：`appliedMinX`、`appliedMinY`、`appliedOrientation`、`appliedGridSize`、`appliedFlipped`、`validateLock`。

顾客恢复保存原 renderer 身份、sprite、颜色/渲染开关、MaterialPropertyBlock 和完整局部变换链（含 `RightLeave` 父级）；DOTween 路径保存游标/方向/速度，seek 时屏蔽回调并在 finally 归还，不重播到店/离店。

群拖/框选：单件拖拽、群组拖拽、按键未释放的框选/待拖拽统一门控；静态选区在预检通过、业务写入前调用原版 `Cancel()`，不调用 `EndGroupDrag` 提交被放弃的时间线。

---

## 3. 多语言标注

- 水质/纯度/评级/酿酒标注优先读取游戏原生语言键（`Mechanic` / `Item` / `UI` 表），随游戏语言切换。
- 回退链：当前语言表 → 英语表 → 代码内英文文本；不修改原生 locale 或表。
- 缓存键 `(locale, table, key)`，上限 2048，语言切换清空；加载中/读取失败的临时回退不缓存。
- 中文环境缺键时回退中文文本，其他语言回退英文文本；未识别的第三方液体保留其自带名称。
- 取词使用非阻塞 `GetTableAsync` 完成检查，不等待本地化加载；`StringTableEntry` 已有 SmartFormat，格式化使用来源 locale。
- 名称替换只作用于悬浮说明 + 拖拽“使用/倒入”提示；背包、交易列表、任务文本保持原名。
- 设置开关：`WaterQuality.Enabled`、`Chemistry.Enabled`、`Wine.Enabled`，默认均为 true。

液体规则速查：µL 存储（`容量 = mL×1000`）；水/酒精 **>65%** 为主成分；占比 = 成分/总量；植物质不竞争主成分名称。纯度排除水、酒精、脂合剂、脂肪、物理污染物、微生物、植物质、不溶性碎屑、三种菇类糊糊；蛋白/酸/碱 ≥1000µL 才扣；化学污染物按剩余量 0.7% 封顶；还原剂不排除。评级阈值 99/90/80/60/45/30。酒精 74–76% 为医用酒精，≥99.95% 为脱水乙醇。扩展化学容量默认开启：150mL 起、无上限；滴管/实验量器不改名，刻度水罐/储水罐例外。设置中化学主开关下依次为成分标注、容器改名、自动搅拌、快速萃取、快速化学反应、机器预览，均默认开启。`Chemistry.AutoExtraction` 仅控制酒精萃取三种糊糊，新增 `Chemistry.RapidReactions` 独立控制九个非热配方，八种总/子开关组合及未配置拒绝已做离线回归；萃取执行期间抑制反应重入，反应执行也有自身重入保护。旧容量/空名细分 XML 配置保留，不再占设置页子项。水培改名仅 tooltip，直接读所放种子原语言键。快速反应逐步同步原版计算标签，有 512 步硬上限，不加速温度总分派、蒸发/热失活/热分解/热水萃取。自动化受化学总开关、加载/恢复/夜间门控约束。

---

## 4. 节点时机与门控

- 顾客节点在开场对话播完后建立（`StoreClient.isMainDialogueStarted`，8 秒兜底）；不要用 `isAboutToStartMainDialog` 当门控。
- `Snapshot.StoreState`：营业(1)/闭店(0) 允许；手动节点允许同 state 回溯（含早市 state=2）；自动顾客节点仍 0/1。
- 允许跨顾客回溯；剧情/对话类顾客跳过建节点但不清空历史。
- 默认同会话保留当前/上一天，每天独立最多 20 个常规内存节点（包括自动闭店与手动节点），另保留一个不计入常规名额的 MORNING 完整图锚点；每日新增一次完整捕获成本。面板按日期/阶段/建立时刻倒序，今日开业前高于昨日全部节点；第三天释放更早图。闭店手动节点不互相覆盖，不覆盖自动闭店节点。关闭 `CustomerCheckpoint.KeepPreviousDay` 回到当天保留；跨天黑屏/结算/晨报/漏斗传输时拒绝写回。开启保留后，同场景离店只暂停恢复，返店后可继续使用；换场景/真正的文件读档仍清空。关闭保留时离店仍清空。
- F5/F7 优先恢复晨间内存锚点以保留历史；若昨日锚点不可用且还有内存节点，F5 提示并拒绝文件回退，不静默清空。新分支提交前可逆浏览“后续”节点，提交后仅保留最多 80 条已释放图的只读“丢弃”摘要，默认折叠且不能恢复。F6 选择面板向下一个可用节点（包括手动），↑/W、↓/S 切换，Enter 确认，Esc 取消；五个导航/确认键均可改，双击恢复不变。
- 未进入 F6 选择时，导航主键（默认 ↑/↓）也可直接进入选择并向对应方向移动一格；W/S 备用键仍仅在选择中生效，不抢占普通操作，文本输入时不启动选择。文件节点原位更新也会递增版本并取消旧选择。
- 真正离店后返店会等待对话、转场、回调、拖拽、开关门等连续稳定 0.5 秒，再建立独立“返店”内存节点，占当天常规名额；初始到店/重复返店回调不建重复点，不替换晨间锚点/自动闭店点，也不消费当前顾客自动捕获边界。
- 返店要求位置为 IN_STORE；CLOSED 状态允许原版整晚保留的卷帘门锁（不修改该锁），OPEN/MORNING 仍要求解锁。RETURN SHOP 记录真实返店；RETURN WAIT 每五秒检查、仅阻塞状态变化才记录。
- 文件层按 `(runID, slot, day)` 合并 MORNING：Mod 文件节点同日原位原子替换、清理旧重复后才做数量/容量 LRU；原版 `SaveFiles.TrimHistory` 前移除已验证的同日旧归档，保留当前旋转得到的最新档，避免同日重走挤掉其他日期。保持原版轮转、槽位写入、IO 重试和失败恢复；禁用回溯或原版清空历史时不拦截。未知/损坏/非晨间/异周目/异槽位/链接文件不参与按日去重。
- 原版每日唯一包含活动槽位：正常载入后、成功 WriteSlotFile 后清理不晚于活动档的同日历史；SaveGame postfix 不作为成功依据（原版吞部分异常）。同日保存轮转时临时预留一份回滚副本名额，写盘成功才删除，避免当天重存先挤掉其他日期。比活动档更新的异常历史副本保留，绝不自动替换活动槽位。
- 原版去重保护 PendingPoint.Path 及所有保留索引引用（含后置的无效索引项），只移除可验证重复；LoadGame/WriteSlotFile 回调也不得删除待恢复文件。载入日期/槽位/周目不匹配时立即取消 Pending/Settling/Busy 并释放临时文件选择，不再等 90 秒；磁盘恢复文件保留。
- `isGeneScannerScanning` 是设施开关，不是扫描动作，禁止作为任何门控。
- 文件节点回退只加载不写回：快照复制为 `pstools_restore.es3`，通过 `NewGameData.saveFile` 读取；保留 `.pre_restore` 备份；不覆盖当前存档。必须收到原版 `LoadGame` 完成且日期/槽位/runID 匹配后才能删除临时文件并重置内存节点，不能以 `PlayerStore` 存在代替加载完成。文件重载更换对象实例，不保留旧内存节点。
- 运行时文件节点仍全局按 `Rewind.MaxCheckpoints`（默认 20）及 `MaxDiskMB`（默认 256 MiB）限额；`.pre_restore` 每槽位一份覆盖，不随回溯次数无限新增。开发部署备份不由游戏内 Mod 自动删除；`scripts/Prune-DeploymentBackups.ps1` 默认仅预览，显式 `-Apply` 才可清理，保留最近 10 份及每种唯一 ES3/备份内容，删除前验证候选和保留副本哈希，拒绝链接、路径越界、变更/额外存档。独立恢复目录不参与清理。部署脚本发现游戏运行直接拒绝，禁止自动终止游戏。
- 快捷键支持键盘及鼠标中键/侧键 1/侧键 2，不接受左右键；捕获改键当帧仍屏蔽动作。

---

## 5. 日志与诊断

- `EventLog.Write` 品牌头：`[QuickSL]` / `[QuickSL/子系统]`；关键捕获/恢复/失败不随 verbose 关闭。
- 默认日志：Session/语言状态、CAPTURE/RESTORE 阶段耗时与规模/后端、失败/拒绝/超限、真实 full 回退、释放错误、群拖异常。
- 建点失败 toast、损坏节点悬停和 LastCaptureError 使用同一具体原因：静态表错误显示底层异常及生成工具/更新指引，日志输出项目地址；object/slot/byte 超限显示当前有效限额、建议 XML 项和齿轮入口。提示按纯文本绘制，避免 `<Entry .../>` 被 TMP 当标签吞掉；未知错误保留异常类型/消息。不再只显示“详情请查看日志”。
- 主菜单模组齿轮注册 `Snapshot.MaxObjects` / `MaxSlots` / `MaxBytesMB`，与既有 XML 双向同步，重启时 XML 为真源（手改不被 native JSON 旧值覆盖）。对象 1000–1000000、槽 10000–8000000、MiB 16–2048。上限仅控制拒绝边界，不扩大未超限节点的遍历量或按上限预分配；允许更大图后实际耗时/内存可能提高，字节预算不代表整个 Mod 的物理内存上限。
- `[Load] native ms(...)` 每次载入一条，区分 Entry/PreLoad/LoadGame/DecodeSaveItem/历史清理；原版解码后的 onLoaded 等阶段也处于化学自动化禁止区间。晨报关闭另记 `closeMs` 与 `morningAnchorPending`，关闭后晨间锚点同步完整捕获耗时仍见 `CAPTURE client=day-start captureMs`。不把原生载入、关闭回调与后续捕获混作同一耗时。
- `[QuickSL/Geom]` 只在物品视图异常（DESTROYED/MISSING/NOSPRITE/HIDDEN/MISPLACED）时输出。
- `PSTools.log` 4MiB × 3 归档；独立错误日志 1MiB × 2；`PSTools_history.log` 保留并轮转。
- 设置页「查看日志」用 `CustomUIManager.Window(...)` 构建，显示末尾 400 行。

---

## 6. 加载器兼容迁移

2026-11-16 beta 适配：在任何 KnownTypes/KnownFactories 令牌解析前，以固定 PlayerStore/object/HashSet/Vector3 模块锚点校验全部 MVID。KnownTypes/Factories 禁止内联；错误版本负例同时含不存在的类型令牌，须先版本拒绝。新版官方保护名单增加 ModWindowControl，生成器与运行时共享排除并离线核对官方全名单。保留全部其余业务闭包，不能泛化排除 CompilerGeneratedAttribute。单分支表复用相同泛型元数的空声明字段工厂，不减覆盖；beta 单表 664576 字节，同 beta/同边界未合并工厂基线 687616 字节。后续通用包通过离线 MergeStaticLayouts 合并两版，4871 工厂去重至 2055，保留完整的各版本注册表；按游戏 MVID 选择、主 DLL 再检查全部模块。无运行时 Assembly.Load/解压/生成，不扩大能力声明；当前 beta Mono/隔离负例通过，正式版活体未复验。

**0.9.5 的 Mono 发布路径已使用构建时静态布局表**，详见 `research/2026-11-12-static-runtime-graph-migration.md` 与 `research/2026-11-12-loader-migration-review-handoff.md`。构建时生成目标 Mono 字段表，包含私有 Entry/Slot 固定类型令牌；版本严格 MVID 门控，未知布局不动态回退。Mono 编译排除旧表达式生成路径，通用 GameFields 用官方 ModReflect；恢复字段/集合物理存储与安全检查不减。两个发布 DLL 均列入 manifest，仅声明/检测 files；ReleaseCompatibility 的退役能力门槛已加入，net8 宿主表隔离于 out。`scripts/Test-StaticLayouts.ps1` 全套通过；实机仍只覆盖简单节点回溯，完整功能矩阵和多 Mod 组合未验收。下方为迁移过程中的性能记录。

后续实机反馈：基本回溯未发现功能异常，但明显变慢。已独立优化批量物品布局的字段访问：两个固定私有缓存字段采用字面 FieldInfo，公开原点属性直接读取；其他低频访问仍用 ModReflect。完整离线测试及能力扫描通过，后续实机结果见下。该项耗时分布与边界见 `research/2026-11-12-static-layout-restore-performance.md`。

布局修复后实机 7 次恢复平均约 1.26 秒（此前约 2.31 秒），geometry 降至 3–4 ms，全部 diff=0。下一候选为静态读取器的常见 SZ 数组下标读取，其他字段路径和所有校验保持；完整离线测试及扫描通过，整次实机收益待确认。详见 `research/2026-11-12-static-vector-read-performance.md`。

数组候选后实机仅 3 次，平均约 1.31 秒、全部 diff=0，未证明整次提速。最新小候选将静态读取器的标量比较准入类型判断缓存到初始化，原有比较及全部校验不变；完整离线测试/扫描通过，局部基准只有小幅收益，不能宣传已恢复旧后端速度。详见 `research/2026-11-12-static-scalar-gate-performance.md`。

随后按用户要求将剩余保守优化合为一批：扫描内结构体数组单元素拆箱复用、逐槽 owner/存储描述复用、有界跨节点只读字段/数组布局共享、按模块去重 MVID 初始化验证。字段值/节点令牌仍独立，部分根字段不共享，写入/异常/释放边界有专门游戏 Mono 测试，所有普查/预检/修复/最终校验保留。全套离线验证通过；8 万对象/72.8 万槽的三组新旧大图对照全部 diff=0，但恢复相关阶段合计中位数旧 1649 ms / 新 1665 ms，**未证明显著提速**。详见 `research/2026-11-12-static-graph-batch-performance.md`，统一部署后再一次性实机验收。

该批实机 5 次平均 1286 ms，同样未明显提速。恢复阶段现另有精简候选：静态固定布局 `CensusForRestore` 仅遍历完整引用拓扑并保留所有布局/预算检查，不再探测当前标量；目标 `ValidateRestore` 对显式稳定存储及已批准块只验证原 owner 保留身份，不计算被丢弃的写前差异；未知后端/块保持原完整读取。owner 修复计划并入同一次完整差异扫描，消除后续全槽规划遍历。原生刷新后扫描及最终全量校验/失败 full fallback 保留；日志用 `censusMode` / `preflightMode` 识别实际路径。同夹具三组离线对照中普查 427→303 ms、预检 275→20 ms，恢复相关阶段合计中位数 1599→1237 ms（约 -22.6%），不含 Unity 刷新，实机待验收。证明、回退边界及验证见 `research/2026-11-12-restore-stage-consolidation.md`；不能把写前稳定存储证明推广成恢复后免校验。

最新阶段精简后的 16:37 会话简单节点测试：5 次恢复平均 **1004.8 ms**，上一批为 1285.8 ms，约缩短 21.9%；全部 diff=0 / releaseOk=True，走 topology / retained-storage，无 finalFallback。不是严格同操作 A/B，亦不是完整功能矩阵验收。首节点仍 1479 ms，初始化卡顿没有解决。暂未发现低风险且明确显著的进一步阶段删减，停止追加性能候选，转入审查。完整范围及风险清单见 `research/2026-11-12-loader-migration-review-handoff.md`。

2026-11-15 晨报关闭停顿跟进：载入后的晨间节点约 18.6 万对象/102 万槽，普通约 15 万对象/83 万槽，不能把旧 1.3 秒当作同规模对照。每日仍需完整读取新晨间锚点，类型缓存本已跨日保留。新增独立性能提交：ManagedObjects 弱身份表清理从每 4096 个反复扫全表改为按新身份数/表规模摊销，最终局部游戏 Mono 对照（双方均缓存回调）490.50→161.46ms，不等于整体快照提速；StateGraph 用现有 _sources 做唯一成员索引，不再保存第二个 HashSet，完整图串行对照 215.48→213.57ms，未显著提速。所有预算/完整恢复/校验保留。CAPTURE 额外记 identitySweepMs/identitySweeps/identityVisited 与 GC 三代次数。证明及实机待验收边界见 `research/2026-11-15-morning-capture-amortization.md`。

22:52 后续会话：冷捕获 1802ms（access 474ms），同规模 185714 对象晨间重载捕获 1365ms，正常跨天晨间 1129ms；首顾客 890ms，返店两次 937/899ms。两次恢复 1802/2020ms，均 diff=0，无 finalFallback；其中一次 owner 修复消除 1 项刷新差异。原版加载仍需 9022/11058ms，DecodeSaveItem 聚合 7618/10039ms；这不是晨间捕获已解决的开销，也不能据被补丁包裹的原版解码计时排除其他 Mod 的影响。跨夜另有 8.8 秒 watchdog，未完成归因，不能归到 1129ms 的随后晨间捕获。完整证据和待审查项见统一交接。

- 技术依据：`research/2026-11-12-static-type-token-proof.md` 证明构建时固定 ldtoken 可表达私有泛型 Entry/Slot。窄探针之后已完成生产源码替换及离线完整图测试，已有基本节点实机反馈，但完整功能矩阵仍待验收；工具/原生离线宿主绝不随 Mod 发布。
- 官方公告 **2026-12-10** 起拒绝 `dynamic-code` 与 `reflection-external`（以及 native/threads）。0.9.5 的 Mono 发布路径已迁移为构建时静态布局表，发布包只声明 `files`；仍只保证已收录的 0.46D / 1.0.0.14 构建，发行说明不得写“已兼容任意下一版”。
- 迁移前源码依赖 `Expression.Compile` 与计算类型 `GetFields/GetMethod`；只删 manifest 声明或关闭 DevelopmentChecks **不是修复**，扫描器按编译进 DLL 的方法判断。现已从 Mono 发布编译排除此类路径，离线参考源码仍保留。
- 历史方案与普通 C# 查找链阻断见 `research/2026-11-12-loader-capability-migration.md`、`research/2026-11-12-runtime-graph-migration-feasibility.md` 与 `tests/CapabilityMigrationProbe/README.md`。其中集合阻断已由构建时令牌解决；ModReflect.Set 拒绝 readonly 的限制仍在，图写回继续使用固定 FieldInfo，不用 Clear/Add 偷换原存储语义。

---

## 7. 安全与部署约束

- 绝不主动调用 `PlayerEngine.SaveGame()`，不修改存档文件。
- 顾客节点纯内存恢复；文件回溯只加载不写回。
- 部署脚本备份存档/Mods、校验存档/游戏程序集/其他 Mod 不变、按完整路径关闭游戏、不自动启动。
- 不 `Process.Start` / `Application.OpenURL`；不修改加载器、信任记录、Steam 类型；不打包 `0Harmony`。
- 当前 Mono 迁移候选只声明 `files`；主 DLL 与 `ProbablyStolenTools.StaticLayouts.dll` 必须同时发货/扫描。Cecil、生成器、net8 宿主字段表、负例与原生离线宿主不得随 Mod 发布。

---

## 8. 已知坑

0.9.1 玩家报告 `Uncovered static snapshot layout: PaperBookRegistry+<>c`：生成器排除注册/信任组件，但运行时按游戏程序集继续展开其闭包。现以共享 `SnapshotTraversalBoundary` 统一边界，受保护引用只保留身份、不读写字段，显式受保护根拒绝；其他未知布局仍拒绝。游戏 Mono 回归和全套测试通过，已部署，玩家实机待测。详情见 `research/2026-11-12-protected-registry-capture-fix.md`；失败日志现含 manual/closed，另一份无日志的自动节点问题尚不能确认同因。

后续边界复查 `79cab6e`：游戏 owner 排除规则（含 ES3 前缀）已由生成器与运行时共享同一函数。目标程序集 111 个被排除类型/嵌套类型全部不展开，其余游戏类型均有表；不表示所有字段组合都支持。另修复 decimal? 的引用检查误入 Decimal 字段，以及 object 集合中的基本装箱标量/顶层 System 枚举缺表；保留原装箱身份及字段恢复，不降级为空对象。全套通过，最终双 DLL 已部署，实机待测；当时审查交接见 `research/snapshot-boundary-review-handoff.md`。第三方自动适配仍搁置；独立游戏布局生成器后于 2026-11-15 按新需求打包完成，尚未上传，见第 1 节。

DS 边界代码审查通过。部署哈希疑点已复核：原 FDC 与 DS 的 CB 主 DLL 在 3026 项类型/字段/方法记录（含 IL）中无差异，内嵌 Git informational version 不同，不能据哈希或提交时间断定旧包缺少代码。现从干净 `970e8c955f510e2bf88202cd8f77029bbfa81606` 完整验证、重复 Rebuild 并重部署，主 DLL `197099BB...`、helper `CBB8F36A...`；完整哈希与来源见 `research/2026-11-13-snapshot-artifact-provenance-followup.md`。后续交接同时记录构建提交号/干净状态/双哈希；纯文档提交变化也可能改变重建哈希，不要要求旧包始终等于文档更新后的 HEAD 构建。

| 坑 | 结论 |
|---|---|
| `GameItemElement.applied*` / `validateLock` 恢复 | `UpdateRectTransform()` 会跳过布局，必须排除 |
| 用 `Validate()` 重排物品 | `validateLock` 残留会整段跳过，必须直接写锚点 |
| 恢复后调用 `UpdateInventoryStateUIImmediately()` | 会按旧布局覆盖锚点，最终锚点写在它之后 |
| 旋转物品回溯变默认朝向 | 最后必须 `RestoreTransforms()` 收尾 |
| 实体块 schema 增删字段 | 回退 legacy，不静默；观察 `SINGLEPASS SCHEMA FALLBACK` |
| 中途关闭 DevelopmentChecks | 旧节点不再走 pilot 写回，但已有诊断载荷不会立即释放 |
| 数组偏移提到循环外 | 所有元素读成第一个；`diff=0` 也可能掩盖 |
| `F8/F9` 与游戏开发者控制台 | 游戏目录有 `dev.txt` 时冲突，启动日志提示 |

---

## 9. 验收清单

1. `dotnet build -c Release`：0 警告 / 0 错误。
2. `CheckpointTests` 430 项全过；`ReleaseCompatibility` 84 挂点/76 字段/漏斗门控/官方保护名单/`HardFlags` 通过；另跑存档动作 13 项、原版历史/齿轮 IO 14 项、玩家生成工具 11 项。beta 与稳定版需要各自验收，不混用实机记录。
3. 部署后：首/普通/手动节点往返、恢复后群拖、顾客贴图、正常交易/送客。
4. 物品：售出/移动/旋转/拆分合并/嵌套容器/前柜台回溯，贴图位置与朝向正确，无“空气占用”。
5. 多语言：切换语言后水质/纯度/评级/酿酒标签跟随；缺键回退；三个设置开关生效。
6. F5/F7：同会话优先内存锚点且保留节点；真正文件回退只加载不写回，`.pre_restore` 备份存在。
7. 关闭 `SemanticEntityBlocks` 的回滚模式可正常走 legacy。

---

## 10. 历史资料

- `research/archive/AGENT-0.6.0-history.md`：旧交接文档。
- `research/2026-11-12-single-pass-entity-candidate.md`：单次遍历实体后端设计与实机记录。
- `research/2026-11-12-customer-visual-rewind.md`、`2026-11-11-multi-select-rewind.md`：顾客视觉与群拖。
- `research/2026-11-12-semantic-backend-review-ds.md`、`2026-11-12-semantic-backend-review-followup.md`：本轮 DS 审查与整改。
- `research/2026-11-12-loader-capability-migration.md`：退役能力迁移方案。
- `research/modding-guide/ModdingGuide.zh-CN.md`：游戏 Mod API 指南（注意能力表可能落后于官方现行政策）。
