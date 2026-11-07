param([string]$Dotnet = 'dotnet', [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts\portable'), [string]$Version='0.6.7')
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$project = Join-Path $PSScriptRoot 'src\SomaFinder\SomaFinder.csproj'
& $Dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None ("-p:Version="+$Version) --disable-build-servers -p:UseSharedCompilation=false -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw '构建失败。需要 .NET 10 SDK。' }
$trayProject=Join-Path $PSScriptRoot 'src/TrayHost/SomaFinder.TrayHost.csproj'
$trayOutput=Join-Path $OutputDirectory 'Components'
& $Dotnet publish $trayProject -c Release -r win-x64 --self-contained true -p:DebugType=None ("-p:Version="+$Version) --disable-build-servers -p:UseSharedCompilation=false -o $trayOutput
if ($LASTEXITCODE -ne 0) { throw '托盘组件构建失败。需要 .NET 10 SDK 与 Visual Studio C++ 构建工具。' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src/SomaFinder/Assets/SomaFinder.ico') -Destination $trayOutput -Force
& (Join-Path $PSScriptRoot 'ci/prepare-media.ps1') -OutputDirectory $OutputDirectory

Copy-Item -LiteralPath (Join-Path $PSScriptRoot '使用说明.txt') -Destination $OutputDirectory -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '更新说明.txt') -Destination $OutputDirectory -Force
$documents=Join-Path $OutputDirectory 'Documents';New-Item -ItemType Directory -Force -Path $documents|Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '未来功能需求.txt') -Destination $documents -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSES') -Destination $documents -Recurse -Force
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot '测试报告.txt')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot '测试报告.txt') -Destination $documents -Force }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $documents 'LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs/LICENSING.md') -Destination (Join-Path $documents 'Licensing.txt') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md') -Destination $documents -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs') -Destination (Join-Path $documents 'Guides') -Recurse -Force
$expected=@('SomaFinder.exe','使用说明.txt','更新说明.txt')
$actual=@(Get-ChildItem -LiteralPath $OutputDirectory -File|Select-Object -ExpandProperty Name)
if(@(Compare-Object $expected $actual).Count -gt 0){throw '一级目录必须恰好包含主程序和两份说明文件；请使用新的空输出目录。'}
Write-Host ('绿色版输出：' + $OutputDirectory)
