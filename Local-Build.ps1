param(
    [Parameter(Mandatory=$true)][ValidatePattern('^Release1\.\d+$')][string]$Version,
    [string]$OutputDirectory,
    [string]$Dotnet,
    [string]$CompilerPath,
    [switch]$TestInstaller
)
$ErrorActionPreference='Stop'
$configPath=Join-Path $PSScriptRoot 'local-build.config.json'
$config=if(Test-Path -LiteralPath $configPath){Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json}else{$null}
if([string]::IsNullOrWhiteSpace($Dotnet)){$Dotnet=if($config.Dotnet){[string]$config.Dotnet}else{'dotnet'}}
if([string]::IsNullOrWhiteSpace($CompilerPath)){$CompilerPath=if($config.CompilerPath){[string]$config.CompilerPath}else{Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'}}
if(!(Test-Path -LiteralPath $CompilerPath -PathType Leaf)){throw 'Supply an existing local Inno Setup compiler with -CompilerPath or local-build.config.json. Nothing will be downloaded.'}
if(!(Get-Command $Dotnet -ErrorAction SilentlyContinue)){throw 'The local .NET SDK is unavailable. Nothing will be downloaded.'}
if([string]::IsNullOrWhiteSpace($OutputDirectory)){
    $outputRoot=if($config.OutputRoot){[string]$config.OutputRoot}else{Join-Path $PSScriptRoot 'artifacts/local'}
    $OutputDirectory=Join-Path $outputRoot ($Version+'-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if($OutputDirectory.StartsWith('\\') -or $CompilerPath.StartsWith('\\')){throw 'Local build output and compiler paths cannot be network shares.'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Choose a new output directory; existing builds will not be overwritten.'}
if($TestInstaller){
    $installedApp=Join-Path $env:LOCALAPPDATA 'Programs/PoteHunter/PoteHunter.exe'
    if((Test-Path -LiteralPath $installedApp) -or (Get-Process -Name PoteHunter -ErrorAction SilentlyContinue)){
        throw 'Installer smoke tests require a disposable Windows profile without an existing PlayPoteBot installation. Use the ordinary local build on this computer.'
    }
}
$engine=(Get-Process -Id $PID).Path
function Invoke-LocalStage {
    param([string]$Script,[string[]]$Parameters)
    & $engine -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot $Script) @Parameters
    if($LASTEXITCODE -ne 0){throw "Local stage $Script failed ($LASTEXITCODE). No upload was attempted."}
}
Invoke-LocalStage -Script 'Package.ps1' -Parameters @('-Version',$Version,'-Dotnet',$Dotnet,'-OutputDirectory',$OutputDirectory)
$installerArguments=@('-Version',$Version,'-OutputDirectory',$OutputDirectory,'-CompilerPath',$CompilerPath)
if($TestInstaller){$installerArguments+='-TestInstall'}
Invoke-LocalStage -Script 'Installer.ps1' -Parameters $installerArguments
$names=@("PoteHunter-$Version-Setup.exe","PoteHunter-$Version-Setup.exe.sha256","PoteHunter-$Version-win-x64.zip","PoteHunter-$Version-win-x64.zip.sha256")
$artifacts=@(foreach($name in $names){
    $file=Get-Item -LiteralPath (Join-Path $OutputDirectory $name)
    @{Name=$name;Bytes=$file.Length;SHA256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
})
@{Version=$Version;CompletedUtc=[DateTime]::UtcNow.ToString('o');BuiltLocally=$true;DependencyDownloads=$false;
    GitHubActionsUsed=$false;Uploaded=$false;LiveInstallationChanged=$false;InstallerSmokeTests=[bool]$TestInstaller;Artifacts=$artifacts} |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'local-build-manifest.json') -Encoding utf8
Write-Output "Local build, checks, portable ZIP, installer and checksums complete: $OutputDirectory"
Write-Output 'The installed app was not replaced. No files were uploaded.'
