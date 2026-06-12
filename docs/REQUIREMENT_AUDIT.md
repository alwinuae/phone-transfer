# Phone Transfer User Requirement Audit

This checklist maps the user's delivered requests to version 0.7.1. It covers
the implementation requests made during development, not the optional future
ideas retained in `PRODUCT_REQUIREMENTS.md` and `ROADMAP.md`.

## Connection And Trust

- Router Wi-Fi discovery, manual address/port, and Windows Mobile Hotspot mode.
- Clear, separate router and hotspot connection controls.
- Saved trusted phones reconnect without the changing access code.
- Multiple trusted phones can be enabled, disabled, removed, and switched.
- Cryptographic device identity replaces MAC-based trust because modern
  devices randomize MAC addresses.
- Connection timeout diagnostics and reachable-endpoint probing.

## Browser And File Management

- Explorer-style Back, Up, Refresh, Escape, Alt+Left, F5, folder tree, and
  breadcrumb navigation, plus Ctrl+1/2/3 view shortcuts.
- Resizable and collapsible left folder pane and connection pane.
- Details, List, and Thumbnail views with file type, size, and modified time.
- Photo and video thumbnails with orientation correction.
- Folder/subfolder browsing, multiple selection checkboxes, create, rename,
  copy, cut, paste, move, and confirmed delete.
- Recursive upload/download, PC drag/drop upload, and phone-item drag/drop.
- Multiple independent folder windows for side-by-side navigation.
- Files can be viewed or opened from the main browser and every folder window.

## Transfers And Playback

- Up to three background uploads/downloads while browsing continues.
- Separate transfer monitor with phone, location, progress, speed, ETA,
  completion state, cancellation, and completed-item clearing.
- Large streaming buffers, keep-alive connections, resumable downloads, and
  temporary partial files.
- Direct photo viewing and video/audio streaming without a permanent PC copy.
- Default-app preference bypasses the Phone Transfer player when enabled.
- Image previous/next, zoom, reset, rotate, fullscreen, and mouse-wheel zoom.
- Video/audio play, pause, stop, seek, rotate, fullscreen, and default-app
  handoff. The center overlay disappears after playback starts.
- EXIF image orientation and Android video rotation metadata are applied.

## Android And Packaging

- Selected-folder mode and optional full shared-storage access toggle.
- Start/stop foreground sharing service, notification, and Quick Settings tile.
- Trusted-PC list and revocation on Android.
- Supplied logo applied to Windows, Android adaptive/round launcher, in-app
  branding, notification, and Quick Settings tile.
- Portable Windows EXE, installable Windows Setup EXE, and signed Android APK.
- Per-user Installed Apps entry includes Phone Transfer, version, publisher,
  icon, normal uninstall, and quiet uninstall metadata.
- Installer supports unattended Store validation with
  `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-`.
- GitHub source, documentation, checksums, and release artifacts.

## Platform Boundaries

- Android cannot expose protected system locations, private app data, or every
  `Android/data` path; the UI and documentation state this limitation.
- Native Wi-Fi Direct negotiation is not used. The requested direct workflow is
  implemented through Windows Mobile Hotspot, which keeps the original router
  Wi-Fi workflow available.
- Cross-phone direct copy is intentionally blocked because Android item IDs are
  phone-specific. Download then upload is the safe supported workflow.
