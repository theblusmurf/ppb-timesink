param(
    [Parameter(Mandatory=$true)][string]$Version,
    [string]$OutputDirectory,
    [string]$CompilerPath,
    [switch]$TestInstall
)
$ErrorActionPreference='Stop'
if($Version -notmatch '^Release1\.\d+$'){throw 'Installer requires an official Release1.x version.'}
if($TestInstall){
    $installedApp=Join-Path $env:LOCALAPPDATA 'Programs/PoteHunter/PoteHunter.exe'
    $appKey='{EAACF1DA-2231-4E70-A901-30D01AEECA30}_is1'
    $registrations=@("HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$appKey","HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$appKey","HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\$appKey")
    if((Test-Path -LiteralPath $installedApp) -or ($registrations | Where-Object {Test-Path -LiteralPath $_}) -or (Get-Process -Name PoteHunter -ErrorAction SilentlyContinue)){
        throw 'Installer smoke tests require a disposable Windows profile without an existing app or uninstall registration. Ordinary compilation never installs or uninstalls.'
    }
}
$output=if([string]::IsNullOrWhiteSpace($OutputDirectory)){Join-Path $PSScriptRoot "artifacts/$Version"}else{[IO.Path]::GetFullPath($OutputDirectory)}
$archive=Join-Path $output "PoteHunter-$Version-win-x64.zip"
$staging=Join-Path ([IO.Path]::GetTempPath()) ('PoteHunter-installer-'+[guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $archive -DestinationPath $staging
$compiler=if([string]::IsNullOrWhiteSpace($CompilerPath)){Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'}else{[IO.Path]::GetFullPath($CompilerPath)}
if(!(Test-Path -LiteralPath $compiler)){
    throw 'Local Inno Setup compiler unavailable. Supply -CompilerPath; no software will be downloaded or installed.'
}
& $compiler "/DPackageSource=$(Join-Path $staging 'PoteHunter')" "/DReleaseTag=$Version" "/DInstallerOutput=$output" (Join-Path $PSScriptRoot 'packaging/PoteHunter.iss')
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed.'}
$installer=Join-Path $output "PoteHunter-$Version-Setup.exe"
$hash=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($installer))" | Set-Content -LiteralPath "$installer.sha256" -Encoding utf8
if(!$TestInstall){
    @{Passed=$true;Version=$Version;CompiledLocally=$true;InstallationTestRun=$false;LiveInstallationChanged=$false;SHA256=$hash} |
        ConvertTo-Json | Set-Content (Join-Path $output 'installer-build.json')
    Write-Output "Local installer built: $installer (not installed)."
    return
}
# Install into a disposable test folder with existing user data. Silent setup
# never launches the game, and no current installation is modified.
$test=Join-Path $staging 'install-test'
New-Item -ItemType Directory -Path $test | Out-Null
$sentinel='preserve-user-settings'
New-Item -ItemType Directory -Path (Join-Path $test 'hunting-logs/test-session') -Force | Out-Null
$preservedFiles=@('settings.json','navigation-routes.json','repair-profile.json','merchant-repair-profile.json','merchant-repair-profile.json.fixture.tmp','revival-profile.json','teleporter-profiles.json','teleporter-profiles.json.backup-fixture','teleporter-profiles.json.fixture.tmp','scroll-route-profiles.json','scroll-route-profiles.json.backup-fixture','scroll-route-profiles.json.fixture.tmp','client-recovery-profile.json','loot-history.csv','session-log.txt','hunting-logs/test-session/events.csv')
function Assert-PreservedUserData {
    param([string]$Stage)
    foreach($name in $preservedFiles){
        if((Get-Content -LiteralPath (Join-Path $test $name) -Raw).Trim() -ne $sentinel){throw "$Stage changed $name"}
    }
}
foreach($name in $preservedFiles) {
    Set-Content -LiteralPath (Join-Path $test $name) -Value $sentinel
}
$process=Start-Process -FilePath $installer -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS',('/DIR="'+$test+'"') -Wait -PassThru -WindowStyle Hidden
if($process.ExitCode -ne 0){throw "Installer smoke test failed: $($process.ExitCode)"}
Assert-PreservedUserData -Stage 'Installer'
if(!(Test-Path -LiteralPath (Join-Path $test 'PoteHunter.exe')) -or
    (Get-Content -LiteralPath (Join-Path $test 'release-version.txt') -Raw).Trim() -ne $Version){throw 'Installer payload/version missing.'}
# Run the actual patcher install routine from the package copy, outside the target.
$patchTestHost=Join-Path $staging 'PoteHunter/PoteHunter.exe'
$process=Start-Process -FilePath $patchTestHost -ArgumentList '--patch-install-check',('"'+$installer+'"'),('"'+$test+'"'),$Version,$hash -Wait -PassThru -WindowStyle Hidden
if($process.ExitCode -ne 0 -or !(Test-Path (Join-Path $staging 'patch-install-check.json'))){throw "Auto patcher install smoke test failed: $($process.ExitCode)"}
Assert-PreservedUserData -Stage 'Auto patcher'
$process=Start-Process -FilePath (Join-Path $test 'unins000.exe') -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait -PassThru -WindowStyle Hidden
if($process.ExitCode -ne 0 -or !(Test-Path (Join-Path $test 'settings.json')) -or !(Test-Path (Join-Path $test 'navigation-routes.json'))){throw 'Uninstall did not preserve user data.'}
Assert-PreservedUserData -Stage 'Uninstall'
@{Passed=$true;Version=$Version;Install=$true;Upgrade=$true;AutoPatcherInstall=$true;StartupBlockedDuringPatch=$true;StartupAllowedAfterPatch=$true;ProfilesAndLogsPreserved=$true;TeleporterProfilesPreserved=$true;PreservedUserDataFiles=$preservedFiles;HuntingSessionLogsPreserved=$true;UninstallPreservesUserData=$true;SHA256=$hash} | ConvertTo-Json | Set-Content (Join-Path $output 'installer-checks.json')

