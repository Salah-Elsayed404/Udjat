namespace Udjat.Windows;

/// <summary>What was typed after Udjat.exe. Unknown words are ignored.</summary>
/// <param name="StartHidden">--tray: start in the tray without opening the dashboard (what the autostart task uses).</param>
/// <param name="Exit">--exit: ask the running Udjat to close, and close. The uninstaller uses this.</param>
/// <param name="DataDir">--data-dir &lt;folder&gt;: keep settings.json and alerts.log there instead of in %ProgramData%\Udjat. For testing only: the folder is used as it is, without the safety checks of <see cref="Storage.DataFolder"/>.</param>
public sealed record StartOptions(bool StartHidden, bool Exit, string? DataDir)
{
    public static StartOptions Parse(IReadOnlyList<string> args)
    {
        var startHidden = false;
        var exit = false;
        string? dataDir = null;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--tray":
                    startHidden = true;
                    break;
                case "--exit":
                    exit = true;
                    break;
                case "--data-dir" when i + 1 < args.Count:
                    dataDir = args[++i];
                    break;
            }
        }

        return new StartOptions(startHidden, exit, dataDir);
    }
}
