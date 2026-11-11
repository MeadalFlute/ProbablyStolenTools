# 语义标签边界：引用审计与集合恢复门槛

日期：2026-11-11，版本保持 0.8.14。本轮用户要求推进到风险高、需要反复实机调试的边界。

## 已确认的实机证据

15:55:47 会话（GameLog_2026-11-11_15-56-24.txt）：

- 节点 id=1：CAPTURE MATCH，captureVerifyMs=69，原摘要 semanticShadow=70ms。
- 2192 个系统、2192 个字典、32300 条目、16192 个唯一标签；286 个库存子类（179 GameSlotInventory、107 GameGridInventory）。
- 同节点 RESTORE MATCH diff=0，verifyMs=4，原摘要 semanticVerify=5ms。
- 4 次旧捕获均成功：1017/480/557/464ms；3 次旧恢复均 diff=0、releaseOk=True：1518/1492/1595ms。
- 唯一 watchdog 在初始加载，报告累计 8.9s，早于首个捕获；没有本次新增语义失败。

这只验证原版基类字段投影的一次实机往返，未验证语义写入，也不是提速 A/B。原旧图 562460 槽与语义载荷 214712 槽覆盖范围不同，不能相减当成可删除槽位。

## 阶段一：只读引用审计

新增 StateGraph.VisitCapturedSlots / IsCapturedRoot，只读已保留的 Entry 和根来源，不重读业务字段、不发起校验、不写入或裁剪。census/释放后拒绝使用；回调失败不会改变旧图。

SemanticTagOwnershipAudit 在一次采样中执行：

1. 按捕获时的系统、字典、标签身份建小型索引。
2. 通过旧图的字典字段槽识别 _entries/_buckets/_keys/_values/_comparer/_syncRoot，不调用 Dictionary.Keys/Values 等可能懒创建对象的 getter。
3. 全图扫描所有已保存引用，统计字典非 owner 引用、内部数组/视图非字典自身引用、显式根、标签非 entries 引用。嵌入结构体的枚举器、回调目标引用也在原槽位中。
4. 统计 standaloneStates（旧图可达但不在语义字典条目内的标签）和相关旧图槽位；共享字典按唯一身份计数。

只保留摘要字符串，不长期保存额外引用索引。每上下文仍只采样一次，不增加每个节点或每帧工作。采样成功后多一条 OWNERSHIP 日志；最多六条来源，每条路径不超过 280 字符，但总计不截断。

边界：

- 字段名未知时增加 unknownMapSlots，不把布局默认为已覆盖。
- scope=captured-graph-only、pruningAllowed=false 固定输出，**没有自动批准或裁剪功能**。
- 静态字段、图外对象、不透明第三方对象内部不可见，零已知别名也不代表真正独占。
- 比较器内部状态与子类额外字段不归语义投影保证。
- legacySlots 仅计已识别 map/storage/linkedState 的槽数，不包含所有语义相关字符串/比较器，不是可删除量。
- auditMs 单独计时；captureVerifyMs / semanticShadow 包含审计，不可与先前 69ms 当控制变量比较。

验证：Mono 构建 0 警告 / 0 错误；125 项测试全过（新增十项审计/检查 API 测试）；55 挂点、73 字段与原生扫描通过，HardFlags 为空。真实游戏类型夹具包含共享字典、entries 别名、Keys/Values 和活动枚举器、独立标签，全部检出并保持旧恢复零差异。没有修改原恢复或视觉处理顺序。

阶段一已通过标准脚本部署，备份 `out/full-release-deploy-20261111-160619`；DLL SHA256 `EA3519BACBFD4DE11807D7AF56839CE0924C32BD2825DCCA9394B9C10455A5CE`，构建与部署一致。78 个已有存档目录文件、游戏程序集与其他 Mod 哈希不变，未导入设置、未启动游戏。

下一阶段先补齐字符串身份及离线反例，仍不启用集合写入。
