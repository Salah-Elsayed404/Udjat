namespace Udjat.Core.Settings;

/// <summary>The secrets Udjat keeps. They never go in the settings file.</summary>
public enum SecretName
{
    SmtpPassword,
    TelegramBotToken,
}

/// <summary>Where the secrets are kept (Windows Credential Manager in the app). A fake of this keeps the save order testable.</summary>
public interface ISecretStore
{
    /// <summary>Windows allows at most 2560 bytes per credential; text is stored as UTF-16, two bytes a character.</summary>
    public const int MaxLength = 1280;

    /// <summary>The secret, or null if none is stored.</summary>
    string? Read(SecretName name);

    /// <summary>Stores the secret, replacing any earlier one. To remove a secret, use <see cref="Delete"/>.</summary>
    /// <exception cref="ArgumentException">The value is empty or longer than <see cref="MaxLength"/>.</exception>
    void Write(SecretName name, string value);

    /// <summary>Removes the secret. Nothing happens if there is none.</summary>
    void Delete(SecretName name);
}
