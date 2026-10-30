using System.Security.Principal;
using System.Xml.Linq;

namespace Udjat.Windows.Autostart;

/// <summary>What <see cref="AutostartTask.Reconcile"/> did.</summary>
public enum AutostartChange
{
    None,
    Created,
    Removed,

    /// <summary>Wanted, but Udjat runs from a folder that is not trusted, so no task was made (and an old task pointing elsewhere was deleted).</summary>
    Refused,
}

/// <summary>A thin wrapper over Windows Task Scheduler, so the rules in <see cref="AutostartTask"/> can be tested with a fake.</summary>
public interface ITaskScheduler
{
    /// <summary>The task's XML definition, or null if there is no such task.</summary>
    string? QueryXml(string taskName);

    /// <summary>Creates the task, or replaces it if it exists.</summary>
    void Create(string taskName, string xml);

    /// <summary>Deletes the task. Nothing happens if there is none.</summary>
    void Delete(string taskName);
}

/// <summary>
/// The "start Udjat when I log on" setting, as a Task Scheduler task. A scheduled task can start a program with
/// administrator rights at logon without a UAC prompt, which a Run registry key cannot. The task is defined in XML
/// because the plain command-line defaults are wrong for a watchdog: they stop a program after 72 hours and skip it on battery.
/// The task starts the exe with full rights at every logon, so it may only point into <paramref name="trustedFolder"/> (Program
/// Files), which standard users cannot change. A folder anyone can write to would let them swap the exe and get administrator rights.
/// </summary>
public sealed class AutostartTask(ITaskScheduler scheduler, string trustedFolder)
{
    public const string TaskName = "Udjat";

    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public bool IsEnabled => scheduler.QueryXml(TaskName) is not null;

    /// <summary>Makes the real task match the setting for the Udjat that is running now, trusting Program Files only.</summary>
    public static AutostartChange ReconcileForThisPc(bool wanted) =>
        new AutostartTask(new ComTaskScheduler(), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
            .Reconcile(wanted, Environment.ProcessPath!, WindowsIdentity.GetCurrent().Name);

    /// <summary>
    /// Makes the task match the setting: creates it if wanted and missing (or pointing at another exe or user),
    /// deletes it if not wanted. Called at every start, so a moved or reinstalled Udjat repairs its own task.
    /// If Udjat runs outside the trusted folder it makes no task, and removes one that points outside it.
    /// </summary>
    public AutostartChange Reconcile(bool wanted, string exePath, string userName)
    {
        var existing = scheduler.QueryXml(TaskName);

        if (!wanted)
        {
            if (existing is null)
                return AutostartChange.None;
            scheduler.Delete(TaskName);
            return AutostartChange.Removed;
        }

        if (!IsInside(exePath, trustedFolder))
        {
            if (existing is not null && !IsInside(ExePathOf(existing), trustedFolder))
                scheduler.Delete(TaskName);
            return AutostartChange.Refused;
        }

        if (existing is not null && IsFor(existing, exePath, userName))
            return AutostartChange.None;

        scheduler.Create(TaskName, BuildXml(exePath, userName));
        return AutostartChange.Created;
    }

    /// <summary>The task definition: at this user's logon, highest available rights, started with --tray, never stopped by a time limit.</summary>
    public static string BuildXml(string exePath, string userName)
    {
        var task = new XElement(Ns + "Task",
            new XAttribute("version", "1.2"),
            new XElement(Ns + "RegistrationInfo",
                new XElement(Ns + "Description", "Starts Udjat, the hardware watchdog, when you log on."),
                new XElement(Ns + "URI", "\\" + TaskName)),
            new XElement(Ns + "Triggers",
                new XElement(Ns + "LogonTrigger",
                    new XElement(Ns + "Enabled", "true"),
                    new XElement(Ns + "UserId", userName))),
            new XElement(Ns + "Principals",
                new XElement(Ns + "Principal",
                    new XAttribute("id", "Author"),
                    new XElement(Ns + "UserId", userName),
                    new XElement(Ns + "LogonType", "InteractiveToken"),
                    new XElement(Ns + "RunLevel", "HighestAvailable"))),
            new XElement(Ns + "Settings",
                new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                new XElement(Ns + "AllowHardTerminate", "true"),
                new XElement(Ns + "StartWhenAvailable", "false"),
                new XElement(Ns + "AllowStartOnDemand", "true"),
                new XElement(Ns + "Enabled", "true"),
                new XElement(Ns + "Hidden", "false"),
                new XElement(Ns + "ExecutionTimeLimit", "PT0S"),  // no limit; the default would end Udjat after 72 hours
                new XElement(Ns + "Priority", "7")),
            new XElement(Ns + "Actions",
                new XAttribute("Context", "Author"),
                new XElement(Ns + "Exec",
                    new XElement(Ns + "Command", exePath),
                    new XElement(Ns + "Arguments", "--tray"))));

        return task.ToString();
    }

    /// <summary>The program a task definition starts, or null if the text is not a task definition.</summary>
    public static string? ExePathOf(string taskXml) =>
        Read(taskXml, doc => doc.Descendants(Ns + "Exec").Elements(Ns + "Command").FirstOrDefault()?.Value.Trim().Trim('"'));

    /// <summary>The user a task definition runs as, or null.</summary>
    public static string? UserOf(string taskXml) =>
        Read(taskXml, doc => doc.Descendants(Ns + "Principal").Elements(Ns + "UserId").FirstOrDefault()?.Value.Trim());

    /// <summary>True if the file is inside the folder. Compares full paths, so <c>..</c> cannot climb out. Anything unreadable is outside.</summary>
    public static bool IsInside(string? filePath, string folder)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        try
        {
            var prefix = Path.GetFullPath(folder).TrimEnd('\\') + '\\';
            return Path.GetFullPath(filePath).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsFor(string taskXml, string exePath, string userName) =>
        string.Equals(ExePathOf(taskXml), exePath, StringComparison.OrdinalIgnoreCase)
        && string.Equals(UserOf(taskXml), userName, StringComparison.OrdinalIgnoreCase);

    private static string? Read(string taskXml, Func<XDocument, string?> pick)
    {
        try
        {
            return pick(XDocument.Parse(taskXml));
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }
}
