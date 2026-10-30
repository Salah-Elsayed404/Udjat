namespace Udjat.Windows;

/// <summary>
/// Makes sure only one Udjat runs: two watchdogs would send every alert twice. A second start does not run;
/// it asks the first one to show its dashboard (or, with --exit, to close).
/// The objects have fixed names, so a program running as the same user could create them first. A real Udjat always makes
/// the two events and then the mutex, so one without the other means someone else made them, and Udjat says so instead of
/// quietly doing nothing (see docs/REVIEW.md, S4).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>The start of the three object names. They are in the session's own namespace, so each signed-in user has their own.</summary>
    public const string DefaultName = @"Local\Udjat";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly EventWaitHandle _exitEvent;
    private readonly RegisteredWaitHandle _showWait;
    private readonly RegisteredWaitHandle _exitWait;

    private SingleInstance(Mutex mutex, EventWaitHandle showEvent, EventWaitHandle exitEvent)
    {
        _mutex = mutex;
        _showEvent = showEvent;
        _exitEvent = exitEvent;
        _showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => ShowRequested?.Invoke(), null, Timeout.Infinite, executeOnlyOnce: false);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(exitEvent, (_, _) => ExitRequested?.Invoke(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>A later start asked this one to show its window. Raised on a thread-pool thread.</summary>
    public event Action? ShowRequested;

    /// <summary>A later start asked this one to close. Raised on a thread-pool thread.</summary>
    public event Action? ExitRequested;

    public static string ShowEventName(string name = DefaultName) => name + ".Show";

    public static string ExitEventName(string name = DefaultName) => name + ".Exit";

    private static string MutexName(string name) => name + ".SingleInstance";

    /// <summary>Returns the claim to be the one running Udjat, or null if another one already is.</summary>
    /// <exception cref="InvalidOperationException">Another program has created one of Udjat's objects, so Udjat cannot tell whether it is running.</exception>
    public static SingleInstance? TryBecomeFirst(string name = DefaultName)
    {
        // The events are made before the mutex, so a second start that sees the mutex can always find them.
        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName(name), out var showIsNew);
        var exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName(name), out var exitIsNew);
        var mutex = new Mutex(true, MutexName(name), out var isFirst);

        var eventsAreNew = showIsNew && exitIsNew;
        var eventsAreOld = !showIsNew && !exitIsNew;

        if (isFirst && eventsAreNew)
            return new SingleInstance(mutex, showEvent, exitEvent);

        if (isFirst)
            mutex.ReleaseMutex();
        mutex.Dispose();
        showEvent.Dispose();
        exitEvent.Dispose();

        if (!isFirst && eventsAreOld)
            return null;  // the normal case: Udjat is already running

        throw new InvalidOperationException(
            $"Another program has created the object {name} that Udjat uses to find out whether it is already running, so Udjat cannot start. " +
            "Sign out of Windows and in again, or restart the PC.");
    }

    /// <summary>Tells the running Udjat something. Does nothing if its event cannot be opened.</summary>
    public static void Signal(string eventName)
    {
        try
        {
            using var handle = EventWaitHandle.OpenExisting(eventName);
            handle.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // The other instance is gone already.
        }
    }

    public void Dispose()
    {
        _showWait.Unregister(null);
        _exitWait.Unregister(null);
        _showEvent.Dispose();
        _exitEvent.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
