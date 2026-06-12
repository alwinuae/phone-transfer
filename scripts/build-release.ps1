param(
    [string]$Version = "0.7.1"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$androidRoot = Join-Path $root "android"
$releaseRoot = Join-Path $root "artifacts\release"
$publishRoot = Join-Path $root "artifacts\publish\windows"
$signingRoot = Join-Path $root ".local-signing"
$keystore = Join-Path $signingRoot "phonefolder-release.jks"
$passwordFile = Join-Path $signingRoot "phonefolder-release.password"

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
New-Item -ItemType Directory -Path $signingRoot -Force | Out-Null

$javaHome = $env:JAVA_HOME
if ([string]::IsNullOrWhiteSpace($javaHome)) {
    $javaHome = Get-ChildItem -LiteralPath "C:\Program Files\Eclipse Adoptium" -Directory |
        Sort-Object Name -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
if ([string]::IsNullOrWhiteSpace($javaHome)) {
    throw "JDK 17 was not found."
}

$androidSdk = $env:ANDROID_HOME
if ([string]::IsNullOrWhiteSpace($androidSdk)) {
    $androidSdk = Join-Path $env:LOCALAPPDATA "Android\Sdk"
}

$env:JAVA_HOME = $javaHome
$env:ANDROID_HOME = $androidSdk

dotnet publish (Join-Path $root "desktop\PhoneFolder.Desktop\PhoneFolder.Desktop.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishRoot
if ($LASTEXITCODE -ne 0) {
    throw "Windows publish failed."
}

$windowsArtifact = Join-Path $releaseRoot "Phone-Transfer-Windows-v$Version.exe"
Copy-Item -LiteralPath (Join-Path $publishRoot "PhoneTransfer.exe") -Destination $windowsArtifact -Force

$iscc = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    "C:\Program Files\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($iscc)) {
    throw "Inno Setup 6 was not found. Install JRSoftware.InnoSetup with winget."
}

$installerScript = Join-Path $root "installer\PhoneTransfer.iss"
& $iscc `
    "/DMyAppVersion=$Version" `
    "/DSourceExe=$(Join-Path $publishRoot 'PhoneTransfer.exe')" `
    "/DOutputDir=$releaseRoot" `
    $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Windows installer build failed."
}
$windowsInstaller = Join-Path $releaseRoot "Phone-Transfer-Windows-Setup-v$Version.exe"

Push-Location $androidRoot
try {
    & (Join-Path $androidRoot "gradlew.bat") :app:lintRelease :app:assembleRelease --console=plain
    if ($LASTEXITCODE -ne 0) {
        throw "Android release build failed."
    }
} finally {
    Pop-Location
}

if (-not (Test-Path -LiteralPath $keystore)) {
    $passwordBytes = New-Object byte[] 24
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $random.GetBytes($passwordBytes)
    } finally {
        $random.Dispose()
    }
    $password = [Convert]::ToBase64String($passwordBytes).Replace("+", "A").Replace("/", "B")
    Set-Content -LiteralPath $passwordFile -Value $password -Encoding ASCII -NoNewline

    & (Join-Path $javaHome "bin\keytool.exe") `
        -genkeypair `
        -keystore $keystore `
        -storepass $password `
        -keypass $password `
        -alias phonefolder `
        -keyalg RSA `
        -keysize 4096 `
        -validity 10000 `
        -dname "CN=PhoneFolder, OU=Desktop and Android, O=PhoneFolder, C=US" `
        -noprompt
    if ($LASTEXITCODE -ne 0) {
        throw "Android signing-key generation failed."
    }
} else {
    $password = Get-Content -LiteralPath $passwordFile -Raw
}

$buildTools = Get-ChildItem -LiteralPath (Join-Path $androidSdk "build-tools") -Directory |
    Sort-Object { [version]$_.Name } -Descending |
    Select-Object -First 1
$apksigner = Join-Path $buildTools.FullName "apksigner.bat"
$unsignedApk = Join-Path $androidRoot "app\build\outputs\apk\release\app-release-unsigned.apk"
$androidArtifact = Join-Path $releaseRoot "Phone-Transfer-Android-v$Version.apk"

& $apksigner sign `
    --v4-signing-enabled false `
    --ks $keystore `
    --ks-key-alias phonefolder `
    --ks-pass "pass:$password" `
    --key-pass "pass:$password" `
    --out $androidArtifact `
    $unsignedApk
if ($LASTEXITCODE -ne 0) {
    throw "Android APK signing failed."
}
if (Test-Path -LiteralPath "$androidArtifact.idsig") {
    Remove-Item -LiteralPath "$androidArtifact.idsig" -Force
}

& $apksigner verify --verbose --print-certs $androidArtifact
if ($LASTEXITCODE -ne 0) {
    throw "Android APK signature verification failed."
}

$checksums = @(
    Get-FileHash -LiteralPath $windowsArtifact -Algorithm SHA256
    Get-FileHash -LiteralPath $windowsInstaller -Algorithm SHA256
    Get-FileHash -LiteralPath $androidArtifact -Algorithm SHA256
)
$checksumLines = $checksums | ForEach-Object {
    "$($_.Hash.ToLowerInvariant())  $(Split-Path -Leaf $_.Path)"
}
Set-Content -LiteralPath (Join-Path $releaseRoot "SHA256SUMS.txt") `
    -Value $checksumLines `
    -Encoding ASCII
Copy-Item -LiteralPath (Join-Path $root "docs\RELEASE_NOTES.md") `
    -Destination (Join-Path $releaseRoot "RELEASE_NOTES.md") `
    -Force

Write-Host "Release artifacts:"
Write-Host "  $windowsArtifact"
Write-Host "  $windowsInstaller"
Write-Host "  $androidArtifact"
