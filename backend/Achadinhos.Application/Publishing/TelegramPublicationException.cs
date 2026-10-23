namespace Achadinhos.Application.Publishing;

public enum TelegramFailureKind
{
    Disabled,
    InvalidRequest,
    Rejected,
    ImageRejected,
    RateLimited,
    UnknownDelivery
}

// Only structured, safe metadata crosses the HTTP boundary. Never retain raw response
// descriptions, request URIs or inner transport exceptions (they may contain the token).
public sealed class TelegramPublicationException(
    TelegramFailureKind kind,
    int? httpStatus = null,
    int? telegramErrorCode = null,
    int? retryAfterSeconds = null) : Exception($"Telegram publication failed: {kind}.")
{
    public TelegramFailureKind Kind { get; } = kind;
    public int? HttpStatus { get; } = httpStatus;
    public int? TelegramErrorCode { get; } = telegramErrorCode;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}
