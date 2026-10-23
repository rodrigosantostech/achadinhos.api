using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Achadinhos.Application;
using Achadinhos.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Achadinhos.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    internal const string Key = "test-only-signing-key-at-least-thirty-two-characters";
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Engine"] = "Host=127.0.0.1;Port=1;Database=unavailable;Username=test;Timeout=1"
            }));
        builder.ConfigureServices(services =>
        {
            services.AddScoped<IOfferQueries, EmptyOfferQueries>();
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = "tests",
                    ValidAudience = "tests",
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    RoleClaimType = "role"
                };
            });
        });
    }

    public HttpClient CreateClientFor(string role)
    {
        var client = CreateClient();
        var token = new JwtSecurityToken("tests", "tests", [new Claim("role", role)],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }

    private sealed class EmptyOfferQueries : IOfferQueries
    {
        public Task<PagedResult<OfferSummary>> ListAsync(int page, int pageSize, CancellationToken ct, string? status = null, string? search = null) =>
            Task.FromResult(new PagedResult<OfferSummary>([], page, pageSize, 0));
    }
}

public sealed class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task LivenessDoesNotDependOnDatabase()
    {
        var response = await factory.CreateClient().GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
    }

    [Fact]
    public async Task ReadinessFailsWhenDatabaseIsUnavailable() =>
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await factory.CreateClient().GetAsync("/health/ready")).StatusCode);

    [Theory]
    [InlineData("/api/offers")]
    [InlineData("/api/integrations")]
    public async Task AdministrativeRoutesRejectAnonymousRequests(string path) =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(path)).StatusCode);

    [Theory]
    [InlineData("Viewer", HttpStatusCode.Forbidden)]
    [InlineData("Reviewer", HttpStatusCode.Forbidden)]
    [InlineData("Admin", HttpStatusCode.OK)]
    public async Task IntegrationStatusRequiresAdmin(string role, HttpStatusCode expected) =>
        Assert.Equal(expected, (await factory.CreateClientFor(role).GetAsync("/api/integrations")).StatusCode);

    [Theory]
    [InlineData(0, 25)]
    [InlineData(1, 101)]
    [InlineData(1000001, 25)]
    public async Task InvalidPaginationReturnsProblemDetails(int page, int size)
    {
        var response = await factory.CreateClientFor("Viewer").GetAsync($"/api/offers?page={page}&pageSize={size}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("correlation_id", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ViewerCanListOffers() =>
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClientFor("Viewer").GetAsync("/api/offers")).StatusCode);

    [Fact]
    public async Task OpenApiDocumentIsAvailableInDevelopment() =>
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync("/openapi/v1.json")).StatusCode);

    [Fact]
    public async Task SwaggerUiIsAvailableInDevelopment()
    {
        var response = await factory.CreateClient().GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("swagger-ui-bundle.js", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/swagger/index.html")]
    [InlineData("/openapi/v1.json")]
    public async Task DocumentationIsUnavailableInProduction(string path)
    {
        using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        Assert.Equal(HttpStatusCode.NotFound, (await production.CreateClient().GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task InvalidTokenIsRejected()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/offers")).StatusCode);
    }
}
