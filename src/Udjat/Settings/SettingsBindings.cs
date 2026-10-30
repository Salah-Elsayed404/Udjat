using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Udjat.Settings;

/// <summary>
/// What the buttons on the settings page ask for. The page handles them, so a tab is only layout and needs no code of its own.
/// </summary>
internal static class SettingsCommands
{
    /// <summary>Send a test message. The parameter is the <c>ChannelKind</c>.</summary>
    public static readonly RoutedUICommand Test = Make(nameof(Test));

    /// <summary>Delete a saved secret. The parameter is "Smtp" or "Telegram".</summary>
    public static readonly RoutedUICommand RemoveSecret = Make(nameof(RemoveSecret));

    public static readonly RoutedUICommand ResetAlerts = Make(nameof(ResetAlerts));
    public static readonly RoutedUICommand OpenLog = Make(nameof(OpenLog));
    public static readonly RoutedUICommand OpenFolder = Make(nameof(OpenFolder));
    public static readonly RoutedUICommand OpenPawnIo = Make(nameof(OpenPawnIo));

    private static RoutedUICommand Make(string name) => new(name, name, typeof(SettingsCommands));
}

/// <summary>Lets a choice of an enum be shown as radio buttons: the button whose name is the parameter is checked.</summary>
internal sealed class EnumIsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is string name ? Enum.Parse(targetType, name) : Binding.DoNothing;
}

/// <summary>
/// A <see cref="PasswordBox"/> cannot be bound, on purpose. This attached property keeps one in step with a text property, so
/// the page can clear it after saving. Used only for the two secrets, which are never shown back.
/// </summary>
internal static class PasswordBinder
{
    public static readonly DependencyProperty PasswordProperty = DependencyProperty.RegisterAttached(
        "Password", typeof(string), typeof(PasswordBinder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordChanged));  // null, so the first "" counts as a change and hooks the box

    public static string? GetPassword(DependencyObject box) => (string?)box.GetValue(PasswordProperty);

    public static void SetPassword(DependencyObject box, string value) => box.SetValue(PasswordProperty, value);

    private static void OnPasswordChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not PasswordBox box)
            return;

        box.PasswordChanged -= OnBoxChanged;
        var text = (string?)e.NewValue ?? "";
        if (box.Password != text)
            box.Password = text;
        box.PasswordChanged += OnBoxChanged;
    }

    private static void OnBoxChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;
        SetPassword(box, box.Password);
    }
}
