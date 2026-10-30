using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Udjat.Core.Status;
using Udjat.Fans;
using Udjat.Settings;
using Udjat.Themes;

namespace Udjat.Dashboard;

/// <summary>
/// The one window: a side menu, the status in words, the notices, and the page the menu selected. It only draws what
/// <see cref="MainView"/> holds; the pages share it through the window's data context.
/// </summary>
internal partial class MainWindow : Window
{
    private readonly Action<Notice> _dismiss;
    private readonly Action<string> _openLink;
    private readonly Action _toggleTheme;

    // The pages are kept, not rebuilt, so each one remembers where it was scrolled to.
    private readonly OverviewPage _overview = new();
    private readonly FansPage _fans = new();
    private readonly SettingsPage _settings;

    public MainWindow(Action<Notice> dismiss, Action<string> openLink, Action toggleTheme, SettingsController settings)
    {
        InitializeComponent();
        _dismiss = dismiss;
        _openLink = openLink;
        _toggleTheme = toggleTheme;

        _settings = new SettingsPage(settings);
        _settings.DirtyChanged += dirty => SettingsDot.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;

        PageHost.Children.Add(_overview);
        PageHost.Children.Add(_fans);
        PageHost.Children.Add(_settings);

        OverviewItem.IsChecked = true;  // raises Checked, which shows the page
    }

    public void Update(MainView view)
    {
        DataContext = view;
        ThemeManager.StyleTitleBar(this, view.Theme);  // the title bar follows the theme too
    }

    private void OnPageChecked(object sender, RoutedEventArgs e)
    {
        var item = (RadioButton)sender;
        PageTitle.Text = (string)item.Content;
        _overview.Visibility = Show(item == OverviewItem);
        _fans.Visibility = Show(item == FansItem);
        _settings.Visibility = Show(item == SettingsItem);
    }

    /// <summary>Opens a page by the name the side menu uses (the tray menu opens Settings this way).</summary>
    public void ShowPage(string name)
    {
        var item = name switch { "Fans" => FansItem, "Settings" => SettingsItem, _ => OverviewItem };
        item.IsChecked = true;
    }

    /// <summary>The "Fans ›" links on the Overview ask for another page by name.</summary>
    private void OnGoToPage(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is string page)
            ShowPage(page);
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private void OnThemeClick(object sender, RoutedEventArgs e) => _toggleTheme();

    private void OnDismiss(object sender, RoutedEventArgs e) => _dismiss(NoticeOf(sender));

    private void OnOpenLink(object sender, RoutedEventArgs e)
    {
        if (NoticeOf(sender).LinkUrl is { } url)
            _openLink(url);
    }

    private static Notice NoticeOf(object button) => ((NoticeView)((FrameworkElement)button).DataContext).Notice;
}
