# 第六步：低频阶段计时与类型摘要

## 日志

保持每次成功捕获/恢复各一行摘要，不引入逐对象或每帧日志。

- CAPTURE 原 access/walk/items/verify 保留；补 prepareHistory（包括闭店旧节点移除与几何诊断）、history（入列/分支淘汰/释放）、retain（保活集合重建）、finish（状态/UI/可选摘要格式化）。各阶段之和等于 total/captureMs。
- RESTORE：validate、census、preflight（目标全量读取和新增视图枚举）、write（回调取消及第一次图写入）、refresh（原生视觉刷新）、reconcile（第一次完整校验、必要的第二次写入、几何及最终变换）、finalVerify、cleanup、release。各阶段之和等于 total。
- RESTORE OK 在普查释放和 timeScale/Restoring 恢复后输出；diff 为实际最终校验结果，不省略校验。即使释放意外抛错，finally 仍恢复 timeScale/Restoring。
- CAPTURE/RESTORE 失败附 phase 与 elapsedMs。失败耗时不等同于成功各阶段，恢复失败日志不含随后 finally 释放时间。
- total 不含入口拒绝门控与摘要日志自身 I/O；捕获还不含摘要之后的 TraceBargain，恢复包含它。

仅打开 Logging.Verbose 时，摘要追加 typesBySlots。只读访问器已有 retained/storage，最多登记 128 种类型、输出槽数前六类，其余汇总为 otherTypes。按类型记录 objects/slots/arrayCapacity/maxArray；不读业务属性、不再次描述、不登记值令牌，不逐对象记日志。

arrayCapacity 是数组全长，不是字典/列表 Count；统计非实际内存估算。类型名截到 96 字符，不输出泛型程序集长名。关闭详细日志时完全跳过类型统计遍历。

## 验证

Release 构建无警告/错误；104 项离线测试（新增数组容量/槽数统计、重复汇总不改变值表回归）。实际游戏程序集的对象图普查/完整捕获覆盖比较、恢复往返、布局缓存排除、55 挂点/73 字段、官方扫描全部通过，HardFlags 为空。

没有启动游戏；实际新增阶段的 Unity 耗时需下一次游玩产生日志，不能把合成基准冒充游戏收益。

## 停止边界

本轮完成多语言评审、对象发现预算、恢复前普查和阶段诊断，各轮分别提交与部署。之前的标量比较、数组预算、同形描述复用仍保留。

到此停在语义化后端开始之前：没有缩小根、排除原生保存缓存、裁剪集合容量、修改节点时机、跨帧读取、后台并行读图、改变视觉恢复顺序，也没有调用原生 SaveGame。

后续先用同一存档验证售出/移动/旋转/前柜台/嵌套/拆分合并、跨顾客、重复回溯、未来节点往返和分支淘汰，再按新阶段计时决定是否需要独立设计语义记录后端。当前不承诺重构收益。

## 本轮部署

各备份均位于 out/，deployment.json 记录构建哈希、存档和其他 mod 校验；每轮 77 个存档目录文件不变、不导入设置、不启动游戏。

| 轮次 | 备份目录 |
| --- | --- |
| 多语言评审 | full-release-deploy-20261111-011500 |
| 第四步 | full-release-deploy-20261111-011651 |
| 第五步 | full-release-deploy-20261111-012022 |
| 第六步（最终） | full-release-deploy-20261111-012346 |

最终 DLL SHA256：`BC08B3FA67BEC25E15B46A9518DE121CCFBC1BB887B9BB3EBB46DCCF066A714D`。版本保持 0.8.13，未推送。
