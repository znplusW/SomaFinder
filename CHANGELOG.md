# Changelog / 更新记录

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
