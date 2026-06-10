# Phone Transfer Product Requirements

## 1. Product Summary

Phone Transfer is a Windows application, supported by an Android companion app,
that provides secure wireless access to user-approved phone storage over a
shared local network. The Windows interface should feel familiar to users of
File Explorer and make common file operations straightforward.

Android storage can be exposed in either of two explicit modes:

- One folder approved through Android's system folder picker.
- Optional access to user-visible shared internal storage on Android 11 and
  newer. Protected system folders and private app data remain excluded.

## 2. Goals

1. Let users connect a Windows PC and Android phone without a USB cable.
2. Let users browse approved phone folders from Windows.
3. Support reliable file and folder transfer in both directions.
4. Keep file data on the local network unless the user explicitly chooses a
   future remote-access feature.
5. Make pairing and permissions understandable to a nontechnical user.
6. Recover cleanly from Wi-Fi changes, app suspension, and interrupted
   transfers.

## 3. Non-Goals

- Access to protected Android system folders or other apps' private data.
- Rooting, bootloader modification, or ADB-based access.
- Mobile screen mirroring, notification sync, SMS, contacts, or call control.
- Internet-based remote access in the MVP.
- iOS, macOS, or Linux clients in the MVP.
- Automatic full-device backup in the MVP.

## 4. Users

### Primary User

A Windows and Android owner who wants a simple way to move photos, documents,
videos, music, and project folders without a cable.

### Secondary User

A technically experienced user who regularly transfers large files and needs
queueing, resume support, transfer history, and predictable conflict handling.

## 5. Assumptions

- Both devices are usually connected to the same Wi-Fi router or LAN.
- The router permits client-to-client communication. Guest networks and access
  point isolation may prevent discovery or connection.
- The Android user installs and opens the companion app.
- The user either explicitly chooses folders or enables Android's special
  all-files access for broad shared-storage browsing.
- The Android app displays an ongoing notification while actively sharing or
  transferring files in the background.
- The initial release supports Android 10 and later and Windows 10 version 22H2
  and later, including Windows 11.

## 6. Core User Journeys

### 6.1 First-Time Setup

1. The user installs both applications.
2. The Android app explains why storage and local-network access are needed.
3. The user selects one or more folders with Android's system folder picker.
4. The user taps **Start sharing**.
5. The Windows app discovers the phone or accepts a manually entered IP
   address.
6. One device displays a one-time pairing code or QR code.
7. The user confirms that both devices show the same pairing information.
8. Both devices store a revocable trusted-device identity.
9. The Windows app opens the phone's approved roots.

### 6.2 Copy PC Content to Phone

1. The user opens a phone destination folder.
2. The user drags files or folders into the window or chooses **Upload**.
3. Phone Transfer checks available space and filename conflicts.
4. The transfer enters a visible queue.
5. The Android app writes into the approved destination.
6. The UI reports success, failure, cancellation, or partial completion.

### 6.3 Copy Phone Content to PC

1. The user selects phone files or folders.
2. The user chooses **Download** or drags them to a Windows destination.
3. The Windows app reserves a temporary partial file.
4. The transfer streams data and verifies size and integrity.
5. The temporary file is atomically renamed after successful completion.

### 6.4 Reconnect

1. A previously paired phone appears in the device list.
2. The user starts sharing on Android if it is not already active.
3. The apps authenticate using stored device credentials.
4. No pairing code is required unless trust was revoked or keys changed.

## 7. Functional Requirements

### 7.1 Android Storage Access

- `FR-STO-001`: Standard mode shall let the user add a folder using
  `ACTION_OPEN_DOCUMENT_TREE`.
- `FR-STO-002`: Standard mode shall request persistent read and write URI
  permissions when the selected provider supports them.
- `FR-STO-003`: The app shall show every shared root with a user-friendly name.
- `FR-STO-004`: The user shall be able to remove a shared root at any time.
- `FR-STO-005`: The app shall detect and explain revoked or unavailable
  storage access.
- `FR-STO-006`: File operations shall use content URIs and document-provider
  APIs rather than assuming raw filesystem paths.
- `FR-STO-007`: Unsupported protected locations shall return a clear
  permission error rather than appearing empty.
- `FR-STO-008`: The Android app shall offer an optional broad shared-storage
  mode using `MANAGE_EXTERNAL_STORAGE` when that mode is included in the
  distribution build.
- `FR-STO-009`: Broad mode shall require an explicit explanation and a
  user-controlled system-settings grant.
- `FR-STO-010`: The app shall work in standard mode when broad access is denied,
  unavailable, or removed from a distribution build.
- `FR-STO-011`: Broad mode shall not claim access to other apps' private
  directories, `Android/data`, protected system paths, or any location the OS
  continues to block.
- `FR-STO-012`: The project shall maintain separate build/distribution
  configuration if broad-access store policy requirements differ from direct
  distribution requirements.

### 7.2 Discovery and Connection

- `FR-CON-001`: The Android app shall advertise an mDNS/DNS-SD service while
  sharing is active.
- `FR-CON-002`: The Windows app shall discover compatible services on the
  current LAN.
- `FR-CON-003`: The Windows app shall support manual host and port entry when
  multicast discovery is unavailable.
- `FR-CON-004`: The UI shall distinguish offline, discovered, pairing,
  connected, busy, and error states.
- `FR-CON-005`: The connection shall survive ordinary IP address changes by
  rediscovering the paired device identity.
- `FR-CON-006`: The Android implementation shall support Android 17 local
  network permission behavior when targeting API level 37 or later.

### 7.3 Pairing and Trust

- `FR-PAIR-001`: An unpaired PC shall not be able to list filenames or file
  metadata.
- `FR-PAIR-002`: Initial pairing shall require confirmation on the Android
  device.
- `FR-PAIR-003`: Pairing shall use a short-lived, single-use code or QR payload.
- `FR-PAIR-004`: Each device shall generate and retain its own cryptographic
  identity.
- `FR-PAIR-005`: Trusted PCs shall be listed in the Android app with name and
  last-connected time.
- `FR-PAIR-006`: Trust can be revoked from either device.
- `FR-PAIR-007`: Re-pairing shall be required after identity loss or explicit
  revocation.

### 7.4 Browsing

- `FR-BRW-001`: The Windows app shall list folders before files by default.
- `FR-BRW-002`: Every item shall show name, type, size where available, and
  modified time where available.
- `FR-BRW-003`: The user shall be able to navigate with breadcrumbs, Back,
  Forward, Up, and Refresh.
- `FR-BRW-004`: The browser shall support sorting and name filtering.
- `FR-BRW-005`: The browser shall support multi-selection.
- `FR-BRW-006`: Large directories shall load incrementally or with pagination.
- `FR-BRW-007`: The UI shall not freeze during listing or transfer operations.
- `FR-BRW-008`: The app shall provide a properties view for a selected item.

### 7.5 File Operations

- `FR-FILE-001`: Upload individual files and recursively upload folders.
- `FR-FILE-002`: Download individual files and recursively download folders.
- `FR-FILE-003`: Create folders in writable shared roots.
- `FR-FILE-004`: Rename files and folders when supported by the document
  provider.
- `FR-FILE-005`: Delete files and folders only after explicit confirmation.
- `FR-FILE-006`: Deletion shall be capability-controlled and may be disabled
  globally or per shared root.
- `FR-FILE-007`: The app shall move files and folders between writable phone
  folders without routing their contents through the PC.
- `FR-FILE-008`: The app shall sanitize Windows-invalid names when downloading
  and report the resulting filename.
- `FR-FILE-009`: Uploads shall reject or safely transform names unsupported by
  the destination provider.

### 7.6 Transfer Queue

- `FR-TRN-001`: All transfers shall appear in a queue with source, destination,
  status, bytes transferred, total bytes, speed, and estimated time.
- `FR-TRN-002`: Users shall be able to pause, resume, cancel, retry, and clear
  completed items.
- `FR-TRN-003`: Transfers shall resume from a verified byte offset when both
  endpoints support random access.
- `FR-TRN-004`: Providers that do not support random access shall restart the
  affected file and explain why.
- `FR-TRN-005`: Partial downloads shall use a temporary filename.
- `FR-TRN-006`: The system shall support configurable parallelism, defaulting
  to two active file transfers.
- `FR-TRN-007`: Folder transfers shall continue past individual file failures
  and produce a final failure summary.
- `FR-TRN-008`: Before upload, the app shall check free space when the provider
  exposes reliable capacity information.
- `FR-TRN-009`: Completed file transfers shall verify byte count. Optional
  SHA-256 verification shall be available for high-integrity mode.

### 7.7 Conflict Handling

- `FR-CNF-001`: On a name conflict, offer Replace, Skip, Keep Both, and Cancel.
- `FR-CNF-002`: The user may apply a choice to all remaining conflicts in the
  current operation.
- `FR-CNF-003`: Replace shall write to a temporary target where possible and
  preserve the existing file until the new content is complete.
- `FR-CNF-004`: Keep Both shall generate a deterministic, valid filename.

### 7.8 History and Diagnostics

- `FR-HIS-001`: The Windows app shall retain a local transfer history.
- `FR-HIS-002`: History shall not contain file contents or authentication
  secrets.
- `FR-HIS-003`: Users shall be able to clear history.
- `FR-HIS-004`: Both apps shall provide exportable diagnostic logs with
  sensitive tokens and full file paths redacted by default.
- `FR-HIS-005`: Logs shall be bounded by size and age.

## 8. Windows Application Screens

### Device Screen

- Discovered phones.
- Trusted offline phones.
- Add by IP address.
- Pair, connect, forget, and connection diagnostics actions.

### File Browser

- Device and shared-root sidebar.
- Breadcrumb address bar.
- File list with Details and optional thumbnail view.
- Upload, Download, New Folder, Rename, Delete, Refresh, and Properties.
- Drag-and-drop from Windows into the current phone folder.

### Transfers

- Active, queued, failed, and completed sections.
- Per-item and aggregate progress.
- Pause, resume, cancel, retry, and reveal downloaded file.

### Settings

- Default Windows download directory.
- Transfer concurrency and bandwidth limit.
- Conflict default.
- Start with Windows and automatic reconnect, both off by default.
- Integrity verification.
- Theme and log controls.

## 9. Android Application Screens

### Home

- Sharing stopped/active status.
- Device name, network name, local address, and current port.
- Start/Stop sharing.
- Add shared folder.
- Current transfer summary.

### Shared Folders

- Storage mode: Standard or Broad shared storage.
- List of granted roots.
- Read/write capability and current availability.
- Enable or disable broad shared-storage access.
- Delete permission toggle.
- Remove access action.

### Pairing

- Pairing code or QR code.
- Requesting PC name and identity fingerprint.
- Approve and Reject actions.
- Automatic expiration countdown.

### Trusted Devices

- PC name, fingerprint, first paired, and last connected.
- Revoke access action.

### Settings and Diagnostics

- Require phone confirmation for every connection.
- Allow sharing on selected Wi-Fi networks only.
- Transfer limits.
- Diagnostic logs and app version.

## 10. Non-Functional Requirements

### Performance

- `NFR-PERF-001`: Begin showing a typical directory within one second after the
  API response starts.
- `NFR-PERF-002`: Sustain at least 70% of the practical TCP throughput available
  between the test devices for large sequential files.
- `NFR-PERF-003`: Support files larger than 4 GB without buffering the whole
  file in memory.
- `NFR-PERF-004`: Keep steady-state memory use under 250 MB on Windows and
  150 MB on Android during a single large transfer, excluding OS caches.

### Reliability

- `NFR-REL-001`: A process crash or Wi-Fi interruption shall not turn a partial
  download into an apparently complete file.
- `NFR-REL-002`: Transfer queue state shall survive Windows app restart.
- `NFR-REL-003`: The Android service shall stop advertising immediately when
  sharing is stopped.
- `NFR-REL-004`: File operations shall be idempotent where practical, using
  operation IDs to prevent accidental duplicate commits.

### Security and Privacy

- `NFR-SEC-001`: All post-discovery traffic shall be encrypted.
- `NFR-SEC-002`: Every request shall be authenticated and authorized to a
  paired device.
- `NFR-SEC-003`: Pairing secrets shall expire and shall not be reusable.
- `NFR-SEC-004`: Long-term private keys shall use Android Keystore and Windows
  Data Protection API or an equivalent OS-backed store.
- `NFR-SEC-005`: The Android server shall bind only to local interfaces.
- `NFR-SEC-006`: The protocol shall enforce path/root authorization using
  opaque item IDs, not client-supplied raw filesystem paths.
- `NFR-SEC-007`: Requests shall have size, time, and concurrency limits.
- `NFR-SEC-008`: The product shall contain no analytics or cloud telemetry by
  default.
- `NFR-SEC-009`: The Android UI shall clearly show when sharing is active.
- `NFR-SEC-010`: Discovery metadata shall not contain filenames or folder names.

### Accessibility and Usability

- Keyboard navigation shall cover all Windows file operations.
- Controls shall have accessible names and visible focus indicators.
- Progress and error states shall not rely on color alone.
- Destructive actions shall use clear language and confirmation.
- Errors shall include a user action such as Retry, Reconnect, Reauthorize, or
  Open Android App.

## 11. Error Cases

The product shall handle:

- Devices on different networks.
- Guest Wi-Fi or access point isolation.
- Windows firewall blocking inbound discovery replies.
- Android local-network permission denied or revoked.
- Storage grant revoked, SD card removed, or document provider unavailable.
- Android app suspended or foreground service stopped.
- Phone screen locked during a transfer.
- Wi-Fi roaming or IP address change.
- Destination out of space.
- Unsupported filenames or metadata.
- Source file changed during transfer.
- Permission denied during rename or delete.
- PC sleep, phone sleep, app crash, and forced process termination.

## 12. Acceptance Criteria for MVP

1. A clean Windows PC and Android phone can pair without entering an IP address
   on a normal home network.
2. A user can approve a Downloads or Documents folder and browse it on Windows.
3. An unpaired PC cannot retrieve the approved root list.
4. A nested folder containing at least 1,000 files can transfer in either
   direction with an accurate final summary.
5. A 1 GB transfer interrupted by Wi-Fi loss resumes where the provider permits
   random access.
6. Canceling a download leaves no file that appears complete.
7. Revoking a PC on Android prevents its next authenticated request.
8. The apps explain Android storage restrictions rather than claiming access to
   inaccessible folders.
9. Transfers work without an Internet connection when the local router remains
   available.
10. The Windows UI remains responsive during folder enumeration and transfer.

## 13. Future Enhancements

- Phone-to-phone and cross-platform desktop clients.
- Native Wi-Fi Direct negotiation without manually enabling Windows Mobile
  Hotspot.
- Selective folder synchronization.
- Photo timeline and thumbnail caching.
- Clipboard text transfer.
- Optional remote access with end-to-end encryption.
- Windows Explorer shell integration.
- Parallel active transfer sessions to multiple phones. Version 0.4.0 already
  stores multiple trusted phones and switches between them.
- Encrypted backup profiles and scheduled transfers.

## 14. Decisions to Confirm Before Implementation

The current recommendation is shown first:

- Product name: **Phone Transfer**.
- Windows framework: **WPF on .NET 10 LTS**.
- Minimum Android: **Android 10**.
- Distribution: direct installer and APK during development; store packaging
  after the MVP.
- Delete support: implemented behind a disabled-by-default capability.
- Transfer direction: both PC-to-phone and phone-to-PC in the MVP.
