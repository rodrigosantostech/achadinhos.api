using Achadinhos.Contracts;

namespace Achadinhos.Application;

public interface IMarketplaceProvider
{
    string Code { get; }
    Task<DiscoveryPage> DiscoverAsync(DiscoveryCriteria criteria, CancellationToken cancellationToken);
    Task<ExternalOffer?> GetOfferAsync(string externalId, CancellationToken cancellationToken);
    Task<AffiliateLinkResult> GenerateAffiliateLinkAsync(
        string productUrl, string trackingId, CancellationToken cancellationToken);
}

public interface IOfferQueries
{
    Task<PagedResult<OfferSummary>> ListAsync(int page, int pageSize, CancellationToken cancellationToken, string? status = null, string? search = null);
}
