namespace Achadinhos.Domain.Discovery;

public sealed class OfferReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OfferId { get; set; }
    public string Decision { get; set; } = "";
    public string? Reason { get; set; }
    public string Operator { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Publication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OfferId { get; set; }
    public Guid? ChannelId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string PayloadSnapshot { get; set; } = "{}";
    public bool IsTest { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public long? TelegramChatId { get; set; }
    public int? TelegramMessageId { get; set; }
    public int AttemptCount { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Status { get; set; } = "Scheduled";
    public DateTimeOffset ScheduledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Operator { get; set; } = "";
}
