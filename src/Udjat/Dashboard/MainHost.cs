using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Udjat.Core.Status;
using Udjat.Settings;
using Udjat.Themes;

namespace Udjat.Dashboard;

/// <summary>
/// Owns the main window: creates it when first needed, refreshes it while it is visible, and hides it
/// instead of closing it, so Udjat keeps watching from the tray. Use from the UI thread only.
/// </summary>
/// <param name="buildView">Makes the current content.</param>
/// <param name="dismiss">The user dismissed a notice.</param>
/// <param name="toggleTheme">The user pressed the theme button in the side menu.</param>
/// <param name="settings">What the settings page needs from the app.</param>
/// <param name="isClosing">True once Udjat itself is closing, when the window really may close.</param>
/// <param name="onHidden">The user closed the window: it was hidden, not destroyed.</param>
internal sealed class MainHost(
    Func<MainView> buildView, Action<Notice> dismiss, Action toggleTheme, SettingsController settings, Func<bool> isClosing, Action onHidden)
{
    private const int RestoreCommand = 9;  // SW_RESTORE

    private MainWindow? _window;

    public void Show()
    {
        _window ??= Create();
        Refresh();
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        RestoreIfMinimized(_window);
        _window.Activate();
    }

    /// <summary>Shows the window on a page: "Overview", "Fans" or "Settings".</summary>
    public void ShowPage(string page)
    {
        Show();
        _window!.ShowPage(page);
    }

    /// <summary>Redraws the window with the current content. Nothing happens while it is hidden.</summary>
    public void Refresh()
    {
        if (_window is { IsVisible: true })
            _window.Update(buildView());
    }

    private MainWindow Create()
    {
        var window = new MainWindow(dismiss, url => ShellOpen.Url(url), toggleTheme, settings);
        window.Closing += OnClosing;
        return window;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (isClosing())
            return;

        e.Cancel = true;
        _window?.Hide();
        onHidden();
    }

    /// <summary>
    /// Asks Windows itself, because it is the truth: twice a minimized window stayed minimized after a show request while WPF
    /// believed it was normal. Opening the dashboard from the tray must always bring it back.
    /// </summary>
    private static void RestoreIfMinimized(Window window)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        if (IsIconic(handle))
            ShowWindow(handle, RestoreCommand);
    }

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);
}
