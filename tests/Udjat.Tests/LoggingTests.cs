using System.Globalization;
using Udjat.Core.Logging;
using Xunit;

namespace Udjat.Tests;

/// <summary>Tests for <see cref="Redactor"/> and <see cref="AlertLog"/>.</summary>
public class LoggingTests
{
    private static readonly DateTimeOffset Time = new(2026, 10, 29, 18, 3, 12, TimeSpan.Zero);

    private static string Stamp(DateTimeOffset time) =>
        time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    // --- Redactor ------------------------------------------------------------

    [Fact]
    public void Redact_HidesEveryKnownSecret()
    {
        var text = Redactor.Redact("login failed for hunter2 with key abc-123", ["hunter2", "abc-123"]);

        Assert.Equal("login failed for *** with key ***", text);
    }

    [Fact]
    public void Redact_HidesALongerSecretWhole_EvenIfItContainsAShorterOne()
    {
        var text = Redactor.Redact("token=abcdef", ["abc", "abcdef"]);

        Assert.Equal("token=***", text);
    }

    [Fact]
    public void Redact_IgnoresEmptySecrets_AndLeavesOrdinaryTextAlone()
    {
        var text = Redactor.Redact("The server timed out at 18:03:12", ["", "  "]);

        Assert.Equal("The server timed out at 18:03:12", text);
    }

    [Fact]
    public void Redact_HidesAnythingShapedLikeATelegramToken_EvenIfUnknown()
    {
        var text = Redactor.Redact("GET https://api.telegram.org/bot123456789:AAFAKEFAKEFAKEFAKEFAKEFAKEFAKEFAKE/sendMessage failed", []);

        Assert.DoesNotContain("AAFAKEFAKEFAKEFAKEFAKEFAKEFAKEFAKE", text);
        Assert.Contains("***", text);
    }

    [Fact]
    public void Redact_IsCaseSensitive()
    {
        Assert.Equal("PASSWORD ***", Redactor.Redact("PASSWORD secret", ["secret"]));
        Assert.Equal("Secret", Redactor.Redact("Secret", ["secret"]));
    }

    // --- AlertLog --------------------------------------------------------------

    [Fact]
    public void Append_WritesTheTimeAndTheFields_OnOneLine()
    {
        using var folder = new TempFolder();
        var log = new AlertLog(folder.File("alerts.log"));

        var written = log.Append(Time, "CRITICAL", "GPU hotspot", "NVIDIA GeForce RTX 3060", "108 °C", "toast: sent, email: failed (TimeoutException)");

        Assert.True(written);
        var lines = File.ReadAllLines(folder.File("alerts.log"));
        Assert.Equal(
            $"{Stamp(Time)} | CRITICAL | GPU hotspot | NVIDIA GeForce RTX 3060 | 108 °C | toast: sent, email: failed (TimeoutException)",
            Assert.Single(lines));
    }

    [Fact]
    public void Append_SkipsEmptyFields_AndKeepsEachEntryOnOneLine()
    {
        using var folder = new TempFolder();
        var log = new AlertLog(folder.File("alerts.log"));

        log.Append(Time, "STOPPED", "", "  ", "since 18:03\r\nsecond line");
        log.Append(Time, "RESUMED");

        var lines = File.ReadAllLines(folder.File("alerts.log"));
        Assert.Equal(2, lines.Length);
        Assert.Equal($"{Stamp(Time)} | STOPPED | since 18:03 second line", lines[0]);
        Assert.Equal($"{Stamp(Time)} | RESUMED", lines[1]);
    }

    [Fact]
    public void Append_CreatesTheFolder_AndAddsToTheFile()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Root, "logs", "alerts.log");
        var log = new AlertLog(path);

        log.Append(Time, "one");
        log.Append(Time, "two");

        Assert.Equal(2, File.ReadAllLines(path).Length);
    }

    [Fact]
    public void Append_StartsANewFile_WhenTheOldOneIsFull()
    {
        using var folder = new TempFolder();
        var path = folder.File("alerts.log");
        var log = new AlertLog(path, maxBytes: 300);

        for (var i = 0; i < 12; i++)
            log.Append(Time, $"entry {i:00}", "padding to make the line longer");

        var current = File.ReadAllLines(path);
        var old = File.ReadAllLines(folder.File("alerts.old.log"));
        Assert.NotEmpty(current);
        Assert.NotEmpty(old);
        Assert.True(new FileInfo(path).Length < 300 + 120);  // never grows far past the limit
        Assert.Contains("entry 11", current[^1]);            // the newest entry is in the new file
    }

    [Fact]
    public void Rotating_ReplacesTheOlderOldFile()
    {
        using var folder = new TempFolder();
        var path = folder.File("alerts.log");
        File.WriteAllText(folder.File("alerts.old.log"), "ancient history");
        File.WriteAllText(path, new string('x', 400));
        var log = new AlertLog(path, maxBytes: 300);

        log.Append(Time, "fresh");

        Assert.Equal(new string('x', 400), File.ReadAllText(folder.File("alerts.old.log")));
        Assert.Contains("fresh", File.ReadAllText(path));
    }

    [Fact]
    public void Append_ReturnsFalse_WhenTheFileCannotBeWritten()
    {
        using var folder = new TempFolder();
        var path = folder.File("alerts.log");
        Directory.CreateDirectory(path);  // a folder where the file should be
        var log = new AlertLog(path);

        Assert.False(log.Append(Time, "cannot be written"));
    }

    [Fact]
    public async Task Append_FromManyThreads_KeepsEveryLineWhole()
    {
        using var folder = new TempFolder();
        var path = folder.File("alerts.log");
        var log = new AlertLog(path);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
                log.Append(Time, "worker " + worker, "entry " + i, "end");
        }, TestContext.Current.CancellationToken)));

        var lines = File.ReadAllLines(path);
        Assert.Equal(400, lines.Length);
        Assert.All(lines, line => Assert.EndsWith(" | end", line));
    }
}
