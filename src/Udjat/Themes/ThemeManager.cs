using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Udjat.Core.Settings;

namespace Udjat.Themes;

/// <summary>
/// Dark and light themes are two dictionaries with the same colour keys. Windows use them through DynamicResource,
/// so replacing the dictionary restyles everything that is open, at once.
/// </summary>
internal static class ThemeManager
{
    private const int UseImmersiveDarkMode = 20;  // DWMWA_USE_IMMERSIVE_DARK_MODE

    private static bool _sharedLoaded;
    private static ResourceDictionary? _theme;

    /// <summary>Loads the shared resources (the first time) and switches to this theme. Call after the Application exists.</summary>
    public static void Apply(AppTheme mode)
    {
        var resources = Application.Current.Resources.MergedDictionaries;

        if (!_sharedLoaded)
        {
            resources.Add(Load("Shared"));
            resources.Add(Load("Controls"));
            resources.Add(Load("Inputs"));
            _sharedLoaded = true;
        }

        var next = Load(mode.ToString());
        if (_theme is not null)
            resources.Remove(_theme);
        resources.Add(next);
        _theme = next;
    }

    /// <summary>Makes the window's title bar dark or light to match, so a dark window does not get a white title bar.</summary>
    public static void StyleTitleBar(Window window, AppTheme mode)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var dark = mode == AppTheme.Dark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref dark, sizeof(int));  // older Windows ignores it
    }

    private static ResourceDictionary Load(string name) =>
        new() { Source = new Uri($"/Udjat;component/Themes/{name}.xaml", UriKind.Relative) };

    [DllImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
