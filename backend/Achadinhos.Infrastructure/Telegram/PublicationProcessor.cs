using System.Text.Json;
using Achadinhos.Application.Publishing;
using Achadinhos.Domain.Discovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Achadinhos.Infrastructure.Telegram;

public sealed class PublicationProcessor(EngineDbContext db, ITelegramPublisher publisher,
    IOptionsMonitor<TelegramOptions> options, ILogger<PublicationProcessor> logger)
{
    public async Task ProcessAsync(Guid publicationId, CancellationToken ct = default)
    {
        if (!options.CurrentValue.Enabled) return;
        var now = DateTimeOffset.UtcNow;
        var publication = await db.Publications.AsNoTracking().SingleOrDefaultAsync(x => x.Id == publicationId, ct);
        if (publication?.ChannelId is null || publication.Status is not ("Scheduled" or "RetryScheduled")) return;
        var channel = await db.PublicationChannels.AsNoTracking().SingleAsync(x => x.Id == publication.ChannelId, ct);
        // This release enables private Development homologation only.
        if (!channel.Enabled || channel.Blocked || channel.Environment != "Development"
            || channel.VerifiedAt < now.AddHours(-24)
            || channel.TelegramChatId.ToString(System.Globalization.CultureInfo.InvariantCulture) != options.CurrentValue.ChatId
            || !publication.IsTest) return;

        var lease = Guid.NewGuid();
        var until = now.AddSeconds(options.CurrentValue.TimeoutSeconds + 60);
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var paced = await db.PublicationChannels.Where(x => x.Id == channel.Id && x.Enabled && !x.Blocked && x.NextSendAt <= now)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.NextSendAt, until), ct);
            if (paced == 0) return;
            var claimed = await db.Publications.Where(x => x.Id == publicationId
                    && (x.Status == "Scheduled" || x.Status == "RetryScheduled") && x.ScheduledAt <= now)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, "Processing")
                    .SetProperty(x => x.LeaseToken, lease).SetProperty(x => x.LeaseUntil, until)
                    .SetProperty(x => x.LastAttemptAt, now).SetProperty(x => x.UpdatedAt, now)
                    .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1), ct);
            if (claimed == 0) return;
            await tx.CommitAsync(ct);
        }
        var claimedPublication = await db.Publications.AsNoTracking().SingleAsync(x => x.Id == publicationId, ct);
        var attempt = new PublicationAttempt
        {
            PublicationId = publicationId,
            Number = claimedPublication.AttemptCount,
            StartedAt = now,
            CorrelationId = lease
        };
        db.PublicationAttempts.Add(attempt);
        await db.SaveChangesAsync(ct); // Must succeed before HTTP.
        TelegramPublicationResult? result = null;
        TelegramPublicationException? failure = null;
        TelegramPublicationRequest? request = null;
        try
        {
            var offer = await db.Offers.AsNoTracking().SingleAsync(x => x.Id == publication.OfferId, ct);
            var marketplaceEnabled = await db.Marketplaces.AnyAsync(x => x.Id == offer.MarketplaceId && x.Enabled, ct);
            request = JsonSerializer.Deserialize<TelegramPublicationRequest>(publication.PayloadSnapshot);
            if (publication.ValidUntil is null || publication.ValidUntil <= now
                || offer.Status != "Scheduled" || offer.Availability != "Available"
                || offer.LastSeenAt < now.AddMinutes(-30) || !marketplaceEnabled || request is null
                || request.Currency != offer.Currency || request.CurrentPrice != offer.CurrentPrice)
                throw new TelegramPublicationException(TelegramFailureKind.InvalidRequest);
            // Keep the approved snapshot immutable; previous confirmed rejection selects text mode.
            if (await db.PublicationAttempts.AnyAsync(x => x.PublicationId == publicationId && x.Outcome == "ImageRejected", ct))
                request = request with { ImageUrl = null };
            // Recheck the kill switch after claim, before the only network side effect.
            if (!options.CurrentValue.Enabled) throw new TelegramPublicationException(TelegramFailureKind.Disabled);
            result = await publisher.PublishAsync(request, ct);
        }
        catch (TelegramPublicationException error) { failure = error; }
        catch (Exception error) when (error is JsonException or OperationCanceledException)
        {
            failure = new TelegramPublicationException(TelegramFailureKind.UnknownDelivery);
        }
        // HTTP may have completed even when cancellation is signaled; persist with a fresh bounded token.
        using var saveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var saveCt = saveTimeout.Token;
        var finished = DateTimeOffset.UtcNow;
        var fallback = failure?.Kind == TelegramFailureKind.ImageRejected && request?.ImageUrl is not null && attempt.Number < 3;
        var retry = fallback || failure?.Kind == TelegramFailureKind.RateLimited && attempt.Number < 3;

        var state = result is not null ? "Published" : retry ? "RetryScheduled"
            : failure?.Kind == TelegramFailureKind.UnknownDelivery ? "UnknownDelivery" : "Failed";
        var retryAt = finished.AddSeconds(Math.Max(3, fallback ? 3 : failure?.RetryAfterSeconds ?? 30));
        await using var finalTx = await db.Database.BeginTransactionAsync(saveCt);
        var changed = await db.Publications.Where(x => x.Id == publicationId && x.Status == "Processing" && x.LeaseToken == lease)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, state)
                .SetProperty(x => x.PublishedAt, result == null ? (DateTimeOffset?)null : result.SentAt)
                .SetProperty(x => x.TelegramChatId, result == null ? (long?)null : result.ChatId)
                .SetProperty(x => x.TelegramMessageId, result == null ? (int?)null : result.MessageId)
                .SetProperty(x => x.LastErrorCode, failure == null ? null : failure.Kind.ToString())

                .SetProperty(x => x.UpdatedAt, finished).SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(x => x.ScheduledAt, retry ? retryAt : claimedPublication.ScheduledAt), saveCt);
        if (changed == 0)
        {
            // Lease expired during HTTP. Keep the channel blocked and retain confirmed evidence for reconciliation.
            state = "UnknownDelivery";
            logger.LogWarning("Publication {PublicationId} lost its lease; manual reconciliation required.", publicationId);
        }
        attempt.FinishedAt = finished;
        attempt.Outcome = fallback && state == "RetryScheduled" ? "ImageRejected" : state;
        attempt.HttpStatus = failure?.HttpStatus;
        attempt.TelegramErrorCode = failure?.TelegramErrorCode;
        if (result is not null)
        {
            attempt.TelegramChatId = result.ChatId;
            attempt.TelegramMessageId = result.MessageId;
        }
        await db.PublicationChannels.Where(x => x.Id == channel.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.NextSendAt, retry ? retryAt : finished.AddSeconds(3))
                .SetProperty(x => x.Blocked, state == "UnknownDelivery"), saveCt);
        await db.SaveChangesAsync(saveCt);
        await finalTx.CommitAsync(saveCt);
        logger.LogInformation("Publication {PublicationId} completed as {State}; attempt={Attempt}.", publicationId, state, attempt.Number);
    }

    public async Task RecoverExpiredAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var expired = await db.Publications.AsNoTracking()
            .Where(x => x.Status == "Processing" && x.LeaseUntil < now).Select(x => x.Id).ToListAsync(ct);
        foreach (var id in expired)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var channelId = await db.Publications.Where(x => x.Id == id).Select(x => x.ChannelId).SingleAsync(ct);
            var changed = await db.Publications.Where(x => x.Id == id && x.Status == "Processing" && x.LeaseUntil < now)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, "UnknownDelivery")
                    .SetProperty(x => x.LastErrorCode, "LeaseExpired").SetProperty(x => x.UpdatedAt, now), ct);
            if (changed > 0)
            {
                await db.PublicationChannels.Where(x => x.Id == channelId)
                    .ExecuteUpdateAsync(set => set.SetProperty(x => x.Blocked, true), ct);
                await db.PublicationAttempts.Where(x => x.PublicationId == id && x.Outcome == "Processing")
                    .ExecuteUpdateAsync(set => set.SetProperty(x => x.Outcome, "UnknownDelivery")
                        .SetProperty(x => x.FinishedAt, now), ct);
            }
            await tx.CommitAsync(ct);
        }
    }
}
