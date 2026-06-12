# Phone Transfer 0.7.1

Phone Transfer 0.7.1 completes the user-request audit and closes the remaining
multi-window, multi-phone, transfer-accounting, and playback-orientation gaps.

## New And Improved

- Router Wi-Fi and PC Mobile Hotspot now have separate discovery and connection
  controls. Android also advertises itself periodically while sharing.
- Uploads and downloads run in the background through independent connections.
  Up to three jobs can run concurrently while all browsing remains available.
- The Transfers window tracks each job's progress, speed, remaining time,
  completion state, phone, destination, and cancellation independently.
- Queue waiting time is excluded from speed and remaining-time calculations.
- The transfer monitor progress binding is explicitly one-way, preventing the
  window from closing the app when the first background job is displayed.
- Folders can be opened in separate windows for side-by-side navigation.
  Checkbox selection, drag/drop, and shared copy/cut/paste support multi-item
  workflows between those windows.
- Images, media, and documents can now be opened from every independent folder
  window. Viewer sessions keep their own phone connection.
- Clipboard and drag payloads carry the source-phone identity, preventing an
  item ID from one phone being pasted into another phone by mistake.
- Details-view checkbox cells remain editable while file metadata columns stay
  read-only, restoring checkbox multi-selection in main and folder windows.
- The Android API exposes video rotation metadata so the built-in Windows
  player starts videos in their recorded orientation.
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

1. Install `Phone-Transfer-Android-v0.7.1.apk`.
2. Install `Phone-Transfer-Windows-Setup-v0.7.1.exe`.
3. Start sharing on Android, then choose either Router Wi-Fi or PC hotspot on
   Windows. Trusting the phone once enables future access-code-free reconnect.

The Android package ID and signing key remain compatible with prior releases.
The Windows files are not Authenticode-signed and may trigger SmartScreen.
