using System.Net;
using System.Text.Json;
using Achadinhos.Application.Publishing;
using Achadinhos.Infrastructure.Telegram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Achadinhos.Tests;

public sealed class TelegramPublisherTests
{
    private const string Token = "123456789:FAKE_TEST_TOKEN_NEVER_USE_REAL";
    private const string Chat = "-1001234567890";
    private const string Success = """{"ok":true,"result":{"message_id":42,"date":1792771200,"chat":{"id":-1001234567890}}}""";
    private static TelegramPublicationRequest Request(string? image = null) =>
        new("SSD <1 TB>", 329.90m, null, "https://example.com/affiliate", image);

    [Theory]
    [InlineData(null, "sendMessage", "text")]
    [InlineData("https://example.com/image.jpg", "sendPhoto", "caption")]
    [InlineData("http://example.com/unsafe.jpg", "sendMessage", "text")]
    public async Task SendsCorrectPayloadAndReadsRealResponseFields(string? image, string method, string field)
    {
        using var handler = new StubHandler(async (message, ct) =>
        {
            Assert.Equal(HttpMethod.Post, message.Method);
            Assert.Equal("api.telegram.org", message.RequestUri!.Host);
            Assert.EndsWith("/" + method, message.RequestUri.AbsolutePath);
            using var json = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(ct));
            var body = json.RootElement;
            Assert.Equal(Chat, body.GetProperty("chat_id").GetString());
            Assert.Equal("HTML", body.GetProperty("parse_mode").GetString());
            Assert.Contains("&lt;1 TB&gt;", body.GetProperty(field).GetString());
            Assert.False(body.TryGetProperty(field == "text" ? "caption" : "text", out _));
            Assert.Equal(method == "sendPhoto", body.TryGetProperty("photo", out _));
            Assert.Equal("https://example.com/affiliate",
                body.GetProperty("reply_markup").GetProperty("inline_keyboard")[0][0].GetProperty("url").GetString());
            return Response(200, Success);
        });
        using var client = new HttpClient(handler);
        var result = await Publisher(client).PublishAsync(Request(image));
        Assert.Equal(-1001234567890L, result.ChatId);
        Assert.Equal(42, result.MessageId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792771200), result.SentAt);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(400, "Bad Request", TelegramFailureKind.Rejected)]
    [InlineData(401, "Unauthorized", TelegramFailureKind.Rejected)]
    [InlineData(403, "Forbidden", TelegramFailureKind.Rejected)]
    [InlineData(429, "Too Many Requests", TelegramFailureKind.RateLimited)]
    [InlineData(500, "Internal Server Error", TelegramFailureKind.UnknownDelivery)]
    [InlineData(503, "Unavailable", TelegramFailureKind.UnknownDelivery)]
    public async Task ClassifiesErrorsWithoutRetryOrLeakingResponse(int status, string description, TelegramFailureKind kind)
    {
        var body = JsonSerializer.Serialize(new
        {
            ok = false,
            error_code = status,
            description = description + Token,
            parameters = new { retry_after = 37 }
        });
        using var handler = new StubHandler((_, _) => Task.FromResult(Response(status, body)));
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<TelegramPublicationException>(() => Publisher(client).PublishAsync(Request()));
        Assert.Equal(kind, error.Kind);
        Assert.Equal(status, error.HttpStatus);
        Assert.Equal(kind == TelegramFailureKind.UnknownDelivery ? null : 37, error.RetryAfterSeconds);
        Assert.DoesNotContain(Token, error.ToString());
        Assert.Null(error.InnerException);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(200, "not json")]
    [InlineData(200, "{}")]
    [InlineData(200, """{"ok":true,"result":{"message_id":42}}""")]
    [InlineData(200, """{"ok":true,"result":{"message_id":42,"date":1792771200,"chat":{"id":-1000000000000}}}""")]
    [InlineData(200, """{"ok":true,"result":{"message_id":42,"date":9223372036854775807,"chat":{"id":-1001234567890}}}""")]
    [InlineData(400, "<html>proxy error</html>")]
    [InlineData(429, """{"ok":false,"error_code":400}""")]
    [InlineData(302, "")]
    public async Task InconclusiveResponsesRequireReconciliation(int status, string body)
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(Response(status, body)));
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<TelegramPublicationException>(() =>
            Publisher(client).PublishAsync(Request("https://example.com/image.jpg")));
        Assert.Equal(TelegramFailureKind.UnknownDelivery, error.Kind);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TransportFailuresAreUncertainAndSanitized(bool timeout)
    {
        using var handler = new StubHandler((_, _) => timeout
            ? Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout " + Token))
            : Task.FromException<HttpResponseMessage>(new HttpRequestException("https://api.telegram.org/bot" + Token)));
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<TelegramPublicationException>(() => Publisher(client).PublishAsync(Request()));
        Assert.Equal(TelegramFailureKind.UnknownDelivery, error.Kind);
        Assert.DoesNotContain(Token, error.ToString());
        Assert.Null(error.InnerException);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RejectedPhotoDoesNotSilentlyMakeASecondAttempt()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(Response(400,
            """{"ok":false,"error_code":400,"description":"Bad Request: failed to get HTTP URL content"}""")));
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<TelegramPublicationException>(() =>
            Publisher(client).PublishAsync(Request("https://example.com/image.jpg")));
        Assert.Equal(TelegramFailureKind.ImageRejected, error.Kind);
        Assert.Equal(1, handler.Calls); // Orchestrator must persist rejection before deciding on fallback.
    }

    [Fact]
    public async Task DisabledInvalidAndPreCancelledRequestsDoNotContactTelegram()
    {
        using var handler = new StubHandler((_, _) => throw new InvalidOperationException("Must not send"));
        using var client = new HttpClient(handler);
        var disabled = new TelegramPublisher(client, Options.Create(new TelegramOptions()));
        var error = await Assert.ThrowsAsync<TelegramPublicationException>(() => disabled.PublishAsync(Request()));
        Assert.Equal(TelegramFailureKind.Disabled, error.Kind);
        await Assert.ThrowsAsync<TelegramPublicationException>(() =>
            Publisher(client).PublishAsync(Request() with { AffiliateUrl = "http://example.com" }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Publisher(client).PublishAsync(Request(), new CancellationToken(true)));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task CancellationAfterDispatchIsUncertain()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new StubHandler((_, ct) =>
        {
            cancellation.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Response(200, Success));
        });
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<TelegramPublicationException>(() =>
            Publisher(client).PublishAsync(Request(), cancellation.Token));
        Assert.Equal(TelegramFailureKind.UnknownDelivery, error.Kind);
    }

    [Fact]
    public void DisabledConfigurationNeedsNoSecretsAndEnabledConfigurationIsValidated()
    {
        var validator = new TelegramOptionsValidator();
        Assert.True(validator.Validate(null, new TelegramOptions()).Succeeded);
        var invalid = validator.Validate(null, new TelegramOptions { Enabled = true, BotToken = Token });
        Assert.True(invalid.Failed);
        Assert.DoesNotContain(Token, string.Join("", invalid.Failures));
        Assert.True(validator.Validate(null, Settings()).Succeeded);
        Assert.True(validator.Validate(null, Settings("@test_channel")).Succeeded);
        var invalidTimeout = Settings();
        invalidTimeout.TimeoutSeconds = 0;
        Assert.True(validator.Validate(null, invalidTimeout).Failed);
    }

    [Fact]
    public void ProductionTransportDisablesRedirectsCookiesAndCredentialBearingActivities()
    {
        var services = new ServiceCollection();
        services.AddTelegramPublishing(Configuration());
        using var provider = services.BuildServiceProvider();
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(nameof(ITelegramPublisher));
        while (handler is DelegatingHandler wrapper) handler = wrapper.InnerHandler!;
        var transport = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.False(transport.AllowAutoRedirect);
        Assert.False(transport.UseCookies);
        Assert.Null(transport.ActivityHeadersPropagator);
    }

    [Fact]
    public async Task DependencyInjectionDoesNotLogTokenEvenWhenTransportExceptionContainsIt()
    {
        var logs = new CaptureLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        services.AddTelegramPublishing(Configuration());
        services.AddHttpClient<ITelegramPublisher, TelegramPublisher>()
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler((_, _) =>
                Task.FromException<HttpResponseMessage>(new HttpRequestException("bot" + Token))));
        using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<ITelegramPublisher>();
        var error = await Assert.ThrowsAsync<TelegramPublicationException>(() => publisher.PublishAsync(Request()));
        Assert.DoesNotContain(Token, error.ToString());
        Assert.All(logs.Messages, text => Assert.DoesNotContain(Token, text));
        Assert.DoesNotContain(Token, Settings().ToString());
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["Telegram:Enabled"] = "true",
            ["Telegram:BotToken"] = Token,
            ["Telegram:ChatId"] = Chat
        }).Build();

    private static TelegramOptions Settings(string chat = Chat) =>
        new() { Enabled = true, BotToken = Token, ChatId = chat };
    private static TelegramPublisher Publisher(HttpClient client) => new(client, Options.Create(Settings()));
    private static HttpResponseMessage Response(int status, string body) =>
        new((HttpStatusCode)status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return send(request, cancellationToken);
        }
    }

    private sealed class CaptureLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Messages);
        public void Dispose() { }
        private sealed class CaptureLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
                => messages.Add(formatter(state, error));
        }
    }
}
