param([string]$Data = '', [string]$Bind = '127.0.0.1', [int]$Port = 8765, [switch]$AllowInsecureHttp, [string]$Python = '', [switch]$NoBrowser)
$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
if (!$Data) { $Data=Join-Path $PSScriptRoot 'data' }
$runtime=Join-Path $PSScriptRoot '.venv\Scripts\python.exe'
if (!(Test-Path -LiteralPath $runtime)) {
    if ($Python) { & $Python -m venv .venv }
    elseif (Get-Command py -ErrorAction SilentlyContinue) { & py -3 -m venv .venv }
    elseif (Get-Command python -ErrorAction SilentlyContinue) { & python -m venv .venv }
    else { throw 'Install Python 3.12 or newer first (python.org), then run this script again.' }
    if ($LASTEXITCODE -ne 0) { throw 'Python venv creation failed. Install Python 3.12+, or specify -Python FULL_PATH_TO_PYTHON_EXE.' }
}
& $runtime -c 'import sys; assert sys.version_info >= (3,12), "Python 3.12+ required"'
if ($LASTEXITCODE -ne 0) { throw 'Python 3.12+ required.' }
$stamp=Join-Path $PSScriptRoot '.venv\requirements.sha256'
$sha=[Security.Cryptography.SHA256]::Create()
try { $hash=[BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot 'requirements.txt')))).Replace('-','') }
finally { $sha.Dispose() }
if (!(Test-Path -LiteralPath $stamp) -or (Get-Content -LiteralPath $stamp -Raw).Trim() -ne $hash) {
    & $runtime -m pip install -r (Join-Path $PSScriptRoot 'requirements.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Dependency installation failed. Check your network; existing data is unchanged.' }
    [IO.File]::WriteAllText($stamp,$hash)
}
$arguments=@('launcher.py','--data',$Data,'--host',$Bind,'--port',"$Port")
if ($AllowInsecureHttp) { $arguments+='--allow-insecure-http' }
if ($NoBrowser) { $arguments+='--no-browser' }
Write-Host "Frameflow Team: http://${Bind}:$Port  | Ctrl+C to stop | Data: $Data"
& $runtime @arguments
exit $LASTEXITCODE
