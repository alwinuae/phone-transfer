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
  -ExePath artifacts\release\Phone-Transfer-Windows-v0.7.1.exe `
  -HostAddress 127.0.0.1 `
  -AccessCode <access-code>
```

Android release validation:

```powershell
cd android
.\gradlew.bat :app:lintRelease :app:assembleRelease --console=plain
```

## 0.7.1 Verification Summary

- Windows and Android release builds completed with zero .NET warnings and no
  Android lint errors.
- Independent folder windows use the same viewer/default-app behavior as the
  main browser.
- Transfer timing begins when a job starts, and rows identify the phone and
  source/destination location.
- Opening the transfer monitor during an active upload no longer attempts to
  write into the read-only progress model.
- Cross-phone clipboard and drag/drop payloads are rejected before a wrong
  phone receives an invalid item ID.
- Android video rotation metadata is consumed by the built-in Windows player.
- The signed APK upgraded in place to version code `11` / version `0.7.1`;
  Android lint and signature verification passed.
- Full integration passed TLS pinning, trusted authentication, concurrent
  requests, recursive transfer, resume, byte-range streaming, thumbnails,
  rotation metadata, copy, move, rename, delete, and cleanup.
- The packaged Windows UI passed taskbar branding, router/hotspot controls,
  trusted reconnect, all three views, resizable/collapsible panes, background
  transfer monitoring, uploads, SHA-256 integrity, and automatic reconnect.
- A random 5 MiB sample averaged 21.54 MiB/s upload and 26.33 MiB/s download
  through the emulator/ADB forwarding path.
- Silent install, quiet uninstall, and final reinstall exited with code `0`.
  Installed Apps registered `Phone Transfer` version `0.7.1`, publisher
  `Alwin Thomas`, icon, normal uninstall, and quiet uninstall; the install
  directory contained only the app and Inno Setup uninstaller files.

## 0.7.0 Verification Summary

- Windows and Android release builds completed successfully.
- Router discovery now combines active probes with passive Android
  announcements; hotspot-only discovery remains adapter-scoped.
- Concurrent transfer jobs use independent authenticated HTTP clients and do
  not disable browsing or connection controls.
- Silent installer and Installed Apps metadata are validated against the
  per-user uninstall registry entry.
- The signed APK upgraded in place to version code `10` / version `0.7.0`.
- Full integration passed TLS pinning, trust, concurrent requests, recursive
  transfers, resume, streaming ranges, thumbnails, copy, move, and cleanup.
- A random 5 MiB sample averaged 26.94 MiB/s upload and 28.90 MiB/s download
  through the emulator/ADB forwarding test path.
- Silent setup exited with code `0` and registered `Phone Transfer` version
  `0.7.0`, publisher `Alwin Thomas`, normal uninstall, and quiet uninstall.

## 0.6.0 Verification Summary

- Windows solution build completed with zero warnings and zero errors.
- Trusted-phone tests verified save, enable, disable, automatic selection,
  switching, and deletion.
- Android release lint and compilation completed successfully with the adaptive
  launcher icon resources.
- Packaged Windows UI verification passed the Setup gear, default-app option,
  compact trusted-phone manager, image zoom controls, taskbar icon, connection,
  Explorer views, transfer integrity, and trusted reconnect.
- Full Android/Windows integration passed TLS identity, trust, recursive
  transfer, resume, byte-range streaming, thumbnails, copy, move, rename, and
  cleanup tests.
- The 5,534,041-byte MP4 sample averaged 11.01 MiB/s upload and 13.82 MiB/s
  download in the emulator/ADB forwarding environment.
- The Windows Setup EXE installed, launched version `0.6.0`, and uninstalled
  cleanly from an isolated per-user directory.

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
