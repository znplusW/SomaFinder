# Third-party components

Original Soma Finder code is GPL-3.0-only with separate commercial licensing available. That option does not relicense third-party components.

| Component | Pinned version | License/source |
|---|---|---|
| .NET / Windows Desktop | Runtime selected by .NET SDK 10.0.403 | MIT and bundled notices in LICENSES |
| YamlDotNet | 16.3.0 | MIT, LICENSES/YamlDotNet.txt |
| PDFium | chromium/8125, Windows x64 | BSD-style and bundled third-party notices |
| FFmpeg / FFprobe | Jellyfin 7.1.4-3 portable win64-clang-gpl | GPL build, license texts and source access below |
| Soma backend and models | Tested Soma 0.10.7, separately deployed | Not included; their own licenses apply |

PDFium is obtained from [pdfium-binaries chromium/8125](https://github.com/bblanchon/pdfium-binaries/tree/chromium/8125). All notices supplied in that binary archive are retained in the portable package.

FFmpeg is called as a separate local process; Finder does not link its C# code to FFmpeg libraries. The exact [upstream binary](https://github.com/jellyfin/jellyfin-ffmpeg/releases/tag/v7.1.4-3) is pinned by SHA256 in ci/prepare-media.ps1. The public release page also provides the matching FFmpeg source archive, including build recipes and patches. Dependencies and pinned revisions are indexed in LICENSES/FFmpeg/dependency-sources.json and in the upstream builder/scripts.d files. Source access may use the listed upstream servers, as permitted by GPLv3 section 6(d); those sources remain under their own licenses.

Source material: [release assets](https://github.com/znplusW/SomaFinder/releases/tag/v0.6.5), [matching FFmpeg source](https://github.com/jellyfin/jellyfin-ffmpeg/tree/v7.1.4-3), [builder instructions](https://github.com/jellyfin/jellyfin-ffmpeg/blob/v7.1.4-3/builder/README.md). This is an unmodified upstream build. No commercial license from this project waives FFmpeg's license obligations.

中文：发行包保留第三方版权和许可证。FFmpeg 为单独调用的 GPL 工具，公开发布页提供对应源码归档及上游构建／依赖资料；本项目的商业授权不替代其许可证。Soma 本体、模型和用户索引不包含在发行包中。
