using System.Xml.Linq;
using Udjat.Windows.Autostart;
using Xunit;

namespace Udjat.Tests;

/// <summary>A task scheduler that keeps its tasks in a dictionary.</summary>
internal sealed class FakeTaskScheduler : ITaskScheduler
{
    public Dictionary<string, string> Tasks { get; } = [];

    public int Creates { get; private set; }

    public string? QueryXml(string taskName) => Tasks.GetValueOrDefault(taskName);

    public void Create(string taskName, string xml)
    {
        Tasks[taskName] = xml;
        Creates++;
    }

    public void Delete(string taskName) => Tasks.Remove(taskName);
}

/// <summary>Tests for <see cref="AutostartTask"/>. No real task is ever registered here.</summary>
public class AutostartTaskTests
{
    private const string Trusted = @"C:\Program Files";
    private const string Exe = @"C:\Program Files\Udjat\Udjat.exe";
    private const string User = @"PC\salah";

    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private static string Value(XDocument doc, params string[] path) =>
        path.Aggregate((XElement)doc.Root!, (element, name) => element.Element(Ns + name)!).Value;

    // --- The task definition ---------------------------------------------------------

    [Fact]
    public void TheTask_StartsAtLogon_WithHighestRights_AndNoTimeLimit()
    {
        var doc = XDocument.Parse(AutostartTask.BuildXml(Exe, User));

        Assert.Equal("HighestAvailable", Value(doc, "Principals", "Principal", "RunLevel"));
        Assert.Equal("InteractiveToken", Value(doc, "Principals", "Principal", "LogonType"));
        Assert.Equal(User, Value(doc, "Principals", "Principal", "UserId"));
        Assert.Equal(User, Value(doc, "Triggers", "LogonTrigger", "UserId"));  // this user's logon only
        Assert.Equal("true", Value(doc, "Triggers", "LogonTrigger", "Enabled"));
        Assert.Equal("PT0S", Value(doc, "Settings", "ExecutionTimeLimit"));    // the default would end Udjat after 72 hours
        Assert.Equal("false", Value(doc, "Settings", "DisallowStartIfOnBatteries"));
        Assert.Equal("false", Value(doc, "Settings", "StopIfGoingOnBatteries"));
        Assert.Equal("IgnoreNew", Value(doc, "Settings", "MultipleInstancesPolicy"));
        Assert.Equal(Exe, Value(doc, "Actions", "Exec", "Command"));
        Assert.Equal("--tray", Value(doc, "Actions", "Exec", "Arguments"));
        Assert.Equal(@"\Udjat", Value(doc, "RegistrationInfo", "URI"));
    }

    [Fact]
    public void SpecialCharacters_InThePathOrUser_AreEscaped()
    {
        const string awkwardExe = @"C:\Tools\A&B <test>\Udjat.exe";
        const string awkwardUser = @"PC\Sal&ah";

        var xml = AutostartTask.BuildXml(awkwardExe, awkwardUser);

        Assert.Equal(awkwardExe, AutostartTask.ExePathOf(xml));
        Assert.Equal(awkwardUser, AutostartTask.UserOf(xml));
    }

    [Fact]
    public void ReadingATaskDefinition_ForgivesQuotesAndRefusesGarbage()
    {
        var quoted = AutostartTask.BuildXml("\"" + Exe + "\"", User);

        Assert.Equal(Exe, AutostartTask.ExePathOf(quoted));
        Assert.Null(AutostartTask.ExePathOf("this is not xml"));
        Assert.Null(AutostartTask.UserOf("<Task></Task>"));
    }

    // --- Keeping the task in step with the setting -----------------------------------------

    [Fact]
    public void Wanted_AndMissing_CreatesTheTask_Once()
    {
        var scheduler = new FakeTaskScheduler();
        var task = new AutostartTask(scheduler, Trusted);

        Assert.Equal(AutostartChange.Created, task.Reconcile(true, Exe, User));
        Assert.Equal(AutostartChange.None, task.Reconcile(true, Exe, User));  // already right

        Assert.Equal(1, scheduler.Creates);
        Assert.True(task.IsEnabled);
        Assert.Equal(Exe, AutostartTask.ExePathOf(scheduler.Tasks["Udjat"]));
    }

    [Fact]
    public void Wanted_ButPointingAtAnotherExe_ReplacesTheTask()
    {
        var scheduler = new FakeTaskScheduler();
        var task = new AutostartTask(scheduler, Trusted);
        task.Reconcile(true, @"C:\Program Files\Old\Udjat.exe", User);

        var change = task.Reconcile(true, Exe, User);

        Assert.Equal(AutostartChange.Created, change);
        Assert.Equal(Exe, AutostartTask.ExePathOf(scheduler.Tasks["Udjat"]));
    }

    [Fact]
    public void Wanted_ButForAnotherUser_ReplacesTheTask()
    {
        var scheduler = new FakeTaskScheduler();
        var task = new AutostartTask(scheduler, Trusted);
        task.Reconcile(true, Exe, @"PC\someone-else");

        Assert.Equal(AutostartChange.Created, task.Reconcile(true, Exe, User));
        Assert.Equal(User, AutostartTask.UserOf(scheduler.Tasks["Udjat"]));
    }

    [Fact]
    public void ADifferenceInLetterCase_IsNotADifference()
    {
        var scheduler = new FakeTaskScheduler();
        var task = new AutostartTask(scheduler, Trusted);
        task.Reconcile(true, Exe, User);

        Assert.Equal(AutostartChange.None, task.Reconcile(true, Exe.ToUpperInvariant(), User.ToLowerInvariant()));
    }

    [Fact]
    public void NotWanted_AndPresent_RemovesTheTask()
    {
        var scheduler = new FakeTaskScheduler();
        var task = new AutostartTask(scheduler, Trusted);
        task.Reconcile(true, Exe, User);

        Assert.Equal(AutostartChange.Removed, task.Reconcile(false, Exe, User));
        Assert.False(task.IsEnabled);
        Assert.Empty(scheduler.Tasks);
    }

    [Fact]
    public void NotWanted_AndAbsent_DoesNothing()
    {
        var scheduler = new FakeTaskScheduler();

        Assert.Equal(AutostartChange.None, new AutostartTask(scheduler, Trusted).Reconcile(false, Exe, User));
        Assert.Equal(0, scheduler.Creates);
    }

    // --- Only a folder standard users cannot write to may be started with full rights -----------

    [Theory]
    [InlineData(@"C:\Udjat\Udjat.exe")]                          // a folder any account can create under C:\
    [InlineData(@"C:\Users\someone\Downloads\Udjat.exe")]
    [InlineData(@"C:\Program Files Evil\Udjat.exe")]             // starts with the same letters, but is another folder
    [InlineData(@"C:\Program Files\..\Evil\Udjat.exe")]          // climbs out of Program Files
    [InlineData(@"Udjat.exe")]                                   // a relative path is not a place
    public void OutsideTheTrustedFolder_NoTaskIsMade(string exe)
    {
        var scheduler = new FakeTaskScheduler();

        Assert.Equal(AutostartChange.Refused, new AutostartTask(scheduler, Trusted).Reconcile(true, exe, User));

        Assert.Empty(scheduler.Tasks);
        Assert.Equal(0, scheduler.Creates);
    }

    [Fact]
    public void OutsideTheTrustedFolder_ATaskPointingOutsideIsDeleted()
    {
        var scheduler = new FakeTaskScheduler();
        scheduler.Tasks["Udjat"] = AutostartTask.BuildXml(@"C:\Udjat\Udjat.exe", User);

        Assert.Equal(AutostartChange.Refused, new AutostartTask(scheduler, Trusted).Reconcile(true, @"D:\Build\Udjat.exe", User));

        Assert.Empty(scheduler.Tasks);  // it was the way in for anyone who could replace that exe
    }

    [Fact]
    public void OutsideTheTrustedFolder_ATaskPointingInsideIsLeftAlone()
    {
        var scheduler = new FakeTaskScheduler();
        var task = new AutostartTask(scheduler, Trusted);
        task.Reconcile(true, Exe, User);

        // A build run from another folder must not repoint, or delete, the installed program's task.
        Assert.Equal(AutostartChange.Refused, task.Reconcile(true, @"D:\Build\Udjat.exe", User));

        Assert.Equal(Exe, AutostartTask.ExePathOf(scheduler.Tasks["Udjat"]));
    }

    [Fact]
    public void NotWanted_RemovesTheTask_FromAnywhere()
    {
        var scheduler = new FakeTaskScheduler();
        scheduler.Tasks["Udjat"] = AutostartTask.BuildXml(Exe, User);

        Assert.Equal(AutostartChange.Removed, new AutostartTask(scheduler, Trusted).Reconcile(false, @"D:\Build\Udjat.exe", User));
    }

    [Theory]
    [InlineData(@"C:\Program Files\Udjat\Udjat.exe", true)]
    [InlineData(@"c:\program files\udjat\udjat.exe", true)]       // letter case does not matter on Windows
    [InlineData(@"C:\Program Files\Udjat\..\Udjat\Udjat.exe", true)]
    [InlineData(@"C:\Program Files", false)]                     // the folder itself is not a file inside it
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsInside_ComparesFullPaths(string? file, bool expected) =>
        Assert.Equal(expected, AutostartTask.IsInside(file, Trusted));

    // --- The real Task Scheduler (opt-in) ---------------------------------------------------

    [Fact]
    public void TheRealScheduler_CanCreateQueryAndDeleteATask()
    {
        IntegrationGate.Require();
        var scheduler = new ComTaskScheduler();
        var name = "UdjatTest-" + Guid.NewGuid().ToString("N");

        // A harmless task for the test: a disabled trigger far in the future and normal rights, so it needs no
        // administrator. (A logon trigger, like the real task has, does need one.)
        var xml = $"""
            <Task version="1.2" xmlns="{Ns}">
              <Triggers><TimeTrigger><StartBoundary>2099-01-01T00:00:00</StartBoundary><Enabled>false</Enabled></TimeTrigger></Triggers>
              <Actions><Exec><Command>cmd.exe</Command><Arguments>/c exit</Arguments></Exec></Actions>
            </Task>
            """;

        try
        {
            Assert.Null(scheduler.QueryXml(name));

            scheduler.Create(name, xml);

            Assert.Contains("cmd.exe", scheduler.QueryXml(name));
        }
        finally
        {
            scheduler.Delete(name);
        }

        Assert.Null(scheduler.QueryXml(name));
        scheduler.Delete(name);  // deleting what is not there is fine
    }
}
