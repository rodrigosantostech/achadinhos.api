namespace Achadinhos.Application.Publishing;

// The caller must verify approval, price freshness, image rights and affiliate provenance.
// ReferencePrice must be backed by evidence; ProductUrl alone is not an affiliate link.
public sealed record TelegramPublicationRequest(
    string Title,
    decimal CurrentPrice,
    decimal? ReferencePrice,
    string AffiliateUrl,
    string? ImageUrl = null,
    string Currency = "BRL");
