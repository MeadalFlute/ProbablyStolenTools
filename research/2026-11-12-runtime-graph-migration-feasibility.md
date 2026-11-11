# 通用运行态图兼容迁移：可行性与功能边界

日期：2026-11-12。承接 `2026-11-12-loader-capability-migration.md`。
本轮为迁移第一步的离线可行性验证，不是生产后端替换。未部署、未修改设置或存档，未修改 manifest、版本或游戏扫描器。

后续进展：`2026-11-12-static-type-token-proof.md` 已用构建时固定 IL 类型令牌打通私有 Entry/Slot 的窄访问路径，官方扫描无能力命中，游戏自带 Mono 离线读写通过。因此下文的“集合访问阻断”是当时普通 C# 生成路线的状态，不再是无法继续开发的理由；完整图等价迁移仍待实现和验证。

## 1. 结论先行

**有限已知类型的图后端可继续迁移；任意类型自动展开的旧通用后端不能原样保留；完整等价迁移尚有集合内部存储这一项实际阻断。**

不能再将“顶层 Dictionary 字段探针通过”推广为“所有运行态字段都能静态生成”。本轮已用官方本机扫描器验证，私有 Entry/Slot 的下一层字段访问仍触发 reflection-external。

这不是证明完整回溯必然做不了，而是现有普通 C# 静态描述方案还没有打通所有存储层，尚无依据承诺零功能损失。先解决这项，再替换生产图；不先上线残缺快照。

建议决策：

- 保留完整回溯为目标，不主动砍掉交易、顾客、RNG、群拖或多节点。
- 接受移除运行时代码生成，以及收紧未知原生类型/新版本的自动适配能力。
- 集合阻断未解决前，不把“只保存业务实体”作为兼容版内存回溯。
- 若目标版本之前仍无法证明等价恢复，兼容发行版应明确停用内存顾客/手动节点，保留原生文件回溯和辅助功能，而非静默漏存状态。此为最后备选，当前未执行。

## 2. 官方边界与复核依据

重新读取官方当前指南：

- https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.md
- https://raw.githubusercontent.com/dragonpan2/probably-stolen-modding/main/ModdingGuide.md

指南仍明确 2026-12-10 起拒绝 reflection-external/dynamic-code/native/threads。
固定 typeof 与固定成员名的查找可静态分析；computed type/member 不能继续使用。
System 类型名字符串查找不是替代途径。ModReflect 是范围受限 API，不提供任意系统对象字段枚举。

本机只读核对 ModReflect：

- 游戏私有字段/闭包在允许范围内；自有 Mod/已声明前置 Mod 和指定 UI 范围另按官方契约处理。
- Get/Set 会选择字段或属性，不能把字段快照悄悄迁移成业务属性 getter/setter。
- Set 对 initonly/readonly 字段明确拒绝。因此不能把整个旧 FieldInfo 写回统一换成 ModReflect.Set。
- 构造器未运行的离线 Difficulty.notes 夹具确实收到 read-only 异常；不涉及真实游戏状态。

不修改安全策略，不隐藏能力，不在运行时加载生成的 DLL；构建工具和旧反射参考后端只能留在离线工具中。

## 3. 本轮新增可复现探针

位置：`tests/CapabilityMigrationProbe/`，命令见该目录 README。
Probe 为 netstandard2.1；Scanner 为 net8 离线宿主，通过官方本机 ModScanner 扫描 Probe.dll。
Scanner 自身的反射不是扫描目标，也绝不能随 Mod 发货。

| 模式 | 结果 | 说明 |
| --- | --- | --- |
| clean | capabilities/findings/hard/pending/error 均为空 | 18 个方法；固定 List/Dictionary/HashSet 顶层字段、公开命名的视图/Enumerator、KeyValuePair、固定 Random 字段、字面游戏闭包及其字段枚举、官方 ModReflect、Array/Delegate 公开 API |
| private | reflection-external，恰好 3 条 | Dictionary 私有 Entry、从 `_entries` 的元素类型继续查字段、HashSet 私有 Slot |
| negative | reflection-external + dynamic-code，恰好 4 条 | 运行时 GetFields、固定 System.Random 的批量 GetFields、字符串 System 私有类型查找、Expression.Compile |

三类均设置断言，负例“通过”表示正确识别其应有的违规，不表示允许发布负例程序集。当前 HardFlags 为空依旧不是未来可用性的证明。

关键反例：

```csharp
// 顶层通过。
typeof(Dictionary<K, V>).GetField("_entries", flags);

// 后续仍被判 computed type；MakeGenericType 不会使其变为静态已知接收者。
typeof(Dictionary<K, V>).GetNestedType("Entry", BindingFlags.NonPublic)
    .MakeGenericType(typeof(K), typeof(V)).GetField("value", flags);
```

这不等于所有构建时生成方案皆不可能，但排除了上一份文档中最容易误认为已经成立的直接展开方案。

本轮扫描不是 Unity 活体执行，也没有验证所有 accessor 在目标 Mono 下读写成功。

## 4. 通用图按组成拆解

| 组成 | 可行性与下一步 |
| --- | --- |
| 图遍历、对象保活、引用身份、预算、历史节点 | 算法不依赖动态编译；保留 IStateAccess/StateGraph 合约，不为兼容删安全检查 |
| 游戏实体/继承字段/私有自动属性 backing field | 构建时枚举并生成准入描述，运行时仅选择；字段访问用明确字面查找或已证明无属性副作用的 ModReflect；不能只收录五种精确实体 |
| 游戏闭包与委托目标 | 字面私有游戏闭包查找已通过；委托 GetInvocationList/Target 保持原身份，不重新编译/创建业务委托 |
| struct/Nullable/浮点 | 生成明确字段路径和静态比较器；保留嵌套值类型写回、nullable 引用限制、NaN/正负零位比较，不用自定义 Equals 替换引用身份 |
| 普通数组、List、公开命名集合视图/Enumerator、Tuple | 有固定字段入口；逐布局覆盖、原数组/容量/版本/别名不变仍需独立读写和往返夹具，并非本轮已完成 |
| Dictionary.Entry[] / HashSet.Slot[] | 当前阻断；普通 C# 不能直接写这些私有类型的 typeof，沿已定位字段或嵌套类型继续查字段被扫描器拒绝 |
| System.Random | 本机 Mono 为 `_inext/_inextp/_seedArray`；固定字段探针通过，应保留完整状态，不必牺牲随机结果回溯 |
| Unity RNG、物品/顾客渲染、DOTween 路径 | 当前 Mono 用公开 API 或固定游戏成员，未发现因本次退役必须删除的理由；仍要照原视觉/业务顺序实测 |
| 未知原生类型/未来布局 | 不能继续兜底动态 GetFields；捕获预检拒绝新节点，显示具体类型/路径，保留历史，不在部分写回后才发现 |
| 第三方 opaque 对象 | 维持旧约定：只保留身份，不恢复其内部字段；不是本轮新增砍掉 55 Mod 支持，更不能声称已经支持所有第三方业务状态 |

本机目标 mscorlib/System.Core 元数据确认：

- Random 三个实例字段如上；不能拿 net8 宿主的不同 Random 布局当 Mono 证据。
- Dictionary 顶层十字段；Entry 为 hashCode/next/key/value。
- HashSet 顶层八字段；Slot 为 hashCode/next/value。
- 游戏定义中有 1243 个 DisplayClass 类型、299 个只读实例字段。它们是全程序集定义统计，不是实际快照可达集合，不能据此估算工作量或宣称全需迁移。

## 5. 为什么不立即换成逻辑字典重建

旧图保存原数组、容量、空闲链、版本、缓存视图和共享引用，不只保存有效 key/value。
Clear/Add 或换新 Dictionary 会改变集合内部数组身份、枚举状态/版本、容量与可能的顺序，还可能调用比较器和业务回调。

整体 Array.Clone/Array.Copy 可保留存储内容，也已在 clean 中确认其调用本身不产生退役能力，但它们没有自动解决：

1. 私有 Entry/Slot 内引用的完整发现与保活，包括独立可达/共享/旧存储数组。
2. 逐槽引用身份与浮点位语义一致的完整校验。
3. 对不属于当前字典有效条目的存储状态如何证明不漏状态。
4. 原有预算、完整普查、owner/full 修复与变更来源的等价性。

因此“复制数组 + 只枚举有效条目”只能作为另一个待证明的集合块设计，不能当作已有的完整图替代，更不能放松最终校验使它通过。

## 6. 哪些功能可能需要阉割

| 玩家能力/开发能力 | 当前判断 |
| --- | --- |
| 水质/化学/酿酒标注、多语言、自动鉴定/提交、双击鉴定、跳对话、设置/日志 UI | 没有已证明需要阉割的理由；共享 GameFields 访问改成合规的固定游戏字段访问即可，仍须全 DLL 扫描及功能回归 |
| 开业前/前一天的原生文件回溯、磁盘节点管理 | 与大图迁移分离，files 目前合法；继续只加载、不主动保存，不因本次两个能力直接删除 |
| 普通/手动内存节点、多节点往返、跨顾客、交易与物品移动/旋转、顾客视觉 | 保留为目标；共同依赖完整运行态图，若集合问题无法解决，应整体拒绝相关节点，不能只砍某个表面业务操作就声称安全 |
| RNG 精确回溯 | 固定布局方向可行，不建议阉割 |
| 任意新游戏类型自动反射适配 | 必须收紧成版本/类型/布局准入；未覆盖类型拒绝捕获并提示，更新生成表后再启用 |
| 运行时表达式编译加速 | 必须移除或替换为构建时静态读取/比较器；这是实现策略改变，不必对应玩家功能损失 |
| 旧开发 oracle/shadow/pilot 的动态反射实现 | 迁移或从发布编译中移出；保留离线参考测试。仅将设置关掉不够 |

这里不宣称未来目标游戏 API 完全不变，也不把当前扫描无命中当作无需回归的通行证。

## 7. 推荐后续顺序与停止条件

1. **集合可行性闸门**：优先解决私有 Entry/Slot 的完整、可审计、非动态代码访问。没有证明前不开始全量替换。
2. 向官方确认：能否对“固定外层泛型类型 + 固定嵌套类型名 + 固定字段名 + 明确泛型实参”提供静态追踪，或提供受限集合快照/字段适配 API。请求不涉及任意系统反射、不扩大 ModReflect 到整个 BCL。
3. 同时可做独立低风险工作：清理 Expression.Compile、固定 GameFields、生成精确游戏字段表；每步单独提交，不把它们宣布为通用图迁移完成。生产行为变更按既定规则闭游戏部署和实测。
4. 集合闸门通过后，实现静态准入图并保留旧反射后端为离线 oracle；先单布局对照，再原生夹具、完整图、预算边界、跨节点、随机状态与未知类型拒绝。
5. 发布 DLL 不得含任何退役能力代码，包括开发模式关闭但仍编译的路径；清单最后再删能力，所有 shipped DLL 均扫描。
6. 首/普通/手动节点、跨顾客、交易、群拖、窗口/容量/锁/贴图、反复往返、非默认议价及文件回溯分阶段实测后才切换默认。

如果官方暂不提供接口而另一个有限集合块设计可被完整证明，可继续验证；不能因为有潜在方案就预先写“完整等价迁移已确认可行”。

本轮停在研究结论与可复现探针，没有生产功能阉割，也没有要求用户现在启动游戏测试。
