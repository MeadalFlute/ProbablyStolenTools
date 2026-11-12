# 边界审查部署产物来源复核

日期：2026-11-13。对应 `2026-11-13-snapshot-boundary-review-ds.md` 第 4 节。版本保持 0.9.1，未推送或上传，无运行时代码修改。

## 结论

已从干净提交重新完成全套验证、同提交重复 Rebuild 和正式双 DLL 部署，部署与已验证产物逐字节一致。原交接缺少主程序集内嵌的构建提交号，导致两个不同提交构建的哈希被直接比较；现在补齐来源。

**原部署主 DLL 与 DS 重建主 DLL 的哈希确实不同，但未发现运行代码差异。不能据此判断“部署缺少最终边界修复”。** 部署早于提交 22 秒也不能证明代码尚未写入：构建可以包含尚未提交的工作区修改，而程序集中的提交号仍是当时 HEAD。

DS 原文保留作为审查历史；本复核取代其“很可能缺少最终修改”的未证实推断，不否认原交接在产物追踪上的不足。

## 不同哈希的证据

对主程序集读取 AssemblyInformationalVersionAttribute：

| 产物 | SHA256 | 内嵌 InformationalVersion |
|---|---|---|
| 上轮部署 | `FDC17F0BDE3FF13FF7B842A01E81A89F3383F6C6F7CAD566C18CDD0932785F75` | `1.0.0+9a2a484...` |
| DS 重建 | `CB954887E1981A4999409A0FEAE060D779250982803060EAE48AB643693B2661` | `1.0.0+7c9913f...` |
| 本次重新验证/部署 | `197099BB5C5DFCEA0D4CA4D7F99B3F376F669A807C2815DF93C048BF57894B39` | `1.0.0+970e8c955f510e2bf88202cd8f77029bbfa81606` |

当前 SDK 构建会将 Git SourceRevisionId 放入程序集 informational version；因此即使只增加文档提交，主 DLL 字节哈希也可能变化。该属性的 `1.0.0` 是既有程序集元数据，不等于 manifest / Plugin 的 0.9.1 功能版本，本轮未修改版本配置。

使用离线 Mono.Cecil 对旧部署 FDC 与 DS 重建 CB 比较了 **3026 项**：

- 类型全名、Attributes、基类；
- 字段签名、Attributes、常量；
- 方法签名、Attributes、ImplAttributes；
- 方法 IL（解析后的操作数）、局部变量类型、InitLocals、MaxStackSize；
- 异常处理区间及 catch 类型。

以上比较差异为 **0**。这不是将整个 PE/符号文件认定为字节相等，也不声称所有程序集自定义属性相等；已明确观察到 informational version 不同。结合 `79cab6e..970e8c9` 在 src/tools/tests/scripts/release 无差异，证据不支持“旧部署漏掉最终业务代码”的判断。

helper 由独立生成器控制元数据和内容指纹，没有主项目自动添加的 Git informational version，所以两轮一直字节相同。

## 本次来源与验证

构建源提交（全套验证和重复构建期间工作区干净）：

```text
970e8c955f510e2bf88202cd8f77029bbfa81606
```

其运行时代码与 `79cab6e` 一致；之后到此构建提交仅增加交接/审查文档。本复核文档是在部署后记录，不能因记录本身形成新 Git 提交，就要求部署 DLL 必须嵌入该文档提交号。

执行：

```powershell
.\scripts\Test-StaticLayouts.ps1 -GameDir '<GAME_DIR>'
dotnet build src/ProbablyStolenTools/ProbablyStolenTools.csproj -c Release -t:Rebuild "-p:GameDir=<GAME_DIR>"
```

第一次为完整套件，第二次为同提交重复构建；自动比较两轮主 DLL 和 helper SHA256，均完全一致。重复构建没有改变已验证的二进制。

本地日志：

- `out/boundary-review-artifact-rebuild-tests.log`
- `out/boundary-review-artifact-repeat-build.log`

完整套件通过：Release 0 警告/0 错误、380 项参考测试、57 挂点/76 字段、双 DLL 合并官方扫描仅 files、HardFlags/Error 为空。实际游戏 Mono 下 2861 类型/106 泛型模板/8108 字段、111 个排除边界、nullable/boxed 标量、完整图往返、预算/身份/释放拒绝、确定性 helper 及错误 MVID 拒绝均通过。

## 最终部署

部署记录：`out/full-release-deploy-20261113-001252/deployment.json`。

```text
Main SHA256:
197099BB5C5DFCEA0D4CA4D7F99B3F376F669A807C2815DF93C048BF57894B39

Helper SHA256:
CBB8F36AECB701BD46BEB87EAB53B63F4782E222056B7FEE0C452D7A2C8E1386
```

部署前确认游戏未运行；未终止/启动游戏，未导入设置，79 个现有存档目录文件、游戏程序集和其他 Mod 校验未变。

这组哈希替代此前交接中的 FDC/CB 主 DLL 哈希。当前主 DLL 不应强求等于 CB：构建提交号已不同，但本次有干净来源、完整验证、同提交重复构建及安装一致性证据。

## 后续约定

交接正式产物时同时记录：功能版本、完整构建提交号、工作区是否干净、informational version、主/helper 双哈希、验证日志和部署记录。先提交需要验证的代码，再从该干净来源构建；部署后补充的纯文档提交保留上述来源，不要求逐次重新构建。

如果同源码出现哈希变化，先检查 Git 构建元数据和构建参数，再比较实际方法/字段；不能仅凭部署时间早于提交就断定功能缺失。反过来也不能只凭 IL 对照跳过正式产物验证和双 DLL 哈希核对。

本轮不调整构建规则来移除追踪信息，不改加载器或扫描器，也不扩大审查范围。原问题玩家及完整 Unity 实机矩阵仍待复测，离线通过不替代实机验收。
