using Achadinhos.Contracts;
using Achadinhos.Domain.Discovery;
using Achadinhos.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Achadinhos.Api;

public static class WorkspaceEndpoints
{
    public static readonly Guid ManualMarketplaceId = Guid.Parse("5b70fb4d-7329-4da9-9167-3e049321a13e");

    public static void MapWorkspaceEndpoints(this WebApplication app)
    {
        app.MapPost("/api/offers", async (CreateOfferRequest input, EngineDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Trim().Length > 1000)
                return Invalid("Informe um título com até 1000 caracteres.");
            if (!Uri.TryCreate(input.ProductUrl, UriKind.Absolute, out var url)
                || url.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(url.UserInfo)
                || input.ProductUrl.Length > 2048)
                return Invalid("Informe uma URL HTTP ou HTTPS válida, sem credenciais.");
            if (!ValidPrice(input.Price) || (input.OriginalPrice is { } original
                && (!ValidPrice(original) || original < input.Price)))
                return Invalid("Preços devem ser positivos, com até duas casas decimais. O preço original não pode ser menor que o atual.");

            var offer = new Offer
            {
                MarketplaceId = ManualMarketplaceId,
                ExternalOfferId = Guid.NewGuid().ToString("N"),
                Title = input.Title.Trim(),
                ProductUrl = url.AbsoluteUri,
                CurrentPrice = input.Price,
                OriginalPrice = input.OriginalPrice,
                LastSeenAt = DateTimeOffset.UtcNow,
                Availability = "Unknown",
                RawPayload = """{"source":"manual"}"""
            };
            db.Offers.Add(offer);
            db.PriceSnapshots.Add(new PriceSnapshot
            {
                OfferId = offer.Id,
                Price = input.Price,
                Source = "manual",
                CapturedAt = offer.LastSeenAt
            });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/offers/{offer.Id}", new { offer.Id });
        }).RequireAuthorization("Write");

        app.MapGet("/api/offers/{id:guid}", async (Guid id, EngineDbContext db, CancellationToken ct) =>
        {
            var offer = await db.Offers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (offer is null) return Results.NotFound();
            var source = await db.Marketplaces.Where(x => x.Id == offer.MarketplaceId).Select(x => x.Name).SingleAsync(ct);
            var prices = await db.PriceSnapshots.AsNoTracking().Where(x => x.OfferId == id)
                .OrderByDescending(x => x.CapturedAt).Take(100)
                .Select(x => new { x.Price, x.Currency, x.Source, x.CapturedAt }).ToListAsync(ct);
            var reviews = await db.OfferReviews.AsNoTracking().Where(x => x.OfferId == id)
                .OrderByDescending(x => x.CreatedAt).Take(100)
                .Select(x => new { x.Decision, x.Reason, x.Operator, x.CreatedAt }).ToListAsync(ct);
            return Results.Ok(new
            {
                offer.Id,
                offer.Title,
                offer.ProductUrl,
                offer.Currency,
                offer.CurrentPrice,
                offer.OriginalPrice,
                offer.Status,
                offer.Availability,
                offer.LastSeenAt,
                source,
                prices,
                reviews
            });
        }).RequireAuthorization("Read");

        app.MapPost("/api/offers/{id:guid}/review", async (Guid id, ReviewOfferRequest input,
            HttpContext context, EngineDbContext db, CancellationToken ct) =>
        {
            if (input.Decision is not ("Approved" or "Rejected") || input.Reason?.Length > 2000)
                return Invalid("Escolha aprovar ou rejeitar; a justificativa aceita até 2000 caracteres.");
            if (input.Decision == "Rejected" && string.IsNullOrWhiteSpace(input.Reason))
                return Invalid("Informe o motivo da rejeição.");
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var changed = await db.Offers.Where(x => x.Id == id && x.Status != input.Decision
                    && (x.Status == "Discovered" || x.Status == "Approved" || x.Status == "Rejected"))
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, input.Decision), ct);
            if (changed == 0) return await MissingOrConflict(db, id, ct);
            db.OfferReviews.Add(new OfferReview
            {
                OfferId = id,
                Decision = input.Decision,
                Reason = input.Reason?.Trim(),
                Operator = context.User.Identity?.Name ?? "operator"
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization("Write");

        app.MapPost("/api/offers/{id:guid}/schedule", async (Guid id, ScheduleOfferRequest input,
            HttpContext context, EngineDbContext db, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            if (input.ScheduledAt <= now || input.ScheduledAt > now.AddDays(365))
                return Invalid("Escolha uma data futura, dentro dos próximos 365 dias.");
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var changed = await db.Offers.Where(x => x.Id == id && x.Status == "Approved")
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, "Scheduled"), ct);
            if (changed == 0) return await MissingOrConflict(db, id, ct);
            var publication = new Publication
            {
                OfferId = id,
                ScheduledAt = input.ScheduledAt.ToUniversalTime(),
                Operator = context.User.Identity?.Name ?? "operator"
            };
            db.Publications.Add(publication);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Created("/api/publications", new { publication.Id });
        }).RequireAuthorization("Write");

        app.MapGet("/api/publications", async (int? page, int? pageSize, EngineDbContext db, CancellationToken ct) =>
        {
            var p = page ?? 1;
            var size = pageSize ?? 25;
            if (p < 1 || p > 1_000_000 || size < 1 || size > 100) return Invalid("Paginação inválida.");
            var query = from publication in db.Publications.AsNoTracking()
                        join offer in db.Offers.AsNoTracking() on publication.OfferId equals offer.Id
                        select new
                        {
                            publication.Id,
                            publication.OfferId,
                            offer.Title,
                            publication.Status,
                            publication.ScheduledAt,
                            canSimulate = publication.Status == "Scheduled" && publication.ChannelId == null && !publication.IsTest,
                            publication.IsTest,
                            publication.TelegramMessageId,
                            publication.TelegramChatId,
                            publication.PublishedAt,
                            publication.AttemptCount,
                            publication.LastErrorCode,
                            publication.CreatedAt
                        };
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
                .Skip((p - 1) * size).Take(size).ToListAsync(ct);
            return Results.Ok(new { items, page = p, pageSize = size, total, deliveryEnabled = false, simulationEnabled = SimulationEndpoints.IsEnabled(app.Environment.IsDevelopment(), app.Configuration.GetValue<bool>("Simulation:Enabled")) });
        }).RequireAuthorization("Read");

        app.MapPost("/api/publications/{id:guid}/cancel", async (Guid id, EngineDbContext db, CancellationToken ct) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var publication = await db.Publications.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (publication is null) return Results.NotFound();
            var changed = await db.Publications.Where(x => x.Id == id && (x.Status == "Scheduled" || x.Status == "RetryScheduled"))
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, "Cancelled"), ct);
            if (changed == 0) return Results.Conflict(new { detail = "Este agendamento já foi cancelado." });
            await db.Offers.Where(x => x.Id == publication.OfferId && x.Status == "Scheduled")
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, "Approved"), ct);
            await transaction.CommitAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization("Write");
    }

    private static bool ValidPrice(decimal value) =>
        value > 0 && value <= 9999999999999999.99m && decimal.Round(value, 2) == value;

    private static IResult Invalid(string detail) =>
        Results.Problem(statusCode: 400, title: "Dados inválidos", detail: detail);

    private static async Task<IResult> MissingOrConflict(EngineDbContext db, Guid id, CancellationToken ct) =>
        await db.Offers.AnyAsync(x => x.Id == id, ct)
            ? Results.Conflict(new { detail = "O estado da oferta mudou ou não permite esta ação. Atualize a página." })
            : Results.NotFound();
}
