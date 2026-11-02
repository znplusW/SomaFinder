$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:RUNNER_OS -ne 'Windows'){throw 'Hosted Windows only'}
New-Item -ItemType Directory -Force reports|Out-Null
$root=Join-Path $env:RUNNER_TEMP ('SomaFinder-cloud-lifetime-'+[Guid]::NewGuid().ToString('N'))
$tools=$root+'-tools';New-Item -ItemType Directory -Path $tools|Out-Null
$soma=Join-Path $tools 'soma.exe'
Invoke-WebRequest -Uri 'https://github.com/AwesomeDog/soma/releases/download/v0.10.7/soma-windows-x64.exe' -OutFile $soma
if((Get-FileHash -LiteralPath $soma).Hash -ne '5F4B360FBF689EC56AC1494E23EE601A6F495071311DA0A498F4BC839406B382'){throw 'Soma checksum mismatch'}
$exe=[IO.Path]::GetFullPath('artifacts/portable/SomaFinder.exe');$owner=$null;$external=$null;$reopen=$null;$tests=@();$failures=0
function Wait-File([string]$Path){for($i=0;$i -lt 200 -and -not(Test-Path -LiteralPath $Path);$i++){Start-Sleep -Milliseconds 100};if(-not(Test-Path -LiteralPath $Path)){throw ('Missing fixture signal: '+$Path)}}
function Wait-Dead([int]$ProcessId){for($i=0;$i -lt 100 -and (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue);$i++){Start-Sleep -Milliseconds 50};if(Get-Process -Id $ProcessId -ErrorAction SilentlyContinue){throw ('Managed process survived owner: '+$ProcessId)}}
function Rpc([string]$Command,[string[]]$Arguments){Invoke-RestMethod -Uri 'http://127.0.0.1:18543/api/run' -Method Post -ContentType 'application/json' -Body (@{command=$Command;args=$Arguments;options=@{}}|ConvertTo-Json -Compress)}
try{
    $external=Start-Process powershell.exe -ArgumentList @('-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 180') -PassThru -WindowStyle Hidden
    $owner=Start-Process $exe -ArgumentList @('--verify-lifetime-owner',('--fixture="'+$root+'"'),('--soma="'+$soma+'"'),'--port=18543') -PassThru -WindowStyle Hidden
    Wait-File ($root+'/owner-ready.json');Wait-File ($root+'/child.json');Wait-File ($root+'/stdout.txt');Wait-File ($root+'/stderr.txt')
    $child=Get-Content -LiteralPath ($root+'/child.json') -Raw|ConvertFrom-Json;$backend=Get-Content -LiteralPath ($root+'/soma.json') -Raw|ConvertFrom-Json
    if($child.environment -ne '中文 Unicode' -or $child.args -notcontains '空 格' -or $child.args -notcontains '引号"尾斜线\'){throw 'Environment/arguments did not round-trip'}
    if((Get-Content -LiteralPath ($root+'/stdout.txt') -Raw) -notmatch 'managed stdout 中文' -or (Get-Content -LiteralPath ($root+'/stderr.txt') -Raw) -notmatch 'managed stderr 中文'){throw 'Output redirection lost'}
    $tests+=@{name='managed environment Unicode quoted arguments and both logs';passed=$true}
    for($i=0;$i -lt 100;$i++){try{$files=Rpc 'project.files' @('docs');if($files.success){break}}catch{};Start-Sleep -Milliseconds 100}
    if(($files|ConvertTo-Json -Depth 12) -notmatch 'sentinel.md'){throw 'Synthetic indexed document not available before kill'}
    # Intentionally kill only Finder, not its process tree; Job Object must do the rest.
    $owner.Kill();$owner.WaitForExit();Wait-Dead $child.id;Wait-Dead $child.leaf;Wait-Dead $backend.id
    if($external.HasExited){throw 'Unrelated process was terminated'}
    $tests+=@{name='single owner kill ends actual Soma plus managed child descendants and leaves unrelated process alive';passed=$true}
    $env:XDG_CONFIG_HOME=$root+'/config';$env:XDG_STATE_HOME=$root+'/state';$env:XDG_DATA_HOME=$root+'/models'
    $reopen=Start-Process $soma -ArgumentList @('--workspace','main','server','--port=18543') -PassThru -WindowStyle Hidden
    $files=$null;for($i=0;$i -lt 100;$i++){try{$files=Rpc 'project.files' @('docs');if($files.success){break}}catch{};Start-Sleep -Milliseconds 100}
    if(($files|ConvertTo-Json -Depth 12) -notmatch 'sentinel.md'){throw 'Committed synthetic index unavailable after owner kill'}
    $tests+=@{name='existing synthetic indexed sentinel remains available after backend restart without scan';passed=$true}
}catch{$failures++;$tests+=@{name='lifetime fixture';passed=$false;error=$_.ToString()}}
finally{
    foreach($process in @($owner,$external,$reopen)){if($process -and -not $process.HasExited){$process.Kill();$process.WaitForExit()}}
    if(Test-Path -LiteralPath $root){Get-ChildItem -LiteralPath $root -File|Where-Object {$_.Extension -eq '.txt' -or $_.Name -in @('owner-ready.json','child.json','soma.json')}|ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination ('reports/lifetime-'+$_.Name)}}
    @{commit=$env:GITHUB_SHA;failures=$failures;tests=$tests;scope='Atomic Job assignment, single-process owner kill, official Soma 0.10.7 and a synthetic preexisting index; no user data/models'}|ConvertTo-Json -Depth 8|Set-Content reports/lifetime.json
}
if($failures -ne 0){throw 'Lifetime targeted checks failed'}
