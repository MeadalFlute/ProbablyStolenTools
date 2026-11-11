# DS 语义化后端审查复核与处理结论

日期：2026-11-12。输入为 `2026-11-12-semantic-backend-review-ds.md`。

本轮只复核和部署已有的 DS 修复，没有额外修改生产恢复、视觉或性能实现，也未启动插件、上传或升版。

## P1：接受 b99864b

核对 CustomerCheckpoint.Restore 后确认：恢复入口重新读取 SemanticDevelopmentChecks，Runtime 与 Entity pilot 均受该值门控；关闭时走完整 Graph.Restore。新实体块自身仍由 Graph 负责正常写回，不会被该门控禁用。

该行为符合原设计。保留已有节点上的独立诊断载荷直至节点正常淘汰/清理即可，不必在开关变化时再增加一次保活释放和节点生命周期操作。

边界需明确：

- 中途关闭开关禁止旧 pilot 写回，不等于立即释放已有诊断载荷。
- 已有节点的 oracle retention 检查和只读 ObserveRestore 仍可能执行；不能把这个修复宣传成“中途关闭立即消除所有诊断开销”。
- 当前设置说明本就要求重启。重启后旧内存节点消失，不再创建开发载荷，这是正常发布的零重复载荷路径。
- 配置文件没有通用实时重载入口；不能让玩家只编辑 XML 就假定进程内配置已经切换。
- 既有离线测试覆盖各写回器与完整图的正确性，但没有直接执行完整 Unity Restore 入口的会话内开关切换。373 项通过不能充当这个专项分支的端到端证明。

## N1-N6 决定

| 项目 | 决定 | 理由 |
| --- | --- | --- |
| N1 后端可见性 | 有价值，随加载器兼容迁移补充，不在本轮改 UI | CAPTURE 已有实际 backend/实体块数和 schema fallback；Session/面板还可加强，但不是已证明的状态丢失问题 |
| N2 int[] 变化后才写 | 暂缓 | 比较再复制可能增加完整数组遍历；Array.Copy 不应仅凭写入次数被认定为热点，需隔离基准 |
| N3 Tag 标量变化后才写 | 暂缓 | 直接赋值很便宜；不能复用早先差异扫描结果而忽略中间原生刷新又改变状态的可能 |
| N4 无顾客视图 | 不作为已复现缺陷，不放宽预检 | 正常离店隐藏共享视图，不等于该 Unity 对象不存在；真正缺失视图时拒绝捕获/写回仍是安全行为 |
| N5 开发 oracle 成本 | 保持 | 发布默认不创建，已有开发开销说明 |
| N6 未知实体严格拒绝 | 保持 | 安全边界不能为兼容更多 Mod 而静默漏状态 |

N4 依据：只读查看本机 StoreUIManager.DisplayClient/HideClient，实际分别为共享 clientGameObject.SetActive(true/false)。CustomerVisual 构造没有把 inactive 视为 missing；后续 Validate 也检查原身份，不要求 active。

此前 02:24 会话手动 id9 已在离店后成功捕获并恢复隐藏状态。因此“不需要有顾客”指合法店铺场景中可无当前业务顾客，不意味着顾客 Unity 视图被销毁或场景无效后仍强行支持恢复。真正缺失 image/parent 的情况仍是待专项覆盖的边界，不宣称所有情形均已验证。

## 验证

重新执行：

```powershell
dotnet build src/ProbablyStolenTools/ProbablyStolenTools.csproj -c Release
dotnet run -c Release --project tests/CheckpointTests/CheckpointTests.csproj
dotnet run -c Release --project tests/ReleaseCompatibility/ReleaseCompatibility.csproj -- `
  "<GAME_DIR>" src/ProbablyStolenTools/bin/Release/Mono/ProbablyStolenTools.dll release/manifest.xml
```

结果：0 警告/0 错误；373 项；57 挂点/76 字段及全部现有原生夹具通过。当前扫描 detected 仍为 files/reflection-external/dynamic-code，HardFlags/Error 为空；另有原生 SaveFiles.Slot 文件能力 Pending。不能称为下一版兼容通过。

## 部署

- 2026-11-12 03:03:15，游戏已关闭才执行部署，没有强制结束进程。
- 已部署运行时为 DS 的 `b99864b` 门控修复；文档 HEAD 为 `e269d19`。
- 版本仍 0.8.14，未推送、未启动游戏、未导入设置。
- 备份：`out/full-release-deploy-20261112-030315`。
- DLL SHA256：`3F3D2AACE4554BA8005C1A525C5C6EE0F826275D92E5CDCF8C722225644DA58B`。
- 部署脚本验证 79 个既有存档区文件未变。
- 本轮没有追加改设置、迁移存档或修改游戏程序集。

需要用户在该部署上补普通发布模式短测：首/普通/手动节点往返、恢复后群拖、顾客贴图和正常交易/送客。旧部署的验收不冒充 DS 修复后的新实机结论。

## 后续边界

当前游戏版本可用性与未来加载器兼容是两条独立结论。N1-N6 没有揭示新的必然丢状态阻断，但退役能力迁移尚未实现；详见 `2026-11-12-loader-capability-migration.md`。

本轮不把所有非阻断建议塞进发布收尾，也不启动全量兼容迁移或宣称已兼容下一版。后续迁移应按阶段提交、验证、部署，保持完整图预算/身份/校验与已有视觉顺序。
