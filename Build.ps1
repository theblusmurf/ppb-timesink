param(
    [string]$Dotnet = 'dotnet',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'),
    [string]$PackageSource = 'https://api.nuget.org/v3/index.json'
)
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $OutputDirectory) -and (Get-ChildItem -LiteralPath $OutputDirectory -Force | Select-Object -First 1)) {
    throw 'Choose a new or empty output folder to keep existing app settings and builds intact.'
}
function Invoke-Dotnet {
    param([string[]]$Arguments)
    & $Dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}
$mainProject = Join-Path $PSScriptRoot 'PoteHunter/PoteHunter.csproj'
Invoke-Dotnet -Arguments @('restore',$mainProject,'-r','win-x64','-p:Portable=true','--source',$PackageSource)
Invoke-Dotnet -Arguments @('publish',$mainProject,'-c','Release','-p:Portable=true','--no-restore','-o',$OutputDirectory)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PoteHunter/Start-Fixed-Detection.cmd') -Destination $OutputDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PoteHunter/Start-PoteHunter.ps1') -Destination $OutputDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'packaging/settings.json') -Destination $OutputDirectory
if (Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File | Where-Object { $_.Name -like 'teleporter-profiles.json*' }) {
    throw 'Build output contains private teleporter setup data. The build cannot be distributed.'
}
Write-Host "Build complete. Run PoteHunter.exe in $OutputDirectory"

