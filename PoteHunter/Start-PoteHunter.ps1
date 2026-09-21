param([string]$WorkingDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$launcher = Join-Path $WorkingDirectory 'PoteHunter.exe'
if (-not (Test-Path -LiteralPath $launcher)) {
    throw 'PoteHunter.exe is missing. Build the source first, then run the application from the dist folder.'
}
Start-Process -FilePath $launcher -ArgumentList '--native-read-compat','--native-input-compat' -WorkingDirectory $WorkingDirectory
