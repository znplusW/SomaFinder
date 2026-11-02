# Project guidelines

- This is the public Soma Finder distribution repository. Publish only source, synthetic fixtures, redistributable components and public documentation.
- Keep credentials, personal files, settings, indexes, model files, local logs, build intermediates and private Git history out of commits and releases.
- Run application and integration tests in GitHub-hosted Windows. Choose targeted checks for small iterations and appropriate broader regression for releases.
- Destructive/rebuild tests must use an isolated cloud fixture and never personal indexes.
- Distinguish native cloud-window/input checks from physical hardware acceptance. Attach results to the tested commit; preserve failures.
- Original code is GPL-3.0-only, with separate commercial licensing available. Do not relicense third-party code or merge external contributions without the required rights.
- Maintain the public Chinese and English introductions and install guides together. Tested backend: Soma 0.10.7 Windows x64.
- Release root files: SomaFinder.exe, 使用说明.txt, 更新说明.txt. Keep other material in subdirectories.
