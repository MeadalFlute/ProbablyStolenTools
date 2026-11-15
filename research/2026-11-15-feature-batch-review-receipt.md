# 功能批次 Code Review 回执

日期：2026-11-15
审查对象：`44cf260..48853fb`（跨天回溯、化学辅助、晨间优化、独立布局生成器）
审查文档基线：`517fdad`（`research/2026-11-15-feature-batch-review-handoff.md`）
审查后修复/发布：`3fc7e86`（测试修复）、`d6e1faf`（0.9.3）
审查方式：重点高风险文件静态审查 + 完整离线套件 + 工具包源码重建 + 原生测试脚本复跑；未启动游戏，不是 70 文件逐行审查。

---

## 1. 结论

| 项目 | 结论 |
|---|---|
| 本批运行时代码 | **通过**离线审查，未发现新增的运行时阻断缺陷 |
| 验证 | 430 项参考测试、84 挂点/76 字段、双 DLL 扫描仅 `files` |
| 审查中发现问题 | 1 个测试验证缺陷，已修复；4 个非阻断风险 |
| 1.0.0.14 兼容 | **仍未解决**；本批不能作为该版本的可发布修复 |
| 推送 | 0.9.3 已推送 GitHub；**未部署、未上传 Workshop** |

> 本批代码以 **0.46D** 静态布局表构建；第三方玩家 1.0.0.14 的 `TypeLoadException` 仍存在，不能向该玩家宣称已修复。

---

## 2. 验证证据

命令与日志：

```powershell
.\scripts\Test-StaticLayouts.ps1 -GameDir '<GAME_DIR>'
```

结果（`out/feature-release-0.9.3.log`）：

```text
Release 构建            0 警告 / 0 错误
CheckpointTests         430 项通过
ReleaseCompatibility    84 挂点 / 76 快照/私有字段
双 DLL 官方扫描         Capabilities: files；HardFlags 空；Error 空
实际游戏 Mono 静态表    2861 类型 / 106 泛型模板 / 8108 字段 / 4 模块
边界普查                excluded=111 serialization=0 admittedWithoutLayout=0
完整图/预算/身份/GC     通过
确定性 helper 重建      字节一致
错误 MVID 拒绝          在返回任何字段前拒绝
```

玩家工具：

```text
tool source package     dotnet build 通过
Test-PlayerLayoutTool   10 项断言全部通过（修复后）
```

产物：

```text
主 DLL SHA256
BED180CE2EFA206A35264363428A76DF1D5C775105ECB2DE97EF03E2BF18FD47

helper SHA256
CBB8F36AECB701BD46BEB87EAB53B63F4782E222056B7FEE0C452D7A2C8E1386
```

说明：主 DLL 哈希与交接中的 `BA01F2C2…` 不同，因为本轮包含 0.9.3 版本/测试提交；构建自干净 `d6e1faf`。

---

## 3. 审查中发现并修复的问题

### R1. 玩家工具测试最后一项实际未通过（已修复）

位置：`tests/Test-PlayerLayoutTool.ps1:45-54`

原代码直接调用生成器并依赖 `$LASTEXITCODE`：

```powershell
& $exe $fakeGame $forbidden *> $null
Check ($LASTEXITCODE -ne 0 ...) 'generator refuses output in Managed'
```

生成器在拒绝写入 `Managed` 时按预期向 stderr 打印并退出非零；但 `$ErrorActionPreference='Stop'` 将原生 stderr 变成终止性 `NativeCommandError`，脚本在最后一项 `Check` 之前以 exit 1 结束。交接中的“10 项断言通过”不可复现。

修复：临时切换为非终止错误处理，捕获拒绝文本，验证：

- 退出码非零；
- 输出包含 `Refusing to write`；
- `Managed` 下没有生成文件。

修复后 10 项全部通过，已提交 `3fc7e86`。

---

## 4. 非阻断风险

| 编号 | 位置 | 风险 | 建议 |
|---|---|---|---|
| N1 | `NativeSaveHistoryPatch.cs:36` | `sharedFile` 只检查当前索引之前的历史项；若同一文件仍被后面的无效/非重复项引用，可能删掉仍被引用的副本 | 使用 retained 集合或全表引用检查，并补同文件多引用的 IO 夹具 |
| N2 | `NativeSaveHistoryPatch.cs:137`、`Rewind.cs:35-40` | `ReconcileActive` 在每次 `LoadGame/WriteSlotFile` 后执行，但不保护 `PendingPoint.Path`；待恢复检查点可能被同日去重清理 | 将 `PendingPoint?.Path` 加入保护集；补“待恢复期间成功写盘/载入”的故障注入 |
| N3 | `Rewind.cs:990` | 原生载入结果与节点不匹配时只提示，仍等待最长 90 秒才取消；玩家可能误以为卡死 | 不匹配时立即清 `PendingPoint`/`Settling` 并恢复输入 |
| N4 | `Publish-StaticLayoutTool.ps1:41-43` | 工作区 dirty 时仍能打出工具包，只在 `package.json` 记录 `BuildDirty=true` | 正式发布时拒绝 dirty 构建；保留当前 metadata 作为审计 |

这些不是本批新增的运行时阻断；N1/N2 是本批最值得补测的存档安全边缘。

---

## 5. 独立阻断：1.0.0.14

第三方 `Player.log` 已确认游戏版本为 **1.0.0.14**（DJO、Client Board、本 Mod 均报告该版本）。当前静态 helper 仍按 0.46D 生成，在该版本上：

```text
System.TypeLoadException: Could not resolve type with token 010001a1
from typeref (expected class '<>c__DisplayClass51_0' in assembly '')
```

位置：`SnapshotStaticLayouts.CreateFactories -> SnapshotLayouts.KnownTypes`。旧 helper 的编译器生成类型 token 在新游戏构建中失效；`CreateFactories` 先解析类型、后验 MVID，因此不是清晰的版本拒绝。

必须另行处理：

1. 在 1.0.0.14 的 `Assembly-CSharp` 上重新生成 helper；
2. 将 `ExpectedModules` MVID 校验提前到 `KnownTypes()` 之前；
3. 生成器排除 `CompilerGeneratedAttribute` / `<>c` / `<>c__DisplayClass*` 嵌套类型，运行时按 opaque 处理；
4. 重建/验证/部署后请该玩家复测；
5. 更新 `release/manifest.xml` 的 `<GameVersions>`（当前仍是 0.46D）。

本批源码推送不改变这一事实；不要部署给 1.0.0.14 玩家或声称兼容。

---

## 6. 推送与部署状态

```text
版本              0.9.3
commit            d6e1faf
远端              d6e1faf（44cf260..d6e1faf）
工作区            clean
部署              未执行，本机 Mods 仍为 0.9.2（BA01F2C2…）
Workshop          未上传
```

本次推送使用用户提供的新 token；token 未写入仓库、脚本或日志。

---

## 7. 交接建议

- 本批可作为 **0.9.3 源码候选**；在解决 1.0.0.14 前不要作为“新游戏版本兼容”发布。
- 下一轮优先补 N1/N2 的存档去重故障注入，再做 1.0.0.14 重建。
- 发布公告保持谨慎：430 项离线测试通过不等于完整 Unity/多 Mod/新游戏版本验收。

## 8. 参考

- `research/2026-11-15-feature-batch-review-handoff.md`
- `out/feature-release-0.9.3.log`
- `out/feature-review-run.log`
- `out/static-layout-tool-player-final-20261115/`
- `D:\Program\GameProject\logs\Probably Stolen\6\Player.log`
- `D:\Program\GameProject\logs\Probably Stolen\6\PSTools.log`
