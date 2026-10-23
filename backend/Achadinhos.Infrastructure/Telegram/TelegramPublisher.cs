using System.Net.Http.Json;
using System.Text.Json;
using Achadinhos.Application.Publishing;
using Microsoft.Extensions.Options;

namespace Achadinhos.Infrastructure.Telegram;

public sealed class TelegramPublisher(HttpClient client, IOptions<TelegramOptions> options) : ITelegramPublisher
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false
    };
    private static readonly HashSet<string> ImageErrors = new(StringComparer.Ordinal)
    {
        "Bad Request: failed to get HTTP URL content",
        "Bad Request: wrong type of the web page content",
        "Bad Request: PHOTO_INVALID_DIMENSIONS",
        "Bad Request: IMAGE_PROCESS_FAILED"
    };

    public async Task<TelegramPublicationResult> PublishAsync(
        TelegramPublicationRequest request, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.Enabled) throw new TelegramPublicationException(TelegramFailureKind.Disabled);
        cancellationToken.ThrowIfCancellationRequested();
        var photo = TelegramMessageFormatter.IsSafeHttpsUrl(request.ImageUrl);
        var text = TelegramMessageFormatter.Format(request, photo);
        var keyboard = new TelegramKeyboard([[new TelegramButton("Ver oferta", request.AffiliateUrl)]]);
        var method = photo ? "sendPhoto" : "sendMessage";
        using var message = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.telegram.org/bot{settings.BotToken}/{method}");
        message.Content = photo
            ? JsonContent.Create(new TelegramPhotoPayload(settings.ChatId, request.ImageUrl!, text, "HTML", keyboard), options: Json)
            : JsonContent.Create(new TelegramTextPayload(settings.ChatId, text, "HTML", keyboard), options: Json);

        // Once SendAsync starts, even cancellation may hide a successfully delivered message.
        // No retry or photo fallback here: the future orchestrator persists each attempt.
        try
        {
            using var response = await client.SendAsync(message, cancellationToken);
            var status = (int)response.StatusCode;
            if (status >= 500 || status is < 200 or >= 300 && status is not (400 or 401 or 403 or 429))
                throw new TelegramPublicationException(TelegramFailureKind.UnknownDelivery, status);

            var body = await response.Content.ReadFromJsonAsync<TelegramApiResponse>(Json, cancellationToken);
            if (response.IsSuccessStatusCode && body is { Ok: true, Result: { MessageId: > 0, Date: > 0, Chat.Id: < 0 } result })
            {
                if (long.TryParse(settings.ChatId, out var configuredChat) && configuredChat != result.Chat.Id)
                    throw new TelegramPublicationException(TelegramFailureKind.UnknownDelivery, status);
                return new TelegramPublicationResult(result.Chat.Id, result.MessageId,
                    DateTimeOffset.FromUnixTimeSeconds(result.Date));
            }

            if (body is { Ok: false, Result: null, ErrorCode: 400 or 401 or 403 or 429 }
                && (status == body.ErrorCode || response.IsSuccessStatusCode))
            {
                var kind = body.ErrorCode == 429 ? TelegramFailureKind.RateLimited
                    : photo && body.ErrorCode == 400 && ImageErrors.Contains(body.Description ?? "")
                        ? TelegramFailureKind.ImageRejected : TelegramFailureKind.Rejected;
                throw new TelegramPublicationException(kind, status, body.ErrorCode,
                    body.Parameters?.RetryAfter is > 0 ? body.Parameters.RetryAfter : null);
            }
            throw new TelegramPublicationException(TelegramFailureKind.UnknownDelivery, status);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException
            or JsonException or IOException or ArgumentOutOfRangeException)
        {
            throw new TelegramPublicationException(TelegramFailureKind.UnknownDelivery);
        }
    }
}
