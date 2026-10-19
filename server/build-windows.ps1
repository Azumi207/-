param([string]$Python='', [string]$Output='')
$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
if (!$Output) { $Output=Join-Path $PSScriptRoot 'generated' }
$Output=[IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { throw 'Output directory already exists. Choose a new -Output path; live deployments are never overwritten.' }
$buildPython=Join-Path $PSScriptRoot '.build-venv\Scripts\python.exe'
if (!(Test-Path -LiteralPath $buildPython)) {
    if ($Python) { & $Python -m venv .build-venv }
    elseif (Get-Command py -ErrorAction SilentlyContinue) { & py -3 -m venv .build-venv }
    else { & python -m venv .build-venv }
    if ($LASTEXITCODE -ne 0) { throw 'Install Python 3.12+, or pass -Python FULL_PATH.' }
}
& $buildPython -c 'import sys; assert sys.version_info >= (3,12)'
if ($LASTEXITCODE -ne 0) { throw 'Python 3.12+ required to generate a package.' }
& $buildPython -m pip install -r requirements-build.txt
if ($LASTEXITCODE -ne 0) { throw 'Build dependency installation failed.' }
& $buildPython package_windows.py --output $Output
if ($LASTEXITCODE -ne 0) { throw 'Package generation failed. Read the error above; existing deployments were not changed.' }
Write-Host "Generated: $Output"
