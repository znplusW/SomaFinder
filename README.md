# Soma Finder

**用自然语言，找到电脑里的文件。**

[English](README.en.md) · [下载绿色版](https://github.com/znplusW/SomaFinder/releases/latest) · [安装指南](docs/INSTALL.zh-CN.md) · [许可与商业授权](docs/LICENSING.md)

![Windows 11 x64](https://img.shields.io/badge/Windows_11-x64-0078D4)
![Version](https://img.shields.io/badge/version-0.6.8-178E8B)
![Soma](https://img.shields.io/badge/tested_Soma-0.10.7-555555)
![License](https://img.shields.io/badge/license-GPLv3_%2F_Commercial-blue)

Soma Finder 是面向 **Windows 11 x64** 的本地文件搜索桌面前端。它使用 [Soma](https://github.com/AwesomeDog/soma) 进行关键词与语义检索，提供接近文件管理器的操作方式：选择目录、输入描述、查看摘要和预览，双击打开原文件。

> **先部署 Soma 后端，再使用 Soma Finder。** 当前测试适配 **Soma 0.10.7（Windows x64）**。发行包不包含 Soma、模型或你的索引。当前软件界面为中文，项目文档提供中英文版本。

## 核心功能

- **自然语言搜索**：混合、关键词、语义三种模式，混合搜索可先显示关键词结果。
- **范围与类型筛选**：勾选目录树、快速选择文件类型，调整搜索广度。
- **三种结果视图**：详细列表、摘要列表、可缩放的瀑布流缩略图。
- **摘要与内容预览**：匹配词标记、图片缩放；有可靠位置时展示匹配页或视频帧。
- **直接打开文件**：双击打开原文件，也可打开所在目录或复制路径。
- **多窗口工作**：全局快捷键、摘要／预览独立窗口、可调面板及浅深主题。
- **图形化索引管理**：添加／排除目录、手动／定时更新、暂停、进度与电源资源策略。
- **可选模型内存回收**：可用内存连续低于 1 GiB 达 2 分钟，且无任务并空闲 15 分钟后回收托管模型；索引期间不回收，下次使用按需加载。
- **暂停索引后释放模型**：独立选项，确认索引停止并持续暂停 1 分钟后回收 VL 和 OCR 组件；保留搜索模型，恢复索引即取消待执行回收并按需重新加载。

![Soma Finder 搜索界面](https://github.com/znplusW/SomaFinder/releases/download/v0.6.5/search-summary.png)

*界面使用云端合成资料演示，不是实际用户文件或真实检索评测。*

## 三步开始

1. [部署 Soma 0.10.7 并准备模型](docs/INSTALL.zh-CN.md#1-先部署-soma-后端)。
2. 从 [Releases](https://github.com/znplusW/SomaFinder/releases/latest) 下载 `SomaFinder-0.6.8-win-x64.zip`，完整解压后打开 `SomaFinder.exe`。
3. 在设置中选择 Soma 后端、添加索引目录，等待索引就绪，再输入你想找的内容。

例如：“关于电池续航的测试报告”“海边旅行的照片”“解释接口认证流程的文档”。语义结果取决于 Soma 模型、文件内容及索引完成情况。

## 使用要点

- **搜索页目录勾选**用于当前搜索范围和结果过滤；**设置中的索引范围**决定长期收录哪些目录。
- “搜索广度”控制返回候选数量，初始值为 **50**。类型过滤、排序只作用于已返回的候选，不等同于全盘文件名索引。
- 首次索引／模型加载可能较慢；语义索引未就绪时，混合搜索会保留可用的关键词结果并提示状态。
- 文档缩略图优先使用可定位的匹配页／片段，否则回退到首页／开头。视频只有可靠时间映射时才显示匹配帧，否则使用封面。
- 默认点击 X 关闭搜索窗口并保留托盘；从托盘退出会一并关闭 Finder 管理的 Soma 进程，索引进行中会先确认。

更多：[安装与升级](docs/INSTALL.zh-CN.md) · [使用说明](docs/USAGE.zh-CN.md) · [更新记录](CHANGELOG.md) · [验证范围](docs/VALIDATION.md)

## 构建与验证

使用 .NET 10 SDK，在 Windows PowerShell 7 中执行：

```powershell
pwsh -File ./构建.ps1
```

输出位于 `artifacts/portable`。发布包自带 .NET 运行时，用户无需另装 .NET。开发验证在 GitHub 托管 Windows 上运行，见 [CI 说明](ci/README.md)。

## 许可

Soma Finder 原创代码采用 **GPLv3（GPL-3.0-only）+ 可另行协商的商业许可**。

遵守 GPLv3 可以使用、修改和再分发，包括商业使用；若希望以不遵守 GPLv3 源码提供要求的闭源方式分发受其约束的衍生作品，请先向维护者 **[znplusW](https://github.com/znplusW)** 申请商业授权。内部私下修改和使用不因其本身要求公开源码。

商业许可须另行达成书面协议，不会自动授予，也不覆盖第三方组件。详见 [LICENSE](LICENSE)、[许可说明](docs/LICENSING.md) 和 [第三方声明](THIRD-PARTY-NOTICES.md)。

Soma Finder 是独立的 Soma 桌面前端项目，不修改 Soma 后端源码。Soma 本体及相关模型仍按各自许可证使用。
