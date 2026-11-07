# Changelog / 更新记录

## 0.6.8 — 2026-11-08

- 新增独立、默认关闭的“索引确认暂停 1 分钟后，释放搜索不需要的模型”选项：回收托管的 VL、OCR 整理模型和 OCR 服务，保留 EMBED、EXPAND、RERANK。ASR 随索引进程退出。
- 计时从确认索引停止后开始；每次卸载前重新核对暂停状态、任务和写入锁。恢复或暂停到期会取消待执行回收，新的暂停重新计时。已经开始的单次卸载完成后才放行新索引。
- 低内存回收的阈值从 2 GiB 改为严格低于 1 GiB，仍需连续 2 分钟低内存和 15 分钟无任务空闲。
- Add a separate opt-in to release index-only models after one minute of confirmed indexing pause, preserving search models. Verify pause and writer state before every release; resume cancels pending work and resets the timer.
- Restore index-only models on demand during later indexing. ASR restarts per file; model files and completed indexes are retained. Initial extraction after reload may take longer.
- Lower the optional low-memory threshold to strictly below 1 GiB, retaining the two-minute pressure and fifteen-minute idle requirements.

## 0.6.7 — 2026-11-08

- 新增默认关闭的模型内存回收选项：可用物理内存低于 2 GiB 持续 2 分钟，且无任务并已空闲 15 分钟，才卸载托管的空闲模型。
- 索引、排队请求、取消界面等待后仍在执行的搜索均阻止回收；新任务与卸载串行协调，下一次实际请求按需加载。
- 使用 Windows 原生进程快照、路径和命令行查询，替代可能在冷启动或高负载下超时的 PowerShell/CIM 进程盘点；保留路径、创建时间与工作区身份核对。
- Add opt-in model memory reclaim after two minutes of RAM pressure and 15 minutes of task idleness. Never reclaim during indexing; preserve pending work and reload models on demand.
- Replace PowerShell/CIM inventory with native Windows process identity queries to avoid cold-start inventory timeouts.
- Existing settings default to reclaim disabled. Model files, indexed content and index databases are retained.

## 0.6.6 — 2026-11-03

- 修复图片内容预览忽略 EXIF 方向标记的问题，覆盖旋转与镜像方向。
- 首次预览、放大后的高清加载和图片缩略图使用统一方向处理；按纠正后的宽高布局。
- 增加 8 种 EXIF 方向的合成 JPEG 像素、缩略图、高清升级、文件句柄与显示比例检查。
- Correct EXIF rotation and mirroring in image previews, high-resolution upgrades and thumbnails, using oriented dimensions for layout.
- Add synthetic JPEG checks for all eight orientations, pixel positions, thumbnail workers, quality upgrades and file-handle release.
- Upgrade preserves existing settings and indexes; original images are read without modification. / 升级保留设置和索引，不改写原图片。

## 0.6.5 — 2026-11-03

First public distribution of Soma Finder / 首个公开发行版。

- Chinese/English product introductions, concise feature lists, installation guides and upgrade instructions.
- Explicit prerequisite and tested backend: Soma 0.10.7 Windows x64.
- GPLv3 original source with separate commercial licensing available; in-app licensing entry and third-party notices.
- Clean source snapshot and public history; portable packaging, checksum and cloud verification provenance.
- Includes existing natural-language search, keyword fallback, root/type filters, three result views, previews, touch controls, multi-window/tray and indexing policies.
- Includes fixes for waterfall reflow flicker and detached summary/preview windows being covered by their parent.
- Keeps preview process startup, monitoring and response parsing off the UI thread to improve cold-start responsiveness / 缩略图子进程启动、监控和结果解析移出界面线程。

Public-source and release material changes do not modify the Soma backend or migrate/clear existing indexes.
