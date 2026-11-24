using System.Threading.RateLimiting;
using Audorix.ChatX.Api.Errors;
using Audorix.ChatX.Api.Hosting;
using Audorix.ChatX.Application.Localization;
using Audorix.ChatX.Contracts.Errors;
using Audorix.ChatX.Contracts.V1.Health;
using Audorix.ChatX.Contracts.V1.Setup;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Api.Setup;

/// <summary>
/// The host used until setup is completed. It registers no database, authentication or
/// application endpoints: only health, the setup API under <c>/api/v1/setup</c> and a
/// <c>setup_required</c> answer for every other API path. Every setup call except
/// <c>GET status</c> needs the setup code in the <c>X-Setup-Token</c> header.
/// </summary>
internal static class SetupMode
{
    public const string TokenHeader = "X-Setup-Token";
    private const string RateLimitPolicy = "setup";

    public static WebApplicationBuilder AddChatXSetupMode(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);

        services.AddOptions<SetupSettings>()
            .BindConfiguration(SetupSettings.SectionName)
            .Validate(s => string.IsNullOrWhiteSpace(s.Token) || s.Token.Trim().Length >= SetupSettings.MinimumTokenLength,
                $"Setup:Token must be at least {SetupSettings.MinimumTokenLength} characters when set.")
            .ValidateOnStart();
        services.AddOptions<LocalizationSettings>().BindConfiguration(LocalizationSettings.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<LocalizationSettings>, LocalizationSettingsValidator>();
        services.AddChatXRequestLocalization();
        services.AddChatXProblemDetails();
        services.AddChatXOpenApi("Knock Twice in setup mode: only the setup API is available until setup is completed (send the setup code from the server log in X-Setup-Token).");
        services.AddChatXDatabaseProviders(builder.Environment.ContentRootPath);
        services.AddSingleton<SetupCoordinator>();
        services.AddSingleton<SetupProvisioner>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        return builder;
    }

    public static WebApplication UseChatXSetupMode(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseChatXRequestBasics();
        app.UseRateLimiter();

        app.MapGet("/health/live", () => Health("healthy", StatusCodes.Status200OK)).ExcludeFromDescription();
        app.MapGet("/health/ready", () => Health("setup_required", StatusCodes.Status503ServiceUnavailable)).ExcludeFromDescription();
        app.UseChatXOpenApi();

        // Swagger shows these endpoints; protected ones need the setup code in X-Setup-Token.
        var setup = app.MapGroup("/api/v1/setup").RequireRateLimiting(RateLimitPolicy).WithTags("Setup");
        setup.MapGet("/status", (SetupCoordinator coordinator) =>
            TypedResults.Ok(new SetupStatusResponse(Required: !coordinator.Completed, Completed: coordinator.Completed)));

        var protectedSetup = setup.MapGroup(string.Empty).AddEndpointFilter<SetupTokenFilter>();
        protectedSetup.MapGet("/configuration", (SetupProvisioner provisioner) => TypedResults.Ok(provisioner.GetConfiguration()));
        protectedSetup.MapPost("/token", () => TypedResults.NoContent());
        protectedSetup.MapPost("/database/test", TestDatabaseAsync);
        protectedSetup.MapPost("/email/test", SendTestEmailAsync);
        protectedSetup.MapPost("/complete", CompleteAsync);

        // Everything else under /api answers "setup required" instead of 404.
        app.Map("/api/{**path}", (RequestDelegate)(static _ =>
            throw new SetupException(StatusCodes.Status503ServiceUnavailable, ErrorCodes.SetupRequired))).ExcludeFromDescription();
        return app;
    }

    /// <summary>Runs the setup host. Returns true when the process should restart into normal mode.</summary>
    public static async Task<(bool Restart, int ExitCode)> RunAsync(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Audorix.ChatX.Setup");
        try
        {
            await app.StartAsync().ConfigureAwait(false);
            var coordinator = app.Services.GetRequiredService<SetupCoordinator>();
            var url = app.Configuration["App:PublicBaseUrl"];
            var where = string.IsNullOrWhiteSpace(url) ? "the web app" : url.TrimEnd('/');
            if (coordinator.TokenWasGenerated)
            {
                // Operator-facing on purpose: whoever can read the server log or the API's data
                // directory may run setup.
                var provisioner = app.Services.GetRequiredService<SetupProvisioner>();
                var codeFile = await provisioner.WriteSetupCodeFileAsync(coordinator.DisplayToken).ConfigureAwait(false);
                logger.LogWarning(
                    "Setup required: no completed setup was found. Open {Where} and enter the setup code {SetupCode} (also saved to {CodeFile}). Settings will be written to {SettingsPath}.",
                    where, coordinator.DisplayToken, codeFile, provisioner.SettingsPath);

                // A banner on standard output as well, so the code is visible whatever the log
                // filters or formatter are.
                var line = new string('=', 64);
                Console.Out.WriteLine();
                Console.Out.WriteLine(line);
                Console.Out.WriteLine($"  Knock Twice setup code:  {coordinator.DisplayToken}");
                Console.Out.WriteLine($"  Open {where} to complete setup.");
                Console.Out.WriteLine($"  The code is also saved to {codeFile}");
                Console.Out.WriteLine(line);
                Console.Out.WriteLine();
            }
            else
            {
                logger.LogWarning("Setup required: open {Where} and enter the configured setup code (Setup:Token).", where);
            }

            await app.WaitForShutdownAsync().ConfigureAwait(false);
            return (coordinator.RestartRequested, 0);
        }
        catch (OptionsValidationException ex)
        {
            logger.LogCritical("Startup aborted because configuration is invalid: {Failures}", string.Join(" ", ex.Failures));
            return (false, ChatXHost.ConfigurationErrorExitCode);
        }
        finally
        {
            // The code is regenerated at every start; never leave a stale one behind.
            if (app.Services.GetService<SetupCoordinator>() is { TokenWasGenerated: true })
            {
                try
                {
                    File.Delete(app.Services.GetRequiredService<SetupProvisioner>().SetupCodePath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogDebug("The setup code file could not be removed: {Message}", ex.Message);
                }
            }

            await app.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static JsonHttpResult<HealthReportResponse> Health(string status, int code) =>
        TypedResults.Json(new HealthReportResponse(status, []), statusCode: code);

    private static async Task<Ok<SetupDatabaseTestResponse>> TestDatabaseAsync(
        SetupDatabaseInput request, SetupCoordinator coordinator, SetupProvisioner provisioner, CancellationToken cancellationToken)
    {
        coordinator.EnsurePending();
        return TypedResults.Ok(await provisioner.TestDatabaseAsync(request, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<NoContent> SendTestEmailAsync(
        SetupEmailTestRequest request, SetupCoordinator coordinator, SetupProvisioner provisioner, CancellationToken cancellationToken)
    {
        coordinator.EnsurePending();
        await provisioner.SendTestEmailAsync(request, cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<SetupCompleteResponse>> CompleteAsync(
        SetupCompleteRequest request,
        HttpContext context,
        SetupCoordinator coordinator,
        SetupProvisioner provisioner,
        IOptions<SetupSettings> settings,
        IHostApplicationLifetime lifetime)
    {
        var restart = settings.Value.RestartAfterCompletion;
        // Not bound to the request: a client disconnect must not abandon a half-finished setup.
        var response = await coordinator.CompleteExclusivelyAsync(
            () => provisioner.CompleteAsync(request, lifetime.ApplicationStopping), restart).ConfigureAwait(false);

        if (restart)
        {
            context.Response.OnCompleted(() =>
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
                    lifetime.StopApplication();
                });
                return Task.CompletedTask;
            });
        }

        return TypedResults.Ok(response);
    }

    private sealed class SetupTokenFilter(SetupCoordinator coordinator) : IEndpointFilter
    {
        public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var token = context.HttpContext.Request.Headers[TokenHeader].ToString();
            return coordinator.VerifyToken(token)
                ? next(context)
                : throw new SetupException(StatusCodes.Status401Unauthorized, ErrorCodes.SetupTokenInvalid);
        }
    }
}
