# GitHub公开交付（2026-11-10）

公开仓库：[lki-H/dungeon-contract](https://github.com/lki-H/dungeon-contract)。默认分支为 `main`，代码许可证MIT；资源与依赖继续遵守各自条款。本次发布的是允许清单内的源码与展示资料，没有把私密目录、数据库或现有Windows玩家包作为发行附件上传。

## 已交付内容

- 中文README、架构图、组队入局／权威事实重试／结算返厅三条时序，以及[可靠性说明](reliability-and-settlement.md)。
- 四张真实截图、2分29秒Web控制流程录屏及[演示脚本](demo-walkthrough.md)，每份媒体标注实际来源和验证边界。
- [浏览器验证](browser-validation.md)、后端与完整游戏启动教程、[面试讲解提纲](interview-notes.md)。
- 审定源码、Schema、迁移、合法资源和Windows／Linux工具、前端及后端CI工作流。

`source-export.json`记录每次导出时的文件清单和状态，不是发布后会自动更新的仓库运行状态。其 `published:false`、`git_initialized:false` 描述导出发生时点；实际公开提交和CI以GitHub为准。公开源与本机开发目录相互独立，没有伪造旧的逐日提交历史。

## 发布前后检查

真实浏览器完成窄范围操作和权限验证，临时库、容器与测试进程已结束。公开图片和录屏中没有明文密码、token或真实玩家数据。临时凭据和开发机工具均在公开源码允许清单外，没有进入提交或Git历史；公开审查以实际提交文件为对象。

实际暂存文件与导出清单逐字节核对；公开clone及GitHub源码ZIP也验证相同清单。SQL／配置／契约保留原字节。Git的文本规范化仅在独立导出目录发生，已经用Git索引、双clone和ZIP回归检查。

首提交 `c6075a4` 的Windows工具检查暴露runner短路径别名问题；后继 `46a2202` 只规范测试夹具路径，并增加模拟实际命中断言。原始失败保留，不删测试、不修改生产校验。Linux工具与前端均已运行；后端工作流包含单元／vet、旧版存储race、当前v8存储race和服务镜像构建。各次结果只归属实际执行的提交，后继文档提交仍由同一工作流检查。

## 当前检查入口

[查看实际GitHub Actions结果](https://github.com/lki-H/dungeon-contract/actions/workflows/verify.yml)。只以目标提交的 `success` 作为该次工作流通过，不能将前一提交成绩迁入。公开CI不要求本机Unity许可证，因此没有将Unity源码构建或真人联机伪装成GitHub Actions任务。

本次未发布预编译游戏Release、没有改变游戏协议或宣布商业生产能力。用户取消的广泛性能和完整通关复验保持取消。[当前范围](current-status.md)继续区分真实验证、模型、历史、真人反馈与取消项。
