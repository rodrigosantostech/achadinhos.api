using Achadinhos.Application.Publishing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Achadinhos.Infrastructure.Telegram;

public static class TelegramServiceCollectionExtensions
{
    public static IServiceCollection AddTelegramPublishing(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TelegramOptions>, TelegramOptionsValidator>());
        services.AddOptions<TelegramOptions>().Bind(configuration.GetSection(TelegramOptions.SectionName))
            .ValidateOnStart();
        services.AddHttpClient<ITelegramPublisher, TelegramPublisher>((provider, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(provider.GetRequiredService<IOptions<TelegramOptions>>().Value.TimeoutSeconds);
            client.MaxResponseContentBufferSize = 65536;
        })
            // The bot credential is in the path, so generic URI logging is unsafe.
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                // .NET 10 omits DiagnosticsHandler when this is null, avoiding token-bearing spans.
                ActivityHeadersPropagator = null
            });
        services.AddTransient<TelegramDiagnostics>();


        services.AddHttpClient("TelegramDiagnostics", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.MaxResponseContentBufferSize = 65536;
        }).RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ActivityHeadersPropagator = null
        });
        return services;
    }
}
