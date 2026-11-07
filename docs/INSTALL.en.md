# Installation and upgrades

[简体中文](INSTALL.zh-CN.md) · [Home](../README.en.md)

**Deploy Soma first, then start Soma Finder.** This release is tested with **Soma 0.10.7 / Windows x64**. Other backend versions are not a full compatibility promise for this release.

## 1. Deploy the Soma backend first

1. Open the [official Soma 0.10.7 release](https://github.com/AwesomeDog/soma/releases/tag/v0.10.7).
2. Download [`soma-windows-x64.exe`](https://github.com/AwesomeDog/soma/releases/download/v0.10.7/soma-windows-x64.exe).
3. Put it in a permanent, writable folder, for example `%USERPROFILE%\Apps\Soma\`. You may rename it to `soma.exe`.
4. Check its version in PowerShell, adjusting the path as needed:

```powershell
$somaExe = "$env:USERPROFILE\Apps\Soma\soma.exe"
& $somaExe --version
```

The output should include `soma 0.10.7`. Soma is a command-line program; a console that immediately closes after double-clicking does not by itself mean installation failed.

Follow the [official 0.10.7 quick start](https://github.com/AwesomeDog/soma/tree/v0.10.7#quick-start) to prepare models and tools. Soma downloads required components during the first sync, which needs an internet connection and disk space for models, tools, and indexes. Existing users can reuse their deployment and indexes.

Downloading the backend EXE alone does not complete semantic indexing. Initial synchronization and model loading can take time.

## 2. Download Soma Finder

1. Download `SomaFinder-0.6.8-win-x64.zip` from [Releases](https://github.com/znplusW/SomaFinder/releases/latest).
2. Extract the **entire** folder to a writable location. Do not run it inside an archive manager or copy only the EXE.
3. Open `SomaFinder.exe`. The .NET runtime is included.

The top level contains the application, Chinese usage notes, and release notes. Supporting components, source, and documents are in subfolders. Keep `Components` intact.

## 3. Select Soma and add folders

1. Finder searches for Soma on first launch. If none is found, it offers a download link and manual selection.
2. If several copies are found, select your **0.10.7** executable and verify its full path.
3. In Settings, add root folders to the persistent indexing scope, configure include/exclude rules, and save.
4. Finder handles internal workspace grouping. Existing indexes can be reused; search-page folder checkboxes do not delete indexes.
5. Indexing follows your sync settings and current policies, or you can request a manual update. Keyword indexing may become useful before semantic indexing is ready; progress is shown in the status bar and Settings.

The defaults block automatic sync on battery, stop a running automatic sync when switching to battery, lower indexing priority, and defer automatic sync under low memory. Periodic sync must be enabled in Settings. A pause blocks new indexing work.

Optional model memory reclaim is disabled by default under Sync and Resources. Both conditions must hold: available physical RAM below 1 GiB continuously for two minutes, and no Soma tasks for at least 15 minutes. It never reclaims during indexing. The next semantic request reloads the required model and takes longer to start.

A second independent option, also disabled by default, releases index-only vision/OCR resources after one minute of confirmed indexing pause. Resuming cancels pending reclaim and later indexing reloads components on demand. Upgrades preserve existing selections.

## 4. Search

Choose folders in the left tree, enter keywords or a description, choose a mode, and search. Double-click a result to open its original file. Hybrid search keeps useful keyword results while semantic work completes or if semantic search is unavailable.

Try a small folder first. Rebuilding an index is not an installation or upgrade step; it clears and recreates the selected index scope.

## Upgrades and data locations

1. Exit the previous version from the tray.
2. Extract the new version to a fresh folder.
3. Copy the previous `data` folder beside the new `SomaFinder.exe`.
4. Start the new version and verify the selected backend path.

Finder preferences are beside the application in `data`. Soma retains its own configuration, model, and index locations. Finder manages scope through Soma's CLI/API and configuration; it does not modify backend source code.

For a stable tray identity across versions, the tray helper is copied to `%LOCALAPPDATA%\SomaFinder\TrayHost`. The self-contained .NET application may also extract native runtime files into a user cache. Portable here means no installer is required, not that every write stays in the extracted folder.

## Troubleshooting

| Symptom | Check |
|---|---|
| Soma is missing | Complete step 1 and select the actual EXE in Settings |
| Only keyword results appear | Check model downloads, vector progress, and backend errors |
| Automatic indexing does not start | Check pause, battery/memory policies, and the sync switch |
| A result cannot open | Verify the original file still exists and its drive is online |
| Different backend version | Prefer the tested 0.10.7; avoid competing copies accessing the same index |

More: [Usage](USAGE.en.md) · [Validation](VALIDATION.md)
