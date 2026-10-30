using Udjat.Core.Alerts;
using Udjat.Core.Settings;
using Xunit;

namespace Udjat.Tests;

/// <summary>Tests for <see cref="SettingsModel"/>, <see cref="SecretField"/> and <see cref="TestState"/>: what the settings page shows and when.</summary>
public class SettingsModelTests
{
    private static SettingsModel Loaded(bool hasPassword = false, bool hasToken = false)
    {
        var model = new SettingsModel();
        model.Load(new AppSettings(), hasPassword, hasToken);
        return model;
    }

    // --- What counts as a change ---------------------------------------------------------

    [Fact]
    public void JustLoaded_NothingIsChanged_AndTheSaveBarIsHidden()
    {
        var model = Loaded();

        Assert.False(model.IsDirty);
        Assert.False(model.BarVisible);
    }

    [Fact]
    public void EditingAField_ShowsTheBar_AndPuttingTheOldValueBack_HidesIt()
    {
        var model = Loaded();

        model.Form.CooldownMinutes = "3";
        model.Touch();
        Assert.True(model.IsDirty);
        Assert.True(model.BarVisible);
        Assert.Equal("You have unsaved changes", model.BarText);

        model.Form.CooldownMinutes = "10";
        model.Touch();
        Assert.False(model.BarVisible);
    }

    [Fact]
    public void EditingAThresholdRow_CountsAsAChange()
    {
        var model = Loaded();

        model.Form.Thresholds[0].Warning = "1";

        Assert.True(model.IsDirty);
    }

    // --- The messages in the bar ---------------------------------------------------------

    [Fact]
    public void AfterSave_TheBarSaysSaved_WithTheNote_UntilSomethingIsReallyEdited()
    {
        var model = new SettingsModel();
        model.Load(new AppSettings(), false, false, justSaved: true, note: "One thing was not applied.");

        Assert.True(model.BarVisible);
        Assert.Equal("Saved. Udjat is using the new settings. One thing was not applied.", model.BarText);

        model.Touch();  // a tab filled in its boxes for the first time: not an edit
        Assert.Equal("Saved. Udjat is using the new settings. One thing was not applied.", model.BarText);

        model.Form.CooldownMinutes = "5";
        model.Touch();
        Assert.Equal("You have unsaved changes", model.BarText);  // the message and its note are gone
    }

    [Fact]
    public void AnError_ReplacesTheText_AndGoesAwayWithTheNextEdit()
    {
        var model = Loaded();
        model.Form.CooldownMinutes = "abc";
        model.Touch();

        model.ShowError("Cooldown: \"abc\" is not a whole number.");
        Assert.True(model.BarIsError);
        Assert.Equal("Cooldown: \"abc\" is not a whole number.", model.BarText);

        model.Form.CooldownMinutes = "7";
        model.Touch();
        Assert.False(model.BarIsError);
        Assert.Equal("You have unsaved changes", model.BarText);
    }

    [Fact]
    public void LoadingAgain_ThrowsAwayUnsavedEdits_AndErrors()
    {
        var model = Loaded();
        model.Form.CooldownMinutes = "99";
        model.SmtpPassword.Typed = "secret";
        model.ShowError("oops");

        model.Load(new AppSettings(), false, false);

        Assert.False(model.IsDirty);
        Assert.False(model.BarIsError);
        Assert.Equal("10", model.Form.CooldownMinutes);
        Assert.Equal("", model.SmtpPassword.Typed);
    }

    [Fact]
    public void WhileLoading_ThePageCanTellItIsNotTheUsersEdit()
    {
        var model = new SettingsModel();
        var seen = new List<bool>();
        model.PropertyChanged += (_, _) => seen.Add(model.IsLoading);

        model.Load(new AppSettings(), false, false);

        Assert.NotEmpty(seen);
        Assert.All(seen, loading => Assert.True(loading));
        Assert.False(model.IsLoading);
    }

    // --- Secrets ------------------------------------------------------------------------

    [Fact]
    public void ATypedPassword_IsAChange_AndIsNeverShownBack()
    {
        var model = Loaded(hasPassword: true);
        Assert.Equal("Saved", model.SmtpPassword.Status);
        Assert.True(model.SmtpPassword.CanRemove);

        model.SmtpPassword.Typed = "hunter2";

        Assert.True(model.IsDirty);
        Assert.Equal("Will be saved", model.SmtpPassword.Status);
        Assert.False(model.SmtpPassword.CanRemove);  // typing replaces it; there is nothing to remove first
    }

    [Fact]
    public void RemovingASecret_IsAChange_AndTypingAgainCancelsIt()
    {
        var model = Loaded(hasToken: true);

        model.TelegramToken.Remove();
        Assert.True(model.IsDirty);
        Assert.Equal("Will be removed", model.TelegramToken.Status);
        Assert.False(model.TelegramToken.CanRemove);

        model.TelegramToken.Typed = "123456789:abc";
        Assert.False(model.TelegramToken.Removed);
        Assert.Equal("Will be saved", model.TelegramToken.Status);
    }

    [Fact]
    public void ASecretThatWasNeverSet_SaysSo_AndOffersNothingToRemove()
    {
        var model = Loaded();

        Assert.Equal("Not set", model.SmtpPassword.Status);
        Assert.False(model.SmtpPassword.CanRemove);
        Assert.False(model.IsDirty);
    }

    // --- The Test buttons ----------------------------------------------------------------

    [Fact]
    public void ATestButton_IsDisabledWhileSending_AndReportsHowItWent()
    {
        var model = Loaded();
        var state = model.Test(ChannelKind.Email);

        state.Start();
        Assert.False(state.CanTest);
        Assert.Equal("Sending…", state.Text);

        state.Finish(false, "The server said no.");
        Assert.True(state.CanTest);
        Assert.True(state.IsError);
        Assert.Equal("The server said no.", state.Text);

        state.Start();
        state.Finish(true, "ignored");
        Assert.False(state.IsError);
        Assert.Equal("Sent. Check that it arrived.", state.Text);
    }

    [Fact]
    public void EachChannel_HasItsOwnTestState()
    {
        var model = Loaded();

        var states = new[] { ChannelKind.Toast, ChannelKind.Sound, ChannelKind.Email, ChannelKind.Telegram }.Select(model.Test).ToList();

        Assert.Equal(4, states.Distinct().Count());
    }
}
