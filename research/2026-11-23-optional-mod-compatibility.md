# 2026-11-23 新 beta 与可选 Mod 兼容候选

## 结论与交付边界

本轮是 0.9.5 开发候选，未推送、未升版，不是已通过实机验收的正式发布。

同日后续需求已将下文最初的 Claims 强制门控改为**一次性默认禁用、允许显式覆盖**，以 `2026-11-23-settings-compatibility-review-handoff.md` 为最新审查入口。首次检测启用 Claims 时将 `CustomerCheckpoint.DisableMemory` 设为 true，并持久记录 ID；手动关闭后不再重复覆盖，移除 Claims 不自动改回。文件载入的原生菜单/不修复顾客和卷帘门策略仍按 Claims 是否运行决定，不受内存强制覆盖影响。旧版强制禁用测试/部署哈希仅代表上一候选。

- 游戏 beta 1.0.0.16 / `VERSION 100-Rev16`：重建精确布局、保留严格模块校验；与原稳定版 0.46D profile 离线合并，继续使用一个主 DLL + 一个 helper。
- Vanilla Brewing Expansion 1.4.4：处理酿酒评分、显示名称和自动化冲突，保留现有图对原版标签/物品的精确恢复；不重复实现扩展自己的配方或品质公式。扩展未发酵酒瓶不追加本 Mod 的预测；扩展自己已有的发酵/成品/桶提示保持原样。
- Probably Stolen: Claims & Uprisings 1.1.18：**仅完成文件读档安全共存，不是完整内存节点适配**。启用时停用内存建点/恢复，给出具体提示；F5/F7 改为文件回退，强制菜单原生载入，不重建顾客或修正卷帘门。不能写成“房间/资源/部队支持交易节点回溯”。
- 未安装或未启用第三方 Mod：不要求前置、不加载第三方 DLL、不修改用户开关，保留原版自动/手动节点与莓酒评分。生产包无第三方资源、反编译源码、测试宿主或生成器。

## 核对对象

| 对象 | 本地核对版本 / 指纹 |
|---|---|
| beta 游戏 | Steam open_beta build 26772488，`VERSION 100-Rev16` |
| Assembly-CSharp.dll | MVID `d927a6e4-ae7a-4e65-a5e2-9f9624c131f3`；SHA256 `2596200FB57087C3B9FC4ECB9F0218285ABADD57092CD44BFE474A444EC94FDA` |
| VBE | Workshop 3842506016，ID `vanilla.brewing.expansion`，1.4.4；DLL SHA256 `5253C36FD1B362E89277A6F474C014D1E539F79A825B146CA3699C98BC4B216E` |
| Claims | Workshop 3841509159，ID `yexiaoyang.InteriorExpansion`，1.1.18；DLL SHA256 `8C56ECB7E0BE0F7AA523EE2956F9065E87289DC858A32EFC436278C4B93D40E2` |

反编译材料只放 `out/compatibility-20261123/`，不提交/分发第三方源码或 DLL。

## 实现

### 可选检测与原版路径

`OptionalModCompatibility` 只读 `ModLoader.Mods` 中公开的 ID / IsEnabled，兼容没有 `ModHelper.IsModRunning` 的旧正式版。不直接引用第三方程序集，不申请新能力，不声明会干扰纯原版安装的前置项。

Rev16 的公开 Mods getter 返回防御性副本，不再像旧版返回原列表。生产代码每次 Update 只取一次供本轮检测复用，实际建点/恢复前再刷新。已知条目的启用状态仍读取原 LoadedMod 引用；不缓存永久的“未安装”结果。日志只在组合变化时写一条；领地安全模式到达游戏后提示一次，F9 拒绝与面板状态持续可见。

### VBE

匹配依据：任何 `VBE_STATE` 标记，或扩展启用时酒瓶/`water_jug` 中存在 `nutrifruit` / `dream_cap`。标记以是否存在为准，不误把 state=0 当成未标记。Mod 已停用但仍残留 VBE 标记的物品，也保留其名称。

- `WineAppraisal.IsUnfermentedBottle` 不把这些物品当作原版莓酒，覆盖摘要、tooltip、缺水警告及违禁酵母原版预测入口。
- `LiquidComposition.Analyze` / `NoRename` 不覆盖其名称、品质及水质展示。
- 自动萃取、快速化学反应和自动搅拌输出不会处理这些容器；手动放入仍交还原生规则，不人为拒绝本来合法的单件放置。
- 普通莓酒、水桶、量器与其它液体容器不受影响。
- `VBE_*` 状态保存在原生 TagSystem/TagState、物品字段和原生窗口里。1.4.4 的持久回调闭包是原回调/物品引用包装；仍按既有第三方 opaque 策略保留其身份，不宣称通用第三方闭包字段恢复能力。已用真实 DLL 创建回调包装器做离线原身份往返，未执行酿造/跨夜/业务回调，仍需实机验证。

### Claims 安全模式为何必要

`ModEntry.Repository` 独立维护房间状态及物品根；`CommandResources`、`DistrictService`、`SoldierService` 等还有静态文档、待交付物品和服务缓存。它们不等于 PlayerStore 的 modData 字符串。

`RoomNativeEnumeration.Append` 在普通查询作用域外直接返回原版物品列表，不保证这些根能通过 EmporiumEntry 被现有图发现。`RoomSceneSlotInventory` 等自定义派生类型也不在现有原生静态表内。只补一个类型、只复制 modData、或恢复后调用 `ResetRuntime` 都不能提供完整原身份恢复；后者会销毁/重建视图并可能再次执行交付副作用。

因此：`CustomerCheckpoint.Enabled/AutoCapture`、F9、最终 Capture 与 Restore 入口均阻止不完整内存恢复；原生文件载入仍允许，并跳过本 Mod 的旧顾客/卷帘门收尾逻辑。用户开关不被改写。不主动 SaveGame，也不会把未保存的领地状态塞进旧磁盘节点。

完整适配尚待：由扩展提供版本化的捕获/预检/恢复/刷新契约，或逐项审计并覆盖全部独立根、静态服务、房间/士兵库存和原生视图生命周期，再做实际组合测试。不能用“把所有第三方类型都展开”代替这项工作。

官方文档的前置依赖支持可选松耦合：缺前置是警告，直接硬引用 DLL 才可能导致加载失败；ModReflect 对已声明且运行的前置开放访问。因此未来并非权限层面完全无解，但本轮不靠声明前置假装已具备完整状态契约。依据为官方 `dragonpan2/probably-stolen-modding` 的现行 ModdingGuide 中 Depending on another mod / ModReflect 部分。

## 布局与验证

- beta 单表：2915 类型 / 8235 字段 / 2033 工厂 / 670208 bytes。
- 稳定原始表：2861 类型 / 8108 字段 / 683008 bytes，保留原始指纹与访问代码。
- 合并：4894 个工厂去重为 2187 个，808448 bytes。两套完整 profile 均做离线等价核对；不是忽略版本差异或共享错误的注册表。
- 合并 helper SHA256：`6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23`。
- beta/universal manifest 从 1.0.0.14 更新为 1.0.0.16；不继续声明已被替代的旧 beta，稳定版 manifest 不变。
- 旧正式版原始游戏程序集仍不可用：本轮只证明稳定 profile 合并等价，**未重跑 0.46D 活体/完整 API 扫描**。

验证日志在 `out/compatibility-20261123/`，不以离线成功代替实机结果。

### 最终验证与开发部署

- 主工程 Release：0 warning / 0 error。
- 430 项快照参考测试、12 项可选兼容策略测试、13 项存档动作测试、14 项历史/设置 IO 测试通过。
- `static-suite.log`：未加载第三方 DLL 的实际游戏 Mono 全套通过，包含 2915 类型/8235 字段精确注册表、Claims 启用/停用门控、VBE 原生标签载荷、完整图/预算/身份/预检/GC 回归、字节可复现和错误 MVID 在缺失令牌解析前拒绝。
- `optional-native.log` / `optional-merged-native.log`：单 beta 与最终合并 helper 均通过实际 VBE 1.4.4 回调包装器测试及三轮标签恢复；自动搅拌保护通过。只执行包装器构造，不执行酿造、跨夜或 Mod 生命周期入口。
- `universal-scan.log`：最终双 DLL 的 84 挂点/76 字段及官方保护边界通过；官方扫描仅 `files`，`HardFlags` 与 `Error` 空。
- Mono 离线宿主输出的 Unity 未绑定 internal-call 提示是无 Unity 活体时的已知限制；所有上述通过均有实际断言和成功退出码，不是忽略测试异常。
- 2026-11-23 已用现有部署脚本部署到本机 Mods；版本仍为 0.9.5，无 push。开发备份/部署记录：`out/full-release-deploy-20261123-141215/deployment.json`。游戏关闭，未启动；89 个原有存档文件、游戏程序集与其他 Mod 校验未变，未导入/改写设置。
- 主 DLL SHA256：`9547C8A0DD1DCEB3B7FBF5C464F2E59718CB4E9256128CCC03FBCF5554E8720D`。
- helper SHA256：`6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23`，808448 bytes。
- 构建来源：基于 HEAD `48652b2dcfff8bca45ea4629d72590c01679818b` 的**未提交工作区改动**。这是本地开发部署，不是 clean-source 正式包，未绕过 `Package-FullRelease.ps1` 的 dirty 拒绝，也未创建正式发布 ZIP。正式交付前仍须审查、提交并按干净源码重新构建/打包，不能把本轮 DLL 标为上述 HEAD 的 clean 构建。
- 原有未跟踪的 `research/2026-11-19-persistent-checkpoint-feasibility.md` 保持原样，未混入本轮改动。

## 实机验收

1. 无这两个 Mod：首节点、自动顾客/手动节点、F5/F7、双击/F6、物品/顾客贴图、群拖、莓酒预测。
2. 仅 VBE：营养果/梦帽菇瓶与桶从备料到发酵、完成、跨夜陈化后分别前后回溯；检查分数/进度/数量/贴图/原名；混合非法配方不能显示错误莓酒品质；普通莓酒和自动化仍正常。
3. 仅 Claims 与两者同时启用：看到安全模式提示；无自动晨间/顾客节点，F9 有解释；F5/F7 完整读档后房间/资源/士兵及原生顾客队列与磁盘档一致，无重复交付、无额外关/开门。
4. 停用扩展后重启：用户原开关不变，原版节点恢复；缺少扩展不会因类型解析/程序集依赖导致本 Mod 加载失败。

暂不向 Steam/GitHub 发布；需用户实机认可本轮范围，尤其是 Claims 的文件模式限制。
