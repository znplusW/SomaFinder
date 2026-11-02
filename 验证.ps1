param([string]$Executable = (Join-Path $PSScriptRoot 'artifacts\portable\SomaFinder.exe'), [switch]$OnlyNew)
$ErrorActionPreference = 'Stop'
$env:SOMAFINDER_TEST = '1'
$reportDirectory = Join-Path $PSScriptRoot 'artifacts\verification'
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
$verificationArgs = if ($OnlyNew) { @('--verify-new') } else { @('--verify','--render') }
$result = Start-Process -FilePath $Executable -ArgumentList ($verificationArgs + @('--report="' + (Join-Path $reportDirectory 'report.json') + '"')) -PassThru -Wait -WindowStyle Hidden
if ($result.ExitCode -ne 0) { throw ('验证失败，请查看 ' + (Join-Path $reportDirectory 'report.json')) }
Write-Host '验证通过。本脚本不结束现有 Soma，不执行清空／重建或真实窗口操作。'
