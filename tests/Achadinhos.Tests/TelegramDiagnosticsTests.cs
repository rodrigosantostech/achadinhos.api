using System.Net;
using Achadinhos.Infrastructure.Telegram;
using Microsoft.Extensions.Options;
using Xunit;

namespace Achadinhos.Tests;

public sealed class TelegramDiagnosticsTests
{
    private const string Token = "123456789:FAKE_TEST_TOKEN_NEVER_USE_REAL";

    [Fact]
    public async Task ValidatesBotChannelAndPostingPermissionWithoutSendingMessages()
    {
        var calls = new List<string>();
        using var handler = new StubHandler(request =>
        {
            var method = request.RequestUri!.Segments.Last();
            calls.Add(method);
            return method switch
            {
                "getMe" => Response("""{"ok":true,"result":{"id":123,"is_bot":true}}"""),
                "getChat" => Response("""{"ok":true,"result":{"id":-1001234567890,"type":"channel"}}"""),
                "getChatMember" => Response("""{"ok":true,"result":{"status":"administrator","can_post_messages":true}}"""),
                _ => throw new InvalidOperationException("Unexpected network side effect.")
            };
        });
        using var client = new HttpClient(handler);
        var result = await new TelegramDiagnostics(new ClientFactory(client), Settings()).CheckAsync();
        Assert.True(result.PrivateChannel);
        Assert.True(result.CanPostMessages);
        Assert.Equal(-1001234567890L, result.ChatId);
        Assert.Equal(new[] { "getMe", "getChat", "getChatMember" }, calls);
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("text/html")]
    public async Task RejectionsReportOnlySafeStageStatusAndResponseKind(string contentType)
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("blocked " + Token, System.Text.Encoding.UTF8, contentType)
        });
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TelegramDiagnostics(new ClientFactory(client), Settings()).CheckAsync());
        Assert.Contains("stage=getMe", error.Message);
        Assert.Contains("HTTP=403", error.Message);
        Assert.DoesNotContain(Token, error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task TransportExceptionNeverLeaksCredential()
    {
        using var handler = new StubHandler(_ => throw new HttpRequestException("bot" + Token));
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TelegramDiagnostics(new ClientFactory(client), Settings()).CheckAsync());
        Assert.DoesNotContain(Token, error.ToString());
        Assert.Null(error.InnerException);
    }

    private static IOptions<TelegramOptions> Settings() =>
        Options.Create(new TelegramOptions { BotToken = Token, ChatId = "-1001234567890" });
    private static HttpResponseMessage Response(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(send(request));
    }
}
