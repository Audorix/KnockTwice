using Audorix.KnockTwice.Application.Administration;
using Audorix.KnockTwice.Application.Localization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Api.Administration;

internal static class AdministrationSetup
{
    public static IServiceCollection AddChatXAdministration(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IAuditContext, HttpAuditContext>();
        services.AddSingleton<RuntimeSettingsLoader>();
        services.AddHostedService(sp => sp.GetRequiredService<RuntimeSettingsLoader>());
        services.AddSingleton<RuntimeSettingsLoaderAccessor>();
        services.AddSingleton<PendingRestart>();
        services.AddSingleton<ConfigurationSources>();
        services.AddScoped<RuntimeSettingsManager>();
        services.AddScoped<ProviderSettingsManager>();
        services.AddScoped<AiTranslationProviderProfiles>();
        services.AddHostedService<AuditRetentionService>();
        services.AddHostedService<DataRetentionHostedService>();
        return services;
    }
}

/// <summary>The caller's address for audit records (the connection's remote address; configure forwarded headers behind a proxy).</summary>
internal sealed class HttpAuditContext(IHttpContextAccessor http) : IAuditContext
{
    public string? IpAddress => http.HttpContext?.Connection.RemoteIpAddress?.ToString();
}

/// <summary>Outside a request (setup candidates, background work) there is no caller address.</summary>
internal sealed class NoAuditContext : IAuditContext
{
    public string? IpAddress => null;
}

/// <summary>Deletes audit records past <c>Audit:RetentionDays</c>, every six hours.</summary>
internal sealed class AuditRetentionService(IServiceScopeFactory scopes, ILogger<AuditRetentionService> logger) : Hosting.QuietBackgroundService
{
    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var removed = await scope.ServiceProvider.GetRequiredService<AuditService>().PurgeAsync(stoppingToken).ConfigureAwait(false);
                if (removed > 0)
                {
                    logger.LogInformation("Removed {Count} audit record(s) past the retention period.", removed);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug("Audit retention skipped ({ExceptionType}).", ex.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}

/// <summary>
/// Applies <c>Retention</c> (Phase 13) every six hours, first five minutes after start: inactive
/// anonymous visitors, never confirmed accounts and, when switched on, old messages.
/// </summary>
internal sealed class DataRetentionHostedService(IServiceScopeFactory scopes, TimeProvider time, ILogger<DataRetentionHostedService> logger) : Hosting.QuietBackgroundService
{
    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), time, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(6), time);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<DataRetentionService>().ApplyAsync(time.GetUtcNow(), stoppingToken).ConfigureAwait(false);
                if (result.Any)
                {
                    logger.LogInformation("Retention removed {Visitors} visitor(s), {Accounts} unconfirmed account(s) and {Messages} message(s).",
                        result.Visitors, result.UnconfirmedAccounts, result.Messages);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Data retention skipped ({ExceptionType}).", ex.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}

/// <summary>
/// Picks the request language from the live <c>Localization</c> settings (Phase 9): only currently
/// supported languages are accepted from the query, cookie or Accept-Language; otherwise the current
/// default. Runs before the standard providers, which know every language the build ships.
/// </summary>
internal sealed class LiveLanguageCultureProvider : RequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var settings = httpContext.RequestServices.GetRequiredService<IOptionsMonitor<LocalizationSettings>>().CurrentValue;
        var supported = settings.EffectiveSupportedLanguages;
        var requested = httpContext.Request.Query["culture"].ToString();
        if (string.IsNullOrEmpty(requested))
        {
            requested = string.Join(',', httpContext.Request.Headers.AcceptLanguage.ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => part.Split(';')[0].Split('-')[0].ToLowerInvariant()));
        }

        var language = requested.Split(',').FirstOrDefault(l => supported.Contains(l.Trim().ToLowerInvariant()))?.Trim().ToLowerInvariant()
            ?? settings.DefaultLanguage.Trim().ToLowerInvariant();
        return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(language));
    }
}
