namespace Achadinhos.Application.Publishing;

public sealed record TelegramPublicationResult(long ChatId, int MessageId, DateTimeOffset SentAt);
