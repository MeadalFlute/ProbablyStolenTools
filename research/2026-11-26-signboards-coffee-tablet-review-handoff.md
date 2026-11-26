# 灯牌、咖啡搅拌和交易平板修复交接

日期：2026-11-26。基于 0.9.8，未升版、提交或推送。不是此前 DS 已审范围。

后续修订：未知灯牌版本不再拒绝建点，改为仅提示并跳过专项适配；平板标记改在真实 RefreshClientHeader 后立即补回。新增 Shift 试剂完整详情、智能挤出及最新部署见 `2026-11-26-extrusion-details-followup.md`，以下首轮严格拒绝策略与部署不再是最新。

## 改动

1. Proper Signboards：本机 Workshop `PawnshopBillboards` 1.0.48，MVID `6a02d0ff-61d7-49db-9895-fe09c04b5741`。它的挂位仓不属于玩家仓位根，原回溯遗漏了独立库存及静态挂位记录，摘牌后会出现物品父仓与挂位身份不一致。新增定点适配：把原生挂位库存加入完整图根，独立保留三个记录表的成员和原记录身份，以及 Placement 的位置、朝向、亮度等字段。恢复前检查版本、owner/run、库存/字典身份和 busy/fault/拖动状态；清理派生视图后恢复记录与完整图，最后调用该模组 Commit 重建序列化缓存。Commit 会替换物品亮度 TagState，故在 finally 中还原原始条目身份；缓存保留新序列化内容。modData 的灯牌布局字符串按已恢复记录重新生成，属于必要派生更新，不宣称 Commit 后图仍逐字段零差异。
2. 不直接保留 CounterStorage 的序列化节点缓存：保存时其 uuid/childItems 会被重映射，浅保留无法可靠回溯。不可调用 BillboardManager.Reset，它会销毁业务库存。未知灯牌构建拒绝内存建点/恢复，不加载生成代码，不尝试遍历未知第三方类型，不新增前置依赖或权限。不安装灯牌时不初始化反射桥接。
3. 自动搅拌：减少重复 Parts/Signature/损耗查询，准入判定一次读取 childItems，避免为每项预约复制列表。仅对 26°C、非加热、且输入和目标仅包含 water/coffee_mush/plant_matter/caffeine 的咖啡配方，把原版每成分十轮加液合并为一次，保留原版损耗取整、AddLiquid 验证/温度/外观回调、溢出处理和销毁。热液、酒精、杂质、未知液体仍用原版 BlendInto；酿酒扩展容器继续排除。批次耗时 >=100ms 才记录 items/combined/ms；没有实机加速倍率结论。
4. 优质酸/碱/脂合剂/还原剂名字前缀由字体不支持的星形改为 ASCII `*`，品质阈值不变。
5. 交易平板并非 PixelWindow，而是 CustomUIWindow。增加其背景的物理 Shift+中键置顶切换，拒绝左键按住/拖动物品/拖动窗口，保留原生按钮；平板与普通窗共用置顶顺序且不跨父层。中文棕色 `[置顶]` / 其他语言 `[PIN]` 加在 clientName 前，不改客户数据；原生 RefreshUI 更新姓名后立即重新标记，取消/关闭还原。未改公告及原有手势。

## 验证

- Release 构建成功；471 项参考测试、22 项可选兼容测试通过。
- 新增咖啡严格白名单及 0..100 损耗、0..299 数量下十轮取整总和对照；带热量/酒精/杂质的边界拒绝优化。
- 原版游戏 Mono 离线夹具通过；同时加载实际 Workshop 灯牌 DLL 和 VBE DLL 的隔离夹具通过。灯牌测试覆盖独立库存根、三轮挂位成员/原记录身份/位置/亮度/不可用记录恢复，以及 busy/fault/disabled 恢复拒绝。没有执行 Unity 视图清理、Commit 序列化或完整灯牌业务流程，不能代替实机。
- 最终合并双版本 helper `6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23`（808448 bytes），官方扫描 104 挂点/76 字段，仅 `files`，HardFlags/Error 空。稳定版原始程序集仍未重验。
- 原版 Mono 首次测试因夹具引用已移除的 Compatible 方法失败；已改成实际路径共用的 BlendBottleAvailable 入口，重新构建并重跑两种环境通过，非忽略失败。
- 原始输出：`out/20261126-fixes-{build,tests,optional,fixture-build,merge,mono-vanilla,mono-optional,compatibility}.log`。

## 实机验收重点

- 挂灯牌建点，摘下后回溯；反向从摘下节点回溯到挂起节点。重复操作、亮度/位置变更、普通保存再重载，确认没有重复物品/灯牌异常；若该 mod 已进入 fault，请先重新读档再测新节点。
- 大批咖啡豆，比较卡顿，检查损耗、容量边界和未处理原料留在原处。复杂混合物仍应保持原版行为；慢批次可读取 `[Chemistry] AUTO BLEND batch ...`。
- 优质试剂前缀 `*`；交易平板 Shift+中键置顶、取消、来客更替、与其他置顶窗口并存时的顺序及棕色标记。

已部署：`out/full-release-deploy-20261126-013515/deployment.json`。主 DLL `CAF3775F40AE36E382D4BFB96DAD4952B7428B3048D67ABB48F2BA8832C26195`，helper 如上；安装目录哈希一致。94 个存档目录文件、设置、其他本地 Mod 和游戏程序集校验不变。未启动游戏、导入设置、重置公告、修改第三方 DLL、提交/升版/推送。
