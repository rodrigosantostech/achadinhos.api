using Achadinhos.Infrastructure;
using Achadinhos.Infrastructure.Telegram;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Achadinhos.Api;

public sealed record TelegramTestRequest(string IdempotencyKey);

public static class AdminTelegramEndpoints
{
    public static void MapAdminTelegramEndpoints(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;
        app.MapPost("/api/admin/telegram/test", async (TelegramTestRequest input, HttpContext context,
            PublicationQueue queue, EngineDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input.IdempotencyKey) || input.IdempotencyKey.Length is < 8 or > 100)
                return Results.BadRequest(new { detail = "Informe uma chave idempotente de 8 a 100 caracteres." });
            var key = "test:" + input.IdempotencyKey;
            try
            {
                var id = await queue.EnqueueTestAsync(context.User.Identity?.Name ?? "jwt-admin", key, ct);
                return Results.Accepted($"/api/admin/telegram/publications/{id}", new { publicationId = id });
            }
            catch (InvalidOperationException) { return Results.Conflict(new { detail = "Canal de homologação indisponível. Execute o diagnóstico no Worker." }); }
            catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.ChangeTracker.Clear();
                var existing = await db.Publications.SingleAsync(x => x.IdempotencyKey == key, ct);
                return Results.Accepted($"/api/admin/telegram/publications/{existing.Id}", new { publicationId = existing.Id });
            }
        }).RequireAuthorization("TelegramAdmin").RequireRateLimiting("TelegramTest");
        app.MapGet("/api/admin/telegram/publications/{id:guid}", async (Guid id, EngineDbContext db, CancellationToken ct) =>
        {
            var row = await db.Publications.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new
                {
                    x.Id,
                    x.Status,
                    x.PublishedAt,
                    x.TelegramChatId,
                    x.TelegramMessageId,
                    x.AttemptCount,
                    x.LastErrorCode,
                    x.IsTest
                }).SingleOrDefaultAsync(ct);
            return row is null ? Results.NotFound() : Results.Ok(row);
        }).RequireAuthorization("TelegramAdmin");
    }
}
