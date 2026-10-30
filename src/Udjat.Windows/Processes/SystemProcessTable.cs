using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Udjat.Core.Protection;

namespace Udjat.Windows.Processes;

/// <summary>
/// The real thing: the processes of this PC, and which account and session each one runs in, so that auto-protect can leave
/// other people's programs (and services) alone.
/// </summary>
public sealed class SystemProcessTable : IProcessTable
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenUser = 1;

    public ProcessOwner? Me { get; } = FindOwner(Environment.ProcessId);

    public IReadOnlyList<ProcessInfo> List()
    {
        var found = new List<ProcessInfo>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    found.Add(new ProcessInfo(process.Id, process.ProcessName));
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    // It ended while we were looking, or the system will not say; either way it is not ours to close.
                }
            }
        }

        return found;
    }

    public ProcessOwner? OwnerOf(ProcessInfo process) => FindOwner(process.Id);

    public void Kill(ProcessInfo info)
    {
        using var process = Process.GetProcessById(info.Id);

        // The id may have been given to another program since the list was made.
        if (!process.ProcessName.Equals(info.Name, StringComparison.OrdinalIgnoreCase) || OwnerOf(info) != Me)
            throw new InvalidOperationException("The process changed since it was listed.");

        process.Kill();
    }

    /// <summary>The session and account of a process, or null if it ended or Windows will not let Udjat ask.</summary>
    private static ProcessOwner? FindOwner(int processId)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero)
            return null;

        try
        {
            if (!ProcessIdToSessionId(processId, out var session) || !OpenProcessToken(process, TokenQuery, out var token))
                return null;

            try
            {
                var sid = UserSidOf(token);
                return sid is null ? null : new ProcessOwner((int)session, sid);
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>The account an access token belongs to, as a SID string. The same account has the same SID whether or not its token is elevated.</summary>
    private static string? UserSidOf(IntPtr token)
    {
        GetTokenInformation(token, TokenUser, IntPtr.Zero, 0, out var size);  // first call: how big the answer is
        if (size <= 0)
            return null;

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            // The answer starts with a pointer to the SID, inside the same buffer.
            return GetTokenInformation(token, TokenUser, buffer, size, out _)
                ? new SecurityIdentifier(Marshal.ReadIntPtr(buffer)).Value
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // System32 only: an elevated app must not load these libraries from anywhere a user could plant a copy.
    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool ProcessIdToSessionId(int processId, out uint sessionId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool GetTokenInformation(IntPtr token, int informationClass, IntPtr buffer, int length, out int returned);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool CloseHandle(IntPtr handle);
}
