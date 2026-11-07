# Validation / 验证范围

Target application: Windows 11 x64. Tested backend compatibility: Soma 0.10.7 Windows x64. Current automated release builds run on GitHub-hosted Windows Server 2025, not physical Windows 11 hardware.

Public release workflow: [Actions](https://github.com/znplusW/SomaFinder/actions/workflows/release.yml). Reports are tied to the tested commit, include failures and are retained as workflow artifacts. Release packages include a build/test summary under Documents.

The release candidate workflow runs core frontend regression, current-window UI, layout/scheduling/discovery checks, floating-window foreground/Z-order checks, waterfall resize regression, preview/thumbnail tests, and a package/license check. Version 0.6.7 adds model-memory policy/settings/task coordination, search cancellation, native Soma 0.10.7 identity/lifecycle integration, and a real pinned llama.cpp b10068 CPU runtime with a SHA256-verified tinyllamas GGUF. All indexing/rebuild fixtures use isolated cloud data. These are not full production-model/VHD/B390 integration or physical GPU memory measurements.

The production thresholds remain two continuous minutes of low available RAM and 15 minutes of task idleness. Boundary scenarios advance an injected monotonic clock while retaining those constants. The HTTP task-coordination fixtures spawn real processes and writer locks; the separate real-runtime check verifies model-worker exit and automatic reload. Available-memory recovery and real B390 residency still need hardware validation.

WPF routed input, native HWND queries and synthetic fixture screenshots are identified as such. They do not prove physical USB hotplug, a real touchscreen, multi-monitor hotplug, all graphics drivers, battery life or Windows 11 25H2 hardware acceptance. Synthetic screenshots demonstrate the UI, not semantic search accuracy.

中文：0.6.7 候选在 GitHub Windows Server 2025 上验证前端、打包、内存回收策略、设置和任务并发，并检查原生 Soma 0.10.7 的进程归属及真实 llama.cpp CPU 模型卸载、按需加载。2 分钟和 15 分钟的生产常量保持不变，边界检查使用可推进的单调时钟。未测本机 B390 实际共享显存释放量，也不代表完整生产模型或 VHD 场景通过。报告绑定提交，保留失败；自动输入和窗口几何不冒充实物设备验收。
