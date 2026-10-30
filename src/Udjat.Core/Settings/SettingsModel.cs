using System.ComponentModel;
using System.Runtime.CompilerServices;
using Udjat.Core.Alerts;
using Udjat.Core.Sensors;

namespace Udjat.Core.Settings;

/// <summary>Raises PropertyChanged for the settings page. A small base, so each class below stays short.</summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// One secret (the email password, the Telegram bot token) as the page handles it: stored or not, typed or not, to be removed
/// or not. The stored value is never read back into the page.
/// </summary>
public sealed class SecretField(bool stored) : Observable
{
    private string _typed = "";

    public bool Stored { get; private set; } = stored;
    public bool Removed { get; private set; }

    public string Typed
    {
        get => _typed;
        set
        {
            _typed = value;
            if (value.Length > 0)
                Removed = false;
            RaiseAll();
        }
    }

    public bool HasChange => Typed.Length > 0 || Removed;

    public string Status => Typed.Length > 0 ? "Will be saved" : Removed ? "Will be removed" : Stored ? "Saved" : "Not set";

    /// <summary>Whether to offer the "Remove" button: there is a saved secret, and nothing is about to replace or remove it.</summary>
    public bool CanRemove => Stored && !Removed && Typed.Length == 0;

    public void Remove()
    {
        Removed = true;
        _typed = "";
        RaiseAll();
    }

    private void RaiseAll()
    {
        Raise(nameof(Typed));
        Raise(nameof(Status));
        Raise(nameof(CanRemove));
    }
}

/// <summary>How a "Test" button's last try went, for the words next to the button.</summary>
public sealed class TestState : Observable
{
    public string Text { get; private set; } = "";
    public bool IsError { get; private set; }
    public bool Busy { get; private set; }

    public bool CanTest => !Busy;

    public void Start() => Set("Sending…", isError: false, busy: true);

    public void Finish(bool success, string detail) => Set(success ? "Sent. Check that it arrived." : detail, isError: !success, busy: false);

    private void Set(string text, bool isError, bool busy)
    {
        (Text, IsError, Busy) = (text, isError, busy);
        Raise(nameof(Text));
        Raise(nameof(IsError));
        Raise(nameof(CanTest));
    }
}

/// <summary>
/// Everything the settings page holds while it is open: the form being edited, the form as saved (to tell what changed), the
/// secrets, the Test buttons' results and the words in the save bar. It lives as long as the window, so unsaved edits survive
/// switching pages and closing the window. No WPF in here, so the rules can be tested.
/// </summary>
public sealed class SettingsModel : Observable
{
    private SettingsForm _saved = new();
    private bool _justSaved;
    private string? _error;
    private string? _note;

    public SettingsForm Form { get; private set; } = new();
    /// <summary>
    /// The temperature unit, for the radio buttons. Choosing one converts the limits in the form, so the whole form is replaced
    /// and the page reads every box again.
    /// </summary>
    public TemperatureUnit TemperatureUnit
    {
        get => Form.TemperatureUnit;
        set
        {
            if (value == Form.TemperatureUnit)
                return;

            Form = Form.WithUnit(value);
            Raise(nameof(Form));
            Raise(nameof(TemperatureUnit));
            Touch();
        }
    }

    public SecretField SmtpPassword { get; private set; } = new(false);
    public SecretField TelegramToken { get; private set; } = new(false);

    public TestState ToastTest { get; } = new();
    public TestState SoundTest { get; } = new();
    public TestState EmailTest { get; } = new();
    public TestState TelegramTest { get; } = new();

    /// <summary>What the General tab says about CPU temperature, and whether to offer the PawnIO link.</summary>
    public string CpuStatus { get; private set; } = "";
    public bool CpuDriverMissing { get; private set; }
    public string Version { get; init; } = "";

    /// <summary>True while <see cref="Load"/> is changing the fields, so the page does not mistake that for the user's edits.</summary>
    public bool IsLoading { get; private set; }

    public void SetCpuStatus(string text, bool driverMissing)
    {
        (CpuStatus, CpuDriverMissing) = (text, driverMissing);
        Raise(nameof(CpuStatus));
        Raise(nameof(CpuDriverMissing));
    }

    public bool IsDirty => Form.Differs(_saved) || SmtpPassword.HasChange || TelegramToken.HasChange;
    public bool BarVisible => IsDirty || _justSaved;
    public string BarText => _error ?? (IsDirty ? "You have unsaved changes" : "Saved. Udjat is using the new settings." + (_note is null ? "" : " " + _note));
    public bool BarIsError => _error is not null;

    public TestState Test(ChannelKind kind) => kind switch
    {
        ChannelKind.Toast => ToastTest,
        ChannelKind.Sound => SoundTest,
        ChannelKind.Email => EmailTest,
        _ => TelegramTest,
    };

    /// <summary>Starts again from these settings: after the page opens, after Save, and after Discard.</summary>
    /// <param name="note">Something to add to the "Saved" message, for example that one setting could not be applied.</param>
    public void Load(AppSettings settings, bool hasPassword, bool hasToken, bool justSaved = false, string? note = null)
    {
        Form = SettingsForm.From(settings);
        _saved = SettingsForm.From(settings);
        SmtpPassword = new SecretField(hasPassword);
        TelegramToken = new SecretField(hasToken);
        (_justSaved, _error, _note) = (justSaved, null, note);

        IsLoading = true;
        try
        {
            Raise(string.Empty);  // every property changed
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Call after any edit: the save bar follows what differs from the saved settings. The "saved" and error messages go away only
    /// once something really differs, because a tab that is shown for the first time also fills in its boxes, and that is not an edit.
    /// </summary>
    public void Touch()
    {
        if (IsDirty)
            (_justSaved, _error, _note) = (false, null, null);

        Raise(nameof(IsDirty));
        Raise(nameof(BarVisible));
        Raise(nameof(BarText));
        Raise(nameof(BarIsError));
    }

    /// <summary>Shows a problem in the save bar. Nothing was saved.</summary>
    public void ShowError(string message)
    {
        _error = message;
        Raise(nameof(BarText));
        Raise(nameof(BarIsError));
    }

    /// <summary>The values changed in place (Reset to defaults): the page reads them again.</summary>
    public void Refresh()
    {
        Raise(nameof(Form));
        Touch();
    }
}
