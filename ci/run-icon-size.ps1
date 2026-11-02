$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:RUNNER_OS -ne 'Windows'){throw 'Hosted Windows only'}
New-Item -ItemType Directory -Force reports|Out-Null
$portable=[IO.Path]::GetFullPath('artifacts/portable')
$expected=@('SomaFinder.exe','使用说明.txt','更新说明.txt')
if(@(Compare-Object $expected @(Get-ChildItem -LiteralPath $portable -File|Select-Object -ExpandProperty Name)).Count){throw 'Unexpected root release files'}
$root=Join-Path $env:RUNNER_TEMP ('SomaFinder-cloud-packaged-'+[Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $root|Out-Null
$copy=Join-Path $root '带空格的绿色目录 Portable App';Copy-Item -LiteralPath $portable -Destination $copy -Recurse
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR=Join-Path $root 'native-cache';$env:DOTNET_ROOT=Join-Path $root 'no-installed-dotnet';$env:DOTNET_MULTILEVEL_LOOKUP='0'
$report=[IO.Path]::GetFullPath('reports/icon-size.json')
$process=Start-Process -FilePath (Join-Path $copy 'SomaFinder.exe') -ArgumentList @('--verify-icon-size',('--report="'+$report+'"')) -PassThru -WindowStyle Normal
if(-not $process.WaitForExit(90000)){$process.Kill();throw 'Icon-size fixture timed out'}
if(-not(Test-Path -LiteralPath $report)){throw 'Missing single-file startup report'}
$result=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json;$result.tests|Format-Table name,passed
@{commit=$env:GITHUB_SHA;rootFiles=$expected;relocatedUnicodeAndSpaces=$true;installedDotnetRootNotUsed=$true;nativeExtractionIsolated=$true;version=(Get-Item -LiteralPath (Join-Path $copy 'SomaFinder.exe')).VersionInfo.FileVersion}|ConvertTo-Json|Set-Content reports/release-layout.json
if($process.ExitCode -ne 0 -or $result.failures -ne 0){throw 'Icon-size/packaging targeted checks failed'}
