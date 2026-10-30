using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using Udjat.Windows.Storage;
using Xunit;

namespace Udjat.Tests;

/// <summary>
/// Tests for <see cref="DataFolder"/>, in temporary folders. A test does not run as an administrator, so the folders it
/// makes are owned by the current user; each test says so by passing that user as the one extra account to trust.
/// </summary>
public class DataFolderTests
{
    private static readonly SecurityIdentifier Me = WindowsIdentity.GetCurrent().User!;
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    private static IEnumerable<FileSystemAccessRule> Rules(string path) =>
        new DirectoryInfo(path).GetAccessControl()
            .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>();

    [Fact]
    public void AMissingFolder_IsCreated_SoOnlyAdministratorsAndSystemCanChangeIt()
    {
        using var temp = new TempFolder();
        var path = temp.File("data");

        DataFolder.Prepare(path, Me);

        Assert.True(DataFolder.IsSafe(path, Me));
        var rules = Rules(path).ToList();
        Assert.DoesNotContain(rules, rule => rule.IsInherited);  // its own permissions, not the parent's
        var users = Assert.Single(rules, rule => rule.IdentityReference == Users);
        Assert.Equal(FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize, users.FileSystemRights);  // everyone may read
        Assert.Contains(rules, rule => rule.IdentityReference.Value == "S-1-5-18" && rule.FileSystemRights.HasFlag(FileSystemRights.FullControl));
        Assert.Contains(rules, rule => rule.IdentityReference.Value == "S-1-5-32-544" && rule.FileSystemRights.HasFlag(FileSystemRights.FullControl));
    }

    [Fact]
    public void AnOrdinaryFolder_IsNotSafe_AndIsMovedAside()
    {
        using var temp = new TempFolder();
        var path = temp.File("data");
        Directory.CreateDirectory(path);  // an ordinary folder: its owner and others can change what is in it
        File.WriteAllText(Path.Combine(path, "planted.txt"), "x");
        Assert.False(DataFolder.IsSafe(path, Me));

        DataFolder.Prepare(path, Me);

        Assert.True(DataFolder.IsSafe(path, Me));
        Assert.Empty(Directory.GetFiles(path));
        var aside = Assert.Single(Directory.GetDirectories(temp.Root, "data.unsafe-*"));
        Assert.True(File.Exists(Path.Combine(aside, "planted.txt")));  // nothing was deleted
    }

    [Fact]
    public void ALooseRightForOtherUsers_MakesAFolderUnsafe()
    {
        using var temp = new TempFolder();
        var path = temp.File("data");
        DataFolder.Prepare(path, Me);
        var info = new DirectoryInfo(path);
        var security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.Modify, AccessControlType.Allow));
        info.SetAccessControl(security);

        Assert.False(DataFolder.IsSafe(path, Me));

        DataFolder.Prepare(path, Me);  // repaired by replacing it
        Assert.True(DataFolder.IsSafe(path, Me));
    }

    [Fact]
    public void AJunction_IsNeverUsed_AndItsTargetIsLeftAlone()
    {
        using var temp = new TempFolder();
        var target = temp.File("somewhere-else");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "important.txt"), "keep me");
        var link = temp.File("data");
        MakeJunction(link, target);
        Assert.False(DataFolder.IsSafe(link, Me));

        DataFolder.Prepare(link, Me);

        Assert.True(DataFolder.IsSafe(link, Me));
        Assert.Empty(Directory.GetFiles(link));  // a real, empty folder now, not the target
        Assert.True(File.Exists(Path.Combine(target, "important.txt")));
    }

    [Fact]
    public void AFolderThatIsNotThere_IsNotSafe()
    {
        using var temp = new TempFolder();

        Assert.False(DataFolder.IsSafe(temp.File("nothing"), Me));
    }

    [Fact]
    public void TheDefaultFolder_IsUnderProgramData()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Udjat");

        Assert.Equal(expected, DataFolder.DefaultPath);
    }

    /// <summary>A junction needs no special rights, unlike a symbolic link.</summary>
    private static void MakeJunction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true };
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
