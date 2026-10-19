param([string]$Output='', [switch]$WithoutMedia)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if (!$Output) { $Output=Join-Path $root 'release-assets\client-build' }
$Output=[IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { throw 'Output exists; choose a fresh directory. Existing deployments are not overwritten.' }
if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install .NET 9 SDK from https://dotnet.microsoft.com/download/dotnet/9.0 first.' }
if (!$WithoutMedia) { & (Join-Path $PSScriptRoot 'install-ffmpeg.ps1') -Destination (Join-Path $root 'tools\ffmpeg') }
& dotnet publish (Join-Path $root 'windows-client\Frameflow.csproj') -c Release --self-contained false -o $Output '-p:UseSharedCompilation=false'
if ($LASTEXITCODE -ne 0) { throw 'Client build failed.' }
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $Output
Copy-Item -LiteralPath (Join-Path $root 'THIRD_PARTY.md') -Destination $Output
Write-Host "Built: $Output. Requires .NET 9 Desktop Runtime. Media engine is a separate GPL component, not covered by MIT."
