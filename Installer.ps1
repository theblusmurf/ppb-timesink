param([Parameter(Mandatory=$true)][string]$Version)
$ErrorActionPreference='Stop'
if($Version -notmatch '^Release1\.\d+$'){throw 'Installer requires an official Release1.x version.'}
$output=Join-Path $PSScriptRoot "artifacts/$Version"
$archive=Join-Path $output "PoteHunter-$Version-win-x64.zip"
$staging=Join-Path ([IO.Path]::GetTempPath()) ('PoteHunter-installer-'+[guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $archive -DestinationPath $staging
$compiler=Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'
if(!(Test-Path -LiteralPath $compiler)){
    & choco install innosetup --yes --no-progress
    if($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $compiler)){throw 'Inno Setup compiler unavailable.'}
}
& $compiler "/DPackageSource=$(Join-Path $staging 'PoteHunter')" "/DReleaseTag=$Version" "/DInstallerOutput=$output" (Join-Path $PSScriptRoot 'packaging/PoteHunter.iss')
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed.'}
$installer=Join-Path $output "PoteHunter-$Version-Setup.exe"
$hash=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($installer))" | Set-Content -LiteralPath "$installer.sha256" -Encoding utf8
# Install into a disposable test folder with existing user data. Silent setup
# never launches the game, and no current installation is modified.
$test=Join-Path $staging 'install-test'
New-Item -ItemType Directory -Path $test | Out-Null
$sentinel='preserve-user-settings'
Set-Content -LiteralPath (Join-Path $test 'settings.json') -Value $sentinel
Set-Content -LiteralPath (Join-Path $test 'navigation-routes.json') -Value $sentinel
$process=Start-Process -FilePath $installer -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS',('/DIR="'+$test+'"') -Wait -PassThru -WindowStyle Hidden
if($process.ExitCode -ne 0){throw "Installer smoke test failed: $($process.ExitCode)"}
foreach($name in 'settings.json','navigation-routes.json'){
    if((Get-Content -LiteralPath (Join-Path $test $name) -Raw).Trim() -ne $sentinel){throw "Installer changed $name"}
}
if(!(Test-Path -LiteralPath (Join-Path $test 'PoteHunter.exe')) -or
    (Get-Content -LiteralPath (Join-Path $test 'release-version.txt') -Raw).Trim() -ne $Version){throw 'Installer payload/version missing.'}
# A second install tests upgrading without losing the same data.
$process=Start-Process -FilePath $installer -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS',('/DIR="'+$test+'"') -Wait -PassThru -WindowStyle Hidden
if($process.ExitCode -ne 0 -or (Get-Content (Join-Path $test 'settings.json') -Raw).Trim() -ne $sentinel){throw 'Installer upgrade smoke test failed.'}
$process=Start-Process -FilePath (Join-Path $test 'unins000.exe') -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait -PassThru -WindowStyle Hidden
if($process.ExitCode -ne 0 -or !(Test-Path (Join-Path $test 'settings.json')) -or !(Test-Path (Join-Path $test 'navigation-routes.json'))){throw 'Uninstall did not preserve user data.'}
@{Passed=$true;Version=$Version;Install=$true;Upgrade=$true;UninstallPreservesUserData=$true;SHA256=$hash} | ConvertTo-Json | Set-Content (Join-Path $output 'installer-checks.json')
