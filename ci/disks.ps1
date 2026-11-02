param([ValidateSet('Create','Detach','Attach','Swap','Cleanup')][string]$Operation,[Parameter(Mandatory=$true)][string]$Root,[ValidateSet('A','B')][string]$Device='A')
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:RUNNER_OS -ne 'Windows'){throw 'Hosted cloud Windows only'}
$Root=[IO.Path]::GetFullPath($Root).TrimEnd('\')
$temp=[IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\')
if(-not $Root.StartsWith($temp+'\',[StringComparison]::OrdinalIgnoreCase) -or -not (Split-Path $Root -Leaf).StartsWith('SomaFinder-cloud-')){throw 'Unsafe virtual disk directory'}
$manifest=Join-Path $Root 'disks.json'
function Owned-Path([string]$Path){$p=[IO.Path]::GetFullPath($Path);if(-not $p.StartsWith($Root+'\',[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetExtension($p) -ne '.vhdx'){throw 'Refusing non-fixture image'};return $p}
function Owned-Partition([string]$Path){$p=Owned-Path $Path;$disk=Get-DiskImage -ImagePath $p|Get-Disk;if(-not $disk -or $disk.IsBoot -or $disk.IsSystem){throw 'Refusing non-fixture disk'};return Get-Partition -DiskNumber $disk.Number|Where-Object {$_.Type -in @('Basic','IFS') -and $_.Size -gt 1MB}|Select-Object -First 1}
if($Operation -eq 'Create'){
    if(Test-Path $manifest){throw 'Disk fixture cannot be reused'}
    New-Item -ItemType Directory -Force -Path (Join-Path $Root 'vhds')|Out-Null
    $used=@(Get-Volume|Where-Object DriveLetter|ForEach-Object {[string]$_.DriveLetter})
    $free=@('R','S','T','U','V','W','X','Y','Z','P','Q'|Where-Object {$_ -notin $used})
    if($free.Count -lt 2){throw 'Two unused letters required'}
    $state=@{APath=(Join-Path $Root 'vhds/a.vhdx');BPath=(Join-Path $Root 'vhds/b.vhdx');ACurrent=$free[0];BCurrent=$free[1]}
    $state|ConvertTo-Json|Set-Content $manifest
    foreach($key in @('A','B')){
        $image=Owned-Path $state[($key+'Path')];$letter=$state[($key+'Current')]
        $commands=@("create vdisk file=`"$image`" maximum=64 type=expandable","select vdisk file=`"$image`"",'attach vdisk','create partition primary',('format fs=ntfs quick label=SomaCloud'+$key),('assign letter='+$letter))
        $script=Join-Path $Root ('diskpart-'+$key+'.txt');$commands|Set-Content $script -Encoding ascii
        & diskpart.exe /s $script
        if($LASTEXITCODE -ne 0){throw ('DiskPart failed for fixture '+$key)}
        if(-not (Test-Path ($letter+':\'))){throw ('Virtual disk '+$key+' did not mount')}
    }
}else{
    $state=Get-Content $manifest -Raw|ConvertFrom-Json -AsHashtable
    if($Operation -eq 'Detach'){Dismount-DiskImage -ImagePath (Owned-Path $state[($Device+'Path')])|Out-Null}
    if($Operation -eq 'Attach'){
        $image=Owned-Path $state[($Device+'Path')];Mount-DiskImage -ImagePath $image|Out-Null
        $part=Owned-Partition $image
        if([string]$part.DriveLetter -ne [string]$state[($Device+'Current')]){Set-Partition -DiskNumber $part.DiskNumber -PartitionNumber $part.PartitionNumber -NewDriveLetter $state[($Device+'Current')]}
    }
    if($Operation -eq 'Swap'){
        $a=Owned-Partition $state.APath;$b=Owned-Partition $state.BPath
        foreach($p in @($a,$b)){if($p.DriveLetter){Remove-PartitionAccessPath -DiskNumber $p.DiskNumber -PartitionNumber $p.PartitionNumber -AccessPath ($p.DriveLetter+':\')}}
        $nextA=$state.BCurrent;$nextB=$state.ACurrent
        Set-Partition -DiskNumber $a.DiskNumber -PartitionNumber $a.PartitionNumber -NewDriveLetter $nextA
        Set-Partition -DiskNumber $b.DiskNumber -PartitionNumber $b.PartitionNumber -NewDriveLetter $nextB
        $state.ACurrent=$nextA;$state.BCurrent=$nextB;$state|ConvertTo-Json|Set-Content $manifest
    }
    if($Operation -eq 'Cleanup'){foreach($image in @($state.APath,$state.BPath)){if((Test-Path -LiteralPath (Owned-Path $image)) -and (Get-DiskImage -ImagePath (Owned-Path $image)).Attached){Dismount-DiskImage -ImagePath $image|Out-Null}}}
}
