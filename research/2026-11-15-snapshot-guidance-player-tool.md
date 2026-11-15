# 建点错误指引、官方齿轮限额设置与玩家布局表工具

## 错误显示

- RecordFailure 统一通过 CheckpointFailureMessage 生成 toast、面板损坏节点说明和
  LastCaptureError，保持底层异常类型/消息，不再只说“详情请查看日志”。
- 解包 TargetInvocation/TypeInitialization 等内层异常。静态表不匹配、未知静态布局、
  TypeLoad 等给出手动生成或等待更新的指引；日志写项目地址。
- SnapshotLimitException 增加只读 Kind/MaxObjects/MaxSlots/MaxBytes/Discovered，
  由结构化字段产生实际有效上限、建议 XML 项，不解析异常字符串或固定写死 200K。
- 已达受支持最大值时不建议继续增大。未知异常保留具体类型/消息及 bug report 指引，
  IO/权限错误单独提示磁盘、权限和占用。
- toast 关闭富文本解析，XML 不被吞；按真实自动换行高度布局并延长阅读时间。

## 官方齿轮面板

- 使用原版 ModHelper.GetConfig / ModConfig.Define 注册三项快照限额，
  不是把它们混进游戏自己的 Gameplay 设置。
- 默认/范围和运行时一致；玩家点击“应用”即同步既有 PSTools.settings.xml。
- 启用时 XML 为真源，覆盖 native JSON 的旧副本；手动编辑 XML 后不会在重启时被反向覆盖。
- OnChanged 与 XML SettingChanged 双向镜像，禁用/重新注册释放旧订阅。
- 当前图结构不按 MaxObjects 预分配；提高上限只改变拒绝边界，
  未超限图对象/槽/字节及完整校验相同。允许原先失败的大图后，实际内存/耗时自然可能增加。
- 主菜单齿轮的原版 UI 仍需玩家实机验收；IO 夹具验证 XML 即时持久化、限额同步、
  范围、旧回调释放与重新注册。

## 独立工具

- Publish-StaticLayoutTool.ps1 打包 Windows x64 自包含 exe、玩家 PowerShell wrapper、
  中英操作文档、匹配源代码、Cecil/.NET 许可证与 runtime 第三方声明。
- 玩家编译排除 --host/--test-mismatched-version，防止错误生成测试宿主/负例表。
- 默认只在工具目录生成。可选 -Install 仅支持默认 Mod 路径，关闭游戏后备份旧
  helper 到工具目录，再替换 helper；不将备份 DLL 留在 Mods。
- 不写活动存档、游戏程序集、主 Mod DLL、manifest、加载器或信任/批准记录。
- 不提供任意第三方类型自动适配，不保证游戏业务 API 变更后的兼容性。
- 玩家使用方法已加入 AGENT.md、README.md、README_EN.md 和工具 PLAYER-README.md。
- **尚未上传 GitHub**：当前机器 `git credential fill` 非交互检查未获得可用凭据，
  也没有 GitHub 发布连接器。可分发 zip 已本地构建并验证；等待发布权限，不能声称已上线。
- 无 push，版本仍 0.9.2。性能行为另在 bd44ce1，可独立回滚。

## 验证

- 430 项参考测试（新增具体错误/限额键/最大值/包装异常/提高上限遍历不变）。
- 10 项原版历史/齿轮 XML IO 夹具。
- 10 项玩家工具断言：默认不安装、helper 与生产表哈希相同、旧 helper 备份、
  主 DLL 不变、四个游戏模块不变、无效目录拒绝、Managed 输出拒绝。
- 完整静态表/游戏 Mono 往返/MVID/官方扫描通过，发布 DLL 仍仅 files。
- 最终干净提交验证、部署和 zip 来源见 out 中记录；玩家版工具必须单独发布。
