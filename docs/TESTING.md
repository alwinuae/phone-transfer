# Phone Transfer Testing

## Automated Commands

```powershell
dotnet build PhoneFolder.slnx -c Release

dotnet run --project tests\PhoneFolder.NetworkTests -c Release -- `
  127.0.0.1 8765

dotnet run --project tests\PhoneFolder.IntegrationTests -c Release -- `
  127.0.0.1 8765 <access-code> artifacts\integration

dotnet run --project tests\PhoneFolder.PerformanceTests -c Release -- `
  127.0.0.1 8765 <access-code> artifacts\performance <source-file>

.\scripts\test-windows-ui.ps1 `
  -ExePath artifacts\release\Phone-Transfer-Windows-v0.5.0.exe `
  -HostAddress 127.0.0.1 `
  -AccessCode <access-code>
```

Android release validation:

```powershell
cd android
.\gradlew.bat :app:lintRelease :app:assembleRelease --console=plain
```

## 0.5.0 Verification Summary

- Signed Android release installed over the prior release without clearing app
  data.
- Full selected-folder integration suite passed, including recursive phone-side
  copy, move, resume, trust, range streaming, thumbnails, and deletion.
- The packaged Windows UI suite passed with taskbar branding, connection and
  folder-pane collapse, all three views, copy action, trusted reconnect, and
  image previous/next/rotate/fullscreen controls.
- The installable Windows Setup EXE installed, launched version `0.5.0`, and
  uninstalled cleanly from an isolated per-user directory.
- The exact 5,534,041-byte MP4 sample averaged 24.85 MiB/s upload and
  20.81 MiB/s download in the emulator/ADB forwarding environment.
- Android Quick Settings exposed the live sharing tile alongside the system
  brightness panel and successfully toggled the foreground sharing service.

## 0.4.0 Verification Summary

Verified on June 9, 2026 with Android emulator `emulator-5554` and the packaged
Windows executable:

- Selected-folder and full shared-storage roots.
- Quick Settings tile start and stop.
- Individual Android trusted-computer revocation.
- Multiple Windows trusted-phone profiles and automatic reconnect.
- Recursive upload/download, resume, move, rename, delete, and malformed
  request handling.
- Image and video thumbnail generation.
- Complete and byte-range streaming through the loopback media proxy.
- Packaged Windows taskbar icon, Setup expander, collapsible sidebar, all view
  modes, native file picker, direct image preview, and automatic reconnect.
- Android release lint and APK v3 signature verification.

The exact sample `1000163476 (2).mp4` was 5,534,041 bytes. In the emulator/ADB
forwarding environment:

| Storage mode | Average upload | Average download | Peak upload |
| --- | ---: | ---: | ---: |
| Selected folder (SAF) | 9.35 MiB/s | 10.34 MiB/s | 14.91 MiB/s |
| Full shared storage | 33.33 MiB/s | 22.80 MiB/s | 39.71 MiB/s |

These figures compare storage backends in the test environment; physical phone
and Wi-Fi throughput will vary.

## Security Boundaries

- File APIs require the current access code or a trusted-device token.
- Windows pins the Android TLS certificate fingerprint.
- Trusted tokens are stored in Windows Credential Manager; Android stores only
  token hashes.
- Direct media playback binds its proxy to `127.0.0.1` and creates no permanent
  PC media copy.
- Full access covers Android shared storage only, never protected system
  locations or another app's private data.
