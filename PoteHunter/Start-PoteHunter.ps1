param([string]$WorkingDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$application = Join-Path $WorkingDirectory 'PoteHunter.exe'
if (-not (Test-Path -LiteralPath $application)) {
    throw 'PoteHunter.exe is missing. Build the source first, then run the application from the dist folder.'
}
Start-Process -FilePath $application -ArgumentList '--native-read-compat','--native-input-compat' -WorkingDirectory $WorkingDirectory

