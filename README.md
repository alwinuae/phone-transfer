# Phone Transfer

Phone Transfer is a local-first Windows and Android application for browsing,
managing, streaming, and transferring files over a normal Wi-Fi router or a
Windows Mobile Hotspot. File contents stay on the local network.

## Version 0.7.6

Version 0.7.6 fixes stale connection/storage status after the phone drops
off, adds transfer timestamps and quick open/reveal actions, shows how the
phone is connected, and adds a Dark/Light/system theme setting.

- The left connection pane now detects a lost connection and updates within
  seconds instead of continuing to show a stale "Connected" state; phone
  storage usage refreshes automatically while connected.
- The left connection pane shows how the current phone is connected (router
  Wi-Fi, PC hotspot, manual address, or online address).
- The Transfers window shows when each item was sent, using your Windows
  regional date/time format, plus Open and Show in folder buttons per row.
- Setup now has a Theme option: Dark, Light, or match the Windows setting.
- Added a GitHub Actions release workflow that builds and publishes the
  Windows installer and Android APK automatically on a version tag.

## Version 0.7.5

Version 0.7.5 fixes the Windows send-to-phone cycles and keeps the 0.7.4
transfer workflow improvements, custom Windows caption fixes, responsive
single-row toolbars, and edge-to-edge charcoal theme.

- Explorer SendTo and right-click launches now forward into the already-open
  app, so selected files and folders are queued in one transfer window.
- Windows right-click verbs use multi-select `%*` forwarding for Wi-Fi and
  Online sends instead of only receiving one selected path.
- Dropping files or folders onto the app now sends them to phone Downloads by
  default.
- The Quick action menu includes separate file and folder sends to phone
  Downloads.
- Internet transfer prompts for a reachable online, VPN, or tunnel address and
  connects directly with the existing access code/trusted device flow.
- Connected-phone certificate text is grouped so it wraps inside the left pane.
- Sleek Notepad++-inspired dark desktop theme with dark Windows title bars.
- Working minimize, maximize, restore, and close controls on every form.
- Responsive fixed-height toolbars shrink cleanly without scrolling or overlap.
- Grouped Upload, Select, and Quick action dropdowns keep related commands
  together.
- Quick actions can send PC files to phone Downloads or download selected
  phone items to Windows Downloads.
- Android share-sheet support queues shared files/text for automatic download
  to the connected laptop.
- Windows right-click and SendTo entries send files or folders to the phone
  over Wi-Fi or an online/VPN/tunnel address.
- Dropping files onto the app can send them directly to phone Downloads.
- Clickable breadcrumbs and `Ctrl+Tab` make open folders easier to navigate.
- `Ctrl+X` now visibly marks files and folders waiting to be pasted.
- Duplicate upload and new-folder name conflicts keep both items with
  numbered names.
- Subtle hover animation on command and title-bar buttons.
- Main and independent folder windows support Details, List, Tiles, and small,
  medium, or large icon views.
- Compact details rows show more files in browser, trusted-phone, and transfer
  tables.
- Explorer-style sorting is available by name, date modified, type, and size.
- Right-click menus expose Open, Refresh, Copy, Cut, Paste, Move, Rename,
  Delete, Sort, and View commands.
- Phone storage usage and available space are visible while connected.
- PDF and common Office/document files receive generated thumbnail previews.
- Duplicate copy/move conflicts keep both items with numbered names.
- Closing one of several Phone Transfer windows keeps and activates the most
  recently used remaining window.
- Copy, cut, paste, copy-to, move-to, rename, delete, and download follow
  standard selection-aware enabled/disabled behavior.
- Transfer percentages are centered and clipped inside a taller progress bar.
- Background transfer queue with up to three concurrent uploads/downloads.
- Transfer window with phone, location, progress, speed, ETA, cancellation, and history.
- Separate router Wi-Fi and PC hotspot discovery workflows.
- Passive Android announcements improve discovery on restrictive Wi-Fi routers.
- Multiple independent folder windows with drag/drop and copy/cut/paste.
- Files open and stream from every independent folder window.
- Cross-phone clipboard mistakes are blocked with a clear recovery message.
- Checkbox selection works across all six Explorer-style views.
- Windows default-app mode bypasses the Phone Transfer player completely.
- Explicit Windows Installed Apps publisher and uninstall registration.
- Explorer-style browsing with folder tree and six view modes.
- Ctrl+1 through Ctrl+6 switch between the six file views.
- Drag-and-drop upload, recursive transfer, resume, copy, move, rename, and delete.
- Optional Windows default-app opening for documents, images, audio, and video.
- In-app image zoom, previous/next, rotation, and fullscreen controls.
- In-app video/audio streaming with seek, rotate, and fullscreen controls.
- Orientation-correct image and video thumbnails.
- Video playback starts with the phone's stored rotation metadata.
- Resizable/collapsible folder navigation and Explorer keyboard shortcuts.
- Optional Android full shared-storage access or one approved-folder mode.
- Android Quick Settings start/stop tile.
- Secure one-time pairing and trusted-device reconnect after code changes.
- Multiple-phone switching with per-phone trust enable/disable and removal.
- Live speed, progress, and ETA for upload and download.
- Router and Windows 5 GHz Mobile Hotspot modes.
- Installable Windows setup and signed Android APK.
- Rounded Windows branding and Android adaptive launcher icon.

## Quick Start

1. Install the Android APK and Windows Setup EXE from the latest GitHub release.
2. Put both devices on the same router, or connect the phone to the PC's
   Windows Mobile Hotspot.
3. On Android, choose a folder or enable full shared-storage access, then tap
   **Start sharing**. The Quick Settings tile can start or stop sharing later.
4. On Windows, select a discovered phone or enter its address, port, and access
   code, then choose **Connect**.
5. Drag files into Phone Transfer to upload them. Use the toolbar or keyboard
   shortcuts to download, copy, move, rename, delete, preview, or open files.
6. After the first trusted connection, reconnect without the changing access
   code. Use **Setup and connection > Trusted phones** to enable, disable, or
   remove saved phones.

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
artifacts/release/Phone-Transfer-Windows-Setup-v0.7.6.exe
artifacts/release/Phone-Transfer-Android-v0.7.6.apk
artifacts/release/SHA256SUMS.txt
```

Pushing a `v*` tag also runs `.github/workflows/release.yml`, which builds
both artifacts on a `windows-latest` runner and publishes them to the
matching GitHub Release. It signs the Android APK with the keystore in the
`ANDROID_KEYSTORE_BASE64`/`ANDROID_KEYSTORE_PASSWORD` repository secrets; if
those secrets are not set yet, the workflow generates a new keystore, uploads
it as a workflow artifact for you to save, and future runs will keep
generating a new one until you add it as secrets (do this once, or every
release invalidates the previous APK as an update target).

## Verification

```powershell
dotnet run --project .\tests\PhoneFolder.IntegrationTests `
  -c Release -- <host> 8765 <access-code> .\artifacts\integration

dotnet run --project .\tests\PhoneFolder.PerformanceTests `
  -c Release -- <host> 8765 <access-code> .\artifacts\performance <source-file>

dotnet run --project .\tests\PhoneFolder.UiLayoutTests -c Release

.\scripts\test-windows-ui.ps1 `
  -ExePath .\artifacts\publish\windows\PhoneTransfer.exe `
  -AccessCode <access-code>

.\tests\Validate-ReleasePackaging.ps1 -RequireBuiltArtifacts
```

See [release notes](docs/RELEASE_NOTES.md),
[requirements](docs/PRODUCT_REQUIREMENTS.md),
[architecture](docs/ARCHITECTURE.md), [testing](docs/TESTING.md), and the
[local API](protocol/openapi.yaml). The delivered user-request checklist is in
[the requirement audit](docs/REQUIREMENT_AUDIT.md). Microsoft Store signing
and MSIX migration steps are in [the Store guide](docs/MICROSOFT_STORE.md).
