# 活动档每日唯一、返店与晨报关闭卡顿

## 本轮范围

- 用户反馈重复 day95、返店不建点、要求清理旧开发存档。
- 后续澄清卡顿发生在载入后关闭“昨夜状态”窗口，不是单指原生读档。
- 不主动保存、不修改活动 ES3、不重新恢复 day94、不启动或终止游戏。

## 修正

### 活动档与历史档共同每日唯一

上轮仅对 history 内部去重，漏掉 active95 + history95。

- 保留 TrimHistory 前按日去重。
- SaveGame 作用域只在确认新旧均为同 run/slot/day 的 MORNING 时，为轮转回滚副本临时增加一个历史名额，不先挤掉另一天。
- 原版 WriteSlotFile 成功 postfix 后检查磁盘活动档及历史档元数据，删除已验证的同日旧历史、更新原版索引。原版 SaveGame 会吞部分异常，不能用它的 postfix 判断写盘成功。
- 正常 LoadGame 后同样整理已有重复。活动文件只读，不覆盖。
- 未知命名、损坏、异 run/slot、非晨间、文件链接不作为重复依据。比活动文件更新的历史副本保留，避免意外丢掉较新进度。
- 独立 IO 夹具直接编译生产 NativeSaveHistoryPatch，伪造游戏 API，而非重新实现算法；覆盖成功/失败、回滚名额、索引失败、锁定、重复索引、较新历史、异布局等 9 项。

### 返店

原版 EndAndClose 增加 lockShutterButtonFlag，直到次日 reset。旧返店 gate 无条件要求零，使闭店返店无法建点。

- CLOSED 允许该原版常驻锁，OPEN/MORNING 保留零锁要求。
- 确认 playerLocation 为 IN_STORE，保留稳定 0.5 秒、对话/转场/拖拽等门控。
- 无顾客时允许不存在 StoreClientMono；有业务顾客时仍拒绝缺模型。
- 加 RETURN SHOP 和有界状态变化 RETURN WAIT 日志，不逐帧输出。

## 晨报关闭卡顿：结论与边界

最近会话两次加载后的 day-start：

| 总捕获 | access | walk | items | verify |
| --- | --- | --- | --- | --- |
| 2775ms | 558 | 1576 | 201 | 434 |
| 3276ms | 0 | 2672 | 322 | 282 |

图约 185714 objects / 1017538 slots，developmentChecks=False，没有额外 oracle/shadow。

TryCaptureDayStart 在晨报/日历打开期间拒绝捕获；关闭后第一个安全 Tick 同步完整捕获。因此该行为与用户关闭窗口后卡顿吻合，主要已知开销为完整图遍历。反编译中 AdvCalendar.CloseUI/StartOfDay.HideUI 主要关面板、音效与 tooltip，未发现它们自己启动大规模业务处理。

新增每次原生加载的聚合计时，和晨报关闭 closeMs/morningAnchorPending。下一次可精确分开原生读档、按钮回调、随后 CAPTURE；尚无新版本实机计时，不宣称完全确认全部来源或提速。

化学自动化禁止区间扩至整个 Entry/LoadGame scope，包括 DecodeNodes 完成后仍在 onLoaded 的阶段。保留原版加载与完整图校验。

**本轮没有解决晨间完整捕获的秒级停顿**，也没有用延迟数帧伪装提速。安全分帧需冻结所有相关业务写入并保持一致性，属于独立高风险工作，不在本次夹带。

## 已执行备份清理

运行 `scripts/Prune-DeploymentBackups.ps1 -Apply -Confirm:$false`：

- 识别 131 份部署目录，保留 98 份，删除 33 份冗余目录，约 9.0 GiB。
- 每个删除目录的 ES3 都有哈希验证后的保留副本。
- 独有内容不删除；无法识别的旧部署记录保留。
- `out/save-recovery-day94-20261115-214109` 保留。
- 未删除原版目录中的未知 broken/before_rollback 恢复档。
- 保留数量是清理时状态，后续新部署会新增一份。

## 验证与待验收

- 426 项参考测试、9 项原版历史 IO 夹具、6 项备份工具测试。
- 完整静态表/能力/游戏 Mono 测试日志：`out/validation-20261115-active-history-return-load-final.log`。
- 实机仍需：载入后 day95 仅一份；重复 F5/跨夜保存不挤掉独立日期；闭店出门再返店有独立节点；晨报关闭后检查 closeMs 与 CAPTURE。
- 不升级版本、不推送。部署来源与双 DLL 哈希见部署目录 deployment.json。
