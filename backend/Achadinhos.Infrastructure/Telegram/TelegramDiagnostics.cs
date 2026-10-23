using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Achadinhos.Infrastructure.Telegram;

public sealed record TelegramConnection(long ChatId, long BotId, bool PrivateChannel, bool CanPostMessages);

public sealed class TelegramDiagnostics(IHttpClientFactory clients, IOptions<TelegramOptions> options)
{
    public async Task<TelegramConnection> CheckAsync(CancellationToken ct = default)
    {
        var settings = options.Value;
        var validation = new TelegramOptionsValidator().Validate(null, new TelegramOptions
        {
            Enabled = true,
            BotToken = settings.BotToken,
            ChatId = settings.ChatId,
            TimeoutSeconds = settings.TimeoutSeconds
        });
        if (validation.Failed) throw new InvalidOperationException("Telegram configuration missing or invalid (values redacted).");
        var stage = "getMe";
        int? httpStatus = null;
        var responseKind = "none";
        try
        {
            using var client = clients.CreateClient("TelegramDiagnostics");
            async Task<JsonElement> Call(string method, object body)
            {
                using var response = await client.PostAsJsonAsync(
                    $"https://api.telegram.org/bot{settings.BotToken}/{method}", body, ct);
                httpStatus = (int)response.StatusCode;
                responseKind = response.Content.Headers.ContentType?.MediaType == "application/json" ? "json" : "non-json";
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"Telegram diagnostic rejected: HTTP {(int)response.StatusCode}.");
                using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (!document.RootElement.GetProperty("ok").GetBoolean())
                    throw new InvalidOperationException("Telegram diagnostic rejected by API.");
                return document.RootElement.GetProperty("result").Clone();
            }
            var me = await Call("getMe", new { });
            if (!me.GetProperty("is_bot").GetBoolean()) throw new InvalidOperationException("Telegram identity is not a bot.");
            var botId = me.GetProperty("id").GetInt64();
            stage = "getChat";
            var chat = await Call("getChat", new { chat_id = settings.ChatId });
            if (chat.GetProperty("type").GetString() != "channel")
                throw new InvalidOperationException("Configured destination is not a Telegram channel.");
            var id = chat.GetProperty("id").GetInt64();
            stage = "getChatMember";
            var member = await Call("getChatMember", new { chat_id = id, user_id = botId });
            var canPost = member.GetProperty("status").GetString() == "administrator"
                && member.TryGetProperty("can_post_messages", out var permission) && permission.GetBoolean();
            if (!canPost) throw new InvalidOperationException("Bot is not a channel administrator with posting permission.");
            return new TelegramConnection(id, botId, !chat.TryGetProperty("username", out _), canPost);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException
            or KeyNotFoundException or InvalidOperationException)
        {
            // Never expose raw API response, request URI or inner exception.
            throw new InvalidOperationException($"Telegram diagnostic failed: stage={stage}; HTTP={httpStatus}; kind={error.GetType().Name}; response={responseKind}. Values redacted.");
        }
    }
}
