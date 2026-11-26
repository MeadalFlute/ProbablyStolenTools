# QuickSL 化学辅助与灯牌兼容审查交接

日期：2026-11-26。当前版本仍0.9.8，未提交、升版、推送或制作正式发布包。本文是本批最终审查入口，取代当天两份阶段交接的部署/未知版本策略/仓位索引说明。没有调用审查插件或宣称本批已获独立审查。

## 范围与用户约束

- 基线HEAD `817af1126c257d9e55e90917183a8b879f93e341`；审查整个dirty工作区，不能只审最后一次仓位修复。`git diff HEAD`不包含新增未跟踪文件，须额外阅读下列文件。
- 新增运行时文件：`src/ProbablyStolenTools/SignboardCompatibility.cs`。
- 新增离线夹具：`tests/CapabilityMigrationProbe/MonoFixture/SignboardCompatibilityFixture.cs`、`SmartExtrusionFixture.cs`；已接入套件。
- 原有快照实体/图后端和静态表生成策略未改；无第三方DLL依赖、无新权限、未修改第三方Mod、存档或玩家设置。
- 未知Proper Signboards版本按用户要求仅提示且不停工，不擅自加入禁用内存名单；未知版本的灯牌回溯依旧有风险，不得宣称完整兼容。

## 改动清单

### 1. 灯牌专项适配

`SignboardCompatibility.cs`与`CustomerCheckpoint.cs`：仅启用PawnshopBillboards时解析固定成员，匹配已检查1.0.48/MVID `6a02d0ff-61d7-49db-9895-fe09c04b5741`才启用桥接。把模组独立原生GameGridInventory加入快照根，保存records/unavailable/unresolved成员、原Placement身份及全部位置/方向/亮度字段，恢复前校验owner/run、库存/字典身份、busy/fault和拖动状态。

恢复业务图前清派生视图/恢复记录，业务图校验后调用模组Commit重建CounterStorage存档缓存与布局字符串。不能浅保留其SaveItemNode缓存，原生保存会重映射节点。Commit会替换物品亮度TagState，finally还原原亮度条目身份；新序列化缓存保留。不要调用Reset，它会销毁独立库存。Commit后的modData字符串属于必要派生更新，不能把前面的图diff=0解释为Commit后仍零差异。

未知构建Root/Capture返回null，一会话提示一次且失败限频重试，不阻断普通快照或原生保存。已适配节点出现故障/身份变化仍拒绝不安全恢复。`CompatibilityNotice.cs`共享弹窗只有相同消息才算已展示，其他消息等待原窗口关闭，防止Claims与灯牌互相吞提示。未新增常规存档前缀/后缀。

### 2. 咖啡自动搅拌

`ChemistryAutomation.cs`与`ChemistryRules.cs`：准入复用成分/损耗/childItems读取，避免预约成员逐项复制库存列表；处理阶段减少重复成分读取。仅在26°C、非加热、输入含coffee_mush且两端仅有water/coffee_mush/plant_matter/caffeine时，合并原生十轮加液。保留逐成分损耗取整、AddLiquid验证/温度/外观、RemoveExcessLiquid与Destroy；热液、酒精、未知/反应性混合物继续原版BlendInto。VBE容器继续排除。批次>=100ms才记items/combined/ms，无实机提速倍数结论。

### 3. 试剂与交易平板

- `TooltipNamePatch.cs`：优质试剂前缀从星形改ASCII `*`，严格低于0.10%重金属阈值不变。
- `CompositionDetails.cs`/`DetailedCompositionTooltip.cs`：试剂普通悬停保留三行含零杂质；Shift/固定详情显式关闭compact模式，显示全部非零组分并按占比降序。
- `NativeWindowGestures.cs`：交易平板是CustomUIWindow，不是PixelWindow；加自己的UI点击组件，共用物理Shift+中键、左键/拖动门控及置顶顺序，不越过父层。来客名之前使用棕色中文`[置顶]`/其他语言`[PIN]`，不写客户业务数据，取消/关闭还原。
- 闪烁根因：RefreshUI只排队，稍后RefreshClientHeader覆盖文字。现挂实际RefreshClientHeader postfix同调用补回，而不是提前排队入口。
- 玩家本轮已实机验收平板置顶及化学详情正常；不将其扩展为灯牌/性能/全部组合验收。

### 4. 智能挤出及本轮修复

`Chemistry.SmartExtrusion=true`，化学主开关下独立中英文子项；不自动生产。GameSlotInventory.UncheckedAccept成功后检查精确机器/槽位/childItem/父仓、双方所有权、液体容器、加载/回溯门控；全组分正量计总、整数比较>=99%，从原版ChemFinisherRecipe注册表按activeIngredientId寻找唯一产品，写原生SELECTED_CHEM_ID_TAG。空/低纯/无匹配/配方歧义保留选择，VBE容器排除；干燥/体积/产出空间仍由原版生产处理。

玩家上轮实测未生效：原实现把无电池的手动挤出机当成搅拌机，用了第2仓，实际该仓是产物仓，因此身份检查直接返回。正确原生顺序为module=0/container=1/output=2，现`IsExtrusionSlot`统一使用1。新Mono夹具通过真实MachineryPanelRow/GetBodyStorage/GetParentItem及旧GridPixelElement验证身份，也拒绝其他机器/无关或null槽；此前纯比例测试未覆盖原生布局，本次已补该缺口。实际选品仍待玩家复测，不能以仓位测试代替完整Unity生产流程。

### 5. 公告与文档

`UpdateNotice.cs`和`UpdateNoticeSchedule.cs`：旧简报/详情整段替换，不保留旧七节。新双语内容存放于可离线测试的UpdateNoticeContent；简报为“新增智能挤出、灯牌适配，优化咖啡搅拌与详情显示”。详情五节：智能挤出、灯牌兼容、自动搅拌、化学成分详情、交易平板置顶。保留5秒自动关闭、6px灰底填充进度条、可点击详情、真实分隔线、去空白段落。修订号`:20261126-chemistry-compat-r1`使相同0.9.8版本也正常重播一次，不手改玩家已读XML。

README中英已同步新功能与风险；AGENT记当前部署。旧阶段文档只作历史依据。

## 验证与产物

- Release构建0警告/错误；474项参考测试、22项可选兼容测试通过。
- 原版游戏Mono完整图回归，以及同时加载实际Workshop灯牌/VBE DLL的隔离Mono回归通过。新增未知MVID不阻断捕获、原生挤出机仓位、试剂全量明细、99%边界与双语公告五节断言。
- 官方扫描105挂点/76字段，仅`files`；HardFlags和Error空。主DLL及实际部署通用helper共同扫描。稳定版原始程序集仍缺，不能宣称0.46D重新实机验收。
- 输出：`out/20261126-extrusion-fix-{build,tests,fixture-build,mono-vanilla,mono-optional,optional,compatibility,deploy}.log`。
- 已部署：`out/full-release-deploy-20261126-145703/deployment.json`，版本0.9.8，dirty源码候选。
- 主DLL SHA256：`E2D9FA412879C516ECC29D750D3613F48FEC1D54A150B985AEE246CE5E25381C`。
- 通用helper SHA256：`6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23`，808448 bytes，两个已收录版本profile不变。
- 95个存档目录文件、玩家设置、其他本地Mod及游戏程序集校验不变；未启动游戏。

## 审查重点与剩余风险

1. 灯牌记录与原生库存一致性、Commit派生更新及亮度原身份还原、未知构建跳过是否严格不写入第三方状态。真实Unity视图清理/完整Commit序列化和保存重载仍未由夹具执行，需摘挂双向回溯+保存重载实测。
2. 咖啡快路径白名单是否足够保守、十轮取整等价与原版回调序列变化边界；大批量性能需实机日志，不承诺测得提速。
3. 智能挤出设置/权限门控、原生第1容器仓和唯一配方匹配；复测高纯咖啡因及另一种配方，99%包含阈值，低于阈值/纯水/关闭功能保持原选择，且不自动生产。
4. 公告新修订只重播一次，中文/英文窄屏排版、分隔线、5秒进度条需确认。不要为测试直接覆盖玩家配置。
5. 最新玩家日志另有多次200K对象上限触发（包括晨间节点），与挤出静默未触发无关。本轮未擅自改上限或裁剪图；需要扩大预算时由玩家从已有齿轮/XML选项调整。该既有容量问题不是本批已修项目。

独立review通过且智能挤出实测正常后再做正式升版/干净源码打包/部署/推送；按项目惯例下一次代码发布升小版本，不能把本轮dirty部署当作已发布0.9.9。
