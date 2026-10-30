namespace Udjat.Core.Protection;

/// <summary>A running program, as listed by the system.</summary>
/// <param name="Name">The process name without ".exe", for example "ollama".</param>
public readonly record struct ProcessInfo(int Id, string Name);

/// <summary>Whose a process is: the Windows session it runs in and the account (a SID such as "S-1-5-21-…"). Both must match to count as the same user's.</summary>
public readonly record struct ProcessOwner(int SessionId, string UserSid);

/// <summary>The running programs and a way to close one. A fake of this keeps ProcessGuard's rules testable.</summary>
public interface IProcessTable
{
    /// <summary>Who Udjat itself runs as, or null if that cannot be found out (then nothing is closed).</summary>
    ProcessOwner? Me { get; }

    IReadOnlyList<ProcessInfo> List();

    /// <summary>Who the process belongs to, or null if the system will not say (services and protected programs).</summary>
    ProcessOwner? OwnerOf(ProcessInfo process);

    /// <summary>Closes the process. Throws if it cannot, or if it is no longer the program that was listed.</summary>
    void Kill(ProcessInfo process);
}
