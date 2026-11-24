# 固定详情在物品栏定位异常

## 用户报告

Shift + 中键在固定物品栏内会把详情定位到左上角，储物容器内正常。

## 修复

- 原 Place 用 Owner.rectTransform.parent 的 rect 作为可视边界。原版固定物品栏使用 windowRoot 中间节点，而可移动窗口可经 EnsureFloatingDock 移入 DockRoot；直接父节点不能统一视作屏幕边界。
- 改用窗口所在 Canvas 的 rootCanvas RectTransform，保留 RenderHandler.windowUICanvas 作为找不到所属 Canvas 时的回退。
- 物品、详情实际边界统一通过 NativeUi.Bounds 转入同一坐标系，尺寸不再混用原生 widthPixels 与 Canvas 单位。
- 按实际左上角差量移动，避免假定提示框 pivot；仍保留原窗口父级、层级、关闭清理、刷新和输入门控。
- 非法或尚未形成尺寸的 Canvas 暂不定位，下一 Tick 重试；无新增日志、权限、挂点、静态字段布局或公告重播。

## 验证与部署

- Release 构建成功；469 项参考测试（新增 Canvas 空间与缩放尺寸布局用例）、22 项可选兼容测试通过。
- 游戏 Mono 离线原版与实际 VBE DLL 夹具通过；最终 ReleaseCompatibility 扫描通过。
- 离线布局测试不能模拟 Unity 的真实 Transform、Canvas、渲染及输入；实际画面需玩家验收，不能称已实机修复确认。
- 部署：`out/full-release-deploy-20261125-062239/deployment.json`。
- 主 DLL：`015C2016E86BA0CD32524B6DFB368874142B5DDFC401C308B33D2CE4FB182FAB`。
- 双版本 helper：`6129A2D3A826044E7BDEBB0E3C04CDC7C3BEC70ADB80C4A96FE36A05D9892B23`。
- 91 个存档目录文件校验不变；未导入设置、未启动游戏、未提交、未升版、未推送。此前 DS 审查不覆盖本增量。

## 实机复测

- 物品栏内不同位置的容器：固定提示应紧随物品，屏幕边缘自动换侧/限位，重复 Shift 中键取消。
- 储物容器内固定、拖动窗口、物品移入另一容器后的跟随，以及关闭外层容器后的清理。
- 若有 UI 缩放或停靠区域变化，确认仍跟随物品而非屏幕左上角。
