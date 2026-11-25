using Audorix.KnockTwice.Api.Identity;
using Audorix.KnockTwice.Application.PrivateChats;
using Audorix.KnockTwice.Contracts.V1.PrivateChats;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using static Audorix.KnockTwice.Api.Endpoints.OrganizationEndpoints;

namespace Audorix.KnockTwice.Api.Endpoints;

/// <summary>
/// <c>/api/v1/private-chats</c>: one-to-one chats held only in server memory for as long as the
/// initiator's sign-in session lasts. Nothing here is written to the database, storage or logs.
/// </summary>
internal static class PrivateChatEndpoints
{
    public static RouteGroupBuilder MapPrivateChatEndpoints(this RouteGroupBuilder v1)
    {
        var chats = v1.MapGroup("/private-chats").RequireAuthorization().WithTags("Private chats");

        chats.MapGet(string.Empty, async (HttpContext http, PrivateChatService service, CancellationToken ct) =>
            TypedResults.Ok((await service.ListAsync(http.User.RequireUserId(), http.User.RequireSessionId(), ct)).Select(ToResponse).ToList()))
            .WithSummary("Running private chats: those you started in this session, and invitations or chats you were invited to.");

        chats.MapPost(string.Empty, async Task<Results<Ok<PrivateChatResponse>, Created<PrivateChatResponse>>> (
            StartPrivateChatRequest request, HttpContext http, PrivateChatService service, CancellationToken ct) =>
        {
            var (view, created) = await service.StartAsync(http.User.RequireUserId(), http.User.RequireSessionId(), request.UserId, ct);
            var response = ToResponse(view);
            return created ? TypedResults.Created($"/api/v1/private-chats/{response.Id}", response) : TypedResults.Ok(response);
        }).WithSummary("Invites a contact or organization colleague to a private chat bound to this sign-in session. They must accept first.");

        chats.MapGet("/{chatId:guid}", async (Guid chatId, HttpContext http, PrivateChatService service, CancellationToken ct) =>
        {
            var detail = await service.GetAsync(http.User.RequireUserId(), http.User.RequireSessionId(), chatId, ct);
            http.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(new PrivateChatDetailResponse(ToResponse(detail.Summary), detail.Messages.Select(ToMessage).ToList()));
        }).WithSummary("The chat and everything said so far (from memory).");

        chats.MapPost("/{chatId:guid}/accept", async (Guid chatId, HttpContext http, PrivateChatService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.AcceptAsync(http.User.RequireUserId(), http.User.RequireSessionId(), chatId, ct))))
            .WithSummary("Accepts an invitation (invitee).");

        chats.MapDelete("/{chatId:guid}", async (Guid chatId, HttpContext http, PrivateChatService service, CancellationToken ct) =>
        {
            await service.EndAsync(http.User.RequireUserId(), http.User.RequireSessionId(), chatId, ct);
            return TypedResults.NoContent();
        }).WithSummary("Ends the chat (initiator) or declines the invitation (invitee). Messages and files are wiped at once.");

        chats.MapPost("/{chatId:guid}/messages", async Task<Results<Ok<PrivateMessageResponse>, Created<PrivateMessageResponse>>> (
            Guid chatId, SendPrivateMessageRequest request, HttpContext http, PrivateChatService service, CancellationToken ct) =>
        {
            var (message, created) = await service.SendAsync(
                http.User.RequireUserId(), http.User.RequireSessionId(), chatId, request.ClientMessageId, request.Body, request.AttachmentIds, ct);
            http.Response.Headers.CacheControl = "no-store";
            return created ? TypedResults.Created($"/api/v1/private-chats/{chatId}", ToMessage(message)) : TypedResults.Ok(ToMessage(message));
        }).WithSummary("Sends a message into an active private chat. Idempotent by clientMessageId while the chat lasts.");

        chats.MapPost("/{chatId:guid}/attachments", async (
            Guid chatId, HttpContext http, PrivateChatService service, IOptions<PrivateChatSettings> settings, CancellationToken ct) =>
        {
            var file = await MultipartUpload.ReadFileAsync(http, settings.Value.MaxFileSize, (name, type, stream) =>
                service.UploadAsync(http.User.RequireUserId(), http.User.RequireSessionId(), chatId, name, type, null, stream, ct), ct);
            return TypedResults.Created($"/api/v1/private-chats/{chatId}/attachments/{file.Id}/content", ToAttachment(file));
        }).WithSummary("Keeps a file in memory for this private chat (multipart/form-data, field 'file'). Wiped when the chat ends.");

        chats.MapGet("/{chatId:guid}/attachments/{fileId:guid}/content", (
            Guid chatId, Guid fileId, bool? download, HttpContext http, PrivateChatService service) =>
        {
            var file = service.OpenFile(http.User.RequireUserId(), http.User.RequireSessionId(), chatId, fileId);
            AttachmentEndpoints.SetDownloadHeaders(http.Response, file.FileName, file.ContentType, download == true, cacheable: false);
            return Results.Bytes(file.Data, file.ContentType);
        }).WithSummary("Downloads a file of the chat. Never cached.");

        return v1;
    }

    private static PrivateChatResponse ToResponse(PrivateChatView view) => new(
        view.Chat.Id,
        view.Chat.Status == PrivateChatStatus.Active ? "active" : "invited",
        view.IsInitiator,
        view.Counterpart is null ? null : ToUser(view.Counterpart),
        view.Initiator is null ? null : ToUser(view.Initiator),
        view.Chat.CreatedAtUtc,
        view.Chat.AcceptedAtUtc,
        view.Chat.InvitationExpiresAtUtc,
        view.UsedBytes,
        view.MaxBytes,
        view.MaxFileSize);

    internal static PrivateMessageResponse ToMessage(PrivateMessageView m) => new(
        m.Id, m.Sequence, m.Author is null ? null : ToUser(m.Author), m.AuthorUserId, m.ClientMessageId, m.Body, m.Attachments.Select(ToAttachment).ToList(), m.CreatedAtUtc);

    private static PrivateAttachmentResponse ToAttachment(PrivateFileView f) =>
        new(f.Id, f.FileName, f.ContentType, AttachmentEndpoints.KindName(f.Kind), f.Size);
}
