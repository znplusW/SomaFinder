param([ValidateSet('full','targeted','backends')][string]$Scope='full')
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted'){throw 'Cloud Windows runner required; this script never falls back to local tests.'}
$root=Split-Path $PSScriptRoot -Parent
$reports=Join-Path $root 'reports'
New-Item -ItemType Directory -Force -Path $reports|Out-Null
Get-ComputerInfo -Property WindowsProductName,WindowsVersion,OsBuildNumber,OsArchitecture|ConvertTo-Json|Set-Content (Join-Path $reports 'environment.json')
$env:XDG_CONFIG_HOME=Join-Path $env:RUNNER_TEMP 'regression-config'
$env:XDG_STATE_HOME=Join-Path $env:RUNNER_TEMP 'regression-state'
$failedChecks=[Collections.Generic.List[string]]::new()
function Run-Check([string]$Flag,[string]$Name){
    try {
    $report=Join-Path $reports ($Name+'.json')
    $p=Start-Process -FilePath (Join-Path $root 'artifacts/portable/SomaFinder.exe') -ArgumentList @($Flag,'--render',('--report="'+$report+'"')) -PassThru -WindowStyle Hidden
    if(-not $p.WaitForExit(600000)){$p.Kill($true);throw ($Name+' timed out')}
    if($p.ExitCode -ne 0){if(Test-Path $report){Get-Content $report};throw ($Name+' failed')}
    $result=Get-Content $report -Raw|ConvertFrom-Json
    if($result.failures -ne 0){throw ($Name+' reported failures')}
    }catch{$failedChecks.Add($Name);Write-Warning $_.Exception.Message}
}
if($Scope -ne 'targeted'){Run-Check '--verify' 'full-regression'}
Run-Check '--verify-new' 'current-ui'
Run-Check '--verify-design' 'design-ui'
Run-Check '--verify-scheduling' 'scheduling'
Run-Check '--verify-discovery' 'backend-discovery'
@{commit=$env:GITHUB_SHA;scope=$Scope;localTests=$false}|ConvertTo-Json|Set-Content (Join-Path $reports 'run.json')
if($failedChecks.Count){throw ('Failed checks: '+($failedChecks -join ', '))}
"## Soma Finder $Scope regression`nCommit: $env:GITHUB_SHA`n`nAll executed assertions passed. See artifacts for counts and screenshots."|Add-Content $env:GITHUB_STEP_SUMMARY
