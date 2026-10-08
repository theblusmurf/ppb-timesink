param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$Dotnet = 'dotnet',
    [string]$OutputDirectory,
    [string]$PackageSource = 'https://api.nuget.org/v3/index.json'
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^[0-9A-Za-z][0-9A-Za-z._-]{0,63}$' -or $Version -in '.', '..') {
    throw 'Version must be 1-64 characters, start with a letter or digit, and contain only letters, digits, periods, underscores, or hyphens.'
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot "artifacts/$Version"
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) {
    throw "Package output already exists: $OutputDirectory"
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("PoteHunter-package-" + [guid]::NewGuid().ToString('N'))
$packageRoot = Join-Path $temporaryRoot 'package'
$applicationRoot = Join-Path $packageRoot 'PoteHunter'
$testRoot = Join-Path $temporaryRoot 'test/PoteHunter'
$verifyOutput = $null
$verifyFailed = $false
$currentPowerShell = (Get-Process -Id $PID).Path
$packageSucceeded = $false

try {
    New-Item -ItemType Directory -Path $applicationRoot -Force | Out-Null

    $verifyOutput = & $currentPowerShell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Verify.ps1') -Dotnet $Dotnet 2>&1
    $verifyOutput | ForEach-Object { $_.ToString() } | Set-Content -LiteralPath (Join-Path $temporaryRoot 'verification-output.txt') -Encoding utf8
    if ($LASTEXITCODE -ne 0) {
        $verifyFailed = $true
        throw "Verify.ps1 failed with exit code $LASTEXITCODE."
    }

    & $currentPowerShell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Build.ps1') -Dotnet $Dotnet -OutputDirectory $applicationRoot -PackageSource $PackageSource
    if ($LASTEXITCODE -ne 0) {
        throw "Build.ps1 failed with exit code $LASTEXITCODE."
    }
    & (Join-Path $PSScriptRoot 'packaging/Test-AdminManifest.ps1') -Path (Join-Path $applicationRoot 'PoteHunter.exe')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'packaging/README.txt') -Destination (Join-Path $applicationRoot 'README.txt')
    Set-Content -LiteralPath (Join-Path $applicationRoot 'release-version.txt') -Value $Version -Encoding utf8
    if (Get-ChildItem -LiteralPath $applicationRoot -Recurse -File | Where-Object { $_.Name -like 'teleporter-profiles.json*' }) {
        throw 'Package payload contains private teleporter setup data. The package cannot be distributed.'
    }

    New-Item -ItemType Directory -Path (Split-Path -Parent $testRoot) | Out-Null
    Copy-Item -LiteralPath $applicationRoot -Destination $testRoot -Recurse
    $testStarted = [DateTime]::UtcNow
    $testProcess = Start-Process -FilePath (Join-Path $testRoot 'PoteHunter.exe') -ArgumentList '--self-test' -WorkingDirectory $testRoot -WindowStyle Hidden -Wait -PassThru
    if ($testProcess.ExitCode -ne 0) {
        throw "Packaged application self-test failed with exit code $($testProcess.ExitCode)."
    }
    $testReportPath = Join-Path $testRoot 'self-test.txt'
    if (!(Test-Path -LiteralPath $testReportPath) -or (Get-Item -LiteralPath $testReportPath).LastWriteTimeUtc -lt $testStarted) {
        throw 'The packaged application did not produce a fresh self-test report.'
    }
    $testReport = (Get-Content -LiteralPath $testReportPath -Raw).Trim()
    if (!$testReport.StartsWith('PASS:')) {
        throw "Unexpected packaged application self-test result: $testReport"
    }

    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
    $zipName = "PoteHunter-$Version-win-x64.zip"
    $zipPath = Join-Path $OutputDirectory $zipName
    Compress-Archive -LiteralPath $applicationRoot -DestinationPath $zipPath -CompressionLevel Optimal

    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $hashPath = "$zipPath.sha256"
    Set-Content -LiteralPath $hashPath -Value "$hash  $zipName" -Encoding utf8

    $reportPath = Join-Path $OutputDirectory "PoteHunter-$Version-package-report.txt"
    $reportLines = @(
        "PoteHunter package version: $Version"
        "Created (UTC): $([DateTime]::UtcNow.ToString('O'))"
        "Archive: $zipName"
        "SHA256: $hash"
        'Repository verification:'
        ($verifyOutput | ForEach-Object { $_.ToString() })
        'Packaged application verification:'
        'Executable manifest: requireAdministrator; UIAccess disabled.'
        $testReport
    )
    Set-Content -LiteralPath $reportPath -Value $reportLines -Encoding utf8

    Write-Host "Package complete: $zipPath"
    Write-Host "SHA256: $hashPath"
    Write-Host "Report: $reportPath"
    $packageSucceeded = $true
}
catch {
    if ($verifyFailed) {
        Write-Host 'Verify.ps1 captured output:'
        if ($verifyOutput) {
            $verifyOutput | ForEach-Object { Write-Host $_.ToString() }
        } else {
            Write-Host '(no captured output)'
        }
    }
    $diagnosticPaths = @(
        (Join-Path $PSScriptRoot 'PoteHunter/bin/Release/net10.0-windows/self-test.txt')
        (Join-Path $PSScriptRoot 'PoteHunter/bin/Release/net10.0-windows/ranged-ui-check.json')
    )
    if (Test-Path -LiteralPath $temporaryRoot) {
        $diagnosticPaths += Get-ChildItem -LiteralPath $temporaryRoot -Recurse -File -Include 'self-test.txt', 'ranged-ui-check.json' | ForEach-Object { $_.FullName }
    }
    foreach ($diagnosticPath in ($diagnosticPaths | Select-Object -Unique)) {
        if (Test-Path -LiteralPath $diagnosticPath -PathType Leaf) {
            Write-Host "Diagnostic tail: $diagnosticPath"
            Get-Content -LiteralPath $diagnosticPath -Tail 40 | ForEach-Object { Write-Host $_ }
        }
    }
    if ((Test-Path -LiteralPath $OutputDirectory) -and !(Get-ChildItem -LiteralPath $OutputDirectory -Force | Select-Object -First 1)) {
        Remove-Item -LiteralPath $OutputDirectory
    }
    Write-Warning "Packaging files were preserved for diagnosis at: $temporaryRoot"
    throw
}
finally {
    if ($packageSucceeded -and (Test-Path -LiteralPath $temporaryRoot)) {
        $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
        $resolvedSystemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        $temporaryLeaf = Split-Path -Leaf $resolvedTemporaryRoot
        if (!$resolvedTemporaryRoot.StartsWith($resolvedSystemTemp, [StringComparison]::OrdinalIgnoreCase) -or !$temporaryLeaf.StartsWith('PoteHunter-package-', [StringComparison]::Ordinal)) {
            throw "Refusing to clean unexpected temporary path: $resolvedTemporaryRoot"
        }
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}
