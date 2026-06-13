param(
    [string]$Version = "0.7.3",
    [switch]$RequireBuiltArtifacts
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$buildScriptPath = Join-Path $root "scripts\build-release.ps1"
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
