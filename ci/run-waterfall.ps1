$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:RUNNER_OS -ne 'Windows'){throw 'Hosted Windows only'}
New-Item -ItemType Directory -Force reports|Out-Null
$portable=[IO.Path]::GetFullPath('artifacts/portable')
$root=Join-Path $env:RUNNER_TEMP ('SomaFinder-cloud-package-'+[Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $root|Out-Null
$copy=Join-Path $root '中文 Portable App';Copy-Item -LiteralPath $portable -Destination $copy -Recurse
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR=Join-Path $root 'runtime-cache';$env:DOTNET_ROOT=Join-Path $root 'no-installed-dotnet';$env:DOTNET_MULTILEVEL_LOOKUP='0'
$report=[IO.Path]::GetFullPath('reports/waterfall.json')
$p=Start-Process -FilePath (Join-Path $copy 'SomaFinder.exe') -ArgumentList @('--verify-waterfall',('--report="'+$report+'"')) -WindowStyle Normal -PassThru
if(-not $p.WaitForExit(240000)){$p.Kill();throw 'Waterfall fixture timed out'}
if(-not(Test-Path -LiteralPath $report)){throw 'Missing waterfall report'}
$result=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json;$result.tests|Format-Table name,passed
if($p.ExitCode -ne 0 -or $result.failures -ne 0){throw 'Waterfall targeted checks failed'}
$expected=@('SomaFinder.exe','使用说明.txt','更新说明.txt');if(@(Compare-Object $expected @(Get-ChildItem -LiteralPath $copy -File|Select-Object -ExpandProperty Name)).Count){throw 'Release root changed'}
@{commit=$env:GITHUB_SHA;rootFiles=$expected;relocatedUnicodeAndSpaces=$true;installedDotnetRootNotUsed=$true;version=(Get-Item -LiteralPath (Join-Path $copy 'SomaFinder.exe')).VersionInfo.FileVersion}|ConvertTo-Json|Set-Content reports/release-layout.json
