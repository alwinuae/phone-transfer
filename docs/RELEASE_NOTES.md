# Phone Transfer 0.7.2

Phone Transfer 0.7.2 is a focused Windows usability and visual-quality release.

## New And Improved

- Added a sleek Notepad++-inspired charcoal theme across Windows, forms,
  buttons, tables, lists, menus, selection states, disabled states, and
  supported Windows title bars.
- Rebuilt the main and folder toolbars with fixed-height controls and true
  wrapping. Buttons no longer shrink, lose spacing, or overlap while resizing.
- Added Details, List, Tiles, Small icons, Medium icons, and Large icons to the
  main browser and every independent folder window.
- Added sorting by name, date modified, type, and size, with ascending and
  descending directions.
- Reduced browser, trusted-phone, and transfer rows to 26 pixels.
- Added standard right-click Open, Refresh, clipboard, file-management, Sort,
  and View commands.
- Added PDF and common Office/document thumbnail previews.
- Added connected-phone storage usage and available-space visibility.
- Copy and move conflicts now retain both files with deterministic numbered
  names instead of failing.
- Closing one of several Phone Transfer windows now leaves the others active
  and brings the most recently used remaining window forward.
- Copy, Cut, Download, Copy to, Move to, Rename, Delete, and Paste now follow
  standard selection rules. Rename requires exactly one item, while Paste is
  available only when the Phone Transfer clipboard contains items.
- Added Copy to, Move to, and Rename operations to independent folder windows.
- Moved the transfer percentage into a taller custom progress track where it
  remains centered, legible, and clipped within the bar.
- Added automated WPF regression checks at multiple main and folder window
  widths, including overlap, sizing, view availability, command states, and
  progress rendering.
- Replaced Windows, Android launcher, in-app, and Quick Settings branding with
  the supplied laptop-and-phone logo.
- This release publishes only the installable Windows Setup EXE and signed APK;
  no portable Windows package is created.

## Installation

1. Install `Phone-Transfer-Android-v0.7.2.apk`.
2. Install `Phone-Transfer-Windows-Setup-v0.7.2.exe`.
3. Start sharing on Android, then connect through router Wi-Fi or Windows
   Mobile Hotspot. Existing trusted-device profiles remain compatible.

The Android package ID and signing key remain compatible with prior releases.
The Windows files are not Authenticode-signed and may trigger SmartScreen.

The GitHub Setup EXE remains unsigned. Do not resubmit that EXE to the Microsoft
Store under policy 10.2.9. Use the Trusted Signing workflow or build an MSIX
with the exact Partner Center identity values documented in
`docs/MICROSOFT_STORE.md`.
