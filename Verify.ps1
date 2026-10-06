param(
    [string]$Dotnet = 'dotnet'
)

$ErrorActionPreference = 'Stop'

foreach ($project in 'PoteHunter', 'PoteMemoryProbe') {
    $projectPath = Join-Path $PSScriptRoot "$project/$project.csproj"
    & $Dotnet build $projectPath -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed for $project with exit code $LASTEXITCODE."
    }
}

$output = Join-Path $PSScriptRoot 'PoteHunter/bin/Release/net10.0-windows'
$application = Join-Path $output 'PoteHunter.exe'
$report = Join-Path $output 'self-test.txt'
$started = [DateTime]::UtcNow
$process = Start-Process -FilePath $application -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) {
    throw "Offline self-tests failed with exit code $($process.ExitCode). See $report."
}
if (!(Test-Path -LiteralPath $report) -or (Get-Item -LiteralPath $report).LastWriteTimeUtc -lt $started) {
    throw "The self-test did not produce a fresh report at $report."
}
$result = Get-Content -LiteralPath $report -Raw
if (!$result.StartsWith('PASS:')) {
    throw "Unexpected self-test result: $result"
}
Write-Output $result
$deskProcess = Start-Process -FilePath $application -ArgumentList '--item-grade-desk-check',('"'+$output+'"') -WindowStyle Hidden -Wait -PassThru
if ($deskProcess.ExitCode -ne 0) { throw "Upgrade Desk checks failed. See $(Join-Path $output 'upgrade-desk-error.txt')." }
Write-Output 'Upgrade Desk data, native rendering and passive window checks passed.'
$uiProcess = Start-Process -FilePath $application -ArgumentList '--ranged-ui-check' -WindowStyle Hidden -Wait -PassThru
if ($uiProcess.ExitCode -ne 0) {
    throw "Ranged settings/layout checks failed. See $(Join-Path $output 'ranged-ui-check.json')."
}
Write-Output 'Verification passed: all three projects build, offline self-tests pass, and ranged settings/layout checks pass.'
