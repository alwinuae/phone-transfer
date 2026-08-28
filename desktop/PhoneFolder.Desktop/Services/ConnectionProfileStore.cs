using PhoneFolder.Desktop.Models;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;

namespace PhoneFolder.Desktop.Services;

public static class ConnectionProfileStore
{
    private const string DefaultTargetName = "PhoneFolder/RememberedPhone";
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaximumProfiles = 8;

    public static RememberedConnection? Load() =>
        LoadAll()
            .Where(profile => profile.IsEnabled)
            .OrderByDescending(profile => profile.LastConnectedAt)
            .FirstOrDefault();

    public static IReadOnlyList<RememberedConnection> LoadAll()
    {
        var json = ReadCredential();
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            if (json.TrimStart().StartsWith('['))
            {
                return JsonSerializer.Deserialize<List<RememberedConnection>>(json) ?? [];
            }

            var legacy = JsonSerializer.Deserialize<RememberedConnection>(json);
            return legacy is null ? [] : [legacy];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(RememberedConnection connection)
    {
        if (IsDisabled)
        {
            return;
        }

        var profiles = LoadAll()
            .Where(profile => !SameDevice(profile, connection))
            .Append(connection)
            .OrderByDescending(profile => profile.LastConnectedAt)
            .Take(MaximumProfiles)
            .ToList();
        WriteCredential(JsonSerializer.Serialize(profiles));
    }

    public static void Delete(string certificateFingerprint)
    {
        if (IsDisabled)
        {
            return;
        }

        var normalized = NormalizeFingerprint(certificateFingerprint);
        var profiles = LoadAll()
            .Where(profile => NormalizeFingerprint(profile.CertificateFingerprint) != normalized)
            .ToList();
        if (profiles.Count == 0)
        {
            Delete();
        }
        else
        {
            WriteCredential(JsonSerializer.Serialize(profiles));
        }
    }

    public static void SetEnabled(string certificateFingerprint, bool enabled)
    {
        if (IsDisabled)
        {
            return;
        }

        var normalized = NormalizeFingerprint(certificateFingerprint);
        var profiles = LoadAll()
            .Select(profile => NormalizeFingerprint(profile.CertificateFingerprint) == normalized
                ? profile with { IsEnabled = enabled }
                : profile)
            .ToList();
        if (profiles.Count > 0)
        {
            WriteCredential(JsonSerializer.Serialize(profiles));
        }
    }

    public static void Delete()
    {
        if (IsDisabled || CredDelete(TargetName, CredentialTypeGeneric, 0))
        {
            return;
        }

        var error = Marshal.GetLastWin32Error();
        if (error != ErrorNotFound)
        {
            throw new Win32Exception(error);
        }
    }

    private static string? ReadCredential()
    {
        if (IsDisabled || !CredRead(TargetName, CredentialTypeGeneric, 0, out var pointer))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return null;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    private static void WriteCredential(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = TargetName,
                Comment = "Phone Transfer trusted device profiles",
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredentialPersistLocalMachine,
                UserName = "Phone Transfer"
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(blob);
        }
    }

    private static bool SameDevice(
        RememberedConnection left,
        RememberedConnection right) =>
        NormalizeFingerprint(left.CertificateFingerprint)
        == NormalizeFingerprint(right.CertificateFingerprint);

    private static string NormalizeFingerprint(string value) =>
        new(value.Where(Uri.IsHexDigit).ToArray());

    private static bool IsDisabled =>
        Environment.GetEnvironmentVariable("PHONEFOLDER_DISABLE_REMEMBERED_DEVICE") == "1";

    private static string TargetName =>
        Environment.GetEnvironmentVariable("PHONEFOLDER_CREDENTIAL_TARGET")
        ?? DefaultTargetName;

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string target,
        uint type,
        uint flags,
        out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Comment;
        public FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string UserName;
    }
}

public sealed record RememberedConnection(
    string Host,
    int Port,
    string Token,
    string CertificateFingerprint,
    string DeviceName,
    string TrustedToken = "",
    string ClientId = "",
    DateTimeOffset LastConnectedAt = default,
    bool IsEnabled = true,
    ConnectionMethod Method = ConnectionMethod.Manual)
{
    public string DisplayName => IsEnabled
        ? $"{DeviceName} ({Host})"
        : $"{DeviceName} ({Host}) - disabled";
}
