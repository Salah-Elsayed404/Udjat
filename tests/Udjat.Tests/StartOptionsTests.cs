using Udjat.Windows;
using Xunit;

namespace Udjat.Tests;

/// <summary>Tests for <see cref="StartOptions"/>: what Udjat does with the words typed after its name.</summary>
public class StartOptionsTests
{
    [Fact]
    public void NoArguments_MeansANormalStart()
    {
        Assert.Equal(new StartOptions(false, false, null), StartOptions.Parse([]));
    }

    [Fact]
    public void Tray_StartsHidden()
    {
        Assert.True(StartOptions.Parse(["--tray"]).StartHidden);
    }

    [Fact]
    public void Exit_AsksTheRunningOneToClose_IgnoringCase()
    {
        Assert.True(StartOptions.Parse(["--EXIT"]).Exit);
    }

    [Fact]
    public void DataDir_TakesTheNextWord()
    {
        var options = StartOptions.Parse(["--tray", "--data-dir", @"C:\Temp\udjat test", "--exit"]);

        Assert.Equal(new StartOptions(true, true, @"C:\Temp\udjat test"), options);
    }

    [Fact]
    public void DataDir_WithoutAFolder_IsIgnored()
    {
        Assert.Null(StartOptions.Parse(["--data-dir"]).DataDir);
    }

    [Fact]
    public void UnknownWords_AreIgnored()
    {
        Assert.Equal(new StartOptions(false, false, null), StartOptions.Parse(["--surprise", "whatever"]));
    }
}
