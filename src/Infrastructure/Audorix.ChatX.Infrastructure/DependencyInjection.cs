using Audorix.ChatX.Application.Installations;
using Audorix.ChatX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Infrastructure;

public static class DependencyInjection
{
    public const string ReadinessTag = "ready";

    /// <summary>
    /// Registers persistence and adapters. At least one <see cref="IDatabaseProvider"/> must be
    /// registered by the host; the configured <c>Database:Provider</c> selects among them.
    /// </summary>
    public static IServiceCollection AddChatXInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();

        services.AddDbContext<ChatXDbContext>((sp, builder) =>
        {
            var options = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value; // validated on first access
            var provider = sp.GetServices<IDatabaseProvider>().Single(p => p.Kind == options.ProviderKind);
            provider.Configure(builder, options.ConnectionString!);
        });

        services.AddScoped<IInstallationStore, EfInstallationStore>();
        services.AddScoped<DatabaseMigrator>();
        services.TryAddSingleton(TimeProvider.System);

        services.AddHealthChecks()
            .AddCheck<DatabaseReadinessHealthCheck>(
                DatabaseReadinessHealthCheck.Name,
                failureStatus: HealthStatus.Unhealthy,
                tags: [ReadinessTag],
                timeout: TimeSpan.FromSeconds(5));

        return services;
    }
}
