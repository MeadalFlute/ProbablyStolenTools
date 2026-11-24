# 2026-11-25 QoL 审查处理回执

> 本文处理的是05:29候选的DS审查。随后玩家发现Shift左键冲突并同意改为Shift中键；该运行时增量未包含在原DS结论中，见`2026-11-25-middle-pin-followup.md`。下文“本轮只改文档”仅指审查处理当时。

## 结论

已读 `2026-11-25-qol-final-review-ds.md`。DS结论为无发布阻断代码问题，可进入玩家实机验收。本轮只修正文档并复核证据，不修改运行时代码，不提交、升版或推送，也不重复部署相同DLL。

当前主DLL `2A8F30EA4FA6E0208A666451A7E4C52FACAAEFFC256A8B2207AD71BE0533CDDD`；已安装helper `6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23`、808448 bytes，安装哈希重新读取一致。当前部署仍为 `out/full-release-deploy-20261125-052936/deployment.json`。

## 逐项处理

### P2-1 文档一致性：采纳

交接正文统一为当前r3、七节、6px/5秒、灰色静态底轨，以及最终部署路径/主DLL哈希。05:21段落明确标注历史记录，不再与正文混称当前候选。基线改为审查所用0815d69，说明与早期ea8f371之间仅文档记录提交。更新实机清单，把VBE首次入桶置首，并加固定详情与窗口置顶并发检查。

### P2-2 VBE CanTarget守卫：复核后保留，实机优先验收

本机实际VBE反编译 `out/20261125-BucketBrew.cs`：SetupWindow创建6×6 GameGridInventory原料仓，回调接收自有营养果/梦帽菇，不调用CanTarget。原生 `ItemMouseDragHandler` 的悬停/EndDrag先通过MayHaveValidInventorySlot及TryFindOneValidInventorySlot尝试入仓，然后独立判断CanTarget并执行Target。当前CanTarget守卫不改变仓过滤或入仓查询。

因此暂不因假设移除守卫；保留后续Target/Dissolve保护。代码证据只覆盖已核对版本，不代表所有扩展版本、其他Mod组合或UI实际路径已验证。仍把“首次原料拖入桶且可正常开始酿造”列为实机第一项。

真实VBE DLL位于Workshop，不在Mods或out：`E:\SteamLibrary\steamapps\workshop\content\4348910\3842506016\VanillaBrewingExpansion.dll`，46592 bytes。本轮设置PSTOOLS_TEST_BREWING_DLL后复跑通过，日志 `out/20261125-qol-review-receipt-vbe-mono.log`。

测试使用已安装主/helper的逐字节隔离副本 `out/20261125-qol-review-deployed/`；首次直接指向安装目录的尝试因夹具要在DLL旁写临时XML而被权限拒绝，随后改用工作区隔离副本，未放宽权限去写游戏目录。真实扩展夹具只验证回调包装保留、标签/字典身份与快照往返；不调用扩展业务回调、不启动游戏，不能称为真实拖放测试。

### P2-3 tooltip计算缓存：暂缓

审查对Evaporate/Consume的描述不符合当前warningsOnly短路：蒸发器在蒸发数组分配前返回，水培在完整生产循环前返回；净水器确实会执行Purify，以遵守玩家要求的“实际预测无去除量”判据。

暂不引入可能短时隐藏报警变化的缓存或完整机器状态指纹。若实机有悬停卡顿，再测净水器路径并针对优化，不把猜测当成已测瓶颈。

### P2-4 净水警报语义：不采纳本次顺序变更

玩家明确指定本次预测无实际去除量即完成警报，当前零预算/空瓶等行为在交接和测试中已明确。保持该规则及顺序，未装瓶仍无预测；不在审查收尾时改为先检查电力或“无”。若需要更细UX语义，应另行确认需求。

### P3-1 取消置顶标题宽度：未确认缺陷，不改

核对原生PixelWindow.Validate，普通窗口依据原始标题的PreferredWidth重算sizeDelta，机器样式MachineryValidate也会按窗口空间重算。当前Restore先恢复原文，再调用Validate。单纯恢复置顶前sizeDelta反而可能覆盖置顶期间原生调整的窗口/标题尺寸，因此不额外保存旧尺寸。

### P3-2 固定提示布局与最大宽度：保留为待测风险

根布局组、极端长名称/窄屏的风险有效，尚无本机实测复现。SetRichText(maxWidth)只限制原生maxCharWidth，原生本身显式关闭自动换行且允许Overflow，单加该参数不保证解决长行溢出。本轮不以未经验证的裁剪代替可读性修复，保留不同缩放/长名称/重叠/滚动实机检查；如复现再针对文本列宽和所属窗口布局修复。

### P3-3 每帧小开销：暂缓

Containers.Exists使用不捕获外部变量的lambda，并非每次形成捕获闭包。FindParentWindow遍历仍存在，但只在已固定项上执行；位置需要每帧跟随。暂不增加分帧延迟或所有权切换延迟，后续按固定项数量与实测成本决定。

### P3-4 部署helper防误用：认可方向，不采纳硬编码常量

本次最终产物已修正为双profile且重新扫描/部署、哈希复核一致。不能把808448 bytes或6129A2D3硬编码为通用部署规则，游戏更新/生成器修正都会合法改变字节和哈希，单版本manifest也允许单表。后续可按manifest所声明的版本集合验证profile覆盖，并在打包清单固定该包哈希；该独立工具改进暂不混入已审查的运行时批次。

## 记录纠正与剩余验收

- DS补充将05:21主DLL D195…与05:29主DLL 2A8…的差异归因于误带单表，不准确：主DLL差异来自进度方向与新增VBE文案的源码变化；单表误部署只影响helper。05:28与05:29主DLL相同、helper不同。本轮交接已明确最终产物，不改DS原始审查文件。
- DS重跑467参考、22可选兼容、14原生IO、101挂点/76字段/仅files扫描。处理回执本轮仅额外重跑真实VBE离线夹具，不重复声称全部测试又跑过一次。
- 未启动游戏，未手动重置公告、改设置/存档或重新部署；r3一次性展示逻辑不变。
- 剩余重点：VBE首次入桶，Shift固定明细与窗口置顶并发，固定提示跟随/关闭/层级，净水报警，建点回溯后的UI残留。玩家未确认前不宣称整批实机验收通过。
