param([string]$Baseline='artifacts/portable',[string]$Upgrade='artifacts/tray-upgrade')
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:RUNNER_OS -ne 'Windows'){throw 'Hosted Windows only'}
New-Item -ItemType Directory -Force reports|Out-Null
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class TrayShellProbe {
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string name,string title);
}
'@
if([TrayShellProbe]::FindWindow('Shell_TrayWnd',$null) -eq [IntPtr]::Zero){
    Start-Process explorer.exe -WindowStyle Hidden|Out-Null
    for($i=0;$i -lt 100 -and [TrayShellProbe]::FindWindow('Shell_TrayWnd',$null) -eq [IntPtr]::Zero;$i++){Start-Sleep -Milliseconds 100}
}
if([TrayShellProbe]::FindWindow('Shell_TrayWnd',$null) -eq [IntPtr]::Zero){throw 'Real Windows Shell unavailable; tray acceptance cannot be marked as passed'}
$root=Join-Path $env:RUNNER_TEMP ('SomaFinder-cloud-tray-versions-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root|Out-Null
$a=Join-Path $root 'SomaFinder-0.5.3'
$b=Join-Path $root 'SomaFinder-0.5.4'
Copy-Item -LiteralPath $Baseline -Destination $a -Recurse
Copy-Item -LiteralPath $Upgrade -Destination $b -Recurse
$records=@();$snapshots=@()
function Invoke-TrayOwner([string]$directory,[string]$name,[switch]$RestartHost,[switch]$Crash){
    $report=[IO.Path]::GetFullPath((Join-Path $PWD ('reports/'+$name+'.json')))
    $fixture=Join-Path $env:RUNNER_TEMP ('SomaFinder-cloud-tray-owner-'+[Guid]::NewGuid().ToString('N'))
    $arguments=@('--verify-tray-owner',('--report='+$report),('--fixture='+$fixture))
    if($RestartHost){$arguments+='--restart-host'}
    if($Crash){$arguments+='--hold'}
    # This owner exercises an interactive search window. SW_HIDE would override its
    # first ShowWindow call; only the actual background tray helper stays hidden.
    $process=Start-Process (Join-Path $directory 'SomaFinder.exe') -ArgumentList $arguments -WindowStyle Normal -PassThru
    if($Crash){
        for($i=0;$i -lt 400 -and -not(Test-Path -LiteralPath $report) -and -not $process.HasExited;$i++){Start-Sleep -Milliseconds 100}
        if(-not(Test-Path -LiteralPath $report)){throw 'Crash fixture did not become ready'}
        $result=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json
        if(-not $result.ready){throw 'Crash fixture failed before ready'}
        $helperPid=[int]$result.snapshot.hostPid
        $process.Kill();$process.WaitForExit()
        for($i=0;$i -lt 100 -and (Get-Process -Id $helperPid -ErrorAction SilentlyContinue);$i++){Start-Sleep -Milliseconds 100}
        if(Get-Process -Id $helperPid -ErrorAction SilentlyContinue){throw 'Tray helper survived its owner'}
        $script:records+=@{name='abrupt frontend exit cleans up its tray host';passed=$true}
    }else{
        if(-not $process.WaitForExit(60000)){$process.Kill();throw 'Tray fixture timed out'}
        $result=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json
        if($process.ExitCode -ne 0 -or $result.failures -ne 0){throw ('Tray owner failed: '+$name)}
    }
    $script:snapshots+=$result.snapshot
    $script:records+=@{name=$name;passed=$true;checks=@($result.tests|Where-Object {$_.passed -eq $true}).Count}
}
$failures=0
try{
    Invoke-TrayOwner $a 'initial-version' -RestartHost
    Invoke-TrayOwner $b 'upgraded-version'
    Invoke-TrayOwner $a 'downgrade-and-reopen'
    Invoke-TrayOwner $b 'abrupt-owner' -Crash
    Invoke-TrayOwner $b 'reopen-after-crash'
    if(@($snapshots.host|Sort-Object -Unique).Count -ne 1 -or @($snapshots.guid|Sort-Object -Unique).Count -ne 1){throw 'Version update changed persistent tray identity'}
    if(@($snapshots.notificationSettings.rows.key|Sort-Object -Unique).Count -ne 1 -or @($snapshots|Where-Object {$_.notificationSettings.count -ne 1}).Count){throw 'Windows tray settings did not retain exactly one identical record'}
    if(@($snapshots.main|Sort-Object -Unique).Count -ne 2 -or @($snapshots.version|Sort-Object -Unique).Count -ne 2 -or @($snapshots.hostVersion|Sort-Object -Unique).Count -ne 2){throw 'Fixture did not exercise different executable paths and binary versions'}
    $records+=@{name='two real main/host binary versions and five launches share exactly one persistent path/GUID';passed=$true}
}catch{$failures++;$records+=@{name='suite';passed=$false;error=$_.ToString()};Write-Host $_}
@{commit=$env:GITHUB_SHA;failures=$failures;tests=$records;snapshots=$snapshots;scope='Only stable tray identity, callback menu, tooltip, taskbar notification, helper/owner crash and upgrade paths. No Soma searches/indexing or unrelated regression.'}|ConvertTo-Json -Depth 12|Set-Content -LiteralPath reports/tray-integration.json -Encoding utf8
if($failures){throw 'Targeted tray tests failed'}
