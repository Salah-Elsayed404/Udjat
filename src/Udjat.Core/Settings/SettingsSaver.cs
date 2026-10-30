using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Udjat.Core.Settings;

/// <summary>
/// The Save button, without any window in it. It checks what is on screen, then does the steps that can fail for reasons outside
/// Udjat first (the autostart task, the secrets), and only then keeps and writes the settings and restarts the pipeline. A
/// problem is shown in the save bar and nothing after it is done.
/// </summary>
/// <param name="current">The settings Udjat is using now.</param>
/// <param name="setAutostart">Applies "start with Windows". Returns true if it was wanted but could not be set up on this PC.</param>
/// <param name="apply">Keeps the new settings and writes the file.</param>
/// <param name="rebuild">Restarts the monitor and the dispatcher with the new settings.</param>
public sealed class SettingsSaver(
    ISecretStore secrets,
    Func<AppSettings> current,
    Func<bool, bool> setAutostart,
    Action<AppSettings> apply,
    Func<Task> rebuild)
{
    /// <summary>Said after Save when "Start with Windows" is on but Udjat is not running from Program Files.</summary>
    public const string AutostartNote = "Start with Windows was not turned on: it only works when Udjat is installed in Program Files.";

    /// <summary>Starts the model again from the settings in use (Discard, or after Save).</summary>
    public void Reload(SettingsModel model, bool justSaved = false, string? note = null) =>
        model.Load(current(), Stored(SecretName.SmtpPassword), Stored(SecretName.TelegramBotToken), justSaved, note);

    public async Task SaveAsync(SettingsModel model)
    {
        try
        {
            var settings = model.Form.ToSettings(current());  // throws ArgumentException for a value that is not valid
            CheckLength(model.SmtpPassword);
            CheckLength(model.TelegramToken);

            var autostartRefused = setAutostart(settings.Autostart);
            Write(SecretName.SmtpPassword, model.SmtpPassword);
            Write(SecretName.TelegramBotToken, model.TelegramToken);
            apply(settings);
            await rebuild();
            Reload(model, justSaved: true, note: autostartRefused ? AutostartNote : null);
        }
        catch (ArgumentException ex)
        {
            model.ShowError(ex.Message);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or Win32Exception or IOException)
        {
            model.ShowError($"Could not save: {ex.Message}");
        }
    }

    /// <summary>A secret as the user sees it now: typed, or removed, or the saved one.</summary>
    public string? SecretOnScreen(SecretName name, SettingsModel model)
    {
        var field = name == SecretName.SmtpPassword ? model.SmtpPassword : model.TelegramToken;
        if (field.Typed.Length > 0)
            return field.Typed;

        return field.Removed ? null : Read(name);
    }

    private static void CheckLength(SecretField field)
    {
        if (field.Typed.Length > ISecretStore.MaxLength)
            throw new ArgumentException($"That is too long: at most {ISecretStore.MaxLength} characters.");
    }

    private void Write(SecretName name, SecretField field)
    {
        if (field.Typed.Length > 0)
            secrets.Write(name, field.Typed);
        else if (field.Removed)
            secrets.Delete(name);
    }

    private bool Stored(SecretName name) => Read(name) is not null;

    private string? Read(SecretName name)
    {
        try
        {
            return secrets.Read(name);
        }
        catch (Win32Exception)
        {
            return null;  // treated as not saved; the user can type it again
        }
    }
}
