using System.Diagnostics;
using Udjat.Core.Protection;
using Udjat.Windows.Processes;
using Xunit;

namespace Udjat.Tests;

/// <summary>
/// A process table made of a fixed list. Records what was closed; chosen ids fail to close. Every process belongs to
/// the same user as Udjat unless <see cref="Owners"/> says otherwise.
/// </summary>
internal sealed class FakeProcessTable(params ProcessInfo[] processes) : IProcessTable
{
    public static readonly ProcessOwner Mine = new(1, "S-1-5-21-1-2-3-1001");

    public List<ProcessInfo> Killed { get; } = [];

    public HashSet<int> FailingIds { get; } = [];

    public ProcessOwner? Me { get; set; } = Mine;

    /// <summary>The owner of chosen ids; null means Windows would not say.</summary>
    public Dictionary<int, ProcessOwner?> Owners { get; } = [];

    public IReadOnlyList<ProcessInfo> List() => processes;

    public ProcessOwner? OwnerOf(ProcessInfo process) => Owners.TryGetValue(process.Id, out var owner) ? owner : Mine;

    public void Kill(ProcessInfo process)
    {
        if (FailingIds.Contains(process.Id))
            throw new UnauthorizedAccessException("Access is denied.");
        Killed.Add(process);
    }
}

/// <summary>Tests for <see cref="ProcessGuard"/> and for the real <see cref="SystemProcessTable"/>.</summary>
public class ProcessGuardTests
{
    private const int OwnId = 999;

    private static ProcessInfo P(int id, string name) => new(id, name);

    // --- Which processes are closed --------------------------------------------------

    [Fact]
    public void OnlyAllowListedNames_AreClosed_IgnoringCaseAndExe()
    {
        var table = new FakeProcessTable(P(10, "ollama"), P(11, "OLLAMA"), P(20, "LM Studio"), P(30, "chrome"));
        var guard = new ProcessGuard(table, OwnId);

        var result = guard.Run(["Ollama.exe", " lm studio "]);

        Assert.Equal([10, 11, 20], table.Killed.Select(p => p.Id));
        Assert.Equal(3, result.ClosedCount);
        Assert.All(result.Outcomes, o => Assert.Equal(ProcessStatus.Closed, o.Status));
    }

    [Fact]
    public void NamesThatOnlyContainAnAllowedName_AreNotClosed()
    {
        var table = new FakeProcessTable(P(10, "ollama app"), P(11, "my-ollama"), P(12, "ollama_runner"));
        var guard = new ProcessGuard(table, OwnId);

        guard.Run(["ollama"]);

        Assert.Empty(table.Killed);
    }

    [Fact]
    public void SystemProgramsAndTheDesktop_AreNeverClosed_EvenIfListed()
    {
        string[] names = ["System", "Idle", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "explorer", "dwm", "fontdrvhost"];
        var table = new FakeProcessTable(names.Select((name, i) => P(100 + i, name)).ToArray());
        var guard = new ProcessGuard(table, OwnId);

        var result = guard.Run(names.Select(n => n.ToUpperInvariant() + ".exe"));

        Assert.Empty(table.Killed);
        Assert.Equal(names.Length, result.Outcomes.Count);
        Assert.All(result.Outcomes, o => Assert.Equal(ProcessStatus.Protected, o.Status));
    }

    [Fact]
    public void UdjatItself_IsNeverClosed()
    {
        var table = new FakeProcessTable(P(OwnId, "udjat"), P(500, "udjat"));
        var guard = new ProcessGuard(table, OwnId);

        var result = guard.Run(["udjat"]);

        Assert.Equal([500], table.Killed.Select(p => p.Id));  // another program with that name is fair game; Udjat is not
        Assert.Equal(ProcessStatus.Protected, result.Outcomes.Single(o => o.Id == OwnId).Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void TheSystemsOwnProcessIds_AreNeverClosed_WhateverTheyAreCalled(int id)
    {
        var table = new FakeProcessTable(P(id, "something"));
        var guard = new ProcessGuard(table, OwnId);

        var result = guard.Run(["something"]);

        Assert.Empty(table.Killed);
        Assert.Equal(ProcessStatus.Protected, Assert.Single(result.Outcomes).Status);
    }

    [Fact]
    public void EntriesThatAreNotPlainNames_AreIgnored()
    {
        var table = new FakeProcessTable(P(10, "ollama"), P(11, "chrome"));
        var guard = new ProcessGuard(table, OwnId);

        var result = guard.Run(["*", @"C:\Tools\ollama.exe", "ollam?", "", "  "]);

        Assert.Empty(table.Killed);
        Assert.Empty(result.Outcomes);
    }

    [Fact]
    public void AnEmptyAllowList_ClosesNothing()
    {
        var table = new FakeProcessTable(P(10, "ollama"));

        var result = new ProcessGuard(table, OwnId).Run([]);

        Assert.Empty(table.Killed);
        Assert.Empty(result.Outcomes);
        Assert.Equal(0, result.ClosedCount);
    }

    [Fact]
    public void NothingMatching_GivesAnEmptyResult()
    {
        var table = new FakeProcessTable(P(30, "chrome"));

        var result = new ProcessGuard(table, OwnId).Run(["ollama"]);

        Assert.Empty(result.Outcomes);
    }

    // --- Only the user's own programs ---------------------------------------------------

    [Fact]
    public void AProgramOfAnotherAccount_IsLeftAlone_EvenWithAListedName()
    {
        var table = new FakeProcessTable(P(10, "ollama"), P(11, "ollama"));
        table.Owners[11] = new ProcessOwner(1, "S-1-5-21-1-2-3-1002");  // same session, another account

        var result = new ProcessGuard(table, OwnId).Run(["ollama"]);

        Assert.Equal([10], table.Killed.Select(p => p.Id));
        Assert.Equal(ProcessStatus.OtherUser, result.Outcomes.Single(o => o.Id == 11).Status);
        Assert.Equal(1, result.ClosedCount);
    }

    [Fact]
    public void AProgramInAnotherSession_IsLeftAlone_EvenForTheSameAccount()
    {
        var table = new FakeProcessTable(P(10, "ollama"));
        table.Owners[10] = FakeProcessTable.Mine with { SessionId = 0 };  // session 0 is where services run

        var result = new ProcessGuard(table, OwnId).Run(["ollama"]);

        Assert.Empty(table.Killed);
        Assert.Equal(ProcessStatus.OtherUser, Assert.Single(result.Outcomes).Status);
    }

    [Fact]
    public void AProgramWhoseOwnerIsUnknown_IsLeftAlone()
    {
        var table = new FakeProcessTable(P(10, "ollama"));
        table.Owners[10] = null;

        new ProcessGuard(table, OwnId).Run(["ollama"]);

        Assert.Empty(table.Killed);
    }

    [Fact]
    public void WhenUdjatDoesNotKnowWhoItIs_NothingIsClosed()
    {
        var table = new FakeProcessTable(P(10, "ollama")) { Me = null };

        var result = new ProcessGuard(table, OwnId).Run(["ollama"]);

        Assert.Empty(table.Killed);
        Assert.Equal(ProcessStatus.OtherUser, Assert.Single(result.Outcomes).Status);
    }

    // --- When closing fails -----------------------------------------------------------

    [Fact]
    public void AProcessThatCannotBeClosed_IsReported_AndTheRestAreStillClosed()
    {
        var table = new FakeProcessTable(P(10, "ollama"), P(20, "LM Studio"), P(11, "ollama"));
        table.FailingIds.Add(10);

        var result = new ProcessGuard(table, OwnId).Run(["ollama", "LM Studio"]);

        Assert.Equal([20, 11], table.Killed.Select(p => p.Id));
        var failed = result.Outcomes.Single(o => o.Status == ProcessStatus.Failed);
        Assert.Equal(10, failed.Id);
        Assert.Equal("UnauthorizedAccessException: Access is denied.", failed.Detail);
        Assert.Equal(2, result.ClosedCount);
    }

    // --- The real process table ---------------------------------------------------------

    [Fact]
    public void SystemProcessTable_ListsTheRunningTestProcess()
    {
        var list = new SystemProcessTable().List();

        Assert.Contains(list, p => p.Id == Environment.ProcessId);
    }

    [Fact]
    public void SystemProcessTable_KnowsWhoItRunsAs_AndThatTheSystemIsSomeoneElse()
    {
        var table = new SystemProcessTable();

        Assert.NotNull(table.Me);
        Assert.Equal(table.Me, table.OwnerOf(new ProcessInfo(Environment.ProcessId, "this test")));
        Assert.NotEqual(table.Me, table.OwnerOf(new ProcessInfo(4, "System")));  // null (not allowed to ask) or the SYSTEM account
    }

    [Fact]
    public void SystemProcessTable_RefusesToCloseAProcessThatChangedName()
    {
        // The id is real (this test process) but the name is not: it must not be touched.
        var wrongName = new ProcessInfo(Environment.ProcessId, "not-this-program");

        Assert.Throws<InvalidOperationException>(() => new SystemProcessTable().Kill(wrongName));
    }

    [Fact]
    public void SystemProcessTable_ReallyClosesAProcess_TheTestStartedItself()
    {
        using var child = Process.Start(new ProcessStartInfo("ping.exe", "-n 60 127.0.0.1")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;

        try
        {
            var info = new ProcessInfo(child.Id, child.ProcessName);

            new SystemProcessTable().Kill(info);

            Assert.True(child.WaitForExit(5000), "the child process should have been closed");
        }
        finally
        {
            if (!child.HasExited)
                child.Kill();
        }
    }
}
