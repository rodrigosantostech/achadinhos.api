using Achadinhos.Domain.Discovery;
using Achadinhos.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Achadinhos.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEST_POSTGRES")))
            Skip = "Set TEST_POSTGRES to a disposable PostgreSQL database to run integration tests.";
    }
}

public sealed class PostgresTests
{
    [PostgresFact]
    public async Task MigrationAndConstraintsWorkAgainstPostgres()
    {
        var connection = Environment.GetEnvironmentVariable("TEST_POSTGRES")!;
        // Separate schema isolates this test without deleting an existing database.
        var schema = "test_" + Guid.NewGuid().ToString("N");
        var cs = new Npgsql.NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString;
        await using var admin = new Npgsql.NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using (var create = new Npgsql.NpgsqlCommand($"CREATE SCHEMA {schema}", admin))
            await create.ExecuteNonQueryAsync();
        try
        {
            await using var db = new EngineDbContext(new DbContextOptionsBuilder<EngineDbContext>().UseNpgsql(cs).Options);
            await db.Database.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            var marketplace = new Marketplace { Code = "fixture", Name = "Contract fixture" };
            db.Marketplaces.Add(marketplace);
            var offer = new Offer
            {
                MarketplaceId = marketplace.Id,
                ExternalOfferId = "1",
                Title = "Observed offer",
                ProductUrl = "https://example.com/1",
                CurrentPrice = 19.99m,
                LastSeenAt = DateTimeOffset.UtcNow,
                RawPayload = """{"source":"test"}"""
            };
            db.Offers.Add(offer);
            db.PriceSnapshots.Add(new PriceSnapshot
            {
                OfferId = offer.Id,
                Price = 19.99m,
                Source = "fixture",
                CapturedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            Assert.Equal(19.99m, (await db.Offers.SingleAsync()).CurrentPrice);
            Assert.Equal(19.99m, (await db.PriceSnapshots.SingleAsync()).Price);
            db.Offers.Add(new Offer
            {
                MarketplaceId = marketplace.Id,
                ExternalOfferId = "1",
                Title = "Duplicate",
                ProductUrl = "https://example.com/1",
                LastSeenAt = DateTimeOffset.UtcNow
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            db.PriceSnapshots.Add(new PriceSnapshot
            {
                OfferId = Guid.NewGuid(),
                Price = 1m,
                Source = "fixture",
                CapturedAt = DateTimeOffset.UtcNow
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        finally
        {
            await using var drop = new Npgsql.NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
