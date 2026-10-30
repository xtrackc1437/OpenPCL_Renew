# PCL2 开源修改版

[上游PCL仓库](https://github.com/Meloong-Git/PCL)

本项目为基于上游PCL仓库，修改补全原开源版的部分重要功能而成的修改版本。<br/>
使用 PCL 的大部分源代码，包括 UI 库、动画模块、下载模块、Minecraft 启动模块等，改造开源高可用版本 OpenPCL。<br/>
本项目不与PCL CE相冲突。OpenPCL只对原版进行极少的改动，不会加入任何完全脱离原版的更新。<br/>

### 项目主要修改点

* 解除原版PCL对Minecraft本体的下载限制，移除 `RestrictMcDownload` 开关及其控制的所有下载限制逻辑。

* 将CurseForge和正版登录功能所需的API配置开放，可在 设置 → 其他 → 网络 API 中自行配置，修改后重启启动器生效。

* 尽量恢复原版所有“百宝箱”内功能，删除与赞助相关的所有功能和提示。

* 精简非必要入口：移除“反馈”“新功能投票”页面、主题更换功能，以及识别码、解锁码相关的界面入口。

* 隐藏自动更新、在线公告与遥测相关的界面，移除匿名遥测上报功能；界面左上角标题栏默认显示 `OpenPCL`。

* 重新实现启动器更新：从本仓库的 GitHub Releases 与 GitHub Actions 构建记录检测新版本，提供 正式版 / 测试版 / 自动构建版 三个通道，可在 设置 → 其他 → 启动器更新 中切换与设置检查频率。

* 若PCL仓库发布功能性更新，本项目修改适配后跟进。

### 获取可执行文件

推荐下载 [Releases](https://github.com/xtrackc1437/OpenPCL_Renew/releases) 中的版本；也可以在 [Github Actions](https://github.com/xtrackc1437/OpenPCL_Renew/actions) 中获取自动构建的二进制文件。

### 发布新版本

1. 修改 `Plain Craft Launcher 2/Modules/Base/ModBase.vb` 中的 `VersionBaseName` 与 `VersionCode`。
2. 提交后打与版本号一致的标签（例如 `v2.99.1.1` 或 `2.99.1.1`）。
3. `Release` 工作流会校验标签与版本号是否一致，构建并发布带 exe 资产的 Release。