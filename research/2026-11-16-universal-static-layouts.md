# 单包双版本静态布局候选

## 范围与实现

按用户要求替代前一轮“稳定/beta 分包”的交付选择。一个包仍只有两个 DLL：
`ProbablyStolenTools.dll` 与 `ProbablyStolenTools.StaticLayouts.dll`，另有 manifest/preview。
0.9.3 不推送、不上传、不切换 Steam 分支、不启动或终止游戏、不主动保存。

`tools/MergeStaticLayouts` 是独立离线 Cecil 合并器，不随 Mod 分发：

- 输入各版原始生成 helper，无需运行时反射枚举或读取游戏内部加载器。
- 保留每版 KnownTypes、KnownFactories、ExpectedModules 三个完整独立入口，全部 NoInlining。
- 字段工厂按泛型元数、返回类型、类型/程序集身份、完整指令/字面字段名去重。只共享等价的访问代码，不推断两版物理状态完全相同；字段真实类型仍由所选版本运行时元数据提供。
- 三个原公开入口保持原签名；用固定 PlayerStore 模块 MVID 选 profile，未匹配不解析任何版本专用类型。ExpectedModules 未匹配时只返回第一版纯字符串指纹，让既有主 DLL 输出详细版本不匹配。
- 主 DLL 原有四模块校验仍先于 KnownTypes；不会用游戏指纹选择代替 BCL/Unity 等模块校验。未知类型/布局/指纹仍拒绝，闭包完整覆盖不变。
- 合并后重新读取产物，对每个输入逐项核对模块字符串、类型顺序和每个工厂规范化完整 IL，包含泛型位置，不是只比覆盖数量。
- 同序输入字节可复现；重复游戏 MVID、非平直生成方法、无法映射工厂拒绝。输入不改写。

## 输入来源与尺寸

正式表来自既有已审查部署备份：
`out/full-release-deploy-20261115-224834/Mods/ProbablyStolenTools/ProbablyStolenTools.StaticLayouts.dll`。

- 正式表 SHA256：`CBB8F36AECB701BD46BEB87EAB53B63F4782E222056B7FEE0C452D7A2C8E1386`
- 正式表 **683008 bytes**，2861 类型，游戏 MVID `911508a6-e0eb-4cc0-91e8-e14b16ea28d7`。
- beta 原始表 SHA256：`B77C9A42591EE47D2D8824E8ABD0455A0029A6D6EDA4B5FD6DB11BA78429C11A`
- beta 表 **664576 bytes**，2883 类型，游戏 MVID `b771feaa-26fd-4405-893b-2dc01e3ddef8`。
- 两份原表合计 **1347584 bytes**；合并表 **783872 bytes**，减少 **563712 bytes / 41.83%**。
- 相对当前 beta 单表增加 **119296 bytes / 116.5 KiB**；4871 个原工厂去重至 2055。
- 前一轮文档的 675328 bytes 是更早的正式表产物，不能当作这份 CBB8 输入的长度。

去重主要减少分发重复代码；版本选择只在注册表初始化调用，不宣称快照捕获/恢复提速。

## 构建和部署

本机只有 beta 原始程序集，正式版保留表作为显式、可校验输入。正式原表不能由 beta 表反推；
恢复干净环境时需另保留此已审查产物，或者用原版程序集与对应边界重新生成并验收。

```powershell
# 干净源码下打包：构建当前 beta 原表，再离线合并另一版表
.\scripts\Package-FullRelease.ps1 `
  -GameDir "<beta游戏目录>" `
  -ManifestPath release/universal/manifest.xml `
  -AdditionalLayoutPaths "<上述正式原始helper路径>" `
  -OutputRoot "<新的输出目录>"

# 部署已打包内容，不要再次用默认单分支构建覆盖 helper
.\scripts\Deploy-FullRelease.ps1 `
  -GameDir "<游戏目录>" `
  -PackageDirectory "<输出目录>/ProbablyStolenTools"

# 独立合并/验证
dotnet run -c Release --project tools/MergeStaticLayouts -- `
  "<输出helper>" "<正式原表>" "<beta原表>"
dotnet run -c Release --project tools/MergeStaticLayouts -- `
  --verify-profile "<合并helper>" "<当前游戏重新生成的原表>"

# 双位置、错误令牌和未知指纹回归
.\tests\Test-MergedStaticLayouts.ps1 `
  -GameDir "<beta游戏目录>" -OtherLayouts "<正式原表>"
```

Package-FullRelease 保留单版本入口，新增 AdditionalLayoutPaths；manifest 声明数必须对应输入数。
当前原表仍需字节复现，其他输入前后校验哈希，官方扫描针对包内两个实际 DLL。
外置 layout-profiles.json 和 provenance.json 记录原表 SHA/MVID/长度、源码提交、最终 SHA；
它们和 Cecil/生成器/宿主/BCL/游戏程序集都不进 Mod。

部署 PackageDirectory 必须恰好四个普通文件，无子目录/文件链接；部署前重新生成当前原表，
逐字段工厂核对匹配 profile，并扫描实际包。仍保留退出游戏、备份、存档/游戏/其他 Mod 哈希保护。
玩家独立生成工具仍生成单版本表；安装它会失去另一个 profile，不是自动保留/适配任意游戏版本。

## 验证与剩余风险

`out/universal-validation-20261116.log`：

- 当前 beta 在 profile 0 和 profile 1 两种顺序下均跑游戏 Mono 精确字段对照和完整图恢复。
- 两版合并前后模块指纹、类型顺序、全部字段访问工厂 IL 等价；两种顺序分别字节复现。
- 故意破坏未选中版类型令牌，beta 精确表仍通过；未知指纹先于无效类型解析被拒绝。
- 重复指纹不生成输出；被改动字段的原表不能通过匹配验证；正式输入哈希未变。
- 合并产物官方扫描只检测 files，HardFlags/Error 空；84 挂点/76 字段及既有实体/集合/边界测试通过。
- 430 项参考测试、14 项历史/齿轮 IO 测试分别见 out/universal-reference-tests-20261116.log、
  out/universal-history-tests-20261116.log。

**正式版 0.46D 原始程序集当前不可用，未重新执行该版本 Mono/Unity 测试。**
保留正式原表等价不证明当前主 DLL 的全部游戏 API 在旧版本仍匹配，也不证明旧版本加载器可扫描所有 beta 专用令牌。
通用包是候选：beta 尚需玩家实机回归，正式分支必须补自身加载器扫描/Mono/实机验收，
不能将“两个选择分支都在 beta 测过”写成“两版游戏都测过”。
