# Phone Transfer

Phone Transfer is a local-first Windows and Android application for browsing,
managing, streaming, and transferring files over a normal Wi-Fi router or a
Windows Mobile Hotspot. File contents stay on the local network.

## Version 0.5.0

- Explorer-style browsing with folder tree, Details, List, and Thumbnail views.
- Drag-and-drop upload, recursive transfer, resume, copy, move, rename, and delete.
- Default-app media streaming plus in-app image/video/audio controls.
- Orientation-correct image and video thumbnails.
- Resizable/collapsible folder navigation and Explorer keyboard shortcuts.
- Optional Android full shared-storage access or one approved-folder mode.
- Android Quick Settings start/stop tile.
- Secure one-time pairing and trusted-device reconnect after code changes.
- Multiple-phone switching and full trusted-device management on both apps.
- Live speed, progress, and ETA for upload and download.
- Router and Windows 5 GHz Mobile Hotspot modes.

## Android Storage Boundary

The full-access option covers accessible shared internal storage. Android does
not allow this app to expose protected system folders or another app's private
data. `Android/data` and similar locations can remain restricted depending on
the Android version and device vendor.

Trusted devices use a random cryptographic client ID and secret token rather
than a MAC address. Modern phones and PCs can randomize MAC addresses, so a MAC
is not a reliable or privacy-safe long-term application identity.

## Build

Prerequisites: .NET 10 SDK, JDK 17, Android SDK 36, and Inno Setup 6.

```powershell
.\scripts\build-release.ps1
```

Artifacts:

```text
artifacts/release/Phone-Transfer-Windows-v0.5.0.exe
artifacts/release/Phone-Transfer-Windows-Setup-v0.5.0.exe
artifacts/release/Phone-Transfer-Android-v0.5.0.apk
artifacts/release/SHA256SUMS.txt
```

## Verification

```powershell
dotnet run --project .\tests\PhoneFolder.IntegrationTests `
  -c Release -- <host> 8765 <access-code> .\artifacts\integration

dotnet run --project .\tests\PhoneFolder.PerformanceTests `
  -c Release -- <host> 8765 <access-code> .\artifacts\performance <source-file>

.\scripts\test-windows-ui.ps1 `
  -ExePath .\artifacts\release\Phone-Transfer-Windows-v0.5.0.exe `
  -AccessCode <access-code>
```

See [release notes](docs/RELEASE_NOTES.md),
[requirements](docs/PRODUCT_REQUIREMENTS.md),
[architecture](docs/ARCHITECTURE.md), [testing](docs/TESTING.md), and the
[local API](protocol/openapi.yaml).
