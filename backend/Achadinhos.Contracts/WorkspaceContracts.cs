namespace Achadinhos.Contracts;

public sealed record CreateOfferRequest(string Title, string ProductUrl, decimal Price, decimal? OriginalPrice);
public sealed record ReviewOfferRequest(string Decision, string? Reason);
public sealed record ScheduleOfferRequest(DateTimeOffset ScheduledAt);
