using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Achadinhos.Api;
using Achadinhos.Contracts;
using Achadinhos.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Achadinhos.Tests;

public sealed class PublicationSimulationTests
{
    [PostgresFact]
    public async Task LocalPanelWorkflowSimulatesIdempotentlyWithoutTelegramIdsOrAttempts()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var factory = new ApiFactory();
        using var local = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Engine"] = fixture.ConnectionString,
                    ["LocalDevelopment:Enabled"] = "true",
                    ["Simulation:Enabled"] = "true"
                }));
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, LoopbackStartup>());
        });
        using var client = local.CreateClient();
        var offer = await client.PostAsJsonAsync("/api/offers",
            new CreateOfferRequest("<script>alert('x')</script> & SSD 🛒", "https://example.com/product", 19.99m, 59.99m));
        var offerId = (await offer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/offers/{offerId}/review",
            new ReviewOfferRequest("Approved", "Teste local explícito."))).StatusCode);
        var scheduled = await client.PostAsJsonAsync($"/api/offers/{offerId}/schedule", new ScheduleOfferRequest(DateTimeOffset.UtcNow.AddHours(1)));
        var publicationId = (await scheduled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var before = await client.GetFromJsonAsync<JsonElement>("/api/publications");
        Assert.True(before.GetProperty("simulationEnabled").GetBoolean());
        Assert.True(before.GetProperty("items")[0].GetProperty("canSimulate").GetBoolean());

        var concurrent = await Task.WhenAll(client.PostAsync($"/api/publications/{publicationId}/simulate", null),
            client.PostAsync($"/api/publications/{publicationId}/simulate", null));
        Assert.All(concurrent, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var receiptA = await concurrent[0].Content.ReadFromJsonAsync<JsonElement>();
        var receiptB = await concurrent[1].Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(receiptA.GetProperty("id").GetGuid(), receiptB.GetProperty("id").GetGuid());
        Assert.False(receiptA.GetProperty("telegramSent").GetBoolean());

        var preview = await client.GetFromJsonAsync<JsonElement>($"/api/publications/{publicationId}/simulation");
        Assert.Equal("Simulation", preview.GetProperty("mode").GetString());
        var text = preview.GetProperty("previewText").GetString()!;
        Assert.Contains("<script>alert('x')</script> & SSD", text); // Plain text; UI renders with React escaping.
        Assert.Contains("19,99", text);
        Assert.DoesNotContain("59,99", text);
        Assert.Contains("afiliação não validada", text);
        Assert.DoesNotContain("Publicidade: link de afiliado", text);
        Assert.Equal("local-operator", preview.GetProperty("operator").GetString());

        await using var db = fixture.Open();
        var publication = await db.Publications.AsNoTracking().SingleAsync();
        Assert.Equal("Simulated", publication.Status);
        Assert.Null(publication.TelegramChatId);
        Assert.Null(publication.TelegramMessageId);
        Assert.Null(publication.PublishedAt);
        Assert.Null(publication.ChannelId);
        Assert.Equal(0, publication.AttemptCount);
        Assert.Equal(0, await db.PublicationAttempts.CountAsync());
        Assert.Equal(0, await db.PublicationChannels.CountAsync());
        Assert.Equal(1, await db.PublicationSimulations.CountAsync());
        Assert.Equal(1, await db.OfferReviews.CountAsync(x => x.Decision == "SimulationCompleted"));
        Assert.Equal("Approved", (await db.Offers.AsNoTracking().SingleAsync()).Status);

        await db.Offers.ExecuteUpdateAsync(set => set.SetProperty(x => x.Title, "Changed after preview"));
        var saved = await client.GetFromJsonAsync<JsonElement>($"/api/publications/{publicationId}/simulation");
        Assert.Equal(text, saved.GetProperty("previewText").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/publications/{publicationId}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/offers/{offerId}/schedule",
            new ScheduleOfferRequest(DateTimeOffset.UtcNow.AddHours(2)))).StatusCode);
    }

    [PostgresFact]
    public async Task CancellationAndSimulationHaveOnlyOneWinner()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = await fixture.ScheduleAsync();
        using var factory = new ApiFactory();
        using var configured = fixture.Configure(factory);
        using var signed = factory.CreateClientFor("Reviewer");
        using var client = configured.CreateClient();
        client.DefaultRequestHeaders.Authorization = signed.DefaultRequestHeaders.Authorization;
        var responses = await Task.WhenAll(client.PostAsync($"/api/publications/{id}/simulate", null),
            client.PostAsync($"/api/publications/{id}/cancel", null));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Single(responses, x => x.IsSuccessStatusCode);
        await using var db = fixture.Open();
        var row = await db.Publications.AsNoTracking().SingleAsync();
        Assert.Contains(row.Status, new[] { "Cancelled", "Simulated" });
        Assert.Equal(row.Status == "Simulated" ? 1 : 0, await db.PublicationSimulations.CountAsync());
        Assert.Equal("Approved", (await db.Offers.AsNoTracking().SingleAsync()).Status);
    }

    [Theory]
    [InlineData("Viewer", HttpStatusCode.Forbidden)]
    [InlineData("Reviewer", HttpStatusCode.NotFound)]
    public async Task AuthorizationIsAppliedBeforeSimulation(string role, HttpStatusCode expected)
    {
        using var factory = new ApiFactory();
        // Simulation defaults disabled, so Reviewer gets 404 before database access.
        var response = await factory.CreateClientFor(role).PostAsync($"/api/publications/{Guid.NewGuid()}/simulate", null);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousIsRejectedAndRoutesAreAbsentInProduction()
    {
        using var factory = new ApiFactory();
        var path = $"/api/publications/{Guid.NewGuid()}/simulate";
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsync(path, null)).StatusCode);
        using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        Assert.Equal(HttpStatusCode.NotFound, (await production.CreateClient().PostAsync(path, null)).StatusCode);
    }

    [PostgresFact]
    public async Task RealTelegramQueueCannotBeConvertedIntoASimulation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = await fixture.ScheduleAsync();
        await using var db = fixture.Open();
        await db.Publications.ExecuteUpdateAsync(set => set.SetProperty(x => x.IsTest, true));
        var simulator = new Achadinhos.Infrastructure.Simulation.PublicationSimulator(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => simulator.SimulateAsync(id, "tester"));
        Assert.Equal(0, await db.PublicationSimulations.CountAsync());
        Assert.Equal("Scheduled", (await db.Publications.AsNoTracking().SingleAsync()).Status);
    }

    [PostgresFact]
    public async Task UnsafePreviewUrlLeavesQueueAndReviewUnchanged()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = await fixture.ScheduleAsync();
        await using var db = fixture.Open();
        await db.Offers.ExecuteUpdateAsync(set => set.SetProperty(x => x.ProductUrl, "http://example.com"));
        var simulator = new Achadinhos.Infrastructure.Simulation.PublicationSimulator(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => simulator.SimulateAsync(id, "tester"));
        Assert.Equal("Scheduled", (await db.Publications.AsNoTracking().SingleAsync()).Status);
        Assert.Equal("Scheduled", (await db.Offers.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(0, await db.PublicationSimulations.CountAsync());
    }

    private sealed class LoopbackStartup : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Loopback;
                await continuation(context);
            });
            next(app);
        };
    }

    private sealed class Fixture(string connection, string schema) : IAsyncDisposable
    {
        public string ConnectionString => new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString;
        public EngineDbContext Open() => new(new DbContextOptionsBuilder<EngineDbContext>().UseNpgsql(ConnectionString).Options);
        public Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Configure(ApiFactory factory) =>
            factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Engine"] = ConnectionString, ["Simulation:Enabled"] = "true" })));
        public static async Task<Fixture> CreateAsync()
        {
            var connection = Environment.GetEnvironmentVariable("TEST_POSTGRES")!;
            var schema = "simulation_" + Guid.NewGuid().ToString("N");
            await using var admin = new NpgsqlConnection(connection);
            await admin.OpenAsync();
            await using (var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", admin)) await create.ExecuteNonQueryAsync();
            var fixture = new Fixture(connection, schema);
            await using var db = fixture.Open();
            await db.Database.MigrateAsync();
            return fixture;
        }
        public async Task<Guid> ScheduleAsync()
        {
            await using var db = Open();
            var offer = new Achadinhos.Domain.Discovery.Offer
            {
                MarketplaceId = Guid.Parse("5b70fb4d-7329-4da9-9167-3e049321a13e"),
                ExternalOfferId = Guid.NewGuid().ToString("N"),
                Title = "Local test",
                ProductUrl = "https://example.com",
                CurrentPrice = 10m,
                Currency = "BRL",
                Status = "Scheduled",
                LastSeenAt = DateTimeOffset.UtcNow
            };
            db.Offers.Add(offer);
            var publication = new Achadinhos.Domain.Discovery.Publication { OfferId = offer.Id, ScheduledAt = DateTimeOffset.UtcNow.AddHours(1) };
            db.Publications.Add(publication);
            await db.SaveChangesAsync();
            return publication.Id;
        }
        public async ValueTask DisposeAsync()
        {
            await using var admin = new NpgsqlConnection(connection);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
