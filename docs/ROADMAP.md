# Phone Transfer Delivery Roadmap

## Shipped In 0.6.0

- Setup gear and persistent Windows default-application preference.
- Image zoom controls, mouse-wheel zoom, and zoom keyboard shortcuts.
- Cleaner video playback overlay behavior and fullscreen toggling.
- Compact trusted-phone table with per-device enable/disable.
- Optimized Windows icon and Android adaptive/round launcher branding.

## Shipped In 0.5.0

- Default Windows media-app streaming handoff and improved in-app controls.
- Image navigation, fullscreen, rotation, and EXIF-aware orientation.
- Orientation-correct Android thumbnails for photos and videos.
- Resizable and collapsible folder navigation.
- Multi-item phone-side copy, move, and delete.
- Explorer keyboard shortcuts and distinct folder icons.
- Dynamic Android Start sharing / Stop sharing Quick Settings tile.
- Installable Windows Setup EXE while retaining the portable executable.

## Shipped In 0.4.0

- Local HTTPS transfer over a router or Windows Mobile Hotspot.
- Manual connection, LAN discovery, certificate pinning, and trusted reconnect.
- Multiple saved phones with switching and full trust-list management.
- Selected-folder SAF mode and optional full shared-storage mode.
- Explorer-style folder tree with Details, List, and Thumbnail views.
- Drag-and-drop, recursive upload/download, resume, rename, move, and delete.
- Direct photo, video, and audio streaming through a loopback-only Windows
  proxy.
- Android foreground sharing service and Quick Settings start/stop tile.
- Transfer speed, progress, and remaining-time display.
- Signed Android APK and self-contained Windows executable.

## Next Reliability Work

- Persist a visible multi-item transfer queue across Windows restarts.
- Add pause, retry, cancellation, and conflict policy controls per queue item.
- Add large-directory pagination and incremental thumbnail loading.
- Expand device coverage across Android vendors, SD cards, Windows 10, and
  Windows 11.
- Add upgrade, uninstall, low-storage, sleep, and network-roaming tests.
- Add Authenticode signing for the Windows executable.

## Next Performance Work

- Reuse larger pooled buffers where device testing shows a benefit.
- Add configurable parallel file transfers for folders containing many small
  files.
- Add adaptive concurrency based on Wi-Fi link speed and phone storage speed.
- Benchmark real 5 GHz and Wi-Fi 6 devices independently from emulator/ADB
  forwarding.
- Add optional checksum verification after transfer.

## Future Expansion

- Native Wi-Fi Direct negotiation without manual hotspot setup.
- Parallel active sessions and transfer queues for multiple phones.
- Selective folder synchronization and backup profiles.
- Photo timeline, metadata search, and persistent thumbnail cache.
- Windows Explorer shell integration.
- Optional end-to-end encrypted remote access.
- Additional desktop and mobile platforms.

## Release Gates

### 0.5

- Persistent transfer queue.
- Conflict choices: Replace, Skip, Keep Both, Cancel.
- Broader physical-device performance and interruption testing.

### 1.0

- Security review and threat-model verification.
- Supported Windows/Android version matrix completed.
- Authenticode-signed Windows package.
- Privacy policy, notices, upgrade tests, and distribution readiness review.

## Main Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Android providers expose inconsistent capabilities | Resume, rename, or move can differ by folder | Keep the SAF fallback capability-aware and test common providers |
| Routers block peer traffic | Discovery or connection fails | Manual address fallback, diagnostics, and Windows Mobile Hotspot mode |
| Android stops background sharing | Transfers disconnect | User-started foreground service, Quick Settings tile, and resumable downloads |
| Broad-storage permission is store-restricted | Google Play distribution may be blocked | Keep selected-folder mode fully functional and document direct APK distribution |
| Wi-Fi or phone storage is the bottleneck | Large transfers do not reach expected speed | Show measured speed/ETA and benchmark network and storage paths separately |
