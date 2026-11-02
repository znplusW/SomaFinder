param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$cache=Join-Path $PSScriptRoot '../artifacts/component-cache';New-Item -ItemType Directory -Force -Path $cache|Out-Null
$media=Join-Path $OutputDirectory 'Components/Media';$licenses=Join-Path $OutputDirectory 'Documents/LICENSES';New-Item -ItemType Directory -Force -Path $media,$licenses|Out-Null
function Fetch([string]$Name,[string]$Url,[string]$Hash){
    $path=Join-Path $cache $Name
    if(-not(Test-Path -LiteralPath $path)){Invoke-WebRequest -Uri $Url -OutFile $path}
    if((Get-FileHash -LiteralPath $path).Hash -ne $Hash){throw ('Dependency SHA256 mismatch: '+$Name)}
    return $path
}
$pdfUrl='https://github.com/bblanchon/pdfium-binaries/releases/download/chromium/8125/pdfium-win-x64.tgz'
$pdf=Fetch 'pdfium-8125.tgz' $pdfUrl '9A964261760CFF52570AABF038AB346D3A5034DD8B30D0D5FE8861235D356B8B'
$pdfExtract=Join-Path $cache 'pdfium-8125';New-Item -ItemType Directory -Force -Path $pdfExtract|Out-Null
& tar -xzf $pdf -C $pdfExtract
if($LASTEXITCODE -ne 0){throw 'PDFium extraction failed'}
$dll=Get-ChildItem -LiteralPath $pdfExtract -Recurse -File -Filter pdfium.dll|Select-Object -First 1
if(!$dll){throw 'PDFium DLL missing'}
Copy-Item -LiteralPath $dll.FullName -Destination $media -Force
$pdfLicense=Join-Path $licenses 'PDFium';New-Item -ItemType Directory -Force -Path $pdfLicense|Out-Null
Get-ChildItem -LiteralPath $pdfExtract -Recurse -File|Where-Object {$_.Name -match '^(LICENSE|COPYING|NOTICE)' -or $_.DirectoryName -match '[\\/]licenses?$'}|ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $pdfLicense ($_.Directory.Name+'-'+$_.Name)) -Force}
$ffUrl='https://github.com/jellyfin/jellyfin-ffmpeg/releases/download/v7.1.4-3/jellyfin-ffmpeg_7.1.4-3_portable_win64-clang-gpl.zip'
$ff=Fetch 'jellyfin-ffmpeg-7.1.4-3.zip' $ffUrl '113ADEB702683C38BE40A65D859F8EF7FFB07BAE9DF16DFB6C3DF5AC3D95EF3C'
$ffExtract=Join-Path $cache 'ffmpeg-7.1.4-3';if(-not(Test-Path -LiteralPath $ffExtract)){Expand-Archive -LiteralPath $ff -DestinationPath $ffExtract}
foreach($name in @('ffmpeg.exe','ffprobe.exe')){$file=Get-ChildItem -LiteralPath $ffExtract -Recurse -File -Filter $name|Select-Object -First 1;if(!$file){throw ('Missing '+$name)};Copy-Item -LiteralPath $file.FullName -Destination $media -Force}
Get-ChildItem -LiteralPath $ffExtract -Recurse -File -Filter '*.dll'|Copy-Item -Destination $media -Force
$ffLicense=Join-Path $licenses 'FFmpeg';New-Item -ItemType Directory -Force -Path $ffLicense|Out-Null
Get-ChildItem -LiteralPath $ffExtract -Recurse -File|Where-Object {$_.Name -match '^(LICENSE|COPYING|NOTICE|README)'}|ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $ffLicense $_.Name) -Force}
@"
Media components are separate tools/libraries. Soma source and existing indexes are not modified.
PDFium: chromium/8125, BSD-style and third-party notices bundled above.
Binary: $pdfUrl
Source and build recipes: https://github.com/bblanchon/pdfium-binaries/tree/chromium/8125
FFmpeg / FFprobe: Jellyfin 7.1.4-3, GPL distribution.
Binary: $ffUrl
Corresponding source and build recipes: https://github.com/jellyfin/jellyfin-ffmpeg/releases/tag/v7.1.4-3
https://github.com/jellyfin/jellyfin-ffmpeg/tree/v7.1.4-3
FFmpeg is invoked as a separate process. No network protocols are allowed for preview inputs.
"@ | Set-Content -LiteralPath (Join-Path $licenses 'Media-components.txt') -Encoding utf8
$files=@(Get-ChildItem -LiteralPath $media -File|ForEach-Object {@{name=$_.Name;bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
@{pdfium=$pdfUrl;ffmpeg=$ffUrl;files=$files}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $media 'components.json') -Encoding utf8
