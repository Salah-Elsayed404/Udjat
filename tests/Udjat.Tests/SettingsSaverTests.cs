using System.ComponentModel;
using System.Runtime.InteropServices;
using Udjat.Core.Settings;
using Xunit;

namespace Udjat.Tests;

/// <summary>Secrets in a dictionary. Every write and delete is added to the shared list of steps, so tests can check the order.</summary>
internal sealed class FakeSecretStore(List<string> steps) : ISecretStore
{
    public Dictionary<SecretName, string> Values { get; } = [];

    /// <summary>When set, <see cref="Write"/> throws it (after the step is recorded).</summary>
    public Exception? WriteFails { get; set; }

    /// <summary>When set, <see cref="Read"/> throws it.</summary>
    public Exception? ReadFails { get; set; }

    public string? Read(SecretName name) => ReadFails is null ? Values.GetValueOrDefault(name) : throw ReadFails;

    public void Write(SecretName name, string value)
    {
        steps.Add($"write:{name}");
        if (WriteFails is not null)
            throw WriteFails;
        Values[name] = value;
    }

    public void Delete(SecretName name)
    {
        steps.Add($"delete:{name}");
        Values.Remove(name);
    }
}

/// <summary>Tests for <see cref="SettingsSaver"/>: what Save does, in what order, and what a failure leaves behind.</summary>
public class SettingsSaverTests
{
    private sealed class Rig
    {
        public Rig()
        {
            Secrets = new FakeSecretStore(Steps);
            Saver = new SettingsSaver(
                Secrets,
                () => Current,
                wanted =>
                {
                    Steps.Add($"autostart:{wanted}");
                    return AutostartFails is null ? AutostartRefused : throw AutostartFails;
                },
                settings =>
                {
                    Steps.Add("apply");
                    Current = settings;
                },
                () =>
                {
                    Steps.Add("rebuild");
                    return Task.CompletedTask;
                });
            Saver.Reload(Model);
        }

        public List<string> Steps { get; } = [];
        public FakeSecretStore Secrets { get; }
        public SettingsSaver Saver { get; }
        public SettingsModel Model { get; } = new();
        public AppSettings Current { get; private set; } = new();
        public bool AutostartRefused { get; set; }
        public Exception? AutostartFails { get; set; }

        public Task Save() => Saver.SaveAsync(Model);
    }

    // --- A good save --------------------------------------------------------------------

    [Fact]
    public async Task Save_DoesTheStepsInOrder_AndStartsFromTheNewSettings()
    {
        var rig = new Rig();
        rig.Model.Form.CooldownMinutes = "3";
        rig.Model.SmtpPassword.Typed = "pw";
        rig.Model.TelegramToken.Typed = "123456789:abc";

        await rig.Save();

        Assert.Equal(["autostart:True", "write:SmtpPassword", "write:TelegramBotToken", "apply", "rebuild"], rig.Steps);
        Assert.Equal(3, rig.Current.CooldownMinutes);
        Assert.False(rig.Model.IsDirty);
        Assert.False(rig.Model.BarIsError);
        Assert.Equal("Saved. Udjat is using the new settings.", rig.Model.BarText);
        Assert.True(rig.Model.SmtpPassword.Stored);  // the page reads whether a secret exists, never the secret
        Assert.Equal("Saved", rig.Model.SmtpPassword.Status);
    }

    [Fact]
    public async Task Save_WithNoSecretsTouched_DoesNotCallTheSecretStore()
    {
        var rig = new Rig();
        rig.Model.Form.CooldownMinutes = "4";

        await rig.Save();

        Assert.Equal(["autostart:True", "apply", "rebuild"], rig.Steps);
    }

    [Fact]
    public async Task Save_RemovesASecret_TheUserRemoved()
    {
        var rig = new Rig();
        rig.Secrets.Values[SecretName.SmtpPassword] = "old";
        rig.Saver.Reload(rig.Model);
        rig.Model.SmtpPassword.Remove();

        await rig.Save();

        Assert.Contains("delete:SmtpPassword", rig.Steps);
        Assert.False(rig.Secrets.Values.ContainsKey(SecretName.SmtpPassword));
        Assert.False(rig.Model.SmtpPassword.Stored);
    }

    [Fact]
    public async Task Save_WhenStartWithWindowsCannotBeSetUpHere_SavesTheRest_AndSaysSo()
    {
        var rig = new Rig { AutostartRefused = true };
        rig.Model.Form.CooldownMinutes = "6";

        await rig.Save();

        Assert.Equal(6, rig.Current.CooldownMinutes);
        Assert.False(rig.Model.BarIsError);
        Assert.EndsWith(SettingsSaver.AutostartNote, rig.Model.BarText);
    }

    // --- When something is wrong: nothing after it happens --------------------------------

    [Fact]
    public async Task Save_WithAValueThatIsNotValid_ShowsWhy_AndChangesNothing()
    {
        var rig = new Rig();
        rig.Model.Form.CooldownMinutes = "ten";
        rig.Model.SmtpPassword.Typed = "pw";

        await rig.Save();

        Assert.Empty(rig.Steps);
        Assert.True(rig.Model.BarIsError);
        Assert.Contains("Cooldown", rig.Model.BarText);
        Assert.Equal("pw", rig.Model.SmtpPassword.Typed);  // what the user typed is still there
        Assert.Equal(10, rig.Current.CooldownMinutes);
    }

    [Fact]
    public async Task Save_WithATooLongSecret_ChangesNothing_NotEvenTheOtherSecret()
    {
        var rig = new Rig();
        rig.Model.SmtpPassword.Typed = "fine";
        rig.Model.TelegramToken.Typed = new string('x', ISecretStore.MaxLength + 1);

        await rig.Save();

        Assert.Empty(rig.Steps);
        Assert.True(rig.Model.BarIsError);
        Assert.Contains("too long", rig.Model.BarText);
        Assert.Empty(rig.Secrets.Values);
    }

    [Fact]
    public async Task Save_WhenTheTaskSchedulerFails_ShowsTheProblem_AndWritesNothing()
    {
        var rig = new Rig { AutostartFails = new COMException("The task service is not available.") };
        rig.Model.SmtpPassword.Typed = "pw";

        await rig.Save();

        Assert.Equal(["autostart:True"], rig.Steps);
        Assert.True(rig.Model.BarIsError);
        Assert.Equal("Could not save: The task service is not available.", rig.Model.BarText);
    }

    [Fact]
    public async Task Save_WhenCredentialManagerFails_ShowsTheProblem_AndDoesNotApplyTheSettings()
    {
        var rig = new Rig();
        rig.Secrets.WriteFails = new Win32Exception("Access is denied.");
        rig.Model.SmtpPassword.Typed = "pw";

        await rig.Save();

        Assert.Equal(["autostart:True", "write:SmtpPassword"], rig.Steps);
        Assert.Equal("Could not save: Access is denied.", rig.Model.BarText);
        Assert.Equal("pw", rig.Model.SmtpPassword.Typed);
    }

    // --- Secrets as the page sees them ---------------------------------------------------

    [Fact]
    public void SecretOnScreen_IsWhatWasTyped_ThenNothingIfRemoved_ElseTheSavedOne()
    {
        var rig = new Rig();
        rig.Secrets.Values[SecretName.SmtpPassword] = "saved";
        var model = rig.Model;

        Assert.Equal("saved", rig.Saver.SecretOnScreen(SecretName.SmtpPassword, model));

        model.SmtpPassword.Typed = "typed";
        Assert.Equal("typed", rig.Saver.SecretOnScreen(SecretName.SmtpPassword, model));

        model.SmtpPassword.Remove();
        Assert.Null(rig.Saver.SecretOnScreen(SecretName.SmtpPassword, model));
        Assert.Null(rig.Saver.SecretOnScreen(SecretName.TelegramBotToken, model));  // never set
    }

    [Fact]
    public void WhenCredentialManagerCannotBeRead_TheSecretCountsAsNotSaved()
    {
        var rig = new Rig();
        rig.Secrets.ReadFails = new Win32Exception("Access is denied.");

        rig.Saver.Reload(rig.Model);

        Assert.False(rig.Model.SmtpPassword.Stored);
        Assert.False(rig.Model.TelegramToken.Stored);
    }

    [Fact]
    public void Reload_StartsAgainFromTheSettingsInUse_AndTellsWhichSecretsExist()
    {
        var rig = new Rig();
        rig.Secrets.Values[SecretName.TelegramBotToken] = "123456789:abc";
        rig.Model.Form.CooldownMinutes = "99";

        rig.Saver.Reload(rig.Model);

        Assert.Equal("10", rig.Model.Form.CooldownMinutes);
        Assert.False(rig.Model.SmtpPassword.Stored);
        Assert.True(rig.Model.TelegramToken.Stored);
    }
}
