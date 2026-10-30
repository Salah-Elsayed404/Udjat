using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Udjat.Core.Settings;

namespace Udjat.Windows.Secrets;

/// <summary>
/// Reads and writes secrets in Windows Credential Manager (generic credentials, stored for this user on this PC).
/// A value is never put in an exception message; errors carry the Windows error code only.
/// </summary>
/// <param name="targetPrefix">
/// Names the entries "Udjat/SmtpPassword" and "Udjat/TelegramBotToken". Tests pass another prefix
/// so they never touch the real entries.
/// </param>
public sealed class SecretStore(string targetPrefix = "Udjat") : ISecretStore
{
    public const int MaxLength = ISecretStore.MaxLength;

    private const uint TypeGeneric = 1;
    private const uint PersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public string? Read(SecretName name)
    {
        if (!CredReadW(Target(name), TypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            return error == ErrorNotFound ? null : throw new Win32Exception(error);
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            try
            {
                return Encoding.Unicode.GetString(bytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Write(SecretName name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        if (value.Length > MaxLength)
            throw new ArgumentException($"A secret can be at most {MaxLength} characters.", nameof(value));

        var bytes = Encoding.Unicode.GetBytes(value);
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = TypeGeneric,
                TargetName = Target(name),
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = "Udjat",
            };

            if (!CredWriteW(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);  // wipe the native copy too
            Marshal.FreeHGlobal(blob);
        }
    }

    public void Delete(SecretName name)
    {
        if (CredDeleteW(Target(name), TypeGeneric, 0))
            return;

        var error = Marshal.GetLastWin32Error();
        if (error != ErrorNotFound)
            throw new Win32Exception(error);
    }

    private string Target(SecretName name) => $"{targetPrefix}/{name}";

    // The Windows CREDENTIALW structure.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    // System32 only: an elevated app must not load these libraries from anywhere a user could plant a copy.
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool CredWriteW(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool CredDeleteW(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void CredFree(IntPtr buffer);
}
