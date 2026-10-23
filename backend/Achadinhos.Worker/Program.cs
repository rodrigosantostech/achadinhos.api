using Achadinhos.Infrastructure;
using Achadinhos.Infrastructure.Telegram;
using Achadinhos.Domain.Discovery;
using Achadinhos.Worker;
using Achadinhos.Worker.Jobs;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddJsonConsole();
builder.Services.AddTelegramPublishing(builder.Configuration);
if (!builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Telegram:Enabled"))
    throw new InvalidOperationException("Telegram publication is limited to Development homologation in this release.");
var action = builder.Configuration["TelegramAction"];
if (action == "check")
{
    using var checkHost = builder.Build();
    try
    {
        var result = await checkHost.Services.GetRequiredService<TelegramDiagnostics>().CheckAsync();
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
    }
    catch (InvalidOperationException error)
    {
        Console.WriteLine(error.Message);
        Environment.ExitCode = 1;
    }
    return;
}
var connection = builder.Configuration.GetConnectionString("Engine")
    ?? throw new InvalidOperationException("Configure ConnectionStrings__Engine.");
builder.Services.AddDbContext<EngineDbContext>(options => options.UseNpgsql(connection));
builder.Services.AddScoped<PublicationQueue>();
builder.Services.AddScoped<PublicationProcessor>();
if (action is not ("migrate" or "prepare-test" or "status"))
{
    builder.Services.AddHangfire(configuration => configuration.UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(storage => storage.UseNpgsqlConnection(connection)));
    builder.Services.AddHangfireServer(options =>
    {
        options.Queues = ["telegram"];
        options.WorkerCount = 2;
    });
    builder.Services.AddTransient<TelegramPublicationJob>();
    builder.Services.AddHostedService<PublicationDispatcher>();
    builder.Services.AddHostedService<FoundationWorker>();
}
using var host = builder.Build();
if (action is "migrate" or "prepare-test" or "status")
{
    if (!builder.Environment.IsDevelopment()) throw new InvalidOperationException("Local maintenance requires Development.");
    await using var scope = host.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<EngineDbContext>();
    if (action == "status")
    {
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            DeliveryEnabled = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<TelegramOptions>>().CurrentValue.Enabled,
            PendingMigrations = (await db.Database.GetPendingMigrationsAsync()).Count(),
            VerifiedChannels = await db.PublicationChannels.CountAsync(x => x.Enabled),
            TestPublications = await db.Publications.CountAsync(x => x.IsTest),
            UnknownDeliveries = await db.Publications.CountAsync(x => x.Status == "UnknownDelivery")
        }));
        return;
    }
    if (action == "migrate")
    {
        await db.Database.MigrateAsync();
        Console.WriteLine("Database migrations applied.");
        return;
    }
    try
    {
        var check = await scope.ServiceProvider.GetRequiredService<TelegramDiagnostics>().CheckAsync();
        if (!check.PrivateChannel) throw new InvalidOperationException("Homologation requires a private channel.");
        var channel = await db.PublicationChannels.SingleOrDefaultAsync(x => x.Code == "telegram-test");
        if (channel is null)
        {
            channel = new PublicationChannel();
            db.PublicationChannels.Add(channel);
        }
        if (channel.Blocked) throw new InvalidOperationException("Unknown delivery blocks this channel; manual reconciliation required.");
        if (channel.VerifiedAt != default && channel.TelegramChatId != check.ChatId)
            throw new InvalidOperationException("Existing channel destination differs. Reconcile pending publications before changing it.");
        channel.TelegramChatId = check.ChatId;
        channel.VerifiedAt = DateTimeOffset.UtcNow;
        channel.Enabled = true;
        await db.SaveChangesAsync();
        var key = builder.Configuration["TelegramTestKey"]
            ?? throw new InvalidOperationException("Supply TelegramTestKey to prevent duplicate test messages.");
        var id = await scope.ServiceProvider.GetRequiredService<PublicationQueue>()
            .EnqueueTestAsync("local-cli", "test:" + key);
        Console.WriteLine($"Test publication queued: {id}. ChannelId={check.ChatId}. No message sent by this command.");
    }
    catch (InvalidOperationException error)
    {
        Console.WriteLine(error.Message);
        Environment.ExitCode = 1;
    }
    return;
}
if (action == "runtime-check")
{
    if (builder.Configuration.GetValue<bool>("Telegram:Enabled"))
        throw new InvalidOperationException("Runtime check requires publication disabled.");
    await host.StartAsync();
    await Task.Delay(TimeSpan.FromSeconds(2));
    await host.StopAsync();
    Console.WriteLine("Hangfire Worker runtime check passed; publication disabled.");
    return;
}
if (action == "run-test")
{
    if (!builder.Environment.IsDevelopment()) throw new InvalidOperationException("Test requires Development.");
    var key = "test:" + (builder.Configuration["TelegramTestKey"] ?? throw new InvalidOperationException("TelegramTestKey required."));
    await host.StartAsync();
    try
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        while (!deadline.IsCancellationRequested)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<EngineDbContext>();
            var publication = await db.Publications.AsNoTracking().SingleAsync(x => x.IdempotencyKey == key, deadline.Token);
            if (publication.Status is "Published" or "Failed" or "UnknownDelivery" or "Cancelled")
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                {
                    publication.Id,
                    publication.Status,
                    publication.TelegramChatId,
                    publication.TelegramMessageId,
                    publication.PublishedAt,
                    publication.AttemptCount,
                    publication.LastErrorCode
                }));
                Environment.ExitCode = publication.Status == "Published" ? 0 : 1;
                return;
            }
            await Task.Delay(TimeSpan.FromSeconds(1), deadline.Token);
        }
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Test deadline reached. Check persisted status before any new attempt.");
        Environment.ExitCode = 1;
    }
    finally { await host.StopAsync(); }
    return;
}
await host.RunAsync();

internal sealed class FoundationWorker(IServiceScopeFactory scopes, ILogger<FoundationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<EngineDbContext>();
                var connected = await db.Database.CanConnectAsync(stoppingToken);
                var ready = connected && !(await db.Database.GetPendingMigrationsAsync(stoppingToken)).Any();
                var enabled = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<TelegramOptions>>().CurrentValue.Enabled;
                logger.LogInformation("Worker heartbeat. DatabaseReady={DatabaseReady}; TelegramEnabled={TelegramEnabled}", ready, enabled);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning("Database probe failed: {ErrorType}", error.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
