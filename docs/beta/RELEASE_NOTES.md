# QuickSL 0.7.0-beta.1

## 简体中文

正式版 Mono 的通用快照兼容性测试版，独立于稳定分支发布。

- 自动捕获可达的第三方数据、集合、派生物品字段和回调闭包。
- 过滤任务、线程、协程执行器、原生句柄与资源对象，记录未覆盖的字段。
- 回溯日志明确区分已记录字段的一致性与完整 Mod 状态覆盖。
- 不包含逐 Mod 适配，不主动调用存档或第三方保存/加载接口。

验证：正式版构建、60 项离线测试、5 个真实 Mod 数据场景、
51 个 Harmony 入口、68 个字段和官方 Mod 扫描通过。
尚未进行 Unity 内组合模组测试，不能据此宣称完整兼容。

独立静态状态、额外库存、后台任务及第三方视图刷新仍存在覆盖边界。
请在所有动画/整理操作结束后测试，并附上 `PSTools_history.log`、`PSTools.log`、
`Player.log` 与 Mod 版本反馈结果。

测试版与稳定版使用相同的 ID，必须替换安装，不能同时启用。
包内提供中文安装、回滚与验收说明。
未包含主目录中尚未提交的卷帘门看门狗修复。
测试通过前不会合并到 `main`。此包不面向旧 Demo / IL2CPP。

## English

Experimental generic snapshot coverage for the full-release Mono game.
Published from a separate branch; not merged into the stable release.

- Capture reachable third-party data, collections, derived item fields and callback closures.
- Exclude tasks, threads, coroutine iterators, native handles and resource objects.
- Report skipped fields and distinguish recorded-field consistency from complete mod coverage.
- No per-mod adapters, forced saves or third-party save/load/reset calls.

Validated: Mono build, 60 offline tests, 5 real-mod data scenarios, 51 Harmony targets,
68 fields and the official mod scanner. Unity gameplay with combined mods is still untested.
Independent static state, extra inventories, background jobs and custom views remain outside
the supported coverage. Wait until animations and inventory sorting finish before testing.

Replace the stable QuickSL; never enable both. The mod ID and settings format are unchanged.
Include the mod versions, reproduction steps and `PSTools_history.log`, `PSTools.log`,
and `Player.log` when reporting results.
The uncommitted shutter watchdog changes in the stable workspace are not included.
This beta is for the full release, not the old Demo / IL2CPP build.
