namespace Achadinhos.Domain.Discovery;

public sealed class PublicationChannel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "telegram-test";
    public long TelegramChatId { get; set; }
    public string Environment { get; set; } = "Development";
    public bool Enabled { get; set; }
    public bool Blocked { get; set; }
    public DateTimeOffset VerifiedAt { get; set; }
    public DateTimeOffset NextSendAt { get; set; }
}

public sealed class PublicationAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PublicationId { get; set; }
    public int Number { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string Outcome { get; set; } = "Processing";
    public int? HttpStatus { get; set; }
    public int? TelegramErrorCode { get; set; }
    public long? TelegramChatId { get; set; }
    public int? TelegramMessageId { get; set; }
    public Guid CorrelationId { get; set; }
}
