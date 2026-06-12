param(
    [Parameter(Mandatory = $true)]
    [string]$SignToolPath,

    [Parameter(Mandatory = $true)]
    [string]$TrustedSigningDlibPath,

    [Parameter(Mandatory = $true)]
    [string]$MetadataJsonPath,

    [Parameter(Mandatory = $true)]
    [string[]]$Files
)

$ErrorActionPreference = "Stop"
foreach ($file in $Files) {
    if (-not (Test-Path -LiteralPath $file)) {
        throw "Signing input does not exist: $file"
    }
    & $SignToolPath sign `
        /v `
        /debug `
        /fd SHA256 `
        /tr "http://timestamp.acs.microsoft.com" `
        /td SHA256 `
        /dlib $TrustedSigningDlibPath `
        /dmdf $MetadataJsonPath `
        $file
    if ($LASTEXITCODE -ne 0) {
        throw "Trusted Signing failed for $file."
    }
    & $SignToolPath verify /pa /v $file
    if ($LASTEXITCODE -ne 0) {
        throw "Authenticode verification failed for $file."
    }
}
