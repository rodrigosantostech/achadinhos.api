using Achadinhos.Infrastructure.Telegram;
using Microsoft.AspNetCore.RateLimiting;
using Achadinhos.Api;
using System.Diagnostics;
using Achadinhos.Application;
using Achadinhos.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddJsonConsole();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["correlation_id"] = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier);
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, context, cancellationToken) =>
{
    document.Components ??= new OpenApiComponents();
    document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
    document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    };
    foreach (var path in document.Paths.Where(path => path.Key.StartsWith("/api/", StringComparison.Ordinal)))
    {
        if (path.Value.Operations is null) continue;
        foreach (var operation in path.Value.Operations.Values)
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
        }
    }
    return Task.CompletedTask;
}));
builder.Services.AddDbContext<EngineDbContext>((services, options) =>
{
    var connection = services.GetRequiredService<IConfiguration>().GetConnectionString("Engine")
        ?? throw new InvalidOperationException("Configure ConnectionStrings__Engine.");
    options.UseNpgsql(connection);
});
builder.Services.AddScoped<IOfferQueries, OfferQueries>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.Authority = builder.Configuration["Authentication:Authority"];
    options.Audience = builder.Configuration["Authentication:Audience"];
    options.RequireHttpsMetadata = true;
    options.TokenValidationParameters.RoleClaimType = "role";
    options.MapInboundClaims = false;
});
builder.Services.AddScoped<PublicationQueue>();
builder.Services.AddScoped<Achadinhos.Infrastructure.Simulation.PublicationSimulator>();
builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter("TelegramTest", limiter =>
{
    limiter.PermitLimit = 5;
    limiter.Window = TimeSpan.FromMinutes(1);
    limiter.QueueLimit = 0;
}));
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("TelegramAdmin", policy => policy
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser().RequireRole("Admin")
        .RequireAssertion(context => context.User.Identities.Any(identity =>
            identity.IsAuthenticated && identity.AuthenticationType != "LocalDevelopment")));
    options.AddPolicy("Write", policy => policy.RequireAuthenticatedUser().RequireRole("Admin", "Reviewer"));
    options.AddPolicy("Read", policy => policy.RequireAuthenticatedUser().RequireRole("Admin", "Reviewer", "Viewer"));
    options.AddPolicy("Admin", policy => policy.RequireAuthenticatedUser().RequireRole("Admin"));
});
var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Correlation-ID"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    await next(context);
});
app.UseAuthentication();
app.UseLocalDevelopmentAccess();
app.UseAuthorization();
app.UseRateLimiter();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (EngineDbContext db, CancellationToken ct) =>
{
    try
    {
        if (!await db.Database.CanConnectAsync(ct))
            return Results.StatusCode(503);
        return (await db.Database.GetPendingMigrationsAsync(ct)).Any()
            ? Results.StatusCode(503) : Results.Ok(new { status = "ready" });
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        return Results.StatusCode(503);
    }
});
app.MapGet("/api/offers", async (int? page, int? pageSize, string? status, string? search, IOfferQueries queries, CancellationToken ct) =>
{
    var p = page ?? 1;
    var size = pageSize ?? 25;
    if (p < 1 || p > 1_000_000 || size < 1 || size > 100)
        return Results.Problem(statusCode: 400, title: "Invalid pagination", detail: "page: 1..1000000; pageSize: 1..100.");
    if (status is not (null or "" or "Discovered" or "Approved" or "Rejected" or "Scheduled") || search?.Length > 200)
        return Results.Problem(statusCode: 400, title: "Filtro inválido");
    return Results.Ok(await queries.ListAsync(p, size, ct, status, search));
}).RequireAuthorization("Read");
app.MapGet("/api/integrations", () => Results.Ok(new[] {
    new { provider = "shopee", enabled = false, status = "AwaitingApprovedContract" }
})).RequireAuthorization("Admin");
app.MapWorkspaceEndpoints();
app.MapSimulationEndpoints();
app.MapAdminTelegramEndpoints();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Achadinhos API v1");
        options.DocumentTitle = "Achadinhos API - Swagger";
    });
    app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
}
app.Run();

public partial class Program;
