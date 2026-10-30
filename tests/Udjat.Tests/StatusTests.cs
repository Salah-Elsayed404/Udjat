using Udjat.Core.Monitoring;
using Udjat.Core.Sensors;
using Udjat.Core.Status;
using Xunit;

namespace Udjat.Tests;

/// <summary>Tests for the logic behind what the UI shows: chart history, the overall status and the notices.</summary>
public class StatusTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 29, 18, 0, 0, TimeSpan.Zero);

    private static SensorStatus S(SensorKind kind, double value, AlertLevel level = AlertLevel.Normal, string id = "sensor") =>
        new(new SensorReading(id, kind, "Some device", value), level);

    private static PollResult Poll(int second, params SensorStatus[] statuses) => new(Start.AddSeconds(second), statuses);

    // --- ReadingHistory ----------------------------------------------------------

    [Fact]
    public void History_KeepsOnlyTheNewestPoints()
    {
        var history = new ReadingHistory(3);

        for (var i = 0; i < 5; i++)
            history.Add(Poll(i * 2, S(SensorKind.CpuTemp, 50 + i)));

        var points = history.Snapshot("sensor");
        Assert.Equal([52.0, 53.0, 54.0], points.Select(p => p.Value));
        Assert.Equal(Start.AddSeconds(4), points[0].Time);
    }

    [Fact]
    public void History_KeepsEachSensorApart()
    {
        var history = new ReadingHistory(10);

        history.Add(Poll(0, S(SensorKind.CpuTemp, 60, id: "cpu"), S(SensorKind.Ram, 90, id: "ram")));
        history.Add(Poll(2, S(SensorKind.CpuTemp, 61, id: "cpu")));

        Assert.Equal([60.0, 61.0], history.Snapshot("cpu").Select(p => p.Value));
        Assert.Equal([90.0], history.Snapshot("ram").Select(p => p.Value));
        Assert.Empty(history.Snapshot("never seen"));
    }

    [Fact]
    public void ASnapshot_IsACopy_ThatLaterPollsDoNotChange()
    {
        var history = new ReadingHistory(10);
        history.Add(Poll(0, S(SensorKind.CpuTemp, 60)));
        var snapshot = history.Snapshot("sensor");

        history.Add(Poll(2, S(SensorKind.CpuTemp, 61)));

        Assert.Single(snapshot);
        Assert.Equal(2, history.Snapshot("sensor").Count);
    }

    [Theory]
    [InlineData(30, 2, 900)]
    [InlineData(30, 7, 258)]   // 1800 / 7 = 257.1, rounded up so the whole window is covered
    [InlineData(1, 120, 1)]    // a window shorter than one poll still keeps one point
    public void CapacityFor_CoversTheWindow(int minutes, int seconds, int expected)
    {
        Assert.Equal(expected, ReadingHistory.CapacityFor(TimeSpan.FromMinutes(minutes), TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void BadHistorySizes_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReadingHistory(0));
        Assert.Throws<ArgumentException>(() => ReadingHistory.CapacityFor(TimeSpan.Zero, TimeSpan.FromSeconds(2)));
        Assert.Throws<ArgumentException>(() => ReadingHistory.CapacityFor(TimeSpan.FromMinutes(30), TimeSpan.Zero));
    }

    [Fact]
    public async Task History_CanBeFilledAndReadFromManyThreads()
    {
        var history = new ReadingHistory(50);
        var token = TestContext.Current.CancellationToken;

        var writers = Enumerable.Range(0, 4).Select(w => Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
                history.Add(Poll(i, S(SensorKind.CpuTemp, i, id: "sensor")));
        }, token));
        var reader = Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
                _ = history.Snapshot("sensor").Count;
        }, token);

        await Task.WhenAll(writers.Append(reader));

        Assert.Equal(50, history.Snapshot("sensor").Count);
    }

    // --- StatusSummary -------------------------------------------------------------

    [Fact]
    public void Summary_WithoutASensorReader_OrWhenStopped_IsNotMonitoring()
    {
        var poll = Poll(0, S(SensorKind.CpuTemp, 50));

        Assert.Equal(new StatusSummary(TrayState.NotMonitoring, "Udjat: not reading sensors"), StatusSummary.From(poll, stopped: false, hasSource: false));
        Assert.Equal(new StatusSummary(TrayState.NotMonitoring, "Udjat: not reading sensors"), StatusSummary.From(poll, stopped: true, hasSource: true));
    }

    [Fact]
    public void Summary_BeforeTheFirstPoll_IsStarting()
    {
        var summary = StatusSummary.From(null, stopped: false, hasSource: true);

        Assert.Equal(TrayState.NotMonitoring, summary.State);
        Assert.Equal("Udjat: starting…", summary.Text);
    }

    [Fact]
    public void Summary_AllNormal()
    {
        var summary = StatusSummary.From(Poll(0, S(SensorKind.CpuTemp, 50), S(SensorKind.Fan, 2000)), false, true);

        Assert.Equal(new StatusSummary(TrayState.Normal, "Udjat: all normal"), summary);
    }

    [Fact]
    public void Summary_OneWarning_NamesIt()
    {
        var summary = StatusSummary.From(Poll(0, S(SensorKind.GpuHotspot, 101.2, AlertLevel.Warning), S(SensorKind.CpuTemp, 50)), false, true);

        Assert.Equal(new StatusSummary(TrayState.Warning, "Udjat: GPU hotspot warning (101 °C)"), summary);
    }

    [Fact]
    public void Summary_SeveralWarnings_AreCounted()
    {
        var poll = Poll(0, S(SensorKind.GpuHotspot, 101, AlertLevel.Warning, "a"), S(SensorKind.Ram, 98, AlertLevel.Warning, "b"));

        Assert.Equal(new StatusSummary(TrayState.Warning, "Udjat: 2 warnings"), StatusSummary.From(poll, false, true));
    }

    [Fact]
    public void Summary_OneCritical_NamesIt_EvenWithWarningsToo()
    {
        var poll = Poll(0, S(SensorKind.GpuHotspot, 109, AlertLevel.Critical, "a"), S(SensorKind.Ram, 98, AlertLevel.Warning, "b"));

        Assert.Equal(new StatusSummary(TrayState.Critical, "Udjat: GPU hotspot CRITICAL (109 °C)"), StatusSummary.From(poll, false, true));
    }

    [Fact]
    public void Summary_SeveralCriticals_AreCounted()
    {
        var poll = Poll(0, S(SensorKind.GpuHotspot, 109, AlertLevel.Critical, "a"), S(SensorKind.CpuTemp, 99, AlertLevel.Critical, "b"));

        Assert.Equal(new StatusSummary(TrayState.Critical, "Udjat: 2 sensors CRITICAL"), StatusSummary.From(poll, false, true));
    }

    [Theory]
    [InlineData("Udjat: all normal", "All normal")]
    [InlineData("Udjat: GPU hotspot CRITICAL (109 °C)", "GPU hotspot CRITICAL (109 °C)")]
    [InlineData("Udjat: starting…", "Starting…")]
    [InlineData("Something else", "Something else")]
    [InlineData("Udjat: ", "Udjat: ")]
    public void ThePillText_DropsTheProductName(string text, string expected)
    {
        Assert.Equal(expected, new StatusSummary(TrayState.Normal, text).PillText);
    }

    [Fact]
    public void Summary_NeverExceedsTheTooltipLimit()
    {
        var poll = Poll(0, S(SensorKind.NvmeTemp, 1e300, AlertLevel.Critical));  // a garbage value has hundreds of digits

        var summary = StatusSummary.From(poll, false, true);

        Assert.Equal(StatusSummary.MaxTextLength, summary.Text.Length);
        Assert.EndsWith("…", summary.Text);
    }

    // --- Notices ----------------------------------------------------------------------

    private static string[] Ids(NoticeInputs input) => Notices.Build(input).Select(n => n.Id).ToArray();

    [Fact]
    public void NoNotices_WhenThereIsNothingToSay()
    {
        Assert.Empty(Notices.Build(new NoticeInputs { CpuTemp = CpuTempStatus.Available }));
        Assert.Empty(Notices.Build(new NoticeInputs()));  // before the first poll the CPU status is not known
    }

    [Fact]
    public void MissingDriver_GivesTheDriverNotice_WithALink()
    {
        var notice = Assert.Single(Notices.Build(new NoticeInputs { CpuTemp = CpuTempStatus.NoValues }));

        Assert.Equal("cpu-driver", notice.Id);
        Assert.Equal(NoticeDismissal.Forever, notice.Dismissal);
        Assert.Equal("https://pawnio.eu", notice.LinkUrl);
        Assert.Contains("PawnIO", notice.Text);
        Assert.NotNull(notice.LinkText);
    }

    [Fact]
    public void UnsupportedCpu_GivesAQuietNotice()
    {
        var notice = Assert.Single(Notices.Build(new NoticeInputs { CpuTemp = CpuTempStatus.NotFound }));

        Assert.Equal("cpu-unsupported", notice.Id);
        Assert.Null(notice.LinkUrl);
    }

    [Fact]
    public void ADismissedForeverNotice_StaysHidden_ButOthersDoNot()
    {
        var input = new NoticeInputs { CpuTemp = CpuTempStatus.NoValues, IsFirstRun = true, Dismissed = ["cpu-driver"] };

        Assert.Equal(["first-run"], Ids(input));
    }

    [Fact]
    public void RealProblems_CannotBeDismissedForever()
    {
        var input = new NoticeInputs
        {
            SourceError = "access denied",
            EmailMissingPassword = true,
            TelegramMissingToken = true,
            SettingsNotice = "The settings file could not be used.",
            Dismissed = ["source-failed", "email-no-password", "telegram-no-token", "settings-unusable"],
        };

        var notices = Notices.Build(input);

        Assert.Equal(["source-failed", "email-no-password", "telegram-no-token", "settings-unusable"], notices.Select(n => n.Id));
        Assert.Equal(NoticeDismissal.Never, notices[0].Dismissal);
        Assert.Equal(NoticeDismissal.ThisRun, notices[3].Dismissal);
        Assert.Contains("access denied", notices[0].Text);
    }

    [Fact]
    public void StartWithWindowsOutsideProgramFiles_SaysHowToFixIt_AndCanBeDismissed()
    {
        var notice = Assert.Single(Notices.Build(new NoticeInputs { AutostartNeedsInstall = true }));

        Assert.Equal("autostart-needs-install", notice.Id);
        Assert.Contains("Program Files", notice.Text);
        Assert.Empty(Notices.Build(new NoticeInputs { AutostartNeedsInstall = true, Dismissed = ["autostart-needs-install"] }));
    }

    [Fact]
    public void WithoutASensorReader_ThereIsNoCpuNotice()
    {
        var input = new NoticeInputs { SourceError = "boom", CpuTemp = CpuTempStatus.NoValues };

        Assert.Equal(["source-failed"], Ids(input));
    }

    [Fact]
    public void Notices_AreOrdered_ProblemsFirst_WelcomeLast()
    {
        var input = new NoticeInputs
        {
            IsFirstRun = true,
            CpuTemp = CpuTempStatus.NoValues,
            SettingsNotice = "unusable",
            EmailMissingPassword = true,
        };

        Assert.Equal(["email-no-password", "settings-unusable", "cpu-driver", "first-run"], Ids(input));
    }
}
