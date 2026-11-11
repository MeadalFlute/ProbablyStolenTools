# 框选批量移动与回溯交互

## 报告与已知边界

用户完成 18:31 int 数组载荷实机测试后报告：“框选多个物品进行批量操作的功能，在移动物品时意外失效”。
未说明是否仅在回溯后发生，也未区分选区消失、单件拖动、整组无法落下。已询问这些信息，不能将下述缺口直接认定为报告根因。

读取本机原生 Assembly-CSharp 中 ItemMultiSelectHandler / InputActionHandler / InputActionManager / GameItemElement，未修改游戏程序集。旧 Player.log 中没有对应异常；已有其他 Mod 启动挂点失败、原生 UI/字体警告不能直接归因于本故障。

## 原版机制

- ItemMultiSelectHandler 是 MonoBehaviour，不是本 Mod 实现的功能，不在业务快照中。
- 状态为 Idle / PendingBand / Banding / Selected / PendingGroupDrag / GroupDragging。HasSelection 对所有非 Idle 状态为真，IsGroupDragging 只对最后一态为真。
- FinalizeBand 记录 selectedItems 与 selectedHomes；PruneSelection 会移除销毁或父库存已改变的项。
- StartGroupDrag 启用 fake 贴图、移到 overlay、关闭 fakeBackground 的 raycastTarget、保存偏移，然后进入 GroupDragging。
- EndGroupDrag 可能移动/堆叠/使用物品、改变业务图，不能用于回溯前“清理”。
- Cancel 在静态 Selected 状态只清选区/高亮/目标；只有 GroupDragging 时才 RestoreGhosts / Validate / 必要时 Destroy。

旧代码只在部分入口检查 ItemMouseDragHandler.IsDraggingItem，顾客自动捕获甚至没有单件拖拽门控；群组拖拽始终未被识别。恢复业务图时不清旧 selectedHomes，下一次原生 PruneSelection 可能按旧库存信息移除选择；拖拽中捕获还可能保存 ghost 的中间视觉状态。这是确实存在的保护缺口，但日志不证明用户当时恰好触发。

## 本轮修改

- 共用门控：singleDragging || groupDragging || (HasSelection && 对应 key 仍按住)。groupDragging 独立判断，覆盖释放事件尚未处理的帧。
- 顾客自动捕获、闭店捕获、手动捕获、恢复拒绝条件及卷帘门看门狗统一使用门控。自动捕获等待结束，不消耗节点尝试；手动和恢复给出原有拒绝提示。
- 静态 Selected 不阻塞自动建点，普通点击且没有选择手势也不阻塞，避免永久等不到顾客节点。
- 恢复通过全部预检后，在第一次图写回之前调用原版 Cancel 清理静态选区。没有群组拖拽时不会走 RestoreGhosts，不提交拖放，不在恢复之后运行旧时间线的清理。
- 新增两个只读 Harmony 诊断挂点：StartGroupDrag / EndGroupDrag。前后计数、HasSelection / IsGroupDragging、当前节点和异常；一轮拖拽最多两条摘要，没有逐帧、逐物品日志。诊断异常内部隔离，原版异常不会被吞掉；不拦截原版批量操作、不修改输入优先级或键位。
- 没有继续改变图载荷、预算、owner 修复、完整校验、语言、存档或其他 Mod。

## 验证与停止边界

- Mono Release 构建 0 警告 / 0 错误，213 项离线测试通过（新增 4 项门控规则回归）。
- 正式版 57 挂点 / 74 快照或私有字段兼容；检查原版 HasSelection / IsGroupDragging / Cancel / key 合约，HardFlags 为空，能力声明不变。
- 离线测试不运行 Unity 鼠标交互，不能证明故障已修复。尚未获得报告的确切触发步骤。
- 维持 0.8.14，独立提交并通过标准脚本部署，不启动游戏、不推送。

需要实机确认：重启后先不回溯，框选至少两个物品，分别同容器移动和跨容器移动；再建节点、移动、回溯、重新框选重复操作。静态选区回溯后预期清除，需要重新框选。拖拽/框选时手动节点和回溯应拒绝、自动节点应等待。若失效，保留现场前后日志，并说明具体表现和是否发生过回溯。

## 部署记录

2026-11-11 18:41，通过 `Deploy-FullRelease.ps1` 部署至 `E:\SteamLibrary\steamapps\common\Probably Stolen`。

- 备份：`out/full-release-deploy-20261111-184109`，含 SaveData / Mods / deployment.json。
- DLL SHA256：`28EF77A3F6AD2B75AEE0EB0DD9FEE294EF333CCF07D33AC90D6BAB36574CE042`，构建与部署一致。
- 78 个原有存档目录文件、游戏程序集及其他 Mod 原有文件哈希校验未变。
- SettingsImported=False；GameStarted=False。未改变玩家设置，未启动游戏，实机交互验收待用户操作。
