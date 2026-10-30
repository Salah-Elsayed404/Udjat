namespace Udjat.Tests;

/// <summary>A new empty folder under the system temp folder for one test. Removed when the test is done.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Root = Path.Combine(Path.GetTempPath(), "UdjatTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    /// <summary>The full path of a file in the folder (the file itself is not created).</summary>
    public string File(string name) => Path.Combine(Root, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp folder is harmless; never fail a test over it.
        }
    }
}
