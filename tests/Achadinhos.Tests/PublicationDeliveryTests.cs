using System.Net;
using Achadinhos.Application.Publishing;
using Achadinhos.Domain.Discovery;
using Achadinhos.Infrastructure;
using Achadinhos.Infrastructure.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using Xunit;

namespace Achadinhos.Tests;

public sealed class PublicationDeliveryTests
{
    [PostgresFact]
    public async Task ConcurrentWorkersPublishOnceAndPersistTelegramEvidence()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var id = await fixture.QueueAsync("concurrent");
        var publisher = new FakePublisher();
        await using var first = fixture.Open();
        await using var second = fixture.Open();
        await Task.WhenAll(Processor(first, publisher).ProcessAsync(id), Processor(second, publisher).ProcessAsync(id));
        await using var check = fixture.Open();
        var row = await check.Publications.SingleAsync(x => x.Id == id);
        Assert.Equal("Published", row.Status);
        Assert.Equal(1, publisher.Calls);
        Assert.Equal(42, row.TelegramMessageId);
        Assert.Equal(-1001234567890L, row.TelegramChatId);
        var attempt = await check.PublicationAttempts.SingleAsync();
        Assert.Equal("Published", attempt.Outcome);
        Assert.Equal(42, attempt.TelegramMessageId);
        Assert.Equal(id, await new PublicationQueue(check).EnqueueTestAsync("test", "concurrent"));
        Assert.Equal(1, await check.Publications.CountAsync());
    }

    [PostgresFact]
    public async Task ConfirmedImageRejectionFallsBackThroughASeparatePersistedAttempt()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var id = await fixture.QueueAsync("image-fallback");
        await using var db = fixture.Open();
        var row = await db.Publications.SingleAsync(x => x.Id == id);
        var request = System.Text.Json.JsonSerializer.Deserialize<TelegramPublicationRequest>(row.PayloadSnapshot)!;
        row.PayloadSnapshot = System.Text.Json.JsonSerializer.Serialize(request with { ImageUrl = "https://example.com/image.jpg" });
        await db.SaveChangesAsync();
        var publisher = new FakePublisher(imageRejectOnce: true);
        var processor = Processor(db, publisher);
        await processor.ProcessAsync(id);
        Assert.Equal("RetryScheduled", (await db.Publications.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(1, publisher.Calls);
        await db.Publications.ExecuteUpdateAsync(set => set.SetProperty(x => x.ScheduledAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        await db.PublicationChannels.ExecuteUpdateAsync(set => set.SetProperty(x => x.NextSendAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        await processor.ProcessAsync(id);
        Assert.Equal("Published", (await db.Publications.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(new[] { true, false }, publisher.ImageRequests);
        var persisted = await db.Publications.AsNoTracking().SingleAsync();
        Assert.Contains("https://example.com/image.jpg", persisted.PayloadSnapshot);
        Assert.Equal("ImageRejected", (await db.PublicationAttempts.AsNoTracking().OrderBy(x => x.Number).FirstAsync()).Outcome);
        Assert.Equal(2, await db.PublicationAttempts.CountAsync());
    }

    [PostgresFact]
    public async Task TimeoutIsNeverRetriedAndBlocksTheChannel()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var id = await fixture.QueueAsync("uncertain");
        var publisher = new FakePublisher(TelegramFailureKind.UnknownDelivery);
        await using var db = fixture.Open();
        var processor = Processor(db, publisher);
        await processor.ProcessAsync(id);
        await processor.ProcessAsync(id);
        var row = await db.Publications.AsNoTracking().SingleAsync();
        Assert.Equal("UnknownDelivery", row.Status);
        Assert.Equal(1, publisher.Calls);
        Assert.True((await db.PublicationChannels.AsNoTracking().SingleAsync()).Blocked);
        Assert.Equal("UnknownDelivery", (await db.PublicationAttempts.AsNoTracking().SingleAsync()).Outcome);
    }

    [PostgresFact]
    public async Task RateLimitWaitsAndStopsAfterThreeAttempts()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var id = await fixture.QueueAsync("rate-limit");
        var publisher = new FakePublisher(TelegramFailureKind.RateLimited);
        await using var db = fixture.Open();
        var processor = Processor(db, publisher);
        var started = DateTimeOffset.UtcNow;
        await processor.ProcessAsync(id);
        var row = await db.Publications.AsNoTracking().SingleAsync();
        Assert.Equal("RetryScheduled", row.Status);
        Assert.True(row.ScheduledAt >= started.AddSeconds(37));
        await processor.ProcessAsync(id);
        Assert.Equal(1, publisher.Calls);
        for (var i = 0; i < 2; i++)
        {
            await db.Publications.ExecuteUpdateAsync(set => set.SetProperty(x => x.ScheduledAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
            await db.PublicationChannels.ExecuteUpdateAsync(set => set.SetProperty(x => x.NextSendAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
            await processor.ProcessAsync(id);
        }
        Assert.Equal("Failed", (await db.Publications.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(3, publisher.Calls);
        Assert.Equal(3, await db.PublicationAttempts.CountAsync());
    }

    [PostgresFact]
    public async Task ExpiredLeaseRequiresReconciliationAndDoesNotRequeue()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var id = await fixture.QueueAsync("expired-lease");
        await using var db = fixture.Open();
        await db.Publications.Where(x => x.Id == id).ExecuteUpdateAsync(set =>
            set.SetProperty(x => x.Status, "Processing").SetProperty(x => x.LeaseUntil, DateTimeOffset.UtcNow.AddMinutes(-1)));
        var publisher = new FakePublisher();
        var processor = Processor(db, publisher);
        await processor.RecoverExpiredAsync();
        await processor.ProcessAsync(id);
        Assert.Equal("UnknownDelivery", (await db.Publications.AsNoTracking().SingleAsync()).Status);
        Assert.True((await db.PublicationChannels.AsNoTracking().SingleAsync()).Blocked);
        Assert.Equal(0, publisher.Calls);
    }

    [PostgresFact]
    public async Task InvalidSnapshotAndKillSwitchPreventSending()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var id = await fixture.QueueAsync("expired-snapshot");
        await using var db = fixture.Open();
        var publisher = new FakePublisher();
        var disabled = new MutableOptions { CurrentValue = new TelegramOptions() };
        await Processor(db, publisher, disabled).ProcessAsync(id);
        Assert.Equal("Scheduled", (await db.Publications.AsNoTracking().SingleAsync()).Status);
        await db.Publications.ExecuteUpdateAsync(set => set.SetProperty(x => x.ValidUntil, DateTimeOffset.UtcNow.AddMinutes(-1)));
        await Processor(db, publisher).ProcessAsync(id);
        Assert.Equal("Failed", (await db.Publications.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(0, publisher.Calls);
    }

    [PostgresFact]
    public async Task MigrationPreservesLegacyQueueAndCanBeReversedBeforeDelivery()
    {
        await using var fixture = await DatabaseFixture.CreateAsync(migrate: false);
        await using var db = fixture.Open();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261023185010_LocalWorkspace");
        var offerId = Guid.NewGuid();
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO offers ("Id","MarketplaceId","ExternalOfferId","Title","ProductUrl","Currency","Availability","Status","LastSeenAt","RawPayload")
            VALUES ({offerId},{Guid.Parse("5b70fb4d-7329-4da9-9167-3e049321a13e")} ,'legacy','Legacy queue','https://example.com','BRL','Unknown','Scheduled',{DateTimeOffset.UtcNow},CAST({"{}"} AS jsonb))
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO publications ("Id","OfferId","Status","ScheduledAt","CreatedAt","Operator")
            VALUES ({id},{offerId},'Scheduled',{DateTimeOffset.UtcNow},{DateTimeOffset.UtcNow},'legacy')
            """);
        await migrator.MigrateAsync();
        var publication = await db.Publications.AsNoTracking().SingleAsync();
        Assert.Null(publication.ChannelId);
        Assert.Equal("{}", publication.PayloadSnapshot);
        Assert.False(publication.IsTest);
        var publisher = new FakePublisher();
        await Processor(db, publisher).ProcessAsync(id);
        Assert.Equal(0, publisher.Calls);
        await migrator.MigrateAsync("20261023185010_LocalWorkspace");
        await migrator.MigrateAsync();
        Assert.Equal(1, await db.Publications.CountAsync());
    }

    [Theory]
    [InlineData("Viewer", HttpStatusCode.Forbidden)]
    [InlineData("Reviewer", HttpStatusCode.Forbidden)]
    [InlineData("Admin", HttpStatusCode.BadRequest)]
    public async Task TestEndpointRequiresJwtAdmin(string role, HttpStatusCode status)
    {
        using var factory = new ApiFactory();
        var response = await factory.CreateClientFor(role).PostAsJsonAsync("/api/admin/telegram/test", new { idempotencyKey = "short" });
        Assert.Equal(status, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousCannotQueueExternalMessages()
    {
        using var factory = new ApiFactory();
        var response = await factory.CreateClient().PostAsJsonAsync("/api/admin/telegram/test", new { idempotencyKey = "test-no-auth" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }


    [Fact]
    public async Task LocalDevelopmentAdminCannotQueueMessages()
    {
        using var factory = new ApiFactory();
        using var local = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["LocalDevelopment:Enabled"] = "true" }));
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, LocalLoopbackStartup>());
        });
        var response = await local.CreateClient().PostAsJsonAsync("/api/admin/telegram/test", new { idempotencyKey = "local-denied" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TestRouteIsUnavailableInProduction()
    {
        using var factory = new ApiFactory();
        using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var response = await production.CreateClient().PostAsJsonAsync("/api/admin/telegram/test", new { idempotencyKey = "prod-denied" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [PostgresFact]
    public async Task AdminEndpointQueuesIdempotentlyAndReturnsPersistedWorkerResult()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        using var factory = new ApiFactory();
        using var configured = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Engine"] = fixture.ConnectionString })));
        using var admin = factory.CreateClientFor("Admin");
        using var client = configured.CreateClient();
        client.DefaultRequestHeaders.Authorization = admin.DefaultRequestHeaders.Authorization;
        var first = await client.PostAsJsonAsync("/api/admin/telegram/test", new { idempotencyKey = "endpoint-test" });
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var body = await first.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var id = body.GetProperty("publicationId").GetGuid();
        var duplicate = await client.PostAsJsonAsync("/api/admin/telegram/test", new { idempotencyKey = "endpoint-test" });
        Assert.Equal(id, (await duplicate.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("publicationId").GetGuid());
        await using var db = fixture.Open();
        await Processor(db, new FakePublisher()).ProcessAsync(id);
        var result = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/admin/telegram/publications/{id}");
        Assert.Equal("Published", result.GetProperty("status").GetString());
        Assert.Equal(42, result.GetProperty("telegramMessageId").GetInt32());
    }

    private sealed class LocalLoopbackStartup : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                context.Request.Host = new Microsoft.AspNetCore.Http.HostString("localhost");
                context.Connection.RemoteIpAddress = IPAddress.Loopback;
                await continuation(context);
            });
            next(app);
        };
    }

    private static PublicationProcessor Processor(EngineDbContext db, ITelegramPublisher publisher, MutableOptions? options = null) =>
        new(db, publisher, options ?? new MutableOptions(), NullLogger<PublicationProcessor>.Instance);

    private sealed class MutableOptions : IOptionsMonitor<TelegramOptions>
    {
        public TelegramOptions CurrentValue { get; set; } = new() { Enabled = true, ChatId = "-1001234567890" };
        public TelegramOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<TelegramOptions, string?> listener) => null;
    }

    private sealed class FakePublisher(TelegramFailureKind? failure = null, bool imageRejectOnce = false) : ITelegramPublisher
    {
        private int calls;
        public int Calls => calls;
        public List<bool> ImageRequests { get; } = [];
        public Task<TelegramPublicationResult> PublishAsync(TelegramPublicationRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref calls);
            ImageRequests.Add(request.ImageUrl is not null);
            if (imageRejectOnce && calls == 1) throw new TelegramPublicationException(TelegramFailureKind.ImageRejected, 400, 400);
            if (failure is { } kind) throw new TelegramPublicationException(kind, 429, 429, 37);
            return Task.FromResult(new TelegramPublicationResult(-1001234567890L, 42, DateTimeOffset.UtcNow));
        }
    }

    private sealed class DatabaseFixture(string connection, string schema) : IAsyncDisposable
    {
        public string ConnectionString => new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString;
        public EngineDbContext Open() => new(new DbContextOptionsBuilder<EngineDbContext>().UseNpgsql(ConnectionString).Options);
        public static async Task<DatabaseFixture> CreateAsync(bool migrate = true)
        {
            var connection = Environment.GetEnvironmentVariable("TEST_POSTGRES")!;
            var schema = "telegram_" + Guid.NewGuid().ToString("N");
            await using var admin = new NpgsqlConnection(connection);
            await admin.OpenAsync();
            await using (var command = new NpgsqlCommand($"CREATE SCHEMA {schema}", admin)) await command.ExecuteNonQueryAsync();
            var fixture = new DatabaseFixture(connection, schema);
            await using var db = fixture.Open();
            if (migrate)
            {
                await db.Database.MigrateAsync();
                db.PublicationChannels.Add(new PublicationChannel
                {
                    TelegramChatId = -1001234567890L,
                    Enabled = true,
                    VerifiedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync();
            }
            return fixture;
        }
        public async Task<Guid> QueueAsync(string key)
        {
            await using var db = Open();
            return await new PublicationQueue(db).EnqueueTestAsync("tests", key);
        }
        public async ValueTask DisposeAsync()
        {
            await using var admin = new NpgsqlConnection(connection);
            await admin.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin);
            await command.ExecuteNonQueryAsync();
        }
    }
}
