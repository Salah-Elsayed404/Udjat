using System.Security.AccessControl;
using System.Security.Principal;

namespace Udjat.Windows.Storage;

/// <summary>
/// The folder for Udjat's settings file and alert log: <c>%ProgramData%\Udjat</c>. Udjat runs with administrator rights, so
/// whatever it writes there must not be redirectable by a program running as a normal user (for example by replacing the
/// folder with a junction that points at a system file). Only administrators and SYSTEM can change this folder; everyone can
/// read it, so the log opens in a normal Notepad. The rules are in docs/REVIEW.md (S2).
/// </summary>
public static class DataFolder
{
    private static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    // Everything that lets an account add, replace, delete or re-protect what is in the folder.
    private const FileSystemRights Changes =
        FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes |
        FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Udjat");

    /// <summary>
    /// Makes sure the folder exists and is safe to write to as an administrator. A missing folder is created with the right
    /// permissions in one step. A folder that other accounts could change (one a normal user created first, a junction, or
    /// one with loose permissions) is renamed out of the way and replaced.
    /// </summary>
    /// <param name="alsoTrustedOwner">Only for tests: an account that may own the folder besides administrators and SYSTEM.</param>
    /// <exception cref="UnauthorizedAccessException">The folder still is not safe, so Udjat must not use it.</exception>
    public static void Prepare(string path, SecurityIdentifier? alsoTrustedOwner = null)
    {
        if (EntryExists(path) && !IsSafe(path, alsoTrustedOwner))
            Directory.Move(path, $"{path}.unsafe-{Guid.NewGuid():N}");

        if (!EntryExists(path))
            Create(path);

        // Checked again after creating: someone may have put their own folder there in between.
        if (!IsSafe(path, alsoTrustedOwner))
            throw new UnauthorizedAccessException($"The folder {path} can be changed by other accounts, so Udjat will not use it. Rename or delete it, then start Udjat again.");
    }

    /// <summary>
    /// True if the folder is a real folder (not a junction or link), is owned by an administrator or SYSTEM, and no one else
    /// has any right to change it.
    /// </summary>
    public static bool IsSafe(string path, SecurityIdentifier? alsoTrustedOwner = null)
    {
        var folder = new DirectoryInfo(path);
        if (!folder.Exists || folder.Attributes.HasFlag(FileAttributes.ReparsePoint))
            return false;

        var security = folder.GetAccessControl();
        var owner = security.GetOwner(typeof(SecurityIdentifier));
        if (owner is null || !(owner == System || owner == Administrators || owner == alsoTrustedOwner))
            return false;

        return security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Where(rule => rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & Changes) != 0)
            .All(rule => rule.IdentityReference == System || rule.IdentityReference == Administrators);
    }

    /// <summary>Creates the folder with its own permissions (not inherited): SYSTEM and administrators full control, everyone else read only.</summary>
    private static void Create(string path)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(Rule(System, FileSystemRights.FullControl));
        security.AddAccessRule(Rule(Administrators, FileSystemRights.FullControl));
        security.AddAccessRule(Rule(Users, FileSystemRights.ReadAndExecute));
        new DirectoryInfo(path).Create(security);
    }

    private static FileSystemAccessRule Rule(SecurityIdentifier who, FileSystemRights rights) =>
        new(who, rights, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow);

    /// <summary>True if anything is at the path, including a link whose target is gone.</summary>
    private static bool EntryExists(string path)
    {
        try
        {
            File.GetAttributes(path);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }
}
