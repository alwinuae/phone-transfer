# Phone Transfer 0.7.0

Phone Transfer 0.7.0 focuses on reliable discovery, uninterrupted concurrent
transfers, clearer connection choices, and Windows Store installer compliance.

## New And Improved

- Router Wi-Fi and PC Mobile Hotspot now have separate discovery and connection
  controls. Android also advertises itself periodically while sharing.
- Uploads and downloads run in the background through independent connections.
  Up to three jobs can run concurrently while all browsing remains available.
- The Transfers window tracks each job's progress, speed, remaining time,
  completion state, and cancellation independently.
- Folders can be opened in separate windows for side-by-side navigation.
  Checkbox selection, drag/drop, and shared copy/cut/paste support multi-item
  workflows between those windows.
- Enabling **Always open files in the Windows default application** completely
  bypasses the Phone Transfer player.
- Disconnected state, navigation outlines, connection grouping, and button
  labels have been clarified.
- The installer now registers explicit app name, version, publisher, icon, and
  uninstall information in Windows Installed Apps and supports silent setup
  with `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-`.
- Desktop, Android launcher, in-app, and Quick Settings branding remains based
  on the supplied laptop-to-phone logo.

## Installation

1. Install `Phone-Transfer-Android-v0.7.0.apk`.
2. Install `Phone-Transfer-Windows-Setup-v0.7.0.exe`.
3. Start sharing on Android, then choose either Router Wi-Fi or PC hotspot on
   Windows. Trusting the phone once enables future access-code-free reconnect.

The Android package ID and signing key remain compatible with prior releases.
The Windows files are not Authenticode-signed and may trigger SmartScreen.
