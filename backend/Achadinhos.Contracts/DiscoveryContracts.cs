namespace Achadinhos.Contracts;

public enum DataConfidence { Unknown, Low, Verified }
public sealed record DiscoveryCriteria(string? Category, string? Cursor, int PageSize = 50);
public sealed record DiscoveryPage(IReadOnlyList<ExternalOffer> Items, string? NextCursor);
public sealed record ExternalOffer(
    string Provider, string ExternalOfferId, string? ExternalProductId,
    string? SellerId, string Title, Uri ProductUrl, Uri? ImageUrl,
    string Currency, decimal? CurrentPrice, decimal? OriginalPrice,
    string? Availability, string? Category, DateTimeOffset CollectedAt,
    string RawPayload, DataConfidence DataConfidence);
public sealed record AffiliateLinkResult(Uri AffiliateUrl, DateTimeOffset? ExpiresAt);
public sealed record OfferSummary(Guid Id, string Title, string Currency, decimal? CurrentPrice,
    string ProductUrl, DateTimeOffset LastSeenAt, string Status = "Discovered", decimal? OriginalPrice = null);
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
