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

# Independently exercise Soma's per-file ASR strategy with the exact pinned
# whisperfile/base artifacts and official whisper.cpp sample audio. These real
# transcriptions complement the frontend's isolated ASR lifetime fixture.
$whisper=Join-Path $fixture 'whisperfile.exe'
$asrModel=Join-Path $fixture 'ggml-base.bin'
$audio=Join-Path $fixture 'jfk.wav'
foreach($download in @(
    @{Url='https://github.com/mozilla-ai/llamafile/releases/download/0.10.1/whisperfile-0.10.1';Path=$whisper;Hash='AF53772522F52657CED04BB232C67C9619DF20CCB4117B9211B73486E624E948'},
    @{Url='https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-base.bin';Path=$asrModel;Hash='60ED5BC3DD14EEA856493D334349B405782DDCAF0028D4B5DF4088345FBA2EFE'},
    @{Url='https://raw.githubusercontent.com/ggml-org/whisper.cpp/v1.8.1/samples/jfk.wav';Path=$audio;Hash='59DFB9A4ACB36FE2A2AFFC14BACBEE2920FF435CB13CC314A08C13F66BA7860E'}
)) {
    Invoke-WebRequest -Uri $download.Url -OutFile $download.Path -MaximumRetryCount 2 -RetryIntervalSec 2
    if((Get-FileHash -LiteralPath $download.Path).Hash -ne $download.Hash){throw 'ASR fixture SHA256 mismatch'}
}
$lastPid=0
foreach($attempt in 1..2){
    $stdout=[IO.Path]::GetFullPath('reports/asr-transcription-'+$attempt+'.txt')
    $stderr=[IO.Path]::GetFullPath('reports/asr-native-'+$attempt+'.txt')
    $asr=Start-Process -FilePath $whisper -ArgumentList @('-m',('"'+$asrModel+'"'),'-f',('"'+$audio+'"'),'--language','en','--no-timestamps','--no-prints','--no-gpu') -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if(-not $asr.WaitForExit(120000)){$asr.Kill($true);throw 'Native ASR transcription timed out'}
    $transcript=(Get-Content -LiteralPath $stdout -Raw).Trim()
    if($asr.ExitCode -ne 0 -or $asr.Id -eq $lastPid -or $transcript.Length -lt 40 -or $transcript -notmatch '(?i)country'){throw ('ASR transcription/restart failed: '+$transcript)}
    $lastPid=$asr.Id
    $result.tests+=@([pscustomobject]@{name=('native whisperfile/base demand loads in fresh process '+$attempt);passed=$true;pid=$asr.Id;exited=$asr.HasExited;transcriptCharacters=$transcript.Length})
}
$result.scope+=' Separate real whisperfile 0.10.1/base-model CPU checks transcribe official whisper.cpp v1.8.1 jfk.wav twice in new exited native processes. These are ASR component checks, not full Soma multimedia indexing.'
$result|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $report -Encoding utf8
