# 基因扫描仪开启时所有回溯被拒绝（0.8.5 / 游戏 0.46D）

## 环境

- Probably Stolen Tools **0.8.5**（工坊 3829188627）
- 游戏 **0.46D**，BuildID **26496118**，Unity 2021.3.45f2 / Mono / x64
- 同时启用 24 个 Mod（EchoProtocol、ProbablyMultiplayer、GunsmithExpansion、Va11HallA 等）。从代码路径看，该问题只涉及游戏本体的 `isGeneScannerScanning` 与本 Mod 的判断，不需要其他 Mod 参与；未单独做“仅本 Mod”对照测试。

## 复现步骤

1. 在店内放置并**开启**“基因扫描仪”设施（原生 Amenities 物品，`genetic_scanner`）。
2. 正常接待顾客，等待检查点建立。
3. 按 F6，或双击“今日回溯”面板中的顾客节点。

## 实际结果

- 弹出：`无法回溯：正在基因扫描`
- 日志：`F6 refused: geneScan (正在基因扫描) ...`
- 同时被影响的还有：
  - F9“立即建立节点”：`无法建立节点：正在基因扫描`
  - 闭店节点：不会生成（`TryCaptureClosed` 的 settled 条件包含 `!isGeneScannerScanning`）
  - 双击物品快速鉴定：被拒绝（reason 含 `geneScan`）
  - 卷帘门卡锁看门狗：把扫描仪视为“正忙”，不会兜底解锁

**关闭扫描仪后，所有回溯功能立刻恢复正常。**

## 日志片段

```
[00:45:06] [QuickSL/Customer] F6 refused: geneScan (正在基因扫描) target=下层区公民 targetClient=0x351 currentClient=0x16821
[00:45:11] [QuickSL/Customer] F6 refused: geneScan (正在基因扫描) target=农场主 ...
[00:50:16] [QuickSL/Customer] F6 refused: geneScan (正在基因扫描) target=囤水者 ...
```

## 根因

`PlayerStore.isGeneScannerScanning` 不是“正在扫描某个顾客”的瞬态状态，而是**基因扫描仪设施是否开启**：

- `AmenitiesItemDirectory.GeneScanner` 的 `onToggleSlotItemFunc`：开启时置 `true`，关闭时置 `false`（同时维护 `IS_ITEM_TOGGLE_ON_TAG`）。
- `onOneSecondItemFunc`：扫描仪开启时每秒重新置 `true`。
- `EmporiumEntry.OneSecPrefixFunction()`：每秒置 `false`。

所以扫描仪开着时，该字段基本恒为 `true`；它和“当前是否有一个扫描动作在进行”没有关系。

0.8.5 源码中共有 5 处把它当作阻塞条件：

- `src/ProbablyStolenTools/CustomerCheckpoint.cs:163`（`ShutterWatchdog` 的 `legitimatelyBusy`）
- `src/ProbablyStolenTools/CustomerCheckpoint.cs:309`（`TryCaptureClosed` 的 `settled`）
- `src/ProbablyStolenTools/CustomerCheckpoint.cs:347`（`CaptureManual`，F9）
- `src/ProbablyStolenTools/CustomerCheckpoint.cs:660`（`Restore` 的 `blockers`）
- `src/ProbablyStolenTools/DoubleClickInspectionPatch.cs:75`（双击鉴定的拒绝原因）

## 建议修复

直接删除以上 5 处对 `isGeneScannerScanning` 的判断即可。理由：

- 扫描仪的开关状态保存在 `PlayerStore` 字段和物品的 `IS_ITEM_TOGGLE_ON_TAG` 中，已经包含在快照里，恢复时会一起还原；
- 当前版本没有“扫描动作进行中”的瞬态状态需要保护（上述 IL 可证）；
- 保留 `BlockerText` 里的 `"geneScan"` 分支不影响。

如果作者原本想防的是旧版本的某段扫描协程，建议确认后改为“仅在真正的扫描 UI/协程存在时判断”，而不是用这个常驻设施开关。

## 备注

`AGENT.md` 中没有提到 `geneScan`，这一保护可能是后来针对某个旧现象加的，在 0.46D 的常驻设施开关语义下变成了永久阻塞。
