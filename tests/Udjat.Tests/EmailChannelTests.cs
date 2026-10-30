using System.Net;
using System.Net.Sockets;
using System.Text;
using MimeKit;
using Udjat.Channels;
using Udjat.Core.Alerts;
using Udjat.Core.Monitoring;
using Udjat.Core.Settings;
using Xunit;

namespace Udjat.Tests;

/// <summary>Tests for <see cref="EmailChannel"/>. Building the email is tested directly; the sending is tested against a tiny fake server.</summary>
public class EmailChannelTests
{
    private static readonly DateTimeOffset Time = new(2026, 10, 29, 18, 3, 12, TimeSpan.Zero);

    private static readonly EmailSettings Settings = new()
    {
        Enabled = true,
        Host = "smtp.example.com",
        From = "udjat@example.com",
        To = "me@example.com",
    };

    private static AlertMessage Message() =>
        new(AlertMessageKind.Alert, AlertLevel.Critical, "Udjat: GPU hotspot is CRITICAL (108 °C)", "NVIDIA GeForce RTX 3060: GPU hotspot is 108 °C.", Time);

    // --- The email itself -----------------------------------------------------------

    [Fact]
    public void BuildMessage_UsesTheTitleAsSubject_AndPlainTextBody()
    {
        var email = EmailChannel.BuildMessage(Settings, Message());

        Assert.Equal("Udjat: GPU hotspot is CRITICAL (108 °C)", email.Subject);
        Assert.Equal("udjat@example.com", Assert.Single(email.From.Mailboxes).Address);
        Assert.Equal("me@example.com", Assert.Single(email.To.Mailboxes).Address);
        Assert.Equal("NVIDIA GeForce RTX 3060: GPU hotspot is 108 °C.", email.TextBody);
        Assert.Null(email.HtmlBody);  // plain text only
    }

    [Fact]
    public async Task Send_HandsTheBuiltEmailToTheTransport()
    {
        MimeMessage? sent = null;
        var channel = new EmailChannel(Settings, (email, _) => { sent = email; return Task.CompletedTask; });

        await channel.SendAsync(Message(), TestContext.Current.CancellationToken);

        Assert.Equal("Udjat: GPU hotspot is CRITICAL (108 °C)", sent!.Subject);
        Assert.Equal(ChannelKind.Email, channel.Kind);
    }

    [Fact]
    public async Task ATransportFailure_PassesThrough()
    {
        var channel = new EmailChannel(Settings, (_, _) => throw new IOException("connection reset"));

        await Assert.ThrowsAsync<IOException>(() => channel.SendAsync(Message(), TestContext.Current.CancellationToken));
    }

    // --- The real transport, against a fake server ------------------------------------

    [Fact]
    public async Task AServerThatIsNotThere_FailsQuickly_WithoutLeakingThePassword()
    {
        var port = FreeClosedPort();
        var settings = Settings with { Host = "127.0.0.1", Port = port, Username = "me" };
        var channel = new EmailChannel(settings, "hunter2");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        var error = await Assert.ThrowsAnyAsync<Exception>(() => channel.SendAsync(Message(), timeout.Token));

        Assert.DoesNotContain("hunter2", error.ToString());
    }

    [Fact]
    public async Task AServerThatCannotEncrypt_GetsNeitherThePasswordNorTheEmail()
    {
        await using var server = new FakeSmtpServer(offersStartTls: false);
        var settings = Settings with { Host = "127.0.0.1", Port = server.Port, Username = "me" };
        var channel = new EmailChannel(settings, "hunter2");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        await Assert.ThrowsAnyAsync<Exception>(() => channel.SendAsync(Message(), timeout.Token));
        await server.FinishAsync();

        Assert.Contains(server.Commands, c => c.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(server.Commands, c => c.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(server.Commands, c => c.StartsWith("MAIL", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(server.Commands, c => c.Contains("hunter2") || c.Contains("CRITICAL"));
    }

    /// <summary>A port nothing listens on: bind one, note the number, let go.</summary>
    private static int FreeClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Speaks just enough SMTP to greet a client and answer EHLO, and records every command it receives.</summary>
    private sealed class FakeSmtpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _serving;
        private readonly CancellationTokenSource _stop = new();

        public FakeSmtpServer(bool offersStartTls)
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serving = Task.Run(() => ServeAsync(offersStartTls, _stop.Token));
        }

        public int Port { get; }

        public List<string> Commands { get; } = [];

        /// <summary>Waits until the client has gone.</summary>
        public Task FinishAsync() => _serving.WaitAsync(TimeSpan.FromSeconds(10));

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            try
            {
                await _serving;
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
            {
                // The client or the test ended the conversation; nothing to report.
            }

            _stop.Dispose();
        }

        private async Task ServeAsync(bool offersStartTls, CancellationToken stop)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(stop);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII);
                await using var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true, NewLine = "\r\n" };

                await writer.WriteLineAsync("220 localhost ESMTP fake");
                while (await reader.ReadLineAsync(stop) is { } line)
                {
                    lock (Commands)
                        Commands.Add(line);

                    if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("250-localhost");
                        await writer.WriteLineAsync(offersStartTls ? "250-STARTTLS" : "250-SIZE 1000000");
                        await writer.WriteLineAsync("250 AUTH PLAIN LOGIN");
                    }
                    else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("221 bye");
                        break;
                    }
                    else
                    {
                        await writer.WriteLineAsync("250 ok");
                    }
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
            {
                // The client hung up, which is exactly what we expect it to do.
            }
        }
    }
}
