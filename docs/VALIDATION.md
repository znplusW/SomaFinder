# Validation / 验证范围

Target application: Windows 11 x64. Tested backend compatibility: Soma 0.10.7 Windows x64. Current automated release builds run on GitHub-hosted Windows Server 2025, not physical Windows 11 hardware.

Public release workflow: [Actions](https://github.com/znplusW/SomaFinder/actions/workflows/release.yml). Reports are tied to the tested commit, include failures and are retained as workflow artifacts. Release packages include a build/test summary under Documents.

The first public release runs the core frontend regression, current-window UI, layout/scheduling/discovery checks, floating-window foreground/Z-order checks, waterfall resize regression, preview/thumbnail tests, and a public-package/license check. All indexing/rebuild fixtures, when explicitly selected, must use isolated cloud data. This release workflow does not re-run the full backend/model/VHD integration suite; previous backend compatibility evidence is historical.

WPF routed input, native HWND queries and synthetic fixture screenshots are identified as such. They do not prove physical USB hotplug, a real touchscreen, multi-monitor hotplug, all graphics drivers, battery life or Windows 11 25H2 hardware acceptance. Synthetic screenshots demonstrate the UI, not semantic search accuracy.

中文：本轮验证公开发行包及前端回归，构建环境为 GitHub Windows Server 2025。后端适配版本为 Soma 0.10.7；本轮不重新运行所有模型／后端／VHD 场景，历史适配记录不当成本轮新通过。报告绑定提交，保留失败；自动输入和窗口几何不冒充实物设备验收。
