# Soma Finder

**Describe a file. Find it on your PC.**

[简体中文](README.md) · [Download](https://github.com/znplusW/SomaFinder/releases/latest) · [Installation guide](docs/INSTALL.en.md) · [Licensing](docs/LICENSING.md)

![Windows 11 x64](https://img.shields.io/badge/Windows_11-x64-0078D4)
![Version](https://img.shields.io/badge/version-0.6.8-178E8B)
![Soma](https://img.shields.io/badge/tested_Soma-0.10.7-555555)
![License](https://img.shields.io/badge/license-GPLv3_%2F_Commercial-blue)

Soma Finder is a local file-search desktop frontend for **Windows 11 x64**, powered by [Soma](https://github.com/AwesomeDog/soma). Choose folders, search with keywords or natural language, read summaries, preview content, and open the original files in their Windows applications.

> **Deploy Soma first.** The tested backend is **Soma 0.10.7 for Windows x64**. Soma, models, and personal indexes are not included. The application UI is currently Chinese; documentation is available in Chinese and English.

## Features at a glance

- **Natural-language search** — hybrid, keyword, and semantic modes; keyword results can appear before hybrid search completes.
- **Folder and type filters** — a checkable folder tree, file-type presets, and adjustable search breadth.
- **Three result views** — details, summary cards, and a zoomable masonry thumbnail view.
- **Summaries and previews** — match highlighting, image zoom, and matching pages or video frames when reliable locations are available.
- **Open original files** — double-click to open, reveal in Explorer, or copy a path.
- **Multiple windows** — a global hotkey, detachable summary/preview panes, resizable layouts, and light/dark themes.
- **Indexing controls** — folder inclusion/exclusion, manual/scheduled updates, pause controls, progress, and power/resource policies.
- **Optional model memory reclaim** — unload managed idle models only after available RAM stays below 1 GiB for two minutes and Soma has no tasks for 15 minutes; never during indexing, with demand loading on the next request.
- **Paused indexing model reclaim** — a separate opt-in releases vision/OCR resources after one minute of confirmed indexing pause, preserving search models; resuming cancels pending reclaim and later indexing reloads resources on demand.

![Soma Finder search interface](https://github.com/znplusW/SomaFinder/releases/download/v0.6.5/search-summary.png)

*The screenshot uses synthetic demonstration files, not personal data or a search-quality benchmark.*

## Get started

1. [Deploy Soma 0.10.7 and prepare its models](docs/INSTALL.en.md#1-deploy-the-soma-backend-first).
2. Download `SomaFinder-0.6.8-win-x64.zip` from [Releases](https://github.com/znplusW/SomaFinder/releases/latest), extract the entire folder, and open `SomaFinder.exe`.
3. Select the Soma executable in Settings, add folders to the index, and wait for indexing before searching.

Try descriptions such as “battery-life test reports” or “documents explaining API authentication.” Semantic results depend on the backend models, file contents, and indexing state.

## Things to know

- Folder checkboxes on the search page control the current search/filter. Settings controls the persistent indexing scope.
- Search breadth is a candidate budget, initially **50**. File-type filters and sorting operate on returned candidates; this is not a complete filename index of every disk.
- Initial indexing and model loading take time. If semantic search is unavailable, hybrid search keeps usable keyword results and explains the state.
- Document thumbnails use a matching page/excerpt when it can be located, otherwise the first page/opening. Video matching frames require reliable timing; other videos use a cover frame.
- Closing a search window normally leaves the app in the tray. Tray Exit also stops Soma processes managed by Finder, with confirmation if indexing is active.

More: [Installation and upgrades](docs/INSTALL.en.md) · [Usage](docs/USAGE.en.md) · [Changelog](CHANGELOG.md) · [Validation](docs/VALIDATION.md)

## Build

With the .NET 10 SDK on Windows, run from PowerShell 7:

```powershell
pwsh -File ./构建.ps1
```

Output: `artifacts/portable`. Release builds include the .NET runtime, so end users do not need to install it. Tests run on GitHub-hosted Windows; see [CI](ci/README.md).

## License

Original Soma Finder code is offered under **GPLv3 (GPL-3.0-only), with separate commercial licensing available**.

GPLv3 permits use, modification, and redistribution, including commercial use, subject to its terms. For closed-source distribution of a covered derivative without the GPLv3 source-provision requirements, obtain a separate commercial agreement from **[znplusW](https://github.com/znplusW)** first. Private internal modification/use does not by itself require public source release.

No commercial license is automatically granted. It does not replace third-party licenses. See [LICENSE](LICENSE), [licensing terms](docs/LICENSING.md), and [third-party notices](THIRD-PARTY-NOTICES.md).

This is an independent desktop frontend. Soma's backend source is not modified; Soma and its models retain their respective licenses.
