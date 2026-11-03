# Cloud verification

`release.yml` builds a self-contained Windows x64 candidate, runs the frontend regression and published-binary checks, and prepares release assets only after the selected checks pass. It runs by manual dispatch or on a `candidate/**` branch push; it never publishes a release automatically. Full candidates include the EXIF orientation and image-preview interaction checks.

Tests use GitHub-hosted Windows with isolated/synthetic data. Never run UI, backend or index fixtures on a user's machine by default. Never point a rebuild test at personal data. See [validation scope](../docs/VALIDATION.md).

Required build environment: .NET 10 SDK (CI pins 10.0.403), PowerShell 7, Windows SDK and the Visual C++ build tools needed by the NativeAOT tray helper. The regular WPF application is self-contained; it is not NativeAOT.

Local source compilation is allowed when requested; test execution remains in the cloud. `构建.ps1` writes to a fresh `artifacts/portable`. Sources/licenses and dependency hashes are shipped alongside the binary package. Media sources/build recipes are offered from the same release page.

The repository has no user settings, indexes, model files, local runtime logs, or private Git history. Do not add them to commits or packages.
