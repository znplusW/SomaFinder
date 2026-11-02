param([ValidateSet('image-preview','summary-touch','summary-density','responsive','native-touch','tray-exit','pane-toggles','waterfall-resize','floating-windows','public-release')][string]$Mode)
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:RUNNER_OS -ne 'Windows'){throw 'Hosted Windows only'}
New-Item -ItemType Directory -Force reports|Out-Null
$report=[IO.Path]::GetFullPath((Join-Path $PWD ('reports/'+$Mode+'.json')))
$process=Start-Process -FilePath artifacts/portable/SomaFinder.exe -ArgumentList @(('--verify-'+$Mode),('--report='+$report)) -PassThru -WindowStyle Normal
if(-not $process.WaitForExit(90000)){$process.Kill();throw 'Interaction fixture timed out'}
if(-not(Test-Path -LiteralPath $report)){throw 'Missing interaction report'}
$result=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json
$result.tests|Format-Table name,passed
if($process.ExitCode -ne 0 -or $result.failures -ne 0){throw 'Targeted interaction checks failed'}
