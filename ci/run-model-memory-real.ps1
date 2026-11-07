$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:RUNNER_OS -ne 'Windows'){throw 'Hosted Windows only'}
$fixture=Join-Path $env:RUNNER_TEMP ('model-memory-runtime-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $fixture,reports|Out-Null
$archive=Join-Path $fixture 'llama.zip'
Invoke-WebRequest 'https://github.com/ggml-org/llama.cpp/releases/download/b10068/llama-b10068-bin-win-cpu-x64.zip' -OutFile $archive -MaximumRetryCount 2 -RetryIntervalSec 2
if((Get-FileHash -LiteralPath $archive).Hash -ne '01D5F30876ACFB4A0BE59396710F450213495C7181D8FBCCE2FAD045835CEB89'){throw 'llama.cpp archive SHA256 mismatch'}
Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $fixture 'llama')
$llama=@(Get-ChildItem -LiteralPath (Join-Path $fixture 'llama') -Recurse -File -Filter llama-server.exe)
if($llama.Count -ne 1){throw 'Expected one llama-server executable'}
$model=Join-Path $fixture 'stories260K.gguf'
Invoke-WebRequest 'https://huggingface.co/ggml-org/models/resolve/main/tinyllamas/stories260K.gguf' -OutFile $model -MaximumRetryCount 2 -RetryIntervalSec 2
if((Get-FileHash -LiteralPath $model).Hash -ne '270CBA1BD5109F42D03350F60406024560464DB173C0E387D91F0426D3BD256D'){throw 'Tiny model SHA256 mismatch'}
$report=[IO.Path]::GetFullPath('reports/model-memory-real.json')
$process=Start-Process artifacts/portable/SomaFinder.exe -ArgumentList @('--verify-model-memory-real',('--report="'+$report+'"'),('--llama="'+$llama[0].FullName+'"'),('--model="'+$model+'"')) -PassThru -WindowStyle Hidden -RedirectStandardOutput reports/model-memory-real-stdout.txt -RedirectStandardError reports/model-memory-real-stderr.txt
if(-not $process.WaitForExit(120000)){$process.Kill($true);throw 'Real runtime checks timed out'}
if(-not(Test-Path -LiteralPath $report)){throw 'Missing real runtime report'}
$result=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json
$result.tests|Format-Table name,passed
if($process.ExitCode -ne 0 -or $result.failures -ne 0){throw 'Real runtime checks failed'}
