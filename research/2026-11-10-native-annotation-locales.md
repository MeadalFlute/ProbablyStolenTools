# 原版标注语言键（2026-11-10）

按用户后续要求，本次替代 `41ff21f` 中“非中英文停用标注”的保守策略。复用游戏已有翻译，不另维护多语言字典。

## 实现

- `AnnotationLabels` 统一描述表名、键和中/英文回退；`InspectionLocale.Label` 读取当前语言已完成加载的表条目。缺表/缺键/空文本/读取异常均回退，不同步等待初始化，也不改游戏的 Locale 或表。
- 六档水质：`mech_condition_pure_water`、`high_quality_water`、`basewater`、`ghostwater`、`rustwater`、`gutterflow`（后五者也带 `mech_condition_` 前缀）；另有 `mech_unknown_water`。品质比较保留原版阈值与最低档钳制，但不为比较而触发 `WaterFeatureHelper` 的本地化静态初始化。
- 七档化学纯度：`mech_condition_lab_pure / pharma_grade / near_pharma / street_standard / cut_product / junk_grade / starch_grade`（每项都带 `mech_condition_`）。既有阈值和颜色不变。
- 六档酒品质：`mech_condition_wine_master_vintage / reserve / production / homebrew / bucket_brew / undrinkable`（每项都带 `mech_condition_wine_`）。预测句仍为自有中/英文；其他语言显示英文句子加原版翻译的品质。
- 36 种成分按当前语言实时查 `Mechanic/liquid_*`，不使用游戏初始化时缓存的旧语言显示名。例外映射：`microbe→microbes`、`fat→fat_oil`、`coffee_mush→coffee_bean_mush`、`contraceptive→contraceptive_hormones`、`methylol→n_methylol_base`、`debris→insoluble_debris`。
- 使用原版水质/化学纯度栏目键、低级违禁品键、发酵缺水键；医用酒精和阴光莓果酿使用 `Item/item_rubbing_alcohol_name`、`Item/item_wine_gloomberry_name`。
- 无精确对应键的“脱水乙醇”、空瓶尺寸、莓果品相、预测分数解释与“酿造失败”等沿用自有中/英文，其他语言回退英文。修复负分预测在英文下仍显示中文“酿造失败”的遗漏。
- 第三方新增成分若无同名原版语言键，保留该成分自己的名字，不改写第三方内容。

## 只读资源验证

从本机正式版 `Assembly-CSharp.dll` 反编译 `WaterFeatureHelper`、`ItemConditionList`、`ItemFeatureList`、`Liquid`、`WineHelper` 确认键及阈值，再用本机已有 UnityPy 读取游戏自带的共享键表与各语言 AssetBundle。未写回游戏资源。

本轮实际使用的 62 个键（Mechanic 60 + Item 2）：

| 语言 | 非空条目 |
|---|---|
| zh / en / fr / de / ja / pl / ru / es | 每种 62/62 |
| it / ko / ptBR | 每种 0/62，需走英文回退 |

这只是本机当前资源包的覆盖率，不表示新增了游戏语言，也不是翻译质量或字体的活体验收。

## 验证

- Release 构建零警告、零错误。
- 76 项离线测试通过，新增 5 项覆盖：水质边界、化学/酒品质边界、所有语言透传及切换、缺键/异常/未就绪回退、成分例外键及第三方回退。
- 正式版兼容性验证：55 挂点、73 字段、清单能力一致、HardFlags 为空。
- 测试前核对自然存档和索引 SHA256 与上轮完整备份一致；离线测试不写自然存档。
- 未启动游戏，尚待语言切换、提示布局/字体及加载期间的活体验收。此次不改快照捕获或存档机制。
- 已部署；完整备份 `out/full-release-deploy-20261110-233340`，316 个既有存档目录文件、游戏程序集与其他 mod 校验未变，未覆盖设置。DLL SHA256 为 `873C1680134473D34F9A62D4C4A3B9D4F1047E94234C29D438D0F7883A59A485`；未推送，版本仍为 0.8.13。
