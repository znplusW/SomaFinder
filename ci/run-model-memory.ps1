$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:RUNNER_OS -ne 'Windows'){throw 'Hosted Windows only'}
New-Item -ItemType Directory -Force reports|Out-Null
$report=[IO.Path]::GetFullPath('reports/model-memory.json')
$process=Start-Process artifacts/portable/SomaFinder.exe -ArgumentList @('--verify-model-memory',('--report='+$report)) -PassThru -WindowStyle Hidden
if(-not $process.WaitForExit(240000)){$process.Kill($true);throw 'Model memory checks timed out'}
if(-not(Test-Path -LiteralPath $report)){throw 'Missing model memory report'}
$result=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json
$result.tests|Format-Table name,passed
if($process.ExitCode -ne 0 -or $result.failures -ne 0){throw 'Model memory checks failed'}
