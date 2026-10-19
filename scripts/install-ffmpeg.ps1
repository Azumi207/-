param([string]$Destination='', [string]$ArchivePath='')
$ErrorActionPreference='Stop'
function Get-Sha256([string]$Path) {
    $stream=[IO.File]::OpenRead($Path);$sha=[Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
    finally { $stream.Dispose();$sha.Dispose() }
}
if (!$Destination) { $Destination=Join-Path $PSScriptRoot 'Engine' }
$Destination=[IO.Path]::GetFullPath($Destination)
$manifest=Join-Path $PSScriptRoot 'ffmpeg-provenance.json'
if (!(Test-Path -LiteralPath $manifest)) { $manifest=Join-Path (Split-Path $PSScriptRoot -Parent) 'tools\ffmpeg-provenance.json' }
$meta=Get-Content -LiteralPath $manifest -Raw -Encoding UTF8 | ConvertFrom-Json
$names=@('ffmpeg.exe','ffprobe.exe','LICENSE','README.txt')
if (Test-Path -LiteralPath $Destination) {
    foreach($name in $names) {
        $file=Join-Path $Destination $name
        if (!(Test-Path -LiteralPath $file) -or (Get-Sha256 $file) -ne $meta.files.$name) { throw 'Existing Engine differs; nothing overwritten. Select a new destination.' }
    }
    Write-Host 'Verified existing FFmpeg engine.'; return
}
$parent=Split-Path $Destination -Parent
New-Item -ItemType Directory -Path $parent -Force | Out-Null
$stage=Join-Path $parent ('.ffmpeg-download-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
if (!$ArchivePath) {
    $ArchivePath=Join-Path $stage 'download.zip'
    Write-Host "Downloading separate GPL FFmpeg from $($meta.archive_url). See https://ffmpeg.org/legal.html"
    [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
    $client=[Net.WebClient]::new()
    try { $client.DownloadFile($meta.archive_url,$ArchivePath) } finally { $client.Dispose() }
}
$ArchivePath=[IO.Path]::GetFullPath($ArchivePath)
if ((Get-Sha256 $ArchivePath) -ne $meta.archive_sha256) { throw 'Archive hash mismatch. Upstream may have changed; refusing to install. Ask maintainer to update the reviewed manifest.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead($ArchivePath)
try {
    foreach($name in $names) {
        $suffix=if($name.EndsWith('.exe')) { '/bin/'+$name } else { '/'+$name }
        $entries=@($zip.Entries | Where-Object { $_.FullName.EndsWith($suffix,[StringComparison]::Ordinal) })
        if ($entries.Count -ne 1) { throw "Unexpected archive entries for $name" }
        $file=Join-Path $stage $name
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entries[0],$file,$false)
        if ((Get-Sha256 $file) -ne $meta.files.$name) { throw "File hash mismatch: $name" }
    }
} finally { $zip.Dispose() }
# No recursive delete or move; preserve the downloaded archive for inspection.
New-Item -ItemType Directory -Path $Destination -ErrorAction Stop | Out-Null
foreach($name in $names) { [IO.File]::Copy((Join-Path $stage $name),(Join-Path $Destination $name),$false) }
Copy-Item -LiteralPath $manifest -Destination (Join-Path $Destination 'PROVENANCE.json')
Write-Host "Verified FFmpeg $($meta.version): $Destination. Download cache retained: $stage"
