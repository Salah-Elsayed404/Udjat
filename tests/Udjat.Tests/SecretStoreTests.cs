using Udjat.Core.Settings;
using Udjat.Windows.Secrets;
using Xunit;

namespace Udjat.Tests;

/// <summary>Tests for <see cref="SecretStore"/>. The ones that really use Credential Manager are opt-in (see <see cref="IntegrationGate"/>).</summary>
public class SecretStoreTests
{
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void AnEmptySecret_IsRefused_BeforeAnythingIsStored(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new SecretStore("UdjatTest-never-used").Write(SecretName.SmtpPassword, value!));
    }

    [Fact]
    public void ASecretThatIsTooLong_IsRefused_AndTheMessageDoesNotRepeatIt()
    {
        var tooLong = new string('x', SecretStore.MaxLength + 1);

        var error = Assert.Throws<ArgumentException>(() => new SecretStore("UdjatTest-never-used").Write(SecretName.TelegramBotToken, tooLong));

        Assert.DoesNotContain("xxxxx", error.Message);
    }

    [Fact]
    public void WriteReadReplaceDelete_WorkAgainstTheRealCredentialManager()
    {
        IntegrationGate.Require();
        var store = new SecretStore("UdjatTest-" + Guid.NewGuid().ToString("N"));

        try
        {
            Assert.Null(store.Read(SecretName.SmtpPassword));

            store.Write(SecretName.SmtpPassword, "first-ÄÖ-pässword");
            store.Write(SecretName.TelegramBotToken, "123456789:token");
            Assert.Equal("first-ÄÖ-pässword", store.Read(SecretName.SmtpPassword));  // Unicode survives

            store.Write(SecretName.SmtpPassword, "second");
            Assert.Equal("second", store.Read(SecretName.SmtpPassword));            // replaced, not added
            Assert.Equal("123456789:token", store.Read(SecretName.TelegramBotToken));  // the other one is untouched

            store.Write(SecretName.SmtpPassword, new string('y', SecretStore.MaxLength));
            Assert.Equal(SecretStore.MaxLength, store.Read(SecretName.SmtpPassword)!.Length);  // the longest allowed
        }
        finally
        {
            store.Delete(SecretName.SmtpPassword);
            store.Delete(SecretName.TelegramBotToken);
        }

        Assert.Null(store.Read(SecretName.SmtpPassword));
        store.Delete(SecretName.SmtpPassword);  // deleting what is not there is fine
    }
}
