# Microsoft Store Submission

## Why The 0.7.1 EXE Failed

Certification policy 10.2.9 requires the Win32 installer and every PE file it
contains to have a SHA-256 Authenticode signature chaining to a CA in the
Microsoft Trusted Root Program. A self-signed certificate does not satisfy this
policy. The 0.7.1 Setup EXE and bundled `PhoneTransfer.exe` were unsigned.

## Option A: Preserve The Existing Win32 Product

Use Microsoft Trusted Signing or a public CA code-signing certificate:

1. Sign `artifacts\publish\windows\PhoneTransfer.exe`.
2. Build the Inno Setup installer so the signed application is embedded.
3. Sign `Phone-Transfer-Windows-Setup-v0.7.5.exe`.
4. Verify both signatures with `signtool verify /pa /v`.
5. Upload the immutable signed Setup EXE to the versioned HTTPS URL and
   resubmit Product ID `4786b76b-9f7e-4932-a097-80d6563c4cdd`.

`scripts\trusted-signing-example.ps1` contains the official SignTool/Dlib
command shape. It requires a verified Trusted Signing account, certificate
profile, and Azure authentication.

## Option B: Move To MSIX

MSIX lets Microsoft sign and host the certified package:

1. In Partner Center, obtain the exact **Package/Identity/Name** and
   **Package/Identity/Publisher** values from Product identity.
2. Because the current product is a Win32 URL submission, release/delete its
   reserved app name before reserving the same name for an MSIX product, as
   instructed in the certification report.
3. Build the package:

```powershell
.\scripts\build-msix.ps1 `
  -IdentityName "<Package/Identity/Name>" `
  -Publisher "<Package/Identity/Publisher>" `
  -PublisherDisplayName "ALWIN THOMAS" `
  -Version "0.7.5.0"
```

4. Upload `artifacts\release\Phone-Transfer-Windows-Store-v0.7.5-x64.msix`
   directly on the Partner Center Packages page. An MSIX submission does not
   use the external Package URL field.

The identity values must match Partner Center exactly. Do not guess or reuse
the Product ID GUID as the package identity name.
