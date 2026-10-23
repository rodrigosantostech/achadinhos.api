using System.Net;
using System.Text.Json;
using Achadinhos.Application.Publishing;
using Achadinhos.Domain.Discovery;
using Achadinhos.Infrastructure.Telegram;
using Microsoft.EntityFrameworkCore;

namespace Achadinhos.Infrastructure.Simulation;

// Offline operation: deliberately has no HTTP client, publisher, channel, token or job dependency.
public sealed class PublicationSimulator(EngineDbContext db)
{
    public async Task<PublicationSimulation> SimulateAsync(Guid publicationId, string operatorName, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var previous = await db.PublicationSimulations.AsNoTracking().SingleOrDefaultAsync(x => x.PublicationId == publicationId, ct);
        if (previous is not null) return previous;
        var publication = await db.Publications.AsNoTracking().SingleOrDefaultAsync(x => x.Id == publicationId, ct)
            ?? throw new KeyNotFoundException();
        if (publication.Status != "Scheduled" || publication.ChannelId is not null || publication.IsTest)
            throw new InvalidOperationException("Somente agendamentos locais pendentes podem ser simulados.");
        var offer = await db.Offers.AsNoTracking().SingleAsync(x => x.Id == publication.OfferId, ct);
        if (offer.Status != "Scheduled" || offer.CurrentPrice is null)
            throw new InvalidOperationException("A oferta precisa estar agendada e ter um preço válido.");
        if (!TelegramMessageFormatter.IsSafeHttpsUrl(offer.ProductUrl))
            throw new InvalidOperationException("A prévia exige um link HTTPS válido, sem credenciais. Nenhuma mensagem foi enviada.");

        // OriginalPrice from manual entry is not proof of a reference price or discount.
        var request = new TelegramPublicationRequest(offer.Title, offer.CurrentPrice.Value, null, offer.ProductUrl, Currency: offer.Currency);
        string html;
        try { html = TelegramMessageFormatter.Format(request, false); }
        catch (TelegramPublicationException)
        {
            throw new InvalidOperationException("Revise título, moeda e preço antes de simular. Nenhuma mensagem foi enviada.");
        }
        html = html[..^TelegramMessageFormatter.Disclosure.Length] + "Simulação local. Link de produto; afiliação não validada.";
        var preview = WebUtility.HtmlDecode(html.Replace("<b>", "").Replace("</b>", ""));
        var changed = await db.Publications.Where(x => x.Id == publicationId && x.Status == "Scheduled"
                && x.ChannelId == null && !x.IsTest)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, "Simulated")
                .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow), ct);
        if (changed == 0)
        {
            var concurrent = await db.PublicationSimulations.AsNoTracking().SingleOrDefaultAsync(x => x.PublicationId == publicationId, ct);
            return concurrent ?? throw new InvalidOperationException("O agendamento mudou. Atualize a fila antes de continuar.");
        }
        var released = await db.Offers.Where(x => x.Id == publication.OfferId && x.Status == "Scheduled")
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, "Approved"), ct);
        if (released != 1) throw new InvalidOperationException("O estado da oferta mudou. Atualize a fila.");
        var simulation = new PublicationSimulation
        {
            PublicationId = publicationId,
            Operator = operatorName,
            PreviewText = preview,
            ProductUrl = offer.ProductUrl,
            Snapshot = JsonSerializer.Serialize(new
            {
                offer.Title,
                offer.CurrentPrice,
                offer.Currency,
                offer.ProductUrl,
                offer.LastSeenAt,
                publication.ScheduledAt,
                Mode = "Simulation",
                ReferencePrice = (decimal?)null
            })
        };
        db.PublicationSimulations.Add(simulation);
        db.OfferReviews.Add(new OfferReview
        {
            OfferId = offer.Id,
            Decision = "SimulationCompleted",
            Operator = operatorName,
            Reason = $"Simulação local da publicação {publicationId} concluída. Nenhuma mensagem enviada."
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return simulation;
    }
}
