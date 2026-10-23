using Achadinhos.Infrastructure;
using Achadinhos.Infrastructure.Simulation;
using Microsoft.EntityFrameworkCore;

namespace Achadinhos.Api;

public static class SimulationEndpoints
{
    public static bool IsEnabled(bool development, bool enabled) => development && enabled;

    public static void MapSimulationEndpoints(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;
        app.MapPost("/api/publications/{id:guid}/simulate", async (Guid id, HttpContext context,
            PublicationSimulator simulator, CancellationToken ct) =>
        {
            if (!app.Configuration.GetValue<bool>("Simulation:Enabled")) return Results.NotFound();
            try
            {
                var result = await simulator.SimulateAsync(id, context.User.Identity?.Name ?? "operator", ct);
                return Results.Ok(new { result.Id, result.PublicationId, result.SimulatedAt, mode = "Simulation", telegramSent = false });
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (InvalidOperationException error) { return Results.Conflict(new { detail = error.Message }); }
        }).RequireAuthorization("Write");
        app.MapGet("/api/publications/{id:guid}/simulation", async (Guid id, EngineDbContext db, CancellationToken ct) =>
        {
            var result = await db.PublicationSimulations.AsNoTracking().Where(x => x.PublicationId == id)
                .Select(x => new
                {
                    x.Id,
                    x.PublicationId,
                    x.SimulatedAt,
                    x.Operator,
                    x.PreviewText,
                    x.ProductUrl,
                    mode = "Simulation",
                    telegramSent = false
                }).SingleOrDefaultAsync(ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization("Read");
    }
}
