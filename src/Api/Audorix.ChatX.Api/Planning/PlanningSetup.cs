using Audorix.ChatX.Api.Endpoints;
using Audorix.ChatX.Api.Identity;
using Audorix.ChatX.Application.Planning;
using Audorix.ChatX.Contracts.V1.Planning;
using Audorix.ChatX.Domain.Conversations;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Api.Planning;

/// <summary>Drafts, scheduled messages and reminders (Phase 20): endpoints and the dispatcher.</summary>
internal static class PlanningSetup
{
    public static IServiceCollection AddChatXPlanningDispatch(this IServiceCollection services)
    {
        services.AddHostedService<PlanningDispatchService>();
        return services;
    }

    public static RouteGroupBuilder MapPlanningEndpoints(this RouteGroupBuilder v1)
    {
        var conversations = v1.MapGroup("/conversations/{conversationId:guid}").RequireAuthorization();

        // ---- Drafts --------------------------------------------------------------------------
        conversations.MapPut("/draft", async (Guid conversationId, SaveDraftRequest request, HttpContext http, PlanningService planning, CancellationToken ct) =>
        {
            await planning.SaveDraftAsync(http.User.RequireUserId(), conversationId, request.ThreadRootId, request.Body, request.ReplyToId, ct);
            return TypedResults.NoContent();
        }).WithTags("Drafts").WithSummary("Saves your draft for the conversation (or a thread). Empty text without a reply target removes it.");
        v1.MapGet("/drafts", async (HttpContext http, PlanningService planning, CancellationToken ct) =>
            TypedResults.Ok((await planning.ListDraftsAsync(http.User.RequireUserId(), ct))
                .Select(d => new DraftResponse(d.ConversationId, d.ThreadRootId, d.Body, d.ReplyToEntryId, d.UpdatedAtUtc)).ToList()))
            .RequireAuthorization().WithTags("Drafts").WithSummary("Your drafts, most recently changed first.");

        // ---- Scheduled messages ----------------------------------------------------------------
        conversations.MapPost("/scheduled", async (Guid conversationId, ScheduleMessageRequest request, HttpContext http, PlanningService planning, CancellationToken ct) =>
        {
            var view = await planning.ScheduleAsync(http.User.RequireUserId(), conversationId, request.Body, request.SendAtUtc, request.ReplyToId, request.ThreadRootId,
                request.AlsoInConversation, request.AttachmentIds, request.MentionUserIds, ct);
            return TypedResults.Created($"/api/v1/scheduled/{view.Message.Id}", ToScheduled(view));
        }).WithTags("Scheduled messages").WithSummary("Schedules a message (up to 30 days ahead, 50 waiting per person). It is sent with the normal rules at its time.");
        var scheduled = v1.MapGroup("/scheduled").RequireAuthorization().WithTags("Scheduled messages");
        scheduled.MapGet(string.Empty, async (HttpContext http, PlanningService planning, CancellationToken ct) =>
            TypedResults.Ok((await planning.ListScheduledAsync(http.User.RequireUserId(), ct)).Select(ToScheduled).ToList()))
            .WithSummary("Your scheduled messages: waiting ones by time, then those that could not be sent.");
        scheduled.MapPut("/{scheduledId:guid}", async (Guid scheduledId, UpdateScheduledRequest request, HttpContext http, PlanningService planning, CancellationToken ct) =>
            TypedResults.Ok(ToScheduled(await planning.UpdateScheduledAsync(http.User.RequireUserId(), scheduledId, request.Body, request.SendAtUtc, ct))))
            .WithSummary("Changes the text or time of a waiting scheduled message.");
        scheduled.MapDelete("/{scheduledId:guid}", async (Guid scheduledId, HttpContext http, PlanningService planning, CancellationToken ct) =>
        {
            await planning.DeleteScheduledAsync(http.User.RequireUserId(), scheduledId, ct);
            return TypedResults.NoContent();
        }).WithSummary("Cancels a waiting scheduled message, or removes one that could not be sent.");

        // ---- Reminders ---------------------------------------------------------------------------
        conversations.MapPost("/messages/{messageId:guid}/reminder", async (
            Guid conversationId, Guid messageId, SetReminderRequest request, HttpContext http, PlanningService planning, CancellationToken ct) =>
        {
            var view = await planning.SetReminderAsync(http.User.RequireUserId(), conversationId, messageId, request.RemindAtUtc, ct);
            return TypedResults.Created($"/api/v1/reminders/{view.Reminder.Id}", ToReminder(view));
        }).WithTags("Reminders").WithSummary("Reminds you about a message at a time (in the app, by push, and by email when you are away).");
        var reminders = v1.MapGroup("/reminders").RequireAuthorization().WithTags("Reminders");
        reminders.MapGet(string.Empty, async (HttpContext http, PlanningService planning, CancellationToken ct) =>
            TypedResults.Ok((await planning.ListRemindersAsync(http.User.RequireUserId(), ct)).Select(ToReminder).ToList()))
            .WithSummary("Your reminders on messages you can still read: due ones first, then by time.");
        reminders.MapDelete("/{reminderId:guid}", async (Guid reminderId, HttpContext http, PlanningService planning, CancellationToken ct) =>
        {
            await planning.DeleteReminderAsync(http.User.RequireUserId(), reminderId, ct);
            return TypedResults.NoContent();
        }).WithSummary("Marks a reminder done, or cancels one that is not due yet.");
        return v1;
    }

    private static ScheduledMessageResponse ToScheduled(ScheduledView view)
    {
        var s = view.Message;
        return new ScheduledMessageResponse(
            s.Id, s.ConversationId, view.Conversation is null ? null : ConversationEndpoints.TypeName(view.Conversation.Type), view.Conversation?.Title, s.Body, s.SendAtUtc,
            s.Status == ScheduledMessageStatus.Failed ? "failed" : "pending", s.FailureCode, view.FileCount, s.ThreadRootId, s.ReplyToEntryId);
    }

    private static ReminderResponse ToReminder(ReminderView view) => new(
        view.Reminder.Id, view.Conversation.Id, ConversationEndpoints.TypeName(view.Conversation.Type), view.Conversation.Title,
        ConversationEndpoints.ToEntry(view.Entry), view.Reminder.RemindAtUtc, view.Reminder.Status == ReminderStatus.Due ? "due" : "pending");
}

/// <summary>
/// Sends due scheduled messages and reminders every <c>Planning:DispatchInterval</c>, and removes old
/// drafts hourly. Each scheduled message is sent in its own scope, so a failed send leaves nothing
/// half-saved behind; its failure is recorded in another scope.
/// </summary>
internal sealed class PlanningDispatchService(
    IServiceScopeFactory scopes, IOptions<PlanningSettings> settings, TimeProvider time, ILogger<PlanningDispatchService> logger) : Hosting.QuietBackgroundService
{
    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        var lastCleanup = DateTimeOffset.MinValue;
        using var timer = new PeriodicTimer(settings.Value.DispatchInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                IReadOnlyList<Guid> due;
                await using (var scope = scopes.CreateAsyncScope())
                {
                    due = await scope.ServiceProvider.GetRequiredService<PlanningService>().ListDueScheduledAsync(50, stoppingToken).ConfigureAwait(false);
                }

                foreach (var id in due)
                {
                    string? failure;
                    await using (var scope = scopes.CreateAsyncScope())
                    {
                        failure = await scope.ServiceProvider.GetRequiredService<PlanningService>().TrySendScheduledAsync(id, stoppingToken).ConfigureAwait(false);
                    }

                    if (failure is not null)
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        await scope.ServiceProvider.GetRequiredService<PlanningService>().MarkScheduledFailedAsync(id, failure, stoppingToken).ConfigureAwait(false);
                        logger.LogInformation("A scheduled message could not be sent ({Code}).", failure);
                    }
                }

                await using (var scope = scopes.CreateAsyncScope())
                {
                    var planning = scope.ServiceProvider.GetRequiredService<PlanningService>();
                    await planning.DispatchDueRemindersAsync(100, stoppingToken).ConfigureAwait(false);
                    if (time.GetUtcNow() - lastCleanup >= TimeSpan.FromHours(1))
                    {
                        await planning.RemoveOldDraftsAsync(stoppingToken).ConfigureAwait(false);

                        // Phase 22: the reconnect change log keeps 24 hours.
                        await scope.ServiceProvider.GetRequiredService<Application.Realtime.ChangeFeedService>().PurgeAsync(stoppingToken).ConfigureAwait(false);
                        lastCleanup = time.GetUtcNow();
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Database unavailable or not migrated yet: try again on the next tick.
                logger.LogDebug("Planning dispatch skipped ({ExceptionType}).", ex.GetType().Name);
            }
        }
    }
}
