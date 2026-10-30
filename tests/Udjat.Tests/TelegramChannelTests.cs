using System.Net;
using System.Text.Json;
using Udjat.Channels;
using Udjat.Core.Alerts;
using Udjat.Core.Logging;
using Udjat.Core.Monitoring;
using Udjat.Core.Settings;
using Xunit;

namespace Udjat.Tests;

/// <summary>An HTTP handler that answers from a function and remembers each request.</summary>
internal sealed class FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(Uri? Url, HttpMethod Method, string Body)> Calls { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Calls.Add((request.RequestUri, request.Method, body));
        return respond(request);
    }
}

/// <summary>Tests for <see cref="TelegramChannel"/>. No real network: a fake handler stands in for api.telegram.org.</summary>
public class TelegramChannelTests
{
    private const string Token = "123456789:AAFAKEFAKEFAKEFAKEFAKEFAKEFAKEFAKE";
    private static readonly DateTimeOffset Time = new(2026, 10, 29, 18, 3, 12, TimeSpan.Zero);

    private static AlertMessage Message() => new(AlertMessageKind.Alert, AlertLevel.Critical, "Udjat: CPU temperature is CRITICAL (98 °C)", "details here", Time);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Send_PostsTheTitleAndBody_ToTheBotApi()
    {
        var handler = new FakeHttpHandler(_ => Json(HttpStatusCode.OK, """{"ok":true}"""));
        var channel = new TelegramChannel(new HttpClient(handler), Token, "-1001234567890");

        await channel.SendAsync(Message(), TestContext.Current.CancellationToken);

        var call = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal($"https://api.telegram.org/bot{Token}/sendMessage", call.Url!.ToString());

        using var json = JsonDocument.Parse(call.Body);
        Assert.Equal(-1001234567890L, json.RootElement.GetProperty("chat_id").GetInt64());
        Assert.Equal("Udjat: CPU temperature is CRITICAL (98 °C)\n\ndetails here", json.RootElement.GetProperty("text").GetString());
        Assert.False(json.RootElement.TryGetProperty("parse_mode", out _));  // plain text, nothing to escape
    }

    [Fact]
    public async Task AnErrorAnswer_BecomesAnExceptionWithTelegramsReason_ButNoToken()
    {
        var handler = new FakeHttpHandler(_ => Json(HttpStatusCode.BadRequest, """{"ok":false,"error_code":400,"description":"Bad Request: chat not found"}"""));
        var channel = new TelegramChannel(new HttpClient(handler), Token, "42");

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => channel.SendAsync(Message(), TestContext.Current.CancellationToken));

        Assert.Equal("Telegram answered 400: Bad Request: chat not found", error.Message);
        Assert.DoesNotContain(Token, error.Message);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1,2]")]
    [InlineData("""{"description": 5}""")]
    [InlineData("")]
    public async Task AnAnswerWithoutAUsableDescription_StillFailsClearly(string body)
    {
        var handler = new FakeHttpHandler(_ => Json(HttpStatusCode.Unauthorized, body));
        var channel = new TelegramChannel(new HttpClient(handler), Token, "42");

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => channel.SendAsync(Message(), TestContext.Current.CancellationToken));

        Assert.Equal("Telegram answered 401", error.Message);
    }

    [Fact]
    public async Task ALongDescription_IsCutShort()
    {
        var handler = new FakeHttpHandler(_ => Json(HttpStatusCode.BadRequest, $$"""{"description":"{{new string('x', 500)}}"}"""));
        var channel = new TelegramChannel(new HttpClient(handler), Token, "42");

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => channel.SendAsync(Message(), TestContext.Current.CancellationToken));

        Assert.True(error.Message.Length < 150);
    }

    [Fact]
    public async Task ANetworkFailure_PassesThrough()
    {
        var handler = new FakeHttpHandler(_ => throw new HttpRequestException("No such host is known."));
        var channel = new TelegramChannel(new HttpClient(handler), Token, "42");

        await Assert.ThrowsAsync<HttpRequestException>(() => channel.SendAsync(Message(), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-colon-here")]
    [InlineData("123456789:has space")]
    [InlineData("123456789:has/slash")]
    [InlineData("abc:AAFAKEFAKEFAKEFAKEFAKEFAKEFAKEFAKE")]
    [InlineData("123456789:AAH../../other")]
    public void ATokenInTheWrongForm_IsRefused_WithoutRepeatingIt(string token)
    {
        var error = Assert.Throws<ArgumentException>(() => new TelegramChannel(new HttpClient(), token, "42"));

        if (token.Length > 5)
            Assert.DoesNotContain(token, error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("my chat")]
    [InlineData("12.5")]
    public void AChatIdThatIsNotAWholeNumber_IsRefused(string chatId)
    {
        Assert.Throws<ArgumentException>(() => new TelegramChannel(new HttpClient(), Token, chatId));
    }

    [Fact]
    public void KindIsTelegram()
    {
        Assert.Equal(ChannelKind.Telegram, new TelegramChannel(new HttpClient(), Token, "42").Kind);
    }

    [Fact]
    public async Task WhenItFails_TheLogNeverContainsTheToken_EvenIfTheErrorTextHasTheUrl()
    {
        // An HTTP library sometimes puts the whole URL, token included, in its error text.
        var handler = new FakeHttpHandler(request => throw new HttpRequestException($"Connection to {request.RequestUri} failed"));
        var channel = new TelegramChannel(new HttpClient(handler), Token, "42");
        using var folder = new TempFolder();
        var settings = new AppSettings { Telegram = new TelegramSettings { Enabled = true } };
        var dispatcher = new AlertDispatcher(settings, [channel], new AlertLog(folder.File("alerts.log")), [Token]);

        dispatcher.Enqueue(Message());
        dispatcher.Complete();
        await dispatcher.RunAsync(TestContext.Current.CancellationToken);

        var line = Assert.Single(File.ReadAllLines(folder.File("alerts.log")));
        Assert.DoesNotContain("AAFAKEFAKEFAKEFAKEFAKEFAKEFAKEFAKE", line);
        Assert.Contains("telegram: failed (HttpRequestException: Connection to https://api.telegram.org/bot***/sendMessage failed)", line);
    }
}
