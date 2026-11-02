param([ValidateSet('full','backends','policy')][string]$Scope='full')
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:RUNNER_OS -ne 'Windows'){throw 'Hosted cloud Windows only'}
$repo=Split-Path $PSScriptRoot -Parent
$reports=Join-Path $repo 'reports';New-Item -ItemType Directory -Force -Path $reports|Out-Null
$fixture=Join-Path $env:RUNNER_TEMP ('SomaFinder-cloud-'+$env:GITHUB_RUN_ID+'-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path ($fixture+'/tools')|Out-Null
$soma=Join-Path $fixture 'tools/soma-windows-x64.exe'
Invoke-WebRequest -Uri 'https://github.com/AwesomeDog/soma/releases/download/v0.10.7/soma-windows-x64.exe' -OutFile $soma
if((Get-FileHash $soma -Algorithm SHA256).Hash -ne '5F4B360FBF689EC56AC1494E23EE601A6F495071311DA0A498F4BC839406B382'){throw 'Soma checksum mismatch'}
$env:XDG_DATA_HOME=Join-Path $fixture 'shared-artifacts'
$env:XDG_CACHE_HOME=Join-Path $fixture 'cache'
try{
    if($Scope -eq 'backends'){
        $oldFolder=Join-Path $fixture 'Soma 0.10.6';New-Item -ItemType Directory -Path $oldFolder|Out-Null
        $oldSoma=Join-Path $oldFolder 'soma.exe'
        Invoke-WebRequest -Uri 'https://github.com/AwesomeDog/soma/releases/download/v0.10.6/soma-windows-x64.exe' -OutFile $oldSoma
        if((Get-FileHash $oldSoma -Algorithm SHA256).Hash -ne '1046A0BC44C67624E6A97616F9532B704F9EB5BDD410C4DECC6F9FDDEF40C843'){throw 'Old Soma checksum mismatch'}
        $args=@('--verify-backends',('--cloud-root="'+$fixture+'"'),('--soma="'+$soma+'"'),('--old-soma="'+$oldSoma+'"'),('--report="'+(Join-Path $reports 'backend-integration.json')+'"'))
    }elseif($Scope -eq 'policy'){
        $args=@('--verify-policy',('--cloud-root="'+$fixture+'"'),('--soma="'+$soma+'"'),('--report="'+(Join-Path $reports 'policy-integration.json')+'"'))
    }else{
        & (Join-Path $PSScriptRoot 'disks.ps1') -Operation Create -Root $fixture
        $args=@('--verify-cloud',('--cloud-root="'+$fixture+'"'),('--soma="'+$soma+'"'),('--disk-script="'+(Join-Path $PSScriptRoot 'disks.ps1')+'"'),('--report="'+(Join-Path $reports 'cloud-integration.json')+'"'))
    }
    $console=Join-Path $reports 'cloud-console.log'
    $p=Start-Process -FilePath (Join-Path $repo 'artifacts/portable/SomaFinder.exe') -ArgumentList $args -WindowStyle Hidden -PassThru -RedirectStandardOutput $console -RedirectStandardError (Join-Path $reports 'cloud-native-errors.log')
    $deadline=[DateTime]::UtcNow.AddMinutes(40);$printed=0
    while(-not $p.WaitForExit(15000)){
        if(Test-Path $console){$lines=@(Get-Content $console);$lines|Select-Object -Skip $printed|Write-Host;$printed=$lines.Count}
        if([DateTime]::UtcNow -gt $deadline){$p.Kill($true);throw 'Cloud suite exceeded 40 minutes'}
    }
    if(Test-Path $console){Get-Content $console|Select-Object -Skip $printed|Write-Host}
    if($p.ExitCode -ne 0){Get-ChildItem $reports -Filter '*integration.json'|ForEach-Object {Get-Content $_.FullName};throw 'Cloud integration assertions failed'}
}finally{
    if(Test-Path ($fixture+'/disks.json')){& (Join-Path $PSScriptRoot 'disks.ps1') -Operation Cleanup -Root $fixture}
    Get-ChildItem $fixture -Filter '*.log' -Recurse -ErrorAction SilentlyContinue|ForEach-Object {Copy-Item $_.FullName (Join-Path $reports ($_.Directory.Name+'-'+$_.Name)) -Force}
}
