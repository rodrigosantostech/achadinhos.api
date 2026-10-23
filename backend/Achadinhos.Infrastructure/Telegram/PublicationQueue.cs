using System.Text.Json;
using Achadinhos.Application.Publishing;
using Achadinhos.Domain.Discovery;
using Microsoft.EntityFrameworkCore;

namespace Achadinhos.Infrastructure.Telegram;

public sealed class PublicationQueue(EngineDbContext db)
{
    public async Task<Guid> EnqueueTestAsync(string operatorName, string idempotencyKey, CancellationToken ct = default)
    {
        var existing = await db.Publications.AsNoTracking().SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, ct);
        if (existing is not null) return existing.Id;
        var now = DateTimeOffset.UtcNow;
        var channel = await db.PublicationChannels.SingleAsync(x => x.Code == "telegram-test", ct);
        if (!channel.Enabled || channel.Blocked || channel.Environment != "Development")
            throw new InvalidOperationException("Test channel is disabled or blocked.");
        var offer = new Offer
        {
            MarketplaceId = Guid.Parse("5b70fb4d-7329-4da9-9167-3e049321a13e"),
            ExternalOfferId = "telegram-test-" + Guid.NewGuid().ToString("N"),
            Title = "[TESTE TELEGRAM] Oferta fictícia — integração Achadinhos. Sem oferta comercial.",
            ProductUrl = "https://example.com/",
            CurrentPrice = 1m,
            Currency = "BRL",
            Status = "Scheduled",
            Availability = "Available",
            LastSeenAt = now,
            RawPayload = """{"source":"telegram-homologation","fictitious":true}"""
        };
        var request = new TelegramPublicationRequest(offer.Title, 1m, null, offer.ProductUrl);
        var publication = new Publication
        {
            OfferId = offer.Id,
            ChannelId = channel.Id,
            ScheduledAt = now,
            Operator = operatorName,
            IdempotencyKey = idempotencyKey,
            IsTest = true,
            ValidUntil = now.AddMinutes(15),
            PayloadSnapshot = JsonSerializer.Serialize(request)
        };
        db.Offers.Add(offer);
        db.OfferReviews.Add(new OfferReview
        {
            OfferId = offer.Id,
            Decision = "Approved",
            Reason = "Homologação técnica com dados fictícios e link controlado.",
            Operator = operatorName
        });
        db.PriceSnapshots.Add(new PriceSnapshot { OfferId = offer.Id, Price = 1m, CapturedAt = now, Source = "telegram-homologation" });
        db.Publications.Add(publication);
        await db.SaveChangesAsync(ct);
        return publication.Id;
    }
}
