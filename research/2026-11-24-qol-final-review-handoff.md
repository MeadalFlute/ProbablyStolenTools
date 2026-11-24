# 2026-11-24 QoL 最终 Code Review 交接

## 审查入口

- 仓库：`D:\Program\GameProject\ProbablyStolenMod`，基线 `8169488`；审查该基线之后的全部工作区改动，包含未跟踪的新文件，不能只看 `git diff`。
- 当前版本仍为 **0.9.6**，未提交、未推送、未生成正式发布包；本次只准备审查，不执行上传。
- 本文是当前候选入口，优先于 `2026-11-24-qol-review-handoff.md` 中的历史哈希、旧行为和“待实测”表述。后者保留需求澄清和开发过程。
- 用户已实机验收上轮全部功能及四项微调；本次只额外修补来源标识的英文回退并增加测试。最终翻译修补尚未单独实机复测，不把离线测试表述为所有语言实机通过。

## 最终产物

- 部署记录：`out/full-release-deploy-20261124-015204/deployment.json`。
- 主 DLL SHA256：`B764A72A5E4A21CD04071F62DE6C8077339447BD7132B29618C708C37EA0E13B`。
- helper SHA256：`6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23`，808448 bytes。
- helper 含 0.46D 与 beta 1.0.0.16 两个精确保留的 profile；本机游戏为 beta，MVID `d927a6e4-ae7a-4e65-a5e2-9f9624c131f3`。
- 安装位置：`E:\SteamLibrary\steamapps\common\Probably Stolen\Mods\ProbablyStolenTools`。已部署源码构建产物和 universal manifest，未使用旧主 DLL。
- 部署未改动 90 个存档目录文件、其他本地 Mod 或游戏程序集，未导入配置、未启动/结束游戏。

## 用户需求与已确认边界

1. 店内固定水龙头：明细开关默认开启，按住任一 Shift 才追加水质、名称和占比；松开回到原版行为。保留原连接说明及原 tooltip，不虚构出水体积。
2. 液体容器：Shift 明细开关默认开启，按住时完整替换 tooltip，松开恢复。列为名称、实际含量、占比；含量自动选 uL/mL/L，占比两位小数，非零痕量 `<0.01%`。列位置按实际 TMP 文字宽度计算。
3. 明细列出所有实际非零成分，包括极微量；主成分橙色、其余白色。按未舍入占比稳定降序，相同占比保持注册顺序，主成分不强制置顶。
4. 酸、碱、脂合剂、**还原剂 `reducing_agent`**固定显示主成分、化学污染物、重金属三行，零杂质也显示；不是稳定剂。重金属严格低于 0.10% 时仅 tooltip 名称加星，不修改真实物品名或存档。
5. 当前占比/预期纯度/评分三个摘要项独立开关，默认仅当前占比；全关不留空括号。详细成分不受摘要三开关裁剪。
6. 工作中机器模组槽解锁独立开关，默认关闭。只覆盖工作机器的精确模组库存查询及新版显式拖拽拦截，不写锁字段/工作标签，不解锁电池、输入、输出等其他仓位。
7. 冲突提示使用紧凑原生窗口，仅标题右上角 X 关闭，不超时消失或响应 Escape。左下角小号 LightBlue 斜体来源标识，中文“快速SL”、其他语言“Quick SL”。
8. 同一冲突 Mod 只提示一次，跨重启保留；只有真正成功显示后才记录，UI 未就绪每秒最多重试一次。
9. 内存禁用时，面板仍只读索引对应槽位/周目的当日及保留昨日晨间文件，显示“开业前 / 存档”节点。索引本身不触发原生保存。

## 多语言核对

- `DetailedCompositionTooltip.AddRows` 的成分名称走 `AnnotationLabels.Liquid` → `InspectionLocale.Label`，水质及标题使用原生 Mechanic 语言键，不把中文名称写死。
- 原生取词顺序：当前语言表 → 英语表 → 英文文本兜底。保留 irregular key 映射；未知第三方液体缺键时保留其自有显示名，无名称时保留 ID，不保证替第三方补全翻译。
- 原生语言未就绪时不查询阻塞等待；缓存按 locale/table/key 隔离，切语言清空，不锁死暂时性回退。没有改动原版语言/表设置。
- 本 Mod 自有设置、列标题、空容器提示、冲突标题和正文采用中文/英文两套文本；其他语言统一英文。这是预期支持边界，不要求把自有界面扩展为所有原生语言。
- 本次发现并修复唯一遗漏：来源标识此前固定中文，现通过 `InspectionLocale.Text` 同步选择显示和测量文本。
- uL/mL/L、百分比和星标为通用符号；数字使用 invariant culture，避免逗号小数破坏列布局。
- 新增测试覆盖中文地区/脚本变体、其他语言及未知/未就绪语言的英文回退，以及本轮成分的原生→英语表→英文文本路径；其余既有测试覆盖缺键、异常、缓存和语言切换。

## 源码地图

| 文件 | 审查内容 |
|---|---|
| `CompositionDetails.cs`（新） | 行过滤、long 总量、稳定排序、星标严格边界、单位/百分比格式、固定列 |
| `DetailedCompositionTooltip.cs`（新） | 原生水源读取、Shift 门控、最终 tooltip、五层 RichText 追加、测宽后恢复 TMP 缓冲 |
| `CompatibilityNotice.cs`（新） | 原生窗口、自适应尺寸、单关闭入口、来源样式和中英文本 |
| `MachineModuleUnlock.cs`（新） | 精确库存身份、工作状态、旧版不存在挂点时跳过 |
| `OptionalModCompatibility.cs` | 持久提示 ID、显示成功后记账、保存失败限频重试、手动 override 保留 |
| `Plugin.cs` / `Panel.cs` | 配置默认值、设置布局及主/子开关依赖、中英文案 |
| `InspectionText.cs` / `WaterQualityPatch.cs` / `TooltipNamePatch.cs` | 摘要组合、试剂三行、只在 tooltip 加星 |
| `Rewind.cs` | 禁用内存时只读晨间文件索引、重载后的单次补锚点尝试 |
| `tests/CheckpointTests/CompositionDetailsTests.cs`（新）及相关测试 | 纯逻辑边界、排序、痕量、列宽、语言回退 |
| `tests/OptionalModCompatibilityTests/` | UI 未就绪不记账、持久去重、重新启用不重弹、IO 失败及 override |
| `tests/CapabilityMigrationProbe/MonoFixture/QualityOfLifeFixture.cs`（新）及相关夹具 | 实际游戏类型、Shift 查询、模块仓身份、文件索引、XML 保存重载 |

## 审查重点

- tooltip 只读边界：不消耗 RNG、不调用加水/化学反应、不改隐藏标签或真实名称；VBE 原有排除不被破坏。
- TMP 的 `GetPreferredValues` 会修改处理缓冲，必须在 finally 用 `SetText` 恢复，即使原版随后复用同一缓存也不串文本；英文长词/CJK 宽度不能简单依赖单个 Tab。
- 水龙头载入路径可能残留原生 `IS_TOOLTIP_DISABLED_TAG`，只在 Shift+开关+解锁水源满足时覆盖 HasTooltip；松开返回原生值。
- `Compatibility.NotifiedMemoryConflicts` 与 `HandledMemoryConflicts` 含义不同，不能用“默认禁用已处理”冒充“弹窗已展示”。前者不应被 Mod 临时禁用/重新启用清空。
- 配置写入失败保留当前进程记忆，去重日志并五秒重试最新值；持续无法写盘且退出时，跨进程去重无法保证，这是明确限制。
- 模块功能不能扩大到非目标库存；只允许操作不代表改变原生运行中换模块的业务结算规则。
- 文件节点必须过滤 day/slot/run/state，索引不能请求保存，历史文件重载与活动槽位日期不同时不能每两秒重复读档或制造节点。
- 旧正式版独立拖拽 API 不存在时不能硬引用；未知静态表仍拒绝。不要要求恢复高权限运行时图反射来“提高兼容性”。

## 验证证据

- `out/qol-review-20261124-static-suite.log`：构建及 **443 参考测试 + 20 可选兼容测试**通过；实际游戏 Mono 完整图/预算/MVID/非法令牌、通知 XML 重载、模块库存、文件索引及水龙头回归通过。
- `out/qol-review-20261124-merge.log`：两个 profile 精确保留，4894 原始工厂去重为 2187，合并体积 808448 bytes。
- `out/qol-review-20261124-universal-scan.log`：最终合并产物 **90 挂点/76 字段**，官方扫描仅 `files`，HardFlags/Error 为空。
- `out/qol-review-20261124-optional-native.log`：无第三方环境及真实 VBE 回调身份往返通过，只保留身份、不调用第三方业务回调。
- `out/qol-review-20261124-deployment.log` 及 deployment.json：部署哈希和数据保护记录；`git diff --check` 通过，存在既有换行规范提示。
- 离线 Mono 有既有 runtime/class-library 提示，断言及进程退出成功；不把它表述为实机结论。
- 稳定分支原始程序集不可用，仅精确保留旧 profile；不能声称 0.46D 新功能重新实机验收，也未实机逐个验证所有游戏语言。

## 审查后发布

审查通过后再按 AGENT 的发布规则决定提交/小版本递增、干净源码重建、双表合并、最终扫描与部署/打包；核对最终源码与主 DLL/helper 哈希一致。当前没有授权提交、升版或上传，不提前执行。
