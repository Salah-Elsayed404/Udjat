namespace Udjat.Core.Protection;

public enum ProcessStatus
{
    Closed,
    Failed,

    /// <summary>On the allow-list, but never closed: a system process or Udjat itself.</summary>
    Protected,

    /// <summary>On the allow-list, but left alone: it runs under another account or in another Windows session than Udjat.</summary>
    OtherUser,
}

/// <summary>What happened to one process that matched the allow-list.</summary>
public sealed record ProcessOutcome(string Name, int Id, ProcessStatus Status, string? Detail = null);

/// <summary>What <see cref="ProcessGuard.Run"/> did. Empty if nothing matched.</summary>
public sealed record ProtectResult(IReadOnlyList<ProcessOutcome> Outcomes)
{
    public int ClosedCount => Outcomes.Count(o => o.Status == ProcessStatus.Closed);
}

/// <summary>
/// Closes the programs on the user's allow-list. The rules are strict because a mistake here closes
/// something the user did not choose:
/// names match exactly (ignoring case and ".exe"), a built-in list of system programs and Udjat itself
/// are never closed even if listed, only the listed processes are closed, not their children, and only
/// processes that belong to the same account and Windows session as Udjat. Udjat runs with administrator
/// rights, so without that last rule the settings file could make it close services or other people's programs.
/// </summary>
public sealed class ProcessGuard(IProcessTable table, int? ownProcessId = null)
{
    // Windows itself, and the desktop. Closing any of these would hurt the whole PC.
    private static readonly HashSet<string> NeverClose = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "smss", "csrss", "wininit", "winlogon", "services",
        "lsass", "svchost", "explorer", "dwm", "fontdrvhost",
    };

    private readonly int _ownProcessId = ownProcessId ?? Environment.ProcessId;

    public ProtectResult Run(IEnumerable<string> allowList)
    {
        // Entries that are not plain names (paths, wildcards) are dropped, not guessed at.
        var names = allowList
            .Select(entry => ProcessNames.TryNormalize(entry, out var name) ? name : null)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var outcomes = new List<ProcessOutcome>();
        if (names.Count == 0)
            return new ProtectResult(outcomes);

        foreach (var process in table.List().Where(p => names.Contains(p.Name)))
            outcomes.Add(Close(process));

        return new ProtectResult(outcomes);
    }

    private ProcessOutcome Close(ProcessInfo process)
    {
        if (IsProtected(process))
            return new ProcessOutcome(process.Name, process.Id, ProcessStatus.Protected);

        if (!IsMine(process))
            return new ProcessOutcome(process.Name, process.Id, ProcessStatus.OtherUser);

        try
        {
            table.Kill(process);
            return new ProcessOutcome(process.Name, process.Id, ProcessStatus.Closed);
        }
        catch (Exception ex)
        {
            return new ProcessOutcome(process.Name, process.Id, ProcessStatus.Failed, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>True only when Windows says the process runs as the same account in the same session as Udjat. Unknown counts as not mine.</summary>
    private bool IsMine(ProcessInfo process) =>
        table.Me is { } me && table.OwnerOf(process) == me;

    private bool IsProtected(ProcessInfo process) =>
        process.Id is 0 or 4 || process.Id == _ownProcessId || NeverClose.Contains(process.Name);
}
