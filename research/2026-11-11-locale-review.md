# 多语言评审修正

## 范围

- 有效水质 0–3499 恢复沟渠水；只有范围外整数显示未知。
- 原生条目缺失、空白或读取失败：当前语言 → 英语表 → 英文文本。自定义液体仍保留第三方自有名称。
- locale/table/key 缓存上限 2048，切换语言清空；加载中或失败的临时回退不缓存。
- 保留非阻塞 GetTableAsync 完成检查。安装版 StringTableEntry 已有 SmartFormat，按来源 locale.Formatter 格式化；同步 GetLocalizedString 会 WaitForCompletion，不采用。无参数标注不会凭空获得 plural/choose 所需业务参数。
- Locale 日志准确标示 fallback=en-table-then-en-text。
- 设置用量文本按状态缓存；磁盘配额只计算自有、符合文件名规则且非链接的检查点。未知文件不计入也不删除，受保护节点仍计入。
- 固定 /20 依照用户要求暂不调整；不增加语言维护表，不修改原生语言设置。

## 验证

- Release 构建 0 警告 / 0 错误；94 项离线测试通过。
- 新增英语表优先级、语言/表/键隔离、异步重试、低水质边界及用量字符串复用回归。
- 离线用例不等同于 Unity 实际语言切换验收；游戏不自动启动。
- 本轮独立部署记录见 out/full-release-deploy-* 中 deployment.json（含 DLL 哈希与存档校验）。
