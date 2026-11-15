# 1.0.0.14 Beta 静态表兼容与审查整改

## 范围

游戏 1.0.0.14 是 beta；用户本机已切换。0.46D 是稳定分支，不能把两个版本当作同一套程序集。
基线 cf7fb8e（0.9.3、DS 回执）；本轮不升级版本、不推送、不上传 Workshop/GitHub，不启动或终止游戏。
beta 离线验证不能替代实机验收，也不表示原有 0.46D 发布包已经被改成跨版本包。

## DS 回执处理

- R1 已由 DS 的 3fc7e86 修复，本轮沿用，不回退 stderr 断言。
- N1/N2：去重共用 RemoveDuplicates，先保护待恢复文件路径，建立全表 retained 文件集合；后置无效索引引用也阻止删除共享文件。重复索引删除只删文件一次，全部过期索引可移除。LoadGame/WriteSlotFile 整理同样保护 PendingPoint。
- 新增 4 项直接编译生产 NativeSaveHistoryPatch 的 IO 测试：后置无效引用、多重复索引、成功写盘/加载整理期间的 pending、Trim 去重期间的 pending。原版历史/齿轮夹具合计 14 项。
- N3：原版加载结果不匹配立即 CancelPendingLoad，清 Busy/Pending/Settling/加载选择，保留 staged 恢复文件。不再等超时才取消。超时复用同一清理方法；真实 Unity 错误载入仍需实测。
- N4：独立工具打包在创建输出前拒绝 dirty 源码，打包结束再次核对提交和干净状态；已验证脏源码拒绝且不创建输出目录。
- 以上整改独立提交 66b5016，beta 生成/布局变更在后续提交。

## 两个实际 beta 阻断

1. 旧 helper 的 KnownTypes 包含旧编译器闭包令牌。CreateFactories 之前先解析类型，才检查 MVID，导致 TypeLoadException 抢先。
2. beta 官方 ModScanner.GuardTypes 新增 ModWindowControl。直接重建旧规则会把它的闭包和 Win32 结构体放进表，产生 loader-target HardFlags，不能部署。

修正：

- 在 KnownTypes/Factories 之前读取纯字符串 ExpectedModules，用固定 PlayerStore/object/HashSet/Vector3 锚点验证所有模块，仍严格拒绝未知/缺失/不一致版本；后置逐实际类型模块检查保留。
- KnownTypes/Factories 标记 NoInlining，保持令牌解析在门控之后。
- 错版本生成负例同时含不存在的 MissingSnapshotVersionSentinel，验证版本错误先于类型解析。另把真实旧 0.46D helper 和当前主 DLL 放进独立 out 夹具，beta Mono 下得到清晰版本拒绝，而非 TypeLoadException。
- 生成器与运行时共享新增 ModWindowControl 排除。ReleaseCompatibility 离线对照官方 16 项 GuardTypes，避免下次漏保护类型；生产不读取加载器内部名单。

**未采纳回执中“一律排除编译器生成闭包”建议**：业务闭包会保存可变 GameItem/回调状态；设成 opaque 会导致恢复不完整。保留非受保护游戏闭包，完整图夹具继续验证闭包字段及别名恢复。测试用字段契约选择真实闭包，避免测试自己固定编译器序号；生产仍是构建时精确 token，不使用动态类型枚举。

## 表与包体

不把稳定/beta 两张大表塞进同一个 helper，也不让游戏运行时生成或解压加载代码。
每个分支独立构建包，仍只安装主 DLL + 一份同名 helper。切换游戏分支需换匹配包或在游戏外重新生成。
稳定 manifest 保持 release/manifest.xml；beta 用 release/beta/manifest.xml，仅声明 1.0.0.14。

- beta 最初旧规则：2893 类型 / 8176 字段，包含不该访问的受保护 owner。
- 修正共享边界：**2883 类型 / 8160 字段**，排除 123 类型/嵌套类型，遗漏布局=0。
- 只合并无声明字段、相同泛型元数的工厂，工厂 **2010** 个；已知类型/泛型模板和每个字段精确覆盖仍完整。
- 同 beta、同边界、旧生成方式：**687616 bytes**。
- 新 helper：**664576 bytes（649 KiB）**，减少 23040 bytes，约 3.35%；比旧 0.46D helper 675328 bytes 还小。
- 同边界基线从 cf7fb8e 生成器导出到 out，机械复制本轮共享 boundary，再生成，不改变游戏程序集或生产输出。
- 主包不带生成器、自包含 .NET、Cecil、游戏程序集、PDB、第二分支表。独立工具仍单独分发，不能把数十 MiB 工具包算作 Mod 必须负担。

新脚本 Package-FullRelease 只接受干净源码，重新构建当前分支，再校验 helper 确定性、manifest 版本、正式双 DLL 的 API/能力；输出压缩包和外置 provenance.json。Deploy-FullRelease 新增 ManifestPath，并在任何部署前重新生成对照 helper，拒绝把其他分支旧表误部署到当前安装。

## 复现命令

```powershell
.\scripts\Test-StaticLayouts.ps1 -GameDir "<beta游戏目录>" -ManifestPath release/beta/manifest.xml
dotnet run -c Release --project tests/NativeSaveHistoryTests
.\scripts\Publish-StaticLayoutTool.ps1 -OutputRoot "<新工具输出目录>"
.\tests\Test-PlayerLayoutTool.ps1 -GameDir "<beta游戏目录>" -ToolDir "<已打包工具目录>"
.\scripts\Package-FullRelease.ps1 -GameDir "<beta游戏目录>" -ManifestPath release/beta/manifest.xml -OutputRoot "<新主包目录>"
.\scripts\Deploy-FullRelease.ps1 -GameDir "<beta游戏目录>" -ManifestPath release/beta/manifest.xml
```

## 验证记录与限制

- 初轮官方扫描实际拦截了 ModWindowControl；不能将“编译通过”当作 beta 兼容。
- 修正后的完整套件：430 参考测试、84 挂点/76 字段、16 官方保护 owner、500 组原版耗水对照、发布扫描仅 files，HardFlags/Error 为空。
- 游戏 Mono 精确布局对照、完整图六次往返、业务闭包/原存储/别名/预算/预检/最终差异检查、错误版本和缺类型负例、字节复现通过。
- out/validation-beta-20261116-pass3.log、out/validation-20261116-review-safety.log 是开发验证；最终干净构建和部署以 out 内相应 clean-head 日志、包 provenance 和 deployment.json 为准。
- 独立工具补查发现：flat source 解压到仓库 out 下时，既有 ../../src 链接会与随包本地 boundary 重复定义。工程改为本地副本优先；工具断言新增源码复建，合计 11 项。这是构建工程修正，不改变生成的 helper 字节。
- 181ae76 的首份 beta 候选主包 536787 字节（约 524 KiB）；后续工程/文档提交重新构建会改变主 DLL informational version，最终来源以新包 provenance 为准，不复用旧哈希冒充新提交。
- 0.46D 当前没有完整已安装程序集副本，不切换/下载 Steam 分支，不声称本轮重跑了稳定版本游戏 Mono 套件。既有稳定发布不覆盖。
- 暂无 beta 实机测试：需建首/普通/手动/返店节点、跨天 F5/F7、物品移动/售出/旋转/群拖、顾客视觉、化学操作和设置；不要直接向玩家宣称所有 beta 功能已验收。
- 本轮不额外性能优化，不复原 day94，不主动保存或修改活动存档，不清理未知玩家文件。

## 最终交付来源

最终代码/工程提交：`f80f63b47d93c8b5bd23f672be6d6085c3220781`，构建时 clean。
本节是后续纯文档更新，不声称产物来自更新文档后的 HEAD。

```text
主 DLL（483840 bytes）
2E3A2129B4411F3D8F8B5EA0A70F840B15C9BEE1F68D05AD53CA5031A773A619
helper（664576 bytes）
B77C9A42591EE47D2D8824E8ABD0455A0029A6D6EDA4B5FD6DB11BA78429C11A
beta 主包 ZIP（536791 bytes，约 524 KiB）
5849E5FA95169697C257DE4E826DCC79F05E8044D185CA229FAD211B419544C9
独立工具 ZIP
13E3001F3F31395906F7EB85F8427C6FFD77D8EE28BA664DF6AC4BDB9E7678D5
```

- 主包：`out/beta-release-f80f63b/ProbablyStolenTools.zip`；外置来源文件 `provenance.json`。
- 工具：`out/static-layout-tool-beta-f80f63b/ProbablyStolenTools-StaticLayoutGenerator-win-x64.zip`；随包 package.json BuildDirty=false。工具须单独分发，未上传。
- 最终完整套件：`out/validation-beta-20261116-f80f63b-clean-head.log`。
- 存档/齿轮 14 项：`out/validation-20261116-safety-clean-head.log`。
- 玩家工具 11 项：`out/validation-player-tool-beta-f80f63b.log`，含负例 stderr、随包源码旁有仓库源文件时复建。
- 打包/部署：`out/package-beta-release-f80f63b.log`、`out/deploy-beta-f80f63b.log`。
- 最终部署到本机 Mods/ProbablyStolenTools，使用 beta manifest，双 DLL 与上述来源一致。
- 备份：`out/full-release-deploy-20261116-001733`，74 个既有存档目录文件校验未变，游戏程序集/其他 Mod 未变，GameStarted=false。
- 本地 Mod 版本仍 0.9.3，主包仅声明 beta 1.0.0.14；尚无 beta 实机验收，不推送/不发布。切回稳定分支须换表，不把这个包当双版本通用包。
