# 2026-11-27 拆分候选交接

后续工具箱新增独立常规提示/Shift详情总开关，详见兄弟仓库
research/2026-11-27-window-settings-handoff.md。QuickSL无需重建；下方工具箱旧哈希
被F0F2970728ECB81DAE85703D4AD2B75E50B9E8073766CC339BF75E2D826E4E22覆盖，
最新工具箱部署回执out/deploy-20261127-014917/deployment.json（工具箱仓库）。

## 范围

- QuickSL恢复拆分版，合入旧全功能版0.9.10提交b90ce4a2的F9热修复。
- 可选灯牌API延迟探测并缓存；建点准备异常一次记录并延迟重试，不毒化全局输入。
- 两个设置页固定顶部模糊搜索，按中英名称、分类、配置键匹配，隐藏空分类，提供清空/无结果状态。
- 输入搜索时屏蔽跨Mod业务快捷键，QuickSL节点选择器也遵循该门控。
- 工具箱默认无需Shift磁吸；标题栏双击左键置顶/取消，可选Shift+中键。
- 独立标题栏P按钮默认关闭，带本地化悬停提示；详情固定仍使用Shift+中键。
- 关闭窗口、禁用功能及回溯reset清理工具箱自有按钮、标题几何和置顶状态。
- 保留两仓库已有未提交修改；未提交、升版、推送或制作正式发布包。

## 验证

- QuickSL参考规则483（包含历史PreSplitToolRules，不代表现行工具箱业务覆盖）。
- 可选兼容规则22；工具箱规则63。
- 原生合约：QuickSL 56 hooks / 76 fields；工具箱54 hooks。
- QuickSL仅files，存在files类Pending，HardFlags/Error空；工具箱全部能力扫描项为空。
- 单装QuickSL、单装工具箱、双装及最终候选包游戏Mono离线夹具通过。
- 真实Proper Signboards专项通过；本轮不声称最终Mono运行加载了真实VBE。
- 构建零警告/错误；未启动游戏，不替代正式版及beta实机验收。

## 部署溯源

本机beta 1.0.0.16，MVID d927a6e4-ae7a-4e65-a5e2-9f9624c131f3。
QuickSL仍0.9.9，工具箱仍0.0.1，均为dirty development candidate。
未绕过正式打包脚本的clean检查；只组装开发候选四文件并经部署脚本扫描/备份。

- QuickSL候选：out/split-20261127-settings/ProbablyStolenTools
- QuickSL回执：out/full-release-deploy-20261127-014336/deployment.json
- 工具箱回执：D:/Program/GameProject/ShiftToolbox/out/deploy-20261127-014405/deployment.json
- 主DLL SHA256：2E7CFF419C3DB58913486680E53C87ADD0CEFC3AD9837C9FF98DEB44BF02C781
- 双版本helper SHA256：E72E5DB89DFC49434C756D98AC11965BF2405933D68F14112080583D95E436A3
- 工具箱DLL SHA256：533C692C187DE34704594B05E653FC8EAEC1957128DFC33E84C5375DC11DE3A7

helper合并当前beta单表与out/quicksl-0.9.10-universal/verify正式版单表，
保留两个exact profile，输出808448 bytes。普通构建只生成单beta表，不能覆盖部署合并表。
106个玩家存档目录文件校验未变；未导入/手改设置，未启动游戏。

## 实机验收清单

- QuickSL单装/双装F9建点及回溯，尤其正式版原故障路径。
- 工具箱单装F8和游戏Mod设置入口；双装共享面板。
- 中英、多词、无结果、清空搜索，滚动与窄窗口排版；输入时不误触业务快捷键。
- 默认无需Shift磁吸，开启要求Shift后只在按住时磁吸。
- 标题栏双击置顶/取消；改Shift+中键后手势互斥；交易平板、多窗置顶顺序。
- 独立按钮开关、标题布局、关闭窗口/回溯后的清理。
- 物品详情Shift+中键固定仍正常，不搬动物品；新设置重启后保留。

GitHub及发布交DS；本轮窗口UI尚未实机验收。
