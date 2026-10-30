using System.Diagnostics;

namespace Udjat;

/// <summary>
/// Opens links, files and folders. Udjat runs with administrator rights, and whatever it starts directly would
/// inherit them, so the work is handed to explorer.exe, which opens it with the normal user's rights.
/// </summary>
internal static class ShellOpen
{
    /// <summary>Opens a web page in the default browser. Only https links are accepted.</summary>
    public static void Url(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Only https links can be opened.", nameof(url));

        Open(uri.AbsoluteUri);
    }

    /// <summary>Opens an existing file or folder. A file that does not exist yet opens its folder instead.</summary>
    public static void Path(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            Open(path);
            return;
        }

        var folder = System.IO.Path.GetDirectoryName(path);
        if (folder is not null && Directory.Exists(folder))
            Open(folder);
    }

    private static void Open(string target)
    {
        var explorer = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var start = new ProcessStartInfo(explorer) { UseShellExecute = false };
        start.ArgumentList.Add(target);
        Process.Start(start)?.Dispose();
    }
}
