param([string]$Portable='artifacts/portable')
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:RUNNER_OS -ne 'Windows'){throw 'Hosted Windows only'}
New-Item -ItemType Directory -Force reports|Out-Null
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class PauseShellProbe {
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string name,string title);
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 public delegate bool Visitor(IntPtr window,IntPtr value);
 [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr window,Visitor visitor,IntPtr value);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window,out Rect rect);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr window,StringBuilder name,int length);
 public static Rect TaskBandRect() {
   var shell=FindWindow("Shell_TrayWnd",null);Rect r;GetWindowRect(shell,out r);
   return r;
 }
}
'@
if([PauseShellProbe]::FindWindow('Shell_TrayWnd',$null) -eq [IntPtr]::Zero){Start-Process explorer.exe -WindowStyle Hidden|Out-Null}
for($i=0;$i -lt 100 -and [PauseShellProbe]::FindWindow('Shell_TrayWnd',$null) -eq [IntPtr]::Zero;$i++){Start-Sleep -Milliseconds 100}
if([PauseShellProbe]::FindWindow('Shell_TrayWnd',$null) -eq [IntPtr]::Zero){throw 'Real Shell unavailable'}
$root=Join-Path $env:RUNNER_TEMP ('SomaFinder-cloud-pause-icons-'+[Guid]::NewGuid().ToString('N'))
$shortcutRoot=$root+'-shortcut';New-Item -ItemType Directory -Path $shortcutRoot|Out-Null
$exe=[IO.Path]::GetFullPath((Join-Path $Portable 'SomaFinder.exe'))
$shortcut=Join-Path $shortcutRoot 'Soma Finder.lnk'
$shell=New-Object -ComObject WScript.Shell;$link=$shell.CreateShortcut($shortcut);$link.TargetPath=$exe;$link.IconLocation=$exe+',0';$link.Save()
$before=(Get-FileHash -LiteralPath $shortcut).Hash
$report=[IO.Path]::GetFullPath((Join-Path $PWD 'reports/pause-icons.json'))
$process=Start-Process -FilePath $exe -ArgumentList @('--verify-pause-icons',('--fixture='+$root),('--report='+$report),('--shortcut="'+$shortcut+'"')) -PassThru -WindowStyle Normal
if(-not $process.WaitForExit(90000)){$process.Kill();throw 'Pause icon fixture timed out'}
if(-not(Test-Path -LiteralPath $report)){throw 'Missing report'}
$result=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json
$result.tests|Format-Table name,passed
if((Get-FileHash -LiteralPath $shortcut).Hash -ne $before){throw 'Shortcut changed after application exit'}
for($i=0;$i -lt 60 -and [PauseShellProbe]::FindWindow('SomaFinder.StableTray.v1',$null) -ne [IntPtr]::Zero;$i++){Start-Sleep -Milliseconds 100}
if([PauseShellProbe]::FindWindow('SomaFinder.StableTray.v1',$null) -ne [IntPtr]::Zero){throw 'Tray helper remained after owner exit'}
Start-Sleep -Milliseconds 500
Add-Type -AssemblyName System.Drawing
$rect=[PauseShellProbe]::TaskBandRect();$image=[Drawing.Bitmap]::new(($rect.Right-$rect.Left),($rect.Bottom-$rect.Top))
$graphics=[Drawing.Graphics]::FromImage($image)
$orange=$null
try{
    $graphics.CopyFromScreen($rect.Left,$rect.Top,0,0,$image.Size)
    $image.Save([IO.Path]::GetFullPath((Join-Path $PWD 'reports/taskbar-after-exit.png')),[Drawing.Imaging.ImageFormat]::Png)
}finally{$graphics.Dispose();$image.Dispose()}
if($result.actualPinned){
    $button=@($result.observations|Where-Object {$_.name -eq 'captured Soma button'})[-1]
    if(-not $button){throw 'Missing actual Soma taskbar button geometry'}
    $image=[Drawing.Bitmap]::new([int]$button.Width,[int]$button.Height);$graphics=[Drawing.Graphics]::FromImage($image);$orange=0
    try{
        $graphics.CopyFromScreen([int]$button.X,[int]$button.Y,0,0,$image.Size)
        $image.Save([IO.Path]::GetFullPath((Join-Path $PWD 'reports/taskbar-pinned-button-after-exit.png')),[Drawing.Imaging.ImageFormat]::Png)
        for($y=0;$y -lt $image.Height;$y++){for($x=0;$x -lt $image.Width;$x++){$c=$image.GetPixel($x,$y);if($c.R -gt 190 -and $c.G -gt 75 -and $c.G -lt 160 -and $c.B -lt 65){$orange++}}}
    }finally{$graphics.Dispose();$image.Dispose()}
}
@{commit=$env:GITHUB_SHA;appExited=$true;helperExited=$true;shortcutUnchanged=$true;actualPinned=$result.actualPinned;pinReason=$result.pinReason;postExitTaskbarOrangePixels=$orange}|ConvertTo-Json|Set-Content reports/pause-icons-postexit.json
if($result.actualPinned -and $orange -ne $result.normalButtonOrange){throw 'Real pinned Soma button retained orange pixels after process exit'}
if($process.ExitCode -ne 0 -or $result.failures -ne 0){throw 'Pause icon tests failed'}
