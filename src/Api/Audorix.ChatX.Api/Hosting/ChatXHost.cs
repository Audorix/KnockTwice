using Audorix.ChatX.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Api.Hosting;

/// <summary>
/// Runs either the HTTP host or the explicit <c>migrate</c> command. Configuration errors end
/// the process with exit code 2 and a concise message that never includes secret values.
/// </summary>
internal static class ChatXHost
{
    public const int ConfigurationErrorExitCode = 2;
    public const int MigrationFailedExitCode = 3;

    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Audorix.ChatX.Startup");
        try
        {
            if (args.Length > 0 && string.Equals(args[0], "migrate", StringComparison.OrdinalIgnoreCase))
            {
                return await MigrateAsync(app, logger).ConfigureAwait(false);
            }

            // Start and wait explicitly (rather than RunAsync) so the host, and its loggers,
            // are only disposed after a startup failure has been reported.
            await app.StartAsync().ConfigureAwait(false);
            await app.WaitForShutdownAsync().ConfigureAwait(false);
            return 0;
        }
        catch (OptionsValidationException ex)
        {
            LogInvalidConfiguration(logger, ex.Failures);
            return ConfigurationErrorExitCode;
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OptionsValidationException))
        {
            LogInvalidConfiguration(logger, ex.InnerExceptions.Cast<OptionsValidationException>().SelectMany(e => e.Failures));
            return ConfigurationErrorExitCode;
        }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<int> MigrateAsync(WebApplication app, ILogger logger)
    {
        // Resolving options triggers the same validation as normal startup.
        _ = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        _ = app.Services.GetRequiredService<IOptions<Application.Localization.LocalizationSettings>>().Value;

        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        await using var scope = app.Services.CreateAsyncScope();
        try
        {
            var result = await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>()
                .MigrateAsync(lifetime.ApplicationStopping)
                .ConfigureAwait(false);
            logger.LogInformation(
                "Migration completed: {Count} migration(s) applied; installation {State}.",
                result.AppliedMigrations.Count,
                result.InstallationCreated ? "initialized" : "unchanged");
            return 0;
        }
        catch (Exception ex) when (ex is not OptionsValidationException)
        {
            // Provider exceptions may include host names but not passwords; keep the message short.
            logger.LogError("Migration failed ({ExceptionType}): {Message}", ex.GetType().Name, ex.Message);
            return MigrationFailedExitCode;
        }
    }

    private static void LogInvalidConfiguration(ILogger logger, IEnumerable<string> failures) =>
        logger.LogCritical("Startup aborted because configuration is invalid: {Failures}", string.Join(" ", failures));
}
