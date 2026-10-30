using System.Runtime.InteropServices;

namespace Udjat.Windows.Autostart;

/// <summary>
/// The real Task Scheduler, reached through its COM interface ("Schedule.Service"). The task definition goes
/// straight from memory to Windows. It is deliberately not written to a temporary file for schtasks.exe to read:
/// any program running as the user could swap such a file in between, and the task would then start that program with
/// administrator rights at every logon.
/// </summary>
public sealed class ComTaskScheduler : ITaskScheduler
{
    private const int TaskCreateOrUpdate = 6;
    private const int TaskLogonInteractiveToken = 3;
    private const int FileNotFound = unchecked((int)0x80070002);

    public string? QueryXml(string taskName)
    {
        try
        {
            dynamic task = RootFolder().GetTask(taskName);
            return (string)task.Xml;
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            return null;
        }
    }

    public void Create(string taskName, string xml)
    {
        // No user or password: the XML names the user, and an interactive token needs no password.
        RootFolder().RegisterTask(taskName, xml, TaskCreateOrUpdate, null, null, TaskLogonInteractiveToken, null);
    }

    public void Delete(string taskName)
    {
        try
        {
            RootFolder().DeleteTask(taskName, 0);
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            // Already gone.
        }
    }

    /// <summary>"No such task". The COM binder turns this one error code into a FileNotFoundException, not a COMException.</summary>
    private static bool IsNotFound(Exception ex) =>
        ex is FileNotFoundException || ex is COMException { HResult: FileNotFound };

    private static dynamic RootFolder()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!;
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        return service.GetFolder("\\");
    }
}
