param([Parameter(Mandatory)][ValidatePattern('^Release1\.[0-9]+$')][string]$Version)
$ErrorActionPreference='Stop'
# CI-only credential, scoped to the public distribution repository. Never bundled.
if ([string]::IsNullOrWhiteSpace($env:PUBLIC_RELEASE_TOKEN)) {
    Write-Warning 'Public mirror pending: configure PUBLIC_RELEASE_TOKEN or upload the four release files to the public repository manually.'
    return
}
$env:GH_TOKEN=$env:PUBLIC_RELEASE_TOKEN
$env:GH_REPO='theblusmurf/PoteHunter-Releases'
try {
    $notes=@("$Version for Windows x64.",'')
    $capturing=$false
    foreach($line in (Get-Content -LiteralPath 'RELEASE-NOTES.md')) {
        if($line.StartsWith('## ')) {
            if($capturing){break}
            $capturing=$line -eq "## $Version"
            continue
        }
        if($capturing){$notes+=$line}
    }
    $notes+='Download the installer for installation/upgrades, or the ZIP for a portable copy. SHA256 sidecars accompany both files. Updates use this public feed without a GitHub account or token.'
    $notesPath=Join-Path $env:RUNNER_TEMP 'public-release-notes.md'
    $notes | Set-Content -LiteralPath $notesPath -Encoding utf8
    $names=@("PoteHunter-$Version-Setup.exe","PoteHunter-$Version-Setup.exe.sha256","PoteHunter-$Version-win-x64.zip","PoteHunter-$Version-win-x64.zip.sha256")
    $files=@($names | ForEach-Object {(Get-Item -LiteralPath (Join-Path 'release-assets' $_)).FullName})
    # Stage assets in a draft so latest never selects an incomplete mirror.
    & gh release view $Version --json isDraft 2>$null
    if($LASTEXITCODE -ne 0) {
        & gh release create $Version --draft --title $Version --notes-file $notesPath
        if($LASTEXITCODE -ne 0){throw 'Creating public draft failed.'}
    } else {
        throw 'Public release already exists; verify it before retrying the mirror.'
    }
    & gh release upload $Version @files
    if($LASTEXITCODE -ne 0){throw 'Public asset upload failed; draft left unpublished.'}
    & gh release edit $Version --draft=false --latest
    if($LASTEXITCODE -ne 0){throw 'Publishing public release failed.'}
} finally {
    Remove-Item Env:GH_TOKEN -ErrorAction SilentlyContinue
    Remove-Item Env:PUBLIC_RELEASE_TOKEN -ErrorAction SilentlyContinue
}
