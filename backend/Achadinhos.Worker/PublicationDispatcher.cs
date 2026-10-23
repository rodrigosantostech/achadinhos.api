using Achadinhos.Infrastructure;
using Achadinhos.Infrastructure.Telegram;
using Achadinhos.Worker.Jobs;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Achadinhos.Worker;

public sealed class PublicationDispatcher(IServiceScopeFactory scopes, ILogger<PublicationDispatcher> logger, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                if (!scope.ServiceProvider.GetRequiredService<IOptionsMonitor<TelegramOptions>>().CurrentValue.Enabled) continue;
                var db = scope.ServiceProvider.GetRequiredService<EngineDbContext>();
                await scope.ServiceProvider.GetRequiredService<PublicationProcessor>().RecoverExpiredAsync(stoppingToken);
                var now = DateTimeOffset.UtcNow;
                // Database remains source of truth; a crash between commit/enqueue is recovered here.
                var testKey = configuration["TelegramAction"] == "run-test" ? "test:" + configuration["TelegramTestKey"] : null;
                var ids = await db.Publications.AsNoTracking()
                    .Where(x => x.ChannelId != null && x.IsTest && x.ScheduledAt <= now
                        && (testKey == null || x.IdempotencyKey == testKey)
                        && (x.Status == "Scheduled" || x.Status == "RetryScheduled"))
                    .OrderBy(x => x.ScheduledAt).Take(10).Select(x => x.Id).ToListAsync(stoppingToken);
                var jobs = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
                foreach (var id in ids) jobs.Enqueue<TelegramPublicationJob>(job => job.RunAsync(id, CancellationToken.None));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                logger.LogWarning("Publication dispatch failed: {ErrorType}.", error.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
