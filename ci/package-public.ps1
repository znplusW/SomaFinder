param([string]$Version='0.6.7',[string]$EvidenceBase='',[string]$EvidenceRun='')
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:RUNNER_OS -ne 'Windows'){throw 'Hosted Windows only'}
$root=Split-Path $PSScriptRoot -Parent
$portable=Join-Path $root 'artifacts/portable'
$release=Join-Path $root 'artifacts/release'
$stage=Join-Path $root ('artifacts/stage/SomaFinder-'+$Version+'-win-x64')
if(Test-Path -LiteralPath $stage){throw 'Use a fresh release staging directory'}
New-Item -ItemType Directory -Path $stage,$release -Force|Out-Null
$reports=Join-Path $root 'reports';$results=@()
$reusedGroups=@()
if($EvidenceBase){
    if($EvidenceBase -notmatch '^[0-9a-f]{40}$' -or $EvidenceRun -notmatch '^\d+$'){throw 'Invalid evidence identity'}
    $allowed=@('src/SomaFinder/PublicReleaseVerification.cs','src/SomaFinder/ModelMemoryVerification.cs','ci/package-public.ps1','.github/workflows/model-memory-repair.yml')
    $changed=@(& git -C $root diff --name-only $EvidenceBase HEAD)
    if($LASTEXITCODE -ne 0 -or @($changed|Where-Object {$_ -notin $allowed}).Count){throw 'Production source changed; full fresh regression required'}
    $reusedGroups=@('full-regression','current-ui','design-ui','scheduling','backend-discovery','floating-windows','waterfall-resize','waterfall','image-preview','backend-integration','search-scenario')
}
foreach($name in @('full-regression','current-ui','design-ui','scheduling','backend-discovery','floating-windows','waterfall-resize','waterfall','image-preview','public-release','model-memory','model-memory-real','backend-integration','search-scenario')){
    $report=Get-Content -LiteralPath (Join-Path $reports ($name+'.json')) -Raw|ConvertFrom-Json
    if($report.failures -ne 0){throw ('Failed check: '+$name)}
    $testedCommit=if($name -in $reusedGroups){$EvidenceBase}else{$env:GITHUB_SHA}
    if($report.commit -and $report.commit -ne $testedCommit){throw 'Mixed test commits'}
    $count=if($report.tests){@($report.tests|Where-Object {$_.passed -eq $true -or $_.status -eq 'passed'}).Count}else{0}
    $results+=@{name=$name;passed=$true;checks=$count;testedCommit=$testedCommit;reused=($name -in $reusedGroups)}
}
foreach($file in @('SomaFinder.exe','Components/SomaFinder.TrayHost.exe')){
    $path=Join-Path $portable $file
    if((Get-Item -LiteralPath $path).VersionInfo.FileVersion -ne ($Version+'.0')){throw 'Wrong binary version'}
    $bytes=[IO.File]::ReadAllBytes($path);$offset=[BitConverter]::ToInt32($bytes,0x3c)
    if([BitConverter]::ToUInt16($bytes,$offset+4) -ne 0x8664){throw 'Wrong architecture'}
}
Get-ChildItem -LiteralPath $portable -Force|Copy-Item -Destination $stage -Recurse
$sourceZip=Join-Path $release ('SomaFinder-'+$Version+'-source.zip')
& git -C $root archive --format=zip ('--output='+$sourceZip) HEAD
if($LASTEXITCODE -ne 0){throw 'Source archive failed'}
Expand-Archive -LiteralPath $sourceZip -DestinationPath (Join-Path $stage 'Source')
$sourceUrl='https://codeload.github.com/jellyfin/jellyfin-ffmpeg/tar.gz/refs/tags/v7.1.4-3'
$ffSource=Join-Path $release 'jellyfin-ffmpeg-v7.1.4-3-source.tar.gz'
Invoke-WebRequest -Uri $sourceUrl -OutFile $ffSource -MaximumRetryCount 2 -RetryIntervalSec 2
if((Get-FileHash -LiteralPath $ffSource).Hash -ne '38FFF90F73B3C4F9C3C7270711411A4EC3CBE63B205D4B4A5525BCC532D3D31F'){throw 'FFmpeg source archive SHA256 mismatch'}
foreach($name in @('search-summary','search-details','search-masonry')){Copy-Item -LiteralPath (Join-Path $reports ($name+'.png')) -Destination $release}
foreach($name in @('data','models','index','.git')){if(Test-Path -LiteralPath (Join-Path $stage $name)){throw ('Unexpected release content: '+$name)}}
if(Get-ChildItem -LiteralPath $stage -Recurse -File -Force|Where-Object {$_.Extension -in @('.db','.sqlite','.sqlite3','.gguf','.pdb')}){throw 'Unexpected database, model or debug data'}
if(Get-ChildItem -LiteralPath (Join-Path $stage 'Source') -Directory -Force -Recurse|Where-Object {$_.Name -in @('.git','obj','bin','data')}){throw 'Private or generated source content'}
$expected=@('SomaFinder.exe','使用说明.txt','更新说明.txt')
if(@(Compare-Object $expected @(Get-ChildItem -LiteralPath $stage -File|Select-Object -ExpandProperty Name)).Count){throw 'Incorrect root layout'}
$proof=@{version=$Version;commit=$env:GITHUB_SHA;repository=$env:GITHUB_REPOSITORY;run=$env:GITHUB_RUN_ID;runUrl=('https://github.com/'+$env:GITHUB_REPOSITORY+'/actions/runs/'+$env:GITHUB_RUN_ID);environment='GitHub hosted Windows Server 2025 x64';checks=$results;evidenceBaseCommit=$EvidenceBase;evidenceBaseRun=$EvidenceRun;localApplicationTested=$false;physicalHardwareTested=$false;syntheticScreenshots=$true;testedSomaVersion='0.10.7 native backend identity/lifecycle integration; production model workloads not exercised';testedLlamaRuntime='b10068 CPU with SHA256-verified tinyllamas GGUF';gpuMemoryReclaimMeasured=$false;sourceArchive=[IO.Path]::GetFileName($sourceZip);binarySHA256=(Get-FileHash -LiteralPath (Join-Path $stage 'SomaFinder.exe')).Hash;signature=(Get-AuthenticodeSignature -LiteralPath (Join-Path $stage 'SomaFinder.exe')).Status.ToString();license='GPL-3.0-only; separate commercial agreement available'}
$proof|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $stage 'Documents/build-provenance.json') -Encoding utf8
$proof|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $release ('SomaFinder-'+$Version+'-build-provenance.json')) -Encoding utf8
$summary="Soma Finder $Version public release candidate`nCommit: $env:GITHUB_SHA`nChecks: $(($results|Measure-Object -Property checks -Sum).Sum) passed cases in $($results.Count) groups; all selected checks passed.`nScope: frontend/package/media preview, memory-policy/task coordination, native Soma backend identity/lifecycle and real llama.cpp CPU model unload/reload; not production model/VHD/B390/physical-hardware acceptance.`nBackend compatibility: Soma 0.10.7.`nSee build-provenance.json and the public workflow for exact scope.`n"
[IO.File]::WriteAllText((Join-Path $stage 'Documents/测试报告.txt'),$summary,[Text.UTF8Encoding]::new($false))
$manifest=@(Get-ChildItem -LiteralPath $stage -File -Recurse -Force|ForEach-Object {[pscustomobject]@{Path=[IO.Path]::GetRelativePath($stage,$_.FullName).Replace('\','/');Bytes=$_.Length;SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
$manifest|Export-Csv -LiteralPath (Join-Path $stage 'Documents/sha256.csv') -NoTypeInformation -Encoding utf8
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=Join-Path $release ('SomaFinder-'+$Version+'-win-x64.zip')
[IO.Compression.ZipFile]::CreateFromDirectory($stage,$zip,[IO.Compression.CompressionLevel]::Optimal,$true)
$opened=[IO.Compression.ZipFile]::OpenRead($zip)
try{
    foreach($item in $manifest){$entry=$opened.GetEntry([IO.Path]::GetFileName($stage)+'/'+$item.Path);if(!$entry){throw 'Missing zip entry'};$stream=$entry.Open();try{$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))}finally{$stream.Dispose()};if($hash -ne $item.SHA256){throw 'Zip integrity mismatch'}}
}finally{$opened.Dispose()}
$sums=@(Get-ChildItem -LiteralPath $release -File|Sort-Object Name|ForEach-Object {(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()+'  '+$_.Name})
[IO.File]::WriteAllLines((Join-Path $release 'SHA256SUMS.txt'),$sums,[Text.UTF8Encoding]::new($false))
@{version=$Version;commit=$env:GITHUB_SHA;files=$manifest.Count;checks=$results;archiveBytes=(Get-Item -LiteralPath $zip).Length}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $reports 'public-package.json') -Encoding utf8
