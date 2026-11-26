# 智能挤出与详情续修

2026-11-26，版本仍 0.9.8，包含上一轮未提交的灯牌/咖啡/平板修复。本轮不升版、推送或重置公告。

后续实测修复：本文容器仓索引2有误，已在14:57修正为1，并补原生布局回归。当前部署和完整审查入口为 `2026-11-26-chemistry-compat-review-handoff.md`；下述14:30部署是历史候选。玩家已确认平板置顶、化学详情工作正常。

## 变化

- 平板置顶标记闪烁：实际 beta 的 RefreshUI 只设置 refreshQueued，稍后 RefreshNow 调用私有 RefreshClientHeader 写入姓名。前轮补丁挂在排队入口，随后原生覆盖标记。现改挂真正的 RefreshClientHeader postfix，在同一调用内恢复棕色 `[置顶]`/`[PIN]`，不写客户数据。保留 Tick 作为开关/语言变化维护。
- Shift 化学品完整明细：原 ContainerRows 和普通 AddReagent 共用默认精简模式，遗漏水/植物质/其他微量成分。Rows 新增 compactReagent 参数；只有详情路径显式 false，普通试剂仍显示三行含零杂质。固定详情复用同一完整路径，不显示不存在的零组分。
- 智能挤出：`Chemistry.SmartExtrusion=true`，化学辅助主开关内独立中英文设置。GameSlotInventory.UncheckedAccept 成功后检查 chem_finisher 的第 2 原生容器仓及 childItem/parentInventory 精确身份、双方所有权、液体容器和加载/回溯门控。按全部正量组分计总量，主成分用整数交叉相乘判定 >=99%，无需达到生产体积。不足阈值不改选择。从原版 ChemFinisherRecipe.chemFinisherRecipes 的 activeIngredientId 寻找唯一匹配 identifier，更新原生 SELECTED_CHEM_ID_TAG，不自动点击生产、不绕过干燥/体积条件、无配方/歧义不改、不干预 VBE 容器，不注册业务闭包或新机器类型。
- 灯牌用户策略修订：只有启用 PawnshopBillboards 才解析专项桥接，匹配已核对构建时才加入额外根和记录。未知构建 Root/Capture 返回 null，一次会话日志及可重试的一次弹窗告知风险；不自动停用内存节点，不拦截正常文件保存。已适配节点的 owner/库存/字典身份变化或 fault/busy 仍保留恢复安全检查。未知构建不是已兼容，摘挂后的回溯仍有数据风险。
- 兼容提示共用窗口已占用时，只有消息相同才返回已展示，避免灯牌通知被 Claims 通知错误吞掉；不同消息等待现有窗口关闭后重试。

## 验证

- Release 构建 0 警告/错误；473 参考、22 可选兼容测试通过。新增四种试剂详情的水/植物质/微量组分、普通精简不变，以及 99% 含边界、大数量/空容器/全部杂质计总等断言。
- 原版游戏 Mono 和真实 Workshop 灯牌+VBE 的隔离 Mono 套件通过。灯牌夹具额外模拟未知 MVID，Root/Capture 跳过且不抛错，再恢复已验证 MVID。没有执行真实机器生产、弹窗、平板 Unity UI 或完整存读档。
- 最终双版本 helper 仍 `6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23`/808448 bytes；105 挂点/76 字段，官方仅 files，HardFlags/Error 空。稳定版原始程序集仍缺，未宣称双版本实机重验。
- 输出 `out/20261126-extrusion-{build,tests,optional,fixture-build,mono-vanilla,mono-optional,compatibility}.log`。

## 待实机

1. 平板置顶后观察多轮刷新、顾客切换及取消置顶，不应闪烁。
2. 酸/碱/脂合剂/还原剂普通悬停三行；按详情键和固定详情应显示全部非零组分。
3. 高纯咖啡因等容器入挤出机自动选对应产品，99% 含边界；低于阈值/纯水/关闭开关保留原选择；生产仍手动。
4. 灯牌正常保存及摘挂回溯；未知版本只警告且不中断功能（离线分支已验证，未替换玩家第三方 DLL 实测）。

已部署 `out/full-release-deploy-20261126-143058/deployment.json`。主 DLL `EB891FB0A124725BF9EC6962E56F54CBEF38E64E8D237CD866AC8854B35D69CB`，helper 如上；95 个存档目录文件、玩家设置、其他本地 Mod 和游戏程序集校验不变。未启动游戏、修改第三方 DLL、导入设置或重置公告。
