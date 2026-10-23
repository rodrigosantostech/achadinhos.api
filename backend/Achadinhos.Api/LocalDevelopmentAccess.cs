using System.Net;
using System.Security.Claims;

namespace Achadinhos.Api;

public static class LocalDevelopmentAccess
{
    public static bool IsAllowed(HttpContext context, bool development, bool enabled)
    {
        var host = context.Request.Host.Host;
        var localHost = host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));
        return development && enabled && localHost
            && context.Connection.RemoteIpAddress is { } remote && IPAddress.IsLoopback(remote)
            && !context.Request.Headers.ContainsKey("Origin")
            && !context.Request.Headers.ContainsKey("Authorization");
    }

    public static void UseLocalDevelopmentAccess(this WebApplication app)
    {
        var enabled = app.Configuration.GetValue<bool>("LocalDevelopment:Enabled");
        app.Use(async (context, next) =>
        {
            if (IsAllowed(context, app.Environment.IsDevelopment(), enabled))
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "local-operator"), new Claim("role", "Admin")],
                    "LocalDevelopment", ClaimTypes.Name, "role"));
            }
            await next(context);
        });
    }
}
