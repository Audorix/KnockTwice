using Audorix.KnockTwice.Api.Endpoints;
using Audorix.KnockTwice.Api.Identity;
using Audorix.KnockTwice.Application.Attachments;
using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Conversations;
using Audorix.KnockTwice.Application.Messaging;
using Audorix.KnockTwice.Application.Widgets;
using Audorix.KnockTwice.Contracts.V1.Conversations;
using Audorix.KnockTwice.Contracts.V1.Widgets;
using Audorix.KnockTwice.Domain.Widgets;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Api.Widgets;

/// <summary>
/// The embedded chat (Phase 10, ADR-024). Two surfaces:
/// <list type="bullet">
/// <item><c>/api/v1/organizations/{orgId}/widgets</c>: widget management by organization owners and
/// administrators (signed-in, antiforgery-protected like the rest of v1).</item>
/// <item><c>/api/v1/widget</c>: the public widget configuration, visitor sessions and the visitor's
/// conversation. Visitors use bearer tokens, never cookies, so this group has no antiforgery filter.</item>
/// </list>
/// </summary>
internal static class WidgetEndpoints
{
    /// <summary>Header the web server copies into <c>Content-Security-Policy: frame-ancestors</c> of the embed page.</summary>
    public const string FrameAncestorsHeader = "X-ChatX-Frame-Ancestors";

    public static RouteGroupBuilder MapWidgetAdminEndpoints(this RouteGroupBuilder v1)
    {
        var widgets = v1.MapGroup("/organizations/{organizationId:guid}/widgets").RequireAuthorization().WithTags("Widgets");

        widgets.MapGet(string.Empty, async (Guid organizationId, HttpContext http, WidgetService service, CancellationToken ct) =>
            TypedResults.Ok((await service.ListAsync(http.User.RequireUserId(), organizationId, ct)).Select(ToResponse).ToList()))
            .WithSummary("The organization's website widgets (owners and administrators).");

        widgets.MapPost(string.Empty, async (Guid organizationId, CreateWidgetRequest request, HttpContext http, WidgetService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(http.User.RequireUserId(), organizationId, request.Name, ct);
            var response = new WidgetSecretResponse(ToResponse(created.View), created.Secret);
            return TypedResults.Created($"/api/v1/organizations/{organizationId}/widgets/{response.Widget.Id}", response);
        }).WithSummary("Creates a widget (switched off). The signing secret is returned once; keep it on your server.");

        widgets.MapGet("/{widgetId:guid}", async (Guid organizationId, Guid widgetId, HttpContext http, WidgetService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.GetAsync(http.User.RequireUserId(), organizationId, widgetId, ct))));

        widgets.MapPut("/{widgetId:guid}", async (
            Guid organizationId, Guid widgetId, UpdateWidgetRequest request, HttpContext http, WidgetService service, CancellationToken ct) =>
        {
            var changes = new WidgetChanges(request.Name, request.Title, request.WelcomeMessage, request.AccentColor, ParsePosition(request.Position),
                request.DefaultLanguage, request.AllowAnonymous, request.AllowedOrigins, request.Enabled, request.ResponderIds);
            return TypedResults.Ok(ToResponse(await service.UpdateAsync(http.User.RequireUserId(), organizationId, widgetId, changes, ct)));
        }).WithSummary("Changes a widget. Null fields stay unchanged. Switching it on needs an allowed origin and a responder.");

        widgets.MapPost("/{widgetId:guid}/secret", async (Guid organizationId, Guid widgetId, HttpContext http, WidgetService service, CancellationToken ct) =>
        {
            var rotated = await service.RotateSecretAsync(http.User.RequireUserId(), organizationId, widgetId, ct);
            return TypedResults.Ok(new WidgetSecretResponse(ToResponse(rotated.View), rotated.Secret));
        }).WithSummary("Replaces the signing secret. Identity tokens signed with the old secret stop working at once.");

        return v1;
    }

    public static RouteGroupBuilder MapWidgetEndpoints(this RouteGroupBuilder group)
    {
        group.WithTags("Widget (visitors)");

        group.MapGet("/{widgetId:guid}/config", async (Guid widgetId, VisitorService visitors, IOptionsSnapshot<AttachmentSettings> attachments, CancellationToken ct) =>
        {
            var w = await visitors.RequirePublicWidgetAsync(widgetId, ct);
            return TypedResults.Ok(new WidgetConfigResponse(w.Id, w.Title, w.WelcomeMessage, w.AccentColor, PositionName(w.Position), w.DefaultLanguage, w.AllowAnonymous,
                attachments.Value.Enabled ? attachments.Value.MaxFileSize : 0));
        }).WithSummary("Public appearance of an enabled widget. Contains no secrets.");

        group.MapGet("/{widgetId:guid}/frame-policy", async (Guid widgetId, HttpContext http, IWidgetStore store, IOptionsSnapshot<WidgetSettings> settings, CancellationToken ct) =>
        {
            var widget = settings.Value.Enabled ? await store.FindAsync(widgetId, ct) : null;
            IReadOnlyList<string> origins = widget is { Enabled: true } ? widget.Origins : [];
            http.Response.Headers[FrameAncestorsHeader] = origins.Count == 0 ? "'none'" : string.Join(' ', origins);
            http.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(new WidgetFramePolicyResponse(origins));
        }).WithSummary("Origins allowed to frame the widget; the web server turns them into frame-ancestors. Unknown or disabled widgets allow none.");

        group.MapPost("/{widgetId:guid}/sessions", async (Guid widgetId, VisitorSessionRequest request, HttpContext http, VisitorService visitors, CancellationToken ct) =>
        {
            var started = await visitors.StartSessionAsync(
                widgetId, request.IdentityToken, request.ParentOrigin, request.Language, http.Connection.RemoteIpAddress?.ToString() ?? "unknown", ct);
            http.Response.Headers.CacheControl = "no-store";
            var v = started.Visitor;
            return TypedResults.Ok(new VisitorSessionResponse(started.Token, started.ExpiresAtUtc, v.VisitorId, v.DisplayName, v.Identified));
        }).WithSummary("Starts a visitor session: anonymous (if the widget allows it) or with an identity token your server signed (HS256 with the widget secret).");

        var visitor = group.MapGroup("/visitor").RequireAuthorization(VisitorAuthenticationHandler.PolicyName);

        visitor.MapDelete("/session", async (HttpContext http, VisitorService visitors, CancellationToken ct) =>
        {
            await visitors.EndSessionAsync(VisitorAuthenticationHandler.RequireVisitor(http.User), VisitorAuthenticationHandler.CurrentToken(http) ?? string.Empty, ct);
            return TypedResults.NoContent();
        }).WithSummary("Ends the visitor session (the conversation stays for the organization).");

        visitor.MapGet("/conversation", async (string? before, HttpContext http, VisitorService visitors, CancellationToken ct) =>
        {
            var me = VisitorAuthenticationHandler.RequireVisitor(http.User);
            var view = await visitors.GetConversationAsync(me, ConversationEndpoints.DecodeCursor(before), ct);
            return TypedResults.Ok(new VisitorConversationResponse(
                view.ConversationId,
                me.UserId,
                view.History.Items.Select(ConversationEndpoints.ToEntry).ToList(),
                view.History.NextBeforeSequence is { } next ? ConversationEndpoints.EncodeCursor(next) : null,
                view.History.HasMore,
                view.Responders.Select(OrganizationEndpoints.ToUser).ToList(),
                view.LastReadSequence));
        }).WithSummary("The visitor's conversation (empty until the first message), newest page first; items ascending.");

        visitor.MapPost("/messages", async Task<Results<Ok<ConversationEntryResponse>, Created<ConversationEntryResponse>>> (
            VisitorMessageRequest request, HttpContext http, VisitorService visitors, CancellationToken ct) =>
        {
            var (message, created) = await visitors.SendAsync(VisitorAuthenticationHandler.RequireVisitor(http.User), request.ClientMessageId, request.Body, request.AttachmentIds, ct);
            var response = ConversationEndpoints.ToEntry(message);
            return created ? TypedResults.Created($"/api/v1/widget/visitor/messages/{response.Id}", response) : TypedResults.Ok(response);
        }).WithSummary("Sends a message; the first one starts the conversation with the widget's responders. Idempotent by clientMessageId.");

        visitor.MapPost("/read", async (MarkConversationReadRequest request, HttpContext http, VisitorService visitors, ConversationService conversations, CancellationToken ct) =>
        {
            var me = VisitorAuthenticationHandler.RequireVisitor(http.User);
            var conversationId = await visitors.RequireConversationIdAsync(me, ct);
            var unread = await conversations.MarkReadAsync(me.UserId, conversationId, request.Sequence, ct);
            var (_, participant) = await conversations.RequireAccessAsync(me.UserId, conversationId, ct);
            return TypedResults.Ok(new ConversationReadResponse(participant.LastReadSequence, unread));
        }).WithSummary("Moves the visitor's read position forward.");

        visitor.MapPost("/delivered", async (MarkConversationDeliveredRequest request, HttpContext http, VisitorService visitors, MessagingService messaging, CancellationToken ct) =>
        {
            var me = VisitorAuthenticationHandler.RequireVisitor(http.User);
            var conversationId = await visitors.RequireConversationIdAsync(me, ct);
            return TypedResults.Ok(new ConversationDeliveredResponse(await messaging.MarkDeliveredAsync(me.UserId, conversationId, request.Sequence, ct)));
        }).WithSummary("Delivery acknowledgement for the visitor's device.");

        visitor.MapPost("/attachments", async (HttpContext http, VisitorService visitors, IOptionsSnapshot<AttachmentSettings> settings, CancellationToken ct) =>
        {
            var me = VisitorAuthenticationHandler.RequireVisitor(http.User);
            var attachment = await MultipartUpload.ReadFileAsync(http, settings.Value.MaxFileSize, (name, type, stream) => visitors.UploadAsync(me, name, type, stream, ct), ct);
            return TypedResults.Created($"/api/v1/widget/visitor/attachments/{attachment.Id}",
                new Contracts.V1.Attachments.MessageAttachmentResponse(attachment.Id, attachment.FileName, attachment.ContentType,
                    AttachmentEndpoints.KindName(FileTypes.KindOf(attachment.ContentType)), attachment.Size));
        }).WithSummary("Uploads a file (multipart/form-data, field 'file'). Identified visitors only.");

        visitor.MapGet("/attachments/{attachmentId:guid}/content", async (
            Guid attachmentId, bool? download, HttpContext http, AttachmentService attachments, CancellationToken ct) =>
        {
            var me = VisitorAuthenticationHandler.RequireVisitor(http.User);
            var authorized = await attachments.AuthorizeReadAsync(me.UserId, attachmentId, ct);
            AttachmentEndpoints.SetDownloadHeaders(http.Response, authorized.FileName, authorized.ContentType, download == true, cacheable: false);
            var content = await attachments.OpenAsync(me.UserId, attachmentId, ct);
            http.Response.ContentLength = content.Attachment.Size;
            return Results.Stream(content.Content, content.Attachment.ContentType);
        }).WithSummary("Downloads an attachment of the visitor's conversation.");

        return group;
    }

    internal static WidgetResponse ToResponse(WidgetView view)
    {
        var w = view.Widget;
        return new WidgetResponse(w.Id, w.OrganizationId, w.Name, w.Title, w.WelcomeMessage, w.AccentColor, PositionName(w.Position), w.DefaultLanguage,
            w.AllowAnonymous, w.Enabled, w.Origins, view.Responders.Select(OrganizationEndpoints.ToUser).ToList(), w.CreatedAtUtc, w.UpdatedAtUtc);
    }

    private static string PositionName(WidgetPosition position) => position == WidgetPosition.Left ? "left" : "right";

    private static WidgetPosition? ParsePosition(string? position) => position?.Trim().ToUpperInvariant() switch
    {
        null => null,
        "RIGHT" => WidgetPosition.Right,
        "LEFT" => WidgetPosition.Left,
        _ => throw new RequestValidationException("position", "position_invalid"),
    };
}
