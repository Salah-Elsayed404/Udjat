using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Udjat.Core.Monitoring;
using Udjat.Core.Sensors;
using Udjat.Core.Settings;
using Udjat.Core.Status;

namespace Udjat.Dashboard;

/// <summary>A panel, plus its chart's lines (and the unit its temperatures are drawn in) and which of its parts to show.</summary>
internal sealed record PanelView(PanelInfo Info, IReadOnlyList<SparkSeries> Series, TemperatureUnit Unit)
{
    public double GaugeValue => Info.GaugePercent ?? double.NaN;
    public double BarPercent => Info.BarPercent ?? 0;

    public Visibility SecondaryVisibility => Show(Info.SecondaryText is not null);
    public Visibility BarVisibility => Show(Info.BarText is not null);
    public Visibility FansVisibility => Show(Info.FansText is not null);
    public Visibility ChartVisibility => Show(Series.Count > 0);
    public Visibility LevelVisibility => Show(Info.LevelText.Length > 0);

    internal static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>A notice, plus which of its buttons to show.</summary>
internal sealed record NoticeView(Notice Notice)
{
    public Visibility LinkVisibility => Notice.LinkUrl is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility DismissVisibility => Notice.Dismissal == NoticeDismissal.Never ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>A fan that turns: its card and the line of its speed.</summary>
internal sealed record FanView(FanInfo Info, IReadOnlyList<SparkSeries> Series);

/// <summary>The fans of one device: the cards, the stopped ones as chips, and the note when a cool card has stopped them all.</summary>
internal sealed record FanGroupView(FanGroup Group, IReadOnlyList<FanView> Cards)
{
    public Visibility CardsVisibility => PanelView.Show(Cards.Count > 0);
    public Visibility StoppedVisibility => PanelView.Show(Group.Stopped.Count > 0);
    public Visibility NoteVisibility => PanelView.Show(Group.Note is not null);
}

/// <summary>Everything the main window shows at one moment. The window and its pages bind to this.</summary>
internal sealed record MainView(
    StatusSummary Status,
    string InfoText,
    IReadOnlyList<NoticeView> Notices,
    IReadOnlyList<PanelView> Panels,
    IReadOnlyList<FanGroupView> Fans,
    string FansEmptyText,
    AppTheme Theme)
{
    public Visibility FansEmptyVisibility => PanelView.Show(Fans.Count == 0);

    public static MainView Build(
        StatusSummary status,
        IReadOnlyList<Notice> notices,
        PollResult? latest,
        AppSettings settings,
        ReadingHistory history)
    {
        var panels = latest is null
            ? []
            : DashboardModel.Panels(latest, settings).Select(panel => new PanelView(panel, SeriesOf(panel, history), settings.TemperatureUnit)).ToList();

        var fans = latest is null
            ? []
            : FanModel.Groups(latest, history)
                .Select(group => new FanGroupView(group, group.Fans.Select(fan => new FanView(fan, SpeedOf(fan, history))).ToList()))
                .ToList();

        var interval = settings.Advanced.PollSeconds.ToString("0.#", CultureInfo.InvariantCulture);
        var info = latest is null ? "Starting…" : $"Watching {latest.Sensors.Count} sensors · every {interval} s";
        var fansEmpty = latest is null
            ? "Reading the sensors…"
            : "No fans found. Graphics card fans show up by themselves; motherboard fans need the free PawnIO driver.";

        return new MainView(status, info, notices.Select(n => new NoticeView(n)).ToList(), panels, fans, fansEmpty, settings.Theme);
    }

    /// <summary>The temperature line, then the load line, of one panel.</summary>
    private static List<SparkSeries> SeriesOf(PanelInfo panel, ReadingHistory history)
    {
        var series = new List<SparkSeries>();
        if (panel.TemperatureId is { } temperature)
            series.Add(new SparkSeries(SparkRole.Temperature, history.Snapshot(temperature)));
        if (panel.LoadId is { } load)
            series.Add(new SparkSeries(SparkRole.Load, history.Snapshot(load)));
        return series;
    }

    private static List<SparkSeries> SpeedOf(FanInfo fan, ReadingHistory history) =>
        [new SparkSeries(SparkRole.Speed, history.Snapshot(fan.Id))];
}

/// <summary>Storage has its own layout (a row per drive); every other panel shares one.</summary>
internal sealed class PanelTemplateSelector : DataTemplateSelector
{
    public override DataTemplate SelectTemplate(object? item, DependencyObject container)
    {
        var key = item is PanelView { Info.Kind: PanelKind.Storage } ? "StoragePanel" : "DevicePanel";
        return (DataTemplate)((FrameworkElement)container).FindResource(key);
    }
}
