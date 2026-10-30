using Udjat.Core.Sensors;

namespace Udjat.Core.Status;

/// <summary>Whether, and for how long, the user can dismiss a notice.</summary>
public enum NoticeDismissal
{
    /// <summary>The problem is real and still there, so the notice stays.</summary>
    Never,

    /// <summary>Hidden until Udjat is restarted.</summary>
    ThisRun,

    /// <summary>Hidden for good; its id is stored in the settings.</summary>
    Forever,
}

/// <summary>A line at the top of the dashboard that explains something to the user.</summary>
/// <param name="Id">Stable, so a notice dismissed for good stays dismissed.</param>
public sealed record Notice(string Id, string Text, NoticeDismissal Dismissal, string? LinkText = null, string? LinkUrl = null);

/// <summary>What <see cref="Notices.Build"/> needs to know. Everything defaults to "nothing to say".</summary>
public sealed record NoticeInputs
{
    /// <summary>Why the sensor reader could not start, if it could not.</summary>
    public string? SourceError { get; init; }

    /// <summary>Null until the first poll has happened, because the answer is not known before that.</summary>
    public CpuTempStatus? CpuTemp { get; init; }

    /// <summary>The message from <c>LoadResult.Notice</c>, when the settings file was unusable.</summary>
    public string? SettingsNotice { get; init; }

    public bool IsFirstRun { get; init; }
    public bool EmailMissingPassword { get; init; }
    public bool TelegramMissingToken { get; init; }

    /// <summary>"Start with Windows" is on, but Udjat does not run from Program Files, so Windows cannot be asked to start it.</summary>
    public bool AutostartNeedsInstall { get; init; }

    /// <summary>Ids of the notices dismissed for good.</summary>
    public IReadOnlyCollection<string> Dismissed { get; init; } = [];
}

/// <summary>The notices on the dashboard, most important first. The rules are in docs/APP.md.</summary>
public static class Notices
{
    public const string PawnIoUrl = "https://pawnio.eu";

    public static IReadOnlyList<Notice> Build(NoticeInputs input)
    {
        var notices = new List<Notice>();

        if (input.SourceError is not null)
            notices.Add(new Notice("source-failed", $"Udjat could not start reading the sensors: {input.SourceError}", NoticeDismissal.Never));

        if (input.EmailMissingPassword)
            notices.Add(new Notice("email-no-password", "Email alerts are on but there is no password. Open Settings.", NoticeDismissal.Never));
        if (input.TelegramMissingToken)
            notices.Add(new Notice("telegram-no-token", "Telegram alerts are on but the bot token is missing or not valid. Open Settings.", NoticeDismissal.Never));

        if (input.AutostartNeedsInstall)
            notices.Add(new Notice("autostart-needs-install", "Start with Windows only works when Udjat is installed in Program Files. Use the installer to turn it on.", NoticeDismissal.Forever));

        if (input.SettingsNotice is not null)
            notices.Add(new Notice("settings-unusable", input.SettingsNotice, NoticeDismissal.ThisRun));

        // Without a sensor reader there is no CPU status to talk about.
        if (input.SourceError is null && input.CpuTemp == CpuTempStatus.NoValues)
        {
            notices.Add(new Notice(
                "cpu-driver",
                "CPU temperature needs the free PawnIO driver. Install it, then restart Udjat.",
                NoticeDismissal.Forever,
                "Open pawnio.eu",
                PawnIoUrl));
        }

        if (input.SourceError is null && input.CpuTemp == CpuTempStatus.NotFound)
            notices.Add(new Notice("cpu-unsupported", "CPU temperature is not supported on this CPU.", NoticeDismissal.Forever));

        if (input.IsFirstRun)
            notices.Add(new Notice("first-run", "Udjat is watching. Open Settings to get alerts by email or Telegram.", NoticeDismissal.Forever));

        return notices.Where(n => !(n.Dismissal == NoticeDismissal.Forever && input.Dismissed.Contains(n.Id))).ToList();
    }
}
