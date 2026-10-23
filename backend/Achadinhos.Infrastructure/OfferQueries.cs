using Achadinhos.Application;
using Achadinhos.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Achadinhos.Infrastructure;

public sealed class OfferQueries(EngineDbContext db) : IOfferQueries
{
    public async Task<PagedResult<OfferSummary>> ListAsync(int page, int pageSize, CancellationToken cancellationToken, string? status = null, string? search = null)
    {
        var query = db.Offers.AsNoTracking();
        if (!string.IsNullOrEmpty(status)) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(x => x.Title.ToLower().Contains(term));
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.LastSeenAt).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new OfferSummary(x.Id, x.Title, x.Currency, x.CurrentPrice, x.ProductUrl, x.LastSeenAt, x.Status, x.OriginalPrice))
            .ToListAsync(cancellationToken);
        return new(items, page, pageSize, total);
    }
}
