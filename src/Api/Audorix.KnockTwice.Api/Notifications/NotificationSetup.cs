using Audorix.KnockTwice.Api.Identity;
using Audorix.KnockTwice.Application.Notifications;
using Audorix.KnockTwice.Contracts.V1.Notifications;
using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Api.Notifications;

/// <summary>Notifications (Phase 15): push subscription endpoints and the dispatcher.</summary>
internal static class NotificationSetup
{
    public static IServiceCollection AddChatXNotificationDispatch(this IServiceCollection services)
    {
        services.AddHostedService<NotificationDispatchService>();
        return services;
    }

    public static RouteGroupBuilder MapNotificationEndpoints(this RouteGroupBuilder v1)
    {
        var push = v1.MapGroup("/notifications/push").RequireAuthorization().WithTags("Notifications");
        push.MapGet(string.Empty, async (NotificationService notifications, CancellationToken ct) =>
        {
            var key = await notifications.GetPublicKeyAsync(ct);
            return TypedResults.Ok(new PushConfigurationResponse(key is not null, key));
        }).WithSummary("Whether Web Push is available, and the VAPID public key browsers subscribe with.");
        push.MapPost("/subscriptions", async (PushSubscriptionRequest request, HttpContext http, NotificationService notifications, CancellationToken ct) =>
        {
            await notifications.SubscribeAsync(http.User.RequireUserId(), http.User.RequireSessionId(), request.Endpoint, request.Keys?.P256dh, request.Keys?.Auth, ct);
            return TypedResults.NoContent();
        }).WithSummary("Registers this browser for push notifications (the body is PushSubscription.toJSON()). Subscribing again replaces it.");
        push.MapPost("/subscriptions/remove", async (PushUnsubscribeRequest request, HttpContext http, NotificationService notifications, CancellationToken ct) =>
        {
            await notifications.UnsubscribeAsync(http.User.RequireUserId(), request.Endpoint, ct);
            return TypedResults.NoContent();
        }).WithSummary("Stops push notifications to this browser.");
        return v1;
    }
}

/// <summary>
/// Sends notifications (Phase 15): pushes new messages every <c>Notifications:DispatchInterval</c>
/// and looks for unread messages to email once a minute.
/// </summary>
internal sealed class NotificationDispatchService(
    IServiceScopeFactory scopes, IOptions<NotificationSettings> settings, TimeProvider time, ILogger<NotificationDispatchService> logger) : Hosting.QuietBackgroundService
{
    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        var lastEmail = DateTimeOffset.MinValue;
        using var timer = new PeriodicTimer(settings.Value.DispatchInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var emailDue = time.GetUtcNow() - lastEmail >= TimeSpan.FromMinutes(1) || settings.Value.DispatchInterval < TimeSpan.FromSeconds(1);
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<NotificationService>().RunAsync(emailDue, stoppingToken).ConfigureAwait(false);
                if (emailDue)
                {
                    lastEmail = time.GetUtcNow();
                }

                if (result.Emails > 0)
                {
                    logger.LogInformation("Sent {Count} notification email(s).", result.Emails);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Database unavailable or not migrated yet: try again on the next tick.
                logger.LogDebug("Notification dispatch skipped ({ExceptionType}).", ex.GetType().Name);
            }
        }
    }
}
