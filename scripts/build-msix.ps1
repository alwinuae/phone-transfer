param(
    [Parameter(Mandatory = $true)]
    [string]$IdentityName,

    [Parameter(Mandatory = $true)]
    [string]$Publisher,

    [string]$PublisherDisplayName = "ALWIN THOMAS",
    [string]$Version = "0.7.3.0",
    [string]$ArtifactName
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$toolProject = Join-Path $root "msix\PhoneTransfer.MsixTools.csproj"
$manifestTemplate = Join-Path $root "msix\AppxManifest.template.xml"
$assetRoot = Join-Path $root "msix\Assets"
$publishRoot = Join-Path $root "artifacts\publish\msix\win-x64"
$stagingRoot = Join-Path $root "artifacts\staging\msix"
$releaseRoot = Join-Path $root "artifacts\release"
$artifactVersion = ($Version -split '\.')[0..2] -join '.'
if ([string]::IsNullOrWhiteSpace($ArtifactName)) {
    $ArtifactName = "Phone-Transfer-Windows-Store-v$artifactVersion-x64.msix"
}
$artifact = Join-Path $releaseRoot $ArtifactName
$validationRoot = Join-Path $root "artifacts\validation\msix"

if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "MSIX version must contain four numeric parts, for example 0.7.3.0."
}
if (-not (Test-Path -LiteralPath $assetRoot)) {
    throw "MSIX visual assets are missing. Run scripts\generate-branding.py first."
}

dotnet restore $toolProject
if ($LASTEXITCODE -ne 0) {
    throw "Windows SDK Build Tools restore failed."
}

$globalPackagesLine = dotnet nuget locals global-packages --list
$globalPackages = ($globalPackagesLine -split ':', 2)[1].Trim()
$makeAppx = Get-ChildItem `
    -LiteralPath (Join-Path $globalPackages "microsoft.windows.sdk.buildtools") `
    -Recurse `
    -Filter makeappx.exe |
    Where-Object { $_.FullName -match '\\x64\\makeappx\.exe$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if ([string]::IsNullOrWhiteSpace($makeAppx)) {
    throw "MakeAppx.exe was not found in Microsoft.Windows.SDK.BuildTools."
}

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
    throw "Windows Store publish failed."
}

if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath (Resolve-Path $stagingRoot) -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
Copy-Item -Path (Join-Path $publishRoot "*") -Destination $stagingRoot -Recurse -Force
Copy-Item -LiteralPath $assetRoot -Destination (Join-Path $stagingRoot "Assets") -Recurse -Force

function Escape-Xml([string]$Value) {
    return [System.Security.SecurityElement]::Escape($Value)
}

$manifest = Get-Content -LiteralPath $manifestTemplate -Raw
$manifest = $manifest.Replace("__IDENTITY_NAME__", (Escape-Xml $IdentityName))
$manifest = $manifest.Replace("__PUBLISHER__", (Escape-Xml $Publisher))
$manifest = $manifest.Replace(
    "__PUBLISHER_DISPLAY_NAME__",
    (Escape-Xml $PublisherDisplayName))
$manifest = $manifest.Replace("__VERSION__", $Version)
Set-Content `
    -LiteralPath (Join-Path $stagingRoot "AppxManifest.xml") `
    -Value $manifest `
    -Encoding UTF8

if (Test-Path -LiteralPath $artifact) {
    Remove-Item -LiteralPath $artifact -Force
}
& $makeAppx pack /d $stagingRoot /p $artifact /o
if ($LASTEXITCODE -ne 0) {
    throw "MSIX packaging failed."
}
if (Test-Path -LiteralPath $validationRoot) {
    Remove-Item -LiteralPath (Resolve-Path $validationRoot) -Recurse -Force
}
& $makeAppx unpack /p $artifact /d $validationRoot /o
if ($LASTEXITCODE -ne 0) {
    throw "MSIX readback validation failed."
}
$validatedManifest = Test-Path -LiteralPath (Join-Path $validationRoot "AppxManifest.xml")
$validatedExecutable = Test-Path -LiteralPath (Join-Path $validationRoot "PhoneTransfer.exe")
if (-not $validatedManifest -or -not $validatedExecutable) {
    throw "MSIX readback validation did not contain the manifest and application."
}

Write-Host "Unsigned Store MSIX created:"
Write-Host "  $artifact"
Write-Host ""
Write-Host "Partner Center will sign this package during certification."
