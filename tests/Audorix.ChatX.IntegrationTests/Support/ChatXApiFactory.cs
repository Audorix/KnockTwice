using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Audorix.ChatX.IntegrationTests.Support;

/// <summary>
/// In-process API host with explicit configuration. Defaults to the Production environment so
/// tests observe production error behaviour. Adds one test-only route, <c>/__test/throw</c>,
/// appended after the real pipeline so the API's own exception handling applies to it.
/// </summary>
public sealed class ChatXApiFactory(IReadOnlyDictionary<string, string?> settings, string environment = "Production")
    : WebApplicationFactory<Program>
{
    public const string ThrowPath = "/__test/throw";
    public const string ThrownMessage = "Sensitive internal detail that must not reach clients";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, ThrowingRouteStartupFilter>());
    }

    private sealed class ThrowingRouteStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use((context, nextMiddleware) =>
                context.Request.Path == ThrowPath
                    ? throw new InvalidOperationException(ThrownMessage)
                    : nextMiddleware(context));
        };
    }
}
