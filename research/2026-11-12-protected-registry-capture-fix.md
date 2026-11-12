# 注册组件闭包导致建点失败

## 报告及定位

玩家日志：工作区 `logs/Probably Stolen/4/PSTools (1).log`。共有 165 条 CAPTURE FAILED，原因均为：

```text
System.InvalidOperationException: Uncovered static snapshot layout: PaperBookRegistry+<>c
```

日志混有历史会话；最新会话为 0.9.1 / 游戏 0.46D。此次拒绝发生在 walk 阶段，不是对象/字节超限，也不是手动节点门控。

静态字段表生成器有意排除注册/加载器/信任组件及它们的嵌套类型，避免将其当作游戏业务状态。运行时 Traversable 却按 Assembly-CSharp 接受所有游戏类型；当业务引用或委托目标连到 PaperBookRegistry 的编译器闭包时，普通对象描述尝试查找被排除的字段表，导致整次捕获拒绝。

手动/自动节点共用 Capture/StateGraph 路径，因此此触达条件可影响两者；本报告旧日志没有 manual 标记，不能据此逐条判断来源。另一个尚未提供日志的自动节点问题不能直接判为同因。DIALOGUE 等原有跳过规则也不是该异常。

## 修复边界

- 新增 `SnapshotTraversalBoundary`，生成器通过源码链接使用同一份受保护 owner 名称清单，避免生成和运行时规则再次分离。
- 运行时仅对游戏程序集中的该清单及最外层声明类型匹配的嵌套类型生效；引用保留原身份，但描述为零槽，不读写其内部字段。不调用它们的方法、构造器或回调。
- 如果将受保护对象显式注册为业务根，则拒绝，不将非法根静默当成空节点。
- 判断缓存沿用每个 access 的 `_traversable`，不在每个普通槽增加元数据查询。
- 其他未知游戏/框架布局仍拒绝；ES3 排除及未知布局规则不变，不开放通用反射回退。不保证恢复受保护对象内部状态，亦不扩大第三方 Mod 内部状态覆盖。
- CAPTURE FAILED 摘要新增 manual / closed，今后可区分手动、自动营业及闭店捕获；保留完整堆栈和预算。

本次没有补齐或枚举 PaperBookRegistry 的字段，也没有修改游戏程序集、加载器、信任记录或扫描规则。

## 验证

运行 `scripts/Test-StaticLayouts.ps1 -GameDir <GAME_DIR>`，最终结果保存在本地 `out/protected-boundary-static-tests.log`：

- Release 0 警告 / 0 错误，380 项参考测试通过。
- 57 挂点 / 76 字段检查通过；两个发布 DLL 合并扫描仍只有 files，HardFlags/Error 为空。
- 游戏 Mono 下新增真实类型回归：PaperBookRegistry 的准确闭包类型、受保护嵌套 owner 为零槽；经委托引用闭包也能捕获；原引用身份往返、完整最终差异检查、引用拓扑普查一致；显式受保护根拒绝。
- 夹具仅创建未初始化的离线身份令牌，不运行受保护构造器/回调，不读取它们的字段。
- 原完整图/实体/Tag/int[]、六轮双向恢复、三个预算、未知根、确定性字段表、错误 MVID 拒绝测试继续通过。
- 生成表仍为 2807 类型 / 8027 字段；helper SHA256 未变。

离线验证不是玩家存档的 Unity 实机验收。

## 部署与待测

确认游戏未运行后部署，版本保持 0.9.1，未推送或上传。

- 记录：`out/full-release-deploy-20261112-234358/deployment.json`
- 主 DLL SHA256：`65C8A48A17A8175A169C948B1C9352E3F0F9A18B75580444ECEEF8679A595FE0`
- helper SHA256：`F2096125D20042E3609E56636FC17A566A5446A2E5B9209CB8059C34EAE70DC0`
- 79 个现有存档目录文件、游戏程序集、其他 Mod 校验未变；未导入设置，未启动游戏。

待玩家在原问题环境测试手动节点、普通顾客自动节点及节点恢复。若仍失败，收集新 CAPTURE FAILED（含 manual/closed）或没有捕获尝试时的会话/门控上下文；不要通过提高预算或删除静态布局检查掩盖异常。
