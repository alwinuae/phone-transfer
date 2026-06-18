param(
    [string]$Version = "0.7.5",
    [switch]$RequireBuiltArtifacts
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$buildScriptPath = Join-Path $root "scripts\build-release.ps1"
$installerScriptPath = Join-Path $root "installer\PhoneTransfer.iss"
$portableWorkflowPath = Join-Path $root ".github\workflows\publish-portable-windows.yml"
$releaseRoot = Join-Path $root "artifacts\release"
$portableArtifact = Join-Path $releaseRoot "Phone-Transfer-Windows-v$Version.exe"
$installerArtifact = Join-Path $releaseRoot "Phone-Transfer-Windows-Setup-v$Version.exe"
$androidArtifact = Join-Path $releaseRoot "Phone-Transfer-Android-v$Version.apk"
$checksumPath = Join-Path $releaseRoot "SHA256SUMS.txt"

function Assert-Condition([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

$buildScript = Get-Content -LiteralPath $buildScriptPath -Raw
Assert-Condition ($buildScript -match "dotnet publish") `
    "The internal Windows publish step required by the installer is missing."
Assert-Condition ($buildScript -match "Phone-Transfer-Windows-Setup-v") `
    "The installable Windows artifact is missing from the release build."
Assert-Condition ($buildScript -match "Phone-Transfer-Android-v") `
    "The Android artifact is missing from the release build."
Assert-Condition (-not ($buildScript -match '\$windowsArtifact')) `
    "The release build still defines or hashes a portable Windows artifact."
Assert-Condition (-not (Test-Path -LiteralPath $portableWorkflowPath)) `
    "The portable Windows GitHub release workflow still exists."

$installerScript = Get-Content -LiteralPath $installerScriptPath -Raw
Assert-Condition (($installerScript | Select-String -Pattern 'MultiSelectModel' -AllMatches).Matches.Count -ge 6) `
    "The Explorer context-menu verbs are missing multi-select support."
Assert-Condition (($installerScript | Select-String -Pattern '%\*' -AllMatches).Matches.Count -ge 6) `
    "The Explorer context-menu verbs must forward all selected paths with %*."
Assert-Condition (-not ($installerScript -match '""%1""')) `
    "The Explorer context-menu verbs still forward only one selected path."
Assert-Condition ($installerScript -match 'Phone Transfer \(Wi-Fi\)' -and $installerScript -match 'Phone Transfer \(Online\)') `
    "The Windows SendTo shortcuts for Wi-Fi and Online transfer are missing."

if ($RequireBuiltArtifacts) {
    Assert-Condition (Test-Path -LiteralPath $installerArtifact) `
        "The Windows setup artifact was not built."
    Assert-Condition (Test-Path -LiteralPath $androidArtifact) `
        "The Android APK was not built."
    Assert-Condition (-not (Test-Path -LiteralPath $portableArtifact)) `
        "A portable Windows artifact was unexpectedly produced."
    Assert-Condition (Test-Path -LiteralPath $checksumPath) `
        "SHA256SUMS.txt was not built."

    $checksumNames = Get-Content -LiteralPath $checksumPath |
        ForEach-Object { ($_ -split '\s+', 2)[1] }
    Assert-Condition ($checksumNames.Count -eq 2) `
        "SHA256SUMS.txt must contain exactly the setup EXE and APK."
    Assert-Condition ($checksumNames -contains (Split-Path -Leaf $installerArtifact)) `
        "The setup EXE checksum is missing."
    Assert-Condition ($checksumNames -contains (Split-Path -Leaf $androidArtifact)) `
        "The APK checksum is missing."
}

Write-Host "PASS: release packaging produces only the setup EXE and Android APK."
