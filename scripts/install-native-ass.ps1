param([string]$Destination='', [string]$Cache='')
$ErrorActionPreference='Stop'
function Sha256([string]$Path) {
    $stream=[IO.File]::OpenRead([IO.Path]::GetFullPath($Path));$hash=[Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hash.ComputeHash($stream))).Replace('-','').ToLowerInvariant() }
    finally { $hash.Dispose();$stream.Dispose() }
}
$root=Split-Path $PSScriptRoot -Parent
if (!$Destination) { $Destination=Join-Path $root 'tools\native-ass' }
$Destination=[IO.Path]::GetFullPath($Destination)
$manifest=Get-Content -LiteralPath (Join-Path $root 'tools\native-ass-provenance.json') -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
foreach($package in $manifest.packages) {
    $needed=$false
    foreach($file in $package.files) {
        $out=Join-Path $Destination $file.destination
        $expected=$manifest.files.($file.destination)
        if (!(Test-Path -LiteralPath $out) -or (Sha256 $out) -ne $expected) { $needed=$true }
    }
    if (!$needed) { continue }
    $stage=Join-Path ([IO.Path]::GetTempPath()) ('Frameflow-libass-'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    try {
        $archive=Join-Path $stage 'package.tar.zst'
        $cached=if($Cache) { Join-Path $Cache ([IO.Path]::GetFileName($package.url)) } else { '' }
        if($cached -and (Test-Path -LiteralPath $cached)) { Copy-Item -LiteralPath $cached -Destination $archive }
        else { Invoke-WebRequest -Uri $package.url -OutFile $archive }
        if ((Sha256 $archive) -ne $package.sha256) { throw "Package checksum mismatch: $($package.name)" }
        foreach($file in $package.files) {
            $out=[IO.Path]::GetFullPath((Join-Path $Destination $file.destination))
            if (!$out.StartsWith($Destination.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid manifest path.' }
            if ($file.member -match '(^/|(^|/)\.\.(/|$))') { throw 'Invalid archive member.' }
            & tar -xf $archive -C $stage $file.member
            if ($LASTEXITCODE -ne 0) { throw 'Cannot extract native renderer; Windows tar with Zstandard support is required by the builder.' }
            $source=Join-Path $stage $file.member
            if ((Sha256 $source) -ne $manifest.files.($file.destination)) { throw "Extracted file checksum mismatch: $($file.member) -> $($file.destination) actual=$(Sha256 $source) expected=$($manifest.files.($file.destination))" }
            New-Item -ItemType Directory -Path (Split-Path $out -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $source -Destination $out
        }
    } finally {
        $resolved=[IO.Path]::GetFullPath($stage)
        $temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
        if ($resolved.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolved -Leaf) -match '^Frameflow-libass-[a-f0-9]{32}$') { Remove-Item -LiteralPath $resolved -Recurse -Force }
    }
}
Copy-Item -LiteralPath (Join-Path $root 'windows-client\Licenses\libass-LICENSE.txt') -Destination $Destination
Copy-Item -LiteralPath (Join-Path $root 'tools\native-ass-provenance.json') -Destination $Destination
Write-Host 'Verified native libass and all pinned DLL dependencies; no system installation or PATH changes.'
