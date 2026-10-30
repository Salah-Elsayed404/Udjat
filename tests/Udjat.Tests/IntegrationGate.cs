using Xunit;

namespace Udjat.Tests;

/// <summary>
/// Tests that change something on the PC outside the test run (a stored credential, a scheduled task)
/// only run when the environment variable UDJAT_INTEGRATION is 1. A normal test run never touches them.
/// </summary>
internal static class IntegrationGate
{
    public static void Require() =>
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("UDJAT_INTEGRATION") == "1",
            "Set UDJAT_INTEGRATION=1 to run tests that briefly create and delete a test credential or scheduled task.");
}
