using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Achadinhos.Api;
using Achadinhos.Application;
using Achadinhos.Contracts;
using Achadinhos.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Achadinhos.Tests;

public sealed class WorkspaceTests
{
    [Theory]
    [InlineData(true, true, "127.0.0.1", "localhost", false, false, true)]
    [InlineData(true, true, "::1", "localhost", false, false, true)]
    [InlineData(false, true, "127.0.0.1", "localhost", false, false, false)]
    [InlineData(true, false, "127.0.0.1", "localhost", false, false, false)]
    [InlineData(true, true, "192.168.1.5", "localhost", false, false, false)]
    [InlineData(true, true, "127.0.0.1", "external.example", false, false, false)]
    [InlineData(true, true, "127.0.0.1", "localhost", true, false, false)]
    [InlineData(true, true, "127.0.0.1", "localhost", false, true, false)]
    public void LocalAccessRequiresExplicitDevelopmentAndLoopback(bool development, bool enabled,
        string remote, string host, bool origin, bool bearer, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        if (origin) context.Request.Headers.Origin = "http://external.example";
        if (bearer) context.Request.Headers.Authorization = "Bearer invalid";
        Assert.Equal(expected, LocalDevelopmentAccess.IsAllowed(context, development, enabled));
    }

    [Fact]
    public async Task LocalModeWorksOverHttpAndStaysDisabledInProduction()
    {
        using var factory = new ApiFactory();
        using var local = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["LocalDevelopment:Enabled"] = "true" }));
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, LoopbackStartup>());
        });
        var client = local.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/offers")).StatusCode);
        client.DefaultRequestHeaders.Add("Origin", "http://external.example");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/offers")).StatusCode);
        using var production = local.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await production.CreateClient().GetAsync("/api/offers")).StatusCode);
    }

    [Theory]
    [InlineData("", "https://example.com/product", 10, null)]
    [InlineData("Produto", "javascript:alert(1)", 10, null)]
    [InlineData("Produto", "https://user:password@example.com", 10, null)]
    [InlineData("Produto", "https://example.com/product", -1, null)]
    [InlineData("Produto", "https://example.com/product", 10, 5d)]
    [InlineData("Produto", "https://example.com/product", 10.123, null)]
    public async Task InvalidOfferIsRejectedBeforeDatabaseAccess(string title, string url, double price, double? original)
    {
        using var factory = new ApiFactory();
        var response = await factory.CreateClientFor("Reviewer").PostAsJsonAsync("/api/offers",
            new CreateOfferRequest(title, url, (decimal)price, original is null ? null : (decimal)original));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ViewerCannotModifyOffers()
    {
        using var factory = new ApiFactory();
        var response = await factory.CreateClientFor("Viewer").PostAsJsonAsync("/api/offers",
            new CreateOfferRequest("Produto", "https://example.com", 10, null));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [PostgresFact]
    public async Task LocalWorkflowPersistsReviewsAndPreventsDuplicateSchedules()
    {
        var connection = Environment.GetEnvironmentVariable("TEST_POSTGRES")!;
        var schema = "workspace_" + Guid.NewGuid().ToString("N");
        var cs = new Npgsql.NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString;
        await using var admin = new Npgsql.NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using (var create = new Npgsql.NpgsqlCommand($"CREATE SCHEMA {schema}", admin))
            await create.ExecuteNonQueryAsync();
        try
        {
            await using var db = new EngineDbContext(new DbContextOptionsBuilder<EngineDbContext>().UseNpgsql(cs).Options);
            await db.Database.MigrateAsync();
            using var factory = new ApiFactory();
            using var local = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Engine"] = cs,
                        ["LocalDevelopment:Enabled"] = "true"
                    }));
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IStartupFilter, LoopbackStartup>();
                    services.AddScoped<IOfferQueries, OfferQueries>();
                });
            });
            var client = local.CreateClient();
            var created = await client.PostAsJsonAsync("/api/offers",
                new CreateOfferRequest("Oferta de teste isolado", "https://example.com/test", 19.99m, 29.99m));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var detail = await client.GetFromJsonAsync<JsonElement>($"/api/offers/{id}");
            Assert.Equal("Discovered", detail.GetProperty("status").GetString());
            Assert.Single(detail.GetProperty("prices").EnumerateArray());
            var filtered = await client.GetFromJsonAsync<PagedResult<OfferSummary>>("/api/offers?status=Discovered&search=TESTE");
            Assert.Equal(1, filtered!.Total);
            var date = DateTimeOffset.UtcNow.AddHours(1);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/offers/{id}/schedule", new ScheduleOfferRequest(date))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/offers/{id}/review", new ReviewOfferRequest("Rejected", null))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/offers/{id}/review", new ReviewOfferRequest("Rejected", "Preço sem evidência"))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/offers/{id}/review", new ReviewOfferRequest("Approved", "Conferida manualmente"))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/offers/{id}/schedule", new ScheduleOfferRequest(DateTimeOffset.UtcNow.AddHours(-1)))).StatusCode);

            var concurrent = await Task.WhenAll(
                client.PostAsJsonAsync($"/api/offers/{id}/schedule", new ScheduleOfferRequest(date)),
                client.PostAsJsonAsync($"/api/offers/{id}/schedule", new ScheduleOfferRequest(date)));
            Assert.Single(concurrent, x => x.StatusCode == HttpStatusCode.Created);
            Assert.Single(concurrent, x => x.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/offers/{id}/review", new ReviewOfferRequest("Rejected", "Não pode mudar enquanto agendada"))).StatusCode);
            var queue = await client.GetFromJsonAsync<JsonElement>("/api/publications");
            Assert.False(queue.GetProperty("deliveryEnabled").GetBoolean());
            Assert.Equal(1, queue.GetProperty("total").GetInt32());
            var publicationId = queue.GetProperty("items")[0].GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/publications/{publicationId}/cancel", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/publications/{publicationId}/cancel", null)).StatusCode);
            detail = await client.GetFromJsonAsync<JsonElement>($"/api/offers/{id}");
            Assert.Equal("Approved", detail.GetProperty("status").GetString());
            Assert.Equal(2, detail.GetProperty("reviews").GetArrayLength());
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/offers/{id}/schedule", new ScheduleOfferRequest(date))).StatusCode);
            Assert.Equal(2, await db.Publications.CountAsync());
            Assert.Equal(2, await db.OfferReviews.CountAsync());
        }
        finally
        {
            await using var drop = new Npgsql.NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
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
}
