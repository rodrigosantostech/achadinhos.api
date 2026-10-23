namespace Achadinhos.Domain.Discovery;

public sealed class Marketplace
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Offer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MarketplaceId { get; set; }
    public string ExternalOfferId { get; set; } = "";
    public string? ExternalSellerId { get; set; }
    public string Title { get; set; } = "";
    public string ProductUrl { get; set; } = "";
    public string? ImageUrl { get; set; }
    public string Currency { get; set; } = "BRL";
    public decimal? CurrentPrice { get; set; }
    public decimal? OriginalPrice { get; set; }
    public string Availability { get; set; } = "Unknown";
    public string Status { get; set; } = "Discovered";
    public DateTimeOffset LastSeenAt { get; set; }
    public string RawPayload { get; set; } = "{}";
}

public sealed class PriceSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OfferId { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "BRL";
    public string PriceType { get; set; } = "Observed";
    public string? CouponConditions { get; set; }
    public string Source { get; set; } = "";
    public DateTimeOffset CapturedAt { get; set; }
}

public sealed class IntegrationRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Provider { get; set; } = "";
    public string JobType { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public int ItemsReceived { get; set; }
    public int ItemsUpserted { get; set; }
    public string? ErrorSummary { get; set; }
}
