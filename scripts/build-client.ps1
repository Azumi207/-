param([string]$Output='', [switch]$WithoutMedia)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if (!$Output) { $Output=Join-Path $root 'release-assets\client-build' }
$Output=[IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { throw 'Output exists; choose a fresh directory. Existing deployments are not overwritten.' }
if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0 first.' }
& (Join-Path $PSScriptRoot 'install-native-ass.ps1')
if (!$WithoutMedia) { & (Join-Path $PSScriptRoot 'install-ffmpeg.ps1') -Destination (Join-Path $root 'tools\ffmpeg') }
$media=if($WithoutMedia) { 'false' } else { 'true' }
# Pin the reviewed runtime patch; SDK/runtime installation is needed only by the builder.
$runtimeVersion='10.0.13'
& dotnet publish (Join-Path $root 'windows-client\Frameflow.csproj') -c Release -r win-x64 --self-contained true -o $Output '-p:UseSharedCompilation=false' "-p:IncludeMediaEngine=$media" "-p:RuntimeFrameworkVersion=$runtimeVersion" '-p:PublishTrimmed=false' '-p:PublishSingleFile=false'
if ($LASTEXITCODE -ne 0) { throw 'Client build failed.' }
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $Output
Copy-Item -LiteralPath (Join-Path $root 'THIRD_PARTY.md') -Destination $Output
foreach($name in @('start-client.cmd','install-ffmpeg.ps1')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $Output }
Copy-Item -LiteralPath (Join-Path $root 'tools\ffmpeg-provenance.json') -Destination $Output
Get-ChildItem -LiteralPath (Join-Path $root 'docs') -Filter '*.txt' -File | Copy-Item -Destination $Output
# Keep notices for both redistributable .NET runtime packs, separate from project MIT.
$packages=$env:NUGET_PACKAGES
if (!$packages) { $packages=Join-Path $env:USERPROFILE '.nuget\packages' }
foreach($pack in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')) {
    $packDir=Join-Path $packages "$pack\$runtimeVersion"
    $licenses=Join-Path $Output "Licenses\$pack"
    New-Item -ItemType Directory -Path $licenses -Force | Out-Null
    $notices=if($pack -eq 'microsoft.netcore.app.runtime.win-x64') { @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT') } else { @('LICENSE') }
    foreach($name in $notices) {
        $file=Join-Path $packDir $name
        if (!(Test-Path -LiteralPath $file)) { throw "Missing runtime notice: $file" }
        Copy-Item -LiteralPath $file -Destination $licenses
    }
}
$config=Get-Content -LiteralPath (Join-Path $Output 'Frameflow.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($config.runtimeOptions.frameworks -or !$config.runtimeOptions.includedFrameworks) { throw 'Publish was not self-contained.' }
foreach($name in @('Frameflow.exe','coreclr.dll','hostfxr.dll','hostpolicy.dll','PresentationFramework.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $Output $name))) { throw "Missing portable runtime: $name" }
}
Write-Host "Built self-contained Windows x64 client (.NET $runtimeVersion included): $Output. End users do not install .NET."
