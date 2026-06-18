# Phone Transfer 0.7.4

Phone Transfer 0.7.4 is a transfer-workflow and Windows polish release
focused on faster send-to-phone actions, phone-to-PC sharing, and cleaner
navigation.

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
- Added `Ctrl+Tab` and `Ctrl+Shift+Tab` to cycle open phone-folder tabs.
- Added clickable breadcrumb navigation for direct jumps to parent folders.
- Added grouped Upload, Select, and Quick action dropdowns, including
  one-click download to PC Downloads and send to phone Downloads.
- Added Select all and Unselect all commands.
- Added visible cut-state styling after `Ctrl+X`.
- Added Android share-sheet support. Shared phone files/text queue in Phone
  Transfer and auto-download to the connected Windows Downloads folder.
- Added Windows SendTo and right-click entries for sending files/folders to
  the phone over Wi-Fi or an online/VPN/tunnel address.
- Added app-level drag/drop to send PC files to the phone Downloads folder.
- Added a dedicated Internet transfer setup option for reachable online,
  VPN, or tunnel addresses.
- Moved connected-phone details higher in the left pane and added a compact
  phone/app icon so connection text remains readable.
- Converted navigation and file-action toolbars to responsive single-row
  layouts. Buttons and labels scale down at compact widths instead of
  wrapping, overlapping, or introducing horizontal scrolling.
- Fixed duplicate-name upload and folder-create conflicts by keeping both
  items with Explorer-style numbered names.
- Fixed minimize, maximize, restore, and close actions across every custom
  dark title bar.
- Fixed the light flash/focus surface that could appear while switching tabs.
- Added subtle hover animation to normal, toolbar, and title-bar buttons.
- Kept the existing compact table rows, sorting, view modes, drag and drop,
  multi-selection, thumbnails, streaming, concurrent transfers, storage
  visibility, and trusted-phone reconnect behavior.

## Installation

1. Install `Phone-Transfer-Android-v0.7.4.apk`.
2. Install `Phone-Transfer-Windows-Setup-v0.7.4.exe`.
3. Start sharing on Android, then connect through router Wi-Fi or Windows
   Mobile Hotspot. Existing trusted-device profiles remain compatible.

The Windows Setup EXE is not Authenticode-signed. Do not submit the unsigned
EXE to the Microsoft Store under policy 10.2.9. Use the Trusted Signing or
MSIX workflow documented in `docs/MICROSOFT_STORE.md`.
