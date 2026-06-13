# Phone Transfer 0.7.3

Phone Transfer 0.7.3 is a Windows interface refinement release focused on a
clean dark workspace and faster multi-folder navigation.

## New And Improved

- Replaced remaining light Windows surfaces with an edge-to-edge charcoal
  theme, including the title bar, frame, menus, scrollbars, checkboxes,
  expanders, tooltips, tables, lists, dialogs, and disabled states.
- Moved the existing Open, upload, download, folder, clipboard, rename,
  delete, view, and sorting commands into one clean top command strip.
- Removed the redundant File/Edit/View menu bar and the duplicate lower
  command band.
- Added phone-folder tabs. Open the current folder or a selected folder in a
  new tab, switch between locations, and retain each tab's path and back
  history.
- Added `Ctrl+T` to open a folder tab and `Ctrl+W` to close the active tab.
- Converted navigation and file-action toolbars to responsive single-row
  layouts. Buttons and labels scale down at compact widths instead of
  wrapping, overlapping, or introducing horizontal scrolling.
- Fixed minimize, maximize, restore, and close actions across every custom
  dark title bar.
- Added subtle hover animation to normal, toolbar, and title-bar buttons.
- Kept the existing compact table rows, sorting, view modes, drag and drop,
  multi-selection, thumbnails, streaming, concurrent transfers, storage
  visibility, and trusted-phone reconnect behavior.

## Installation

1. Install `Phone-Transfer-Android-v0.7.3.apk`.
2. Install `Phone-Transfer-Windows-Setup-v0.7.3.exe`.
3. Start sharing on Android, then connect through router Wi-Fi or Windows
   Mobile Hotspot. Existing trusted-device profiles remain compatible.

The Windows Setup EXE is not Authenticode-signed. Do not submit the unsigned
EXE to the Microsoft Store under policy 10.2.9. Use the Trusted Signing or
MSIX workflow documented in `docs/MICROSOFT_STORE.md`.
