# 固定类型令牌：私有集合访问阻断的构建时解法

日期：2026-11-12。更新上一份 `2026-11-12-runtime-graph-migration-feasibility.md` 的集合访问结论。

## 已证明的窄结论

普通 C# 的 GetNestedType/FieldType -> GetField 查找链仍会被扫描器拒绝；该证据不变。
但可以在构建时用 Mono.Cecil 直接生成固定 `ldtoken`，使最终程序集明确包含私有泛型 Entry/Slot 的类型和固定字段名。

这与可静态分析的 `typeof(具体类型).GetField("固定字段")` 相同，只是 C# 编译器因私有类型的源语言可访问性而无法写出相同表达式。
生成结果没有运行时类型枚举、Expression.Compile、私有 ldfld/stfld 访问、跳过可见性检查或安全策略修改，仍通过正常 FieldInfo 读写。

官方本机扫描器对三个生成方法均返回：

```text
Capabilities:
Findings:
HardFlags:
Error:
MethodsScanned: 3
Pending:
```

反编译可直接看到：

```csharp
typeof(Dictionary<T0, T1>.Entry).GetField("value", flags);
typeof(Dictionary<T0, T1>.Entry).GetField("next", flags);
typeof(HashSet<T0>.Slot).GetField("value", flags);
```

没有将反射藏到辅助 DLL：生成 DLL 本身就是扫描目标；以后所有实际发货 DLL 都必须扫描。

## 目标运行时执行

在独立离线进程中嵌入游戏自己的 Mono，加载它的 mscorlib/System.Core 和一个纯测试夹具，未启动 Unity 游戏。
新建内存 Dictionary/HashSet，使用生成的泛型类型令牌取 FieldInfo：

- 读取 Entry.next；
- 写入装箱 Entry.value，再通过原 Array.SetValue 写回原数组；
- 原字典通过公开索引器返回相同引用；
- 读取 HashSet.Slot.value 并验证相同引用。

实际输出：

```text
PASS actual game Mono: generic private Entry/Slot type tokens, field reads, boxed value writeback.
```

最初尝试在 net8 宿主执行游戏 mscorlib 令牌，得到 Entry TypeLoadException；宿主 BCL 私有嵌套类型转发不等于目标 Mono，不能据此否定或验证 Mono 路径。已改用目标 Mono 真实执行，不用宿主特例代替。

## 工具与安全边界

工具在 `tests/CapabilityMigrationProbe/{LiteralTokens,MonoFixture,MonoHost}`，复现见 README。

- Cecil 是构建工具依赖，不装入 Mod。
- MonoHost 的 native bootstrap 只属于离线测试进程，绝不随 Mod 发货。
- MonoFixture 的 Assembly.LoadFrom/MakeGenericMethod 等只用于离线驱动测试，不装入 Mod。
- 没有访问存档、修改信任或扫描器，也没有启动/终止游戏进程。
- 没有生产后端修改或部署。

参考原始文档：

- https://github.com/jbevain/cecil
- https://www.mono-project.com/docs/advanced/embedding/
- https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.md

## 后续不再被该窄问题阻断

可以继续构建有限类型的静态字段表，包括游戏闭包与泛型集合内部类型。
完整图是否等价仍需验证字段覆盖、目标程序集/布局版本门控、嵌套结构体写回、引用拓扑、预算、全部校验/修复与实际 Unity 视觉。
不能把三个字段的成功当作所有集合和完整图迁移完成，也不因这条路径立即移除安全检查。
