using Audorix.ChatX.Api.Identity;
using Audorix.ChatX.Application.Identity;
using Audorix.ChatX.Application.Realtime;
using Audorix.ChatX.Contracts.V1.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Api.Realtime;

/// <summary>SignalR registration, the <c>/hubs/chat</c> endpoint and the background sweeper.</summary>
internal static class RealtimeSetup
{
    public static IServiceCollection AddChatXRealtime(this IServiceCollection services)
    {
        services.AddOptions<RealtimeSettings>().BindConfiguration(RealtimeSettings.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<RealtimeSettings>, DelegatingValidator<RealtimeSettings>>(
            _ => new DelegatingValidator<RealtimeSettings>(s => s.Validate()));

        services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = false;
            options.MaximumReceiveMessageSize = 32 * 1024;
            options.AddFilter<RealtimeHubFilter>();
        });
        services.AddSingleton<RealtimeHubFilter>();
        services.AddSingleton<SignalRConnections>();
        services.AddSingleton<IRealtimeConnections>(sp => sp.GetRequiredService<SignalRConnections>());
        services.AddSingleton<IRealtimePublisher, SignalRRealtimePublisher>();
        services.AddHostedService<RealtimeMaintenanceService>();
        services.AddHostedService<OutboxDispatcherService>();
        services.AddHostedService<AttachmentCleanupService>();
        services.AddHostedService<PrivateChatMaintenanceService>();
        return services;
    }

    public static WebApplication UseChatXRealtimeOriginCheck(this WebApplication app)
    {
        // Browsers send cookies on cross-site WebSocket handshakes, so the hub only accepts the
        // web app's own origin (or none, for non-browser clients). The API configures no CORS.
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments(RealtimeContract.HubPath, StringComparison.OrdinalIgnoreCase),
            branch => branch.Use((context, next) =>
            {
                var origin = context.Request.Headers.Origin.ToString();
                if (origin.Length > 0 && !IsAllowedOrigin(context, origin))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                return next(context);
            }));
        return app;
    }

    public static WebApplication MapChatXRealtime(this WebApplication app)
    {
        app.MapHub<ChatHub>(RealtimeContract.HubPath).RequireAuthorization();
        return app;
    }

    private static bool IsAllowedOrigin(HttpContext context, string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        {
            return false;
        }

        var own = $"{context.Request.Scheme}://{context.Request.Host}";
        if (string.Equals(originUri.GetLeftPart(UriPartial.Authority), own, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var publicUrl = context.RequestServices.GetRequiredService<IOptions<PublicUrlSettings>>().Value.PublicBaseUrl;
        return Uri.TryCreate(publicUrl, UriKind.Absolute, out var allowed)
            && string.Equals(originUri.GetLeftPart(UriPartial.Authority), allowed.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Transactional outbox dispatcher (Architecture §7.3): publishes message notifications that were
/// not published right after their commit (process crash, transport error), with backoff.
/// </summary>
internal sealed class OutboxDispatcherService(
    IServiceScopeFactory scopes,
    IOptions<Application.Messaging.MessagingSettings> settings,
    TimeProvider time,
    ILogger<OutboxDispatcherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(settings.Value.OutboxPollInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var failed = await scope.ServiceProvider.GetRequiredService<Application.Messaging.MessagingService>()
                    .DispatchDueAsync(100, stoppingToken).ConfigureAwait(false);
                if (failed > 0)
                {
                    logger.LogWarning("{Count} outbox notification(s) could not be published; they will be retried.", failed);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Database unavailable or not migrated yet: try again on the next tick.
                logger.LogDebug("Outbox dispatch skipped ({ExceptionType}).", ex.GetType().Name);
            }
        }
    }
}

/// <summary>
/// Attachment cleanup (Phase 7): removes abandoned and unfinished uploads, retries content removal
/// after deletions and rescans quarantined files when a scanner is configured. Idempotent.
/// </summary>
internal sealed class AttachmentCleanupService(
    IServiceScopeFactory scopes,
    IOptions<Application.Attachments.AttachmentSettings> settings,
    TimeProvider time,
    ILogger<AttachmentCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(settings.Value.CleanupInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<Application.Attachments.AttachmentService>()
                    .CleanUpAsync(200, stoppingToken).ConfigureAwait(false);
                if (result.Abandoned + result.ContentRemoved + result.Rescanned > 0)
                {
                    logger.LogInformation(
                        "Attachment cleanup: {Abandoned} abandoned upload(s) deleted, {Removed} stored file(s) removed, {Rescanned} quarantined file(s) decided.",
                        result.Abandoned, result.ContentRemoved, result.Rescanned);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Database or storage unavailable (or not migrated yet): try again on the next tick.
                logger.LogDebug("Attachment cleanup skipped ({ExceptionType}).", ex.GetType().Name);
            }
        }
    }
}

/// <summary>
/// Private chats: expires unanswered invitations and ends chats whose initiator session ended.
/// On shutdown every private chat ends and is wiped (nothing survives the process).
/// </summary>
internal sealed class PrivateChatMaintenanceService(
    IServiceScopeFactory scopes,
    IOptions<Application.PrivateChats.PrivateChatSettings> settings,
    TimeProvider time,
    ILogger<PrivateChatMaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(settings.Value.SweepInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<Application.PrivateChats.PrivateChatService>().SweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug("Private chat sweep skipped ({ExceptionType}).", ex.GetType().Name);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<Application.PrivateChats.PrivateChatService>().EndAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug("Ending private chats at shutdown failed ({ExceptionType}).", ex.GetType().Name);
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Publishes offline transitions after the grace period and expired typing indicators, and
/// periodically closes connections whose sign-in session has been revoked or has expired.
/// </summary>
internal sealed class RealtimeMaintenanceService(
    IServiceScopeFactory scopes,
    IOptions<RealtimeSettings> settings,
    TimeProvider time,
    ILogger<RealtimeMaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = settings.Value;
        using var timer = new PeriodicTimer(options.SweepInterval, time);
        var lastSessionCheck = time.GetUtcNow();
        var lastHeartbeat = DateTimeOffset.MinValue;
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var realtime = scope.ServiceProvider.GetRequiredService<RealtimeService>();
                await realtime.SweepAsync(stoppingToken).ConfigureAwait(false);

                var now = time.GetUtcNow();
                if (now - lastHeartbeat >= ScaleOut.ScaleOutSettings.HeartbeatInterval)
                {
                    lastHeartbeat = now;
                    await realtime.RefreshClusterPresenceAsync(stoppingToken).ConfigureAwait(false);
                }

                if (now - lastSessionCheck >= options.SessionCheckInterval)
                {
                    lastSessionCheck = now;
                    var closed = await realtime.CloseRevokedSessionsAsync(stoppingToken).ConfigureAwait(false);
                    if (closed > 0)
                    {
                        logger.LogInformation("Closed {Count} realtime connection(s) whose session ended.", closed);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Keep the loop alive; ephemeral state recovers on the next tick.
                logger.LogError(ex, "Realtime maintenance failed.");
            }
        }
    }
}
