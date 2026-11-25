using Audorix.KnockTwice.Application.Identity;
using Audorix.KnockTwice.Infrastructure.Captcha;
using Audorix.KnockTwice.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Api.Hosting;

/// <summary>
/// Runs the HTTP host or an explicit operator command:
/// <list type="bullet">
/// <item><c>migrate</c> — apply pending migrations and initialize installation metadata.</item>
/// <item><c>grant-role &lt;email&gt; &lt;role&gt;</c> — add an existing account to an active role
/// (for example the first <c>InstallationAdministrator</c>).</item>
/// </list>
/// Configuration errors end the process with exit code 2 and a concise message that never
/// includes secret values.
/// </summary>
internal static class ChatXHost
{
    public const int ConfigurationErrorExitCode = 2;
    public const int CommandFailedExitCode = 3;
    public const int UsageErrorExitCode = 64;

    public static bool IsCommand(string[] args) =>
        args.Length > 0 && args[0].ToUpperInvariant() is "MIGRATE" or "GRANT-ROLE";

    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Audorix.KnockTwice.Startup");
        try
        {
            var command = args.Length > 0 ? args[0].ToUpperInvariant() : null;
            switch (command)
            {
                case "MIGRATE":
                    return await MigrateAsync(app, logger).ConfigureAwait(false);
                case "GRANT-ROLE":
                    return await GrantRoleAsync(app, logger, args).ConfigureAwait(false);
            }

            await PrepareSchemaAsync(app, logger).ConfigureAwait(false);

            // Start and wait explicitly (rather than RunAsync) so the host, and its loggers,
            // are only disposed after a startup failure has been reported.
            await app.StartAsync().ConfigureAwait(false);
            WarnAboutUnsafeSettings(app, logger);
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
            return CommandFailedExitCode;
        }
    }

    private static async Task<int> GrantRoleAsync(WebApplication app, ILogger logger, string[] args)
    {
        if (args.Length != 3)
        {
            logger.LogError("Usage: grant-role <email> <role>. Active roles: {Roles}.", string.Join(", ", Domain.Authorization.Roles.Active));
            return UsageErrorExitCode;
        }

        _ = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        await using var scope = app.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountManager>();
        var account = await accounts.FindByEmailAsync(args[1], CancellationToken.None).ConfigureAwait(false);
        if (account is null)
        {
            logger.LogError("No account exists for the given email address.");
            return CommandFailedExitCode;
        }

        var result = await accounts.AddToRoleAsync(account.Id, args[2], CancellationToken.None).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            logger.LogError("Role assignment failed: {Errors}.", string.Join(", ", result.Errors.SelectMany(e => e.Value)));
            return CommandFailedExitCode;
        }

        logger.LogInformation("Account {UserId} now has role {Role}. It applies at the user's next sign-in.", account.Id, args[2]);
        return 0;
    }

    /// <summary>
    /// With <c>Database:MigrateOnStartup</c> (Development), applies pending migrations before
    /// serving. Otherwise reports pending migrations clearly instead of failing later on a missing
    /// table; readiness stays "not ready" until <c>migrate</c> runs. A database that cannot be
    /// reached is left to the readiness check.
    /// </summary>
    private static async Task PrepareSchemaAsync(WebApplication app, ILogger logger)
    {
        var options = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        await using var scope = app.Services.CreateAsyncScope();
        try
        {
            if (options.MigrateOnStartup)
            {
                var result = await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync(CancellationToken.None).ConfigureAwait(false);
                if (result.AppliedMigrations.Count > 0)
                {
                    logger.LogInformation("Database:MigrateOnStartup applied {Count} migration(s): {Migrations}.",
                        result.AppliedMigrations.Count, string.Join(", ", result.AppliedMigrations));
                }

                return;
            }

            var pending = await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().GetPendingMigrationsAsync(CancellationToken.None).ConfigureAwait(false);
            if (pending.Count > 0)
            {
                logger.LogError(
                    "The database schema is out of date: {Count} pending migration(s) ({Migrations}). Run the 'migrate' command (for example: dotnet run --project src/Api/Audorix.KnockTwice.Api -- migrate) or set Database:MigrateOnStartup=true.",
                    pending.Count, string.Join(", ", pending));
            }
        }
        catch (Exception ex) when (ex is not OptionsValidationException)
        {
            logger.LogWarning("The database schema could not be checked at startup ({ExceptionType}); see /health/ready.", ex.GetType().Name);
        }
    }

    private static void WarnAboutUnsafeSettings(WebApplication app, ILogger logger)
    {
        var captcha = app.Services.GetRequiredService<IOptions<CaptchaOptions>>().Value;
        if (CaptchaProviders.Normalize(captcha.Provider) == CaptchaProviders.None)
        {
            logger.LogWarning("Captcha:Provider is None: registration and recovery endpoints are not protected by human verification.");
        }

        if (app.Services.GetRequiredService<IOptions<AccountSettings>>().Value.AutoConfirmEmail)
        {
            logger.LogWarning("Development mode (Identity:AutoConfirmEmail): new accounts are confirmed without email verification. Do not use this on a shared installation.");
        }

        // The setup wizard's development mode also allows demo data, whose accounts share a published
        // password. Fine on a test machine; dangerous anywhere others can sign in.
        if (app.Services.GetRequiredService<IOptions<Application.Demo.DemoDataSettings>>().Value.Enabled && !app.Environment.IsDevelopment())
        {
            logger.LogWarning("DemoData:Enabled is on outside the Development environment: administrators can load demo accounts that share a published password. Turn it off (or set up without development mode) on any installation others can reach.");
        }
    }

    private static void LogInvalidConfiguration(ILogger logger, IEnumerable<string> failures) =>
        logger.LogCritical("Startup aborted because configuration is invalid: {Failures}", string.Join(" ", failures));
}
