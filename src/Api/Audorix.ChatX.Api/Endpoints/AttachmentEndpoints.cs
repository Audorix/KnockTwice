using Audorix.ChatX.Api.Identity;
using Audorix.ChatX.Application.Attachments;
using Audorix.ChatX.Application.Common;
using Audorix.ChatX.Contracts.V1.Attachments;
using Audorix.ChatX.Domain.Attachments;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Audorix.ChatX.Api.Endpoints;

/// <summary>
/// Attachments (Phase 7): upload into a conversation, metadata, authorized download and removal of
/// unsent uploads. Uploads are <c>multipart/form-data</c> with one file part, streamed and checked
/// against the configured limits while they arrive.
/// </summary>
internal static class AttachmentEndpoints
{
    public static RouteGroupBuilder MapAttachmentEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapPost("/conversations/{conversationId:guid}/attachments", async (
            Guid conversationId, HttpContext http, AttachmentService service, IOptions<AttachmentSettings> settings, CancellationToken ct) =>
        {
            var attachment = await MultipartUpload.ReadFileAsync(http, settings.Value.MaxFileSize, (name, type, stream) =>
                service.UploadAsync(http.User.RequireUserId(), conversationId, name, type, null, stream, ct), ct);
            return TypedResults.Created($"/api/v1/attachments/{attachment.Id}", ToResponse(attachment));
        })
            .RequireAuthorization()
            .WithTags("Attachments")
            .WithSummary("Uploads a file (multipart/form-data, field 'file') into a conversation. Send it with a message using its id. Unsent uploads are removed after 24 hours.");

        var attachments = v1.MapGroup("/attachments").RequireAuthorization().WithTags("Attachments");

        attachments.MapGet("/limits", (IOptions<AttachmentSettings> settings, IMalwareScanner scanner) =>
        {
            var s = settings.Value;
            return TypedResults.Ok(new AttachmentLimitsResponse(s.MaxFileSize, s.MaxPerMessage, s.UserQuota, s.EffectiveExtensions.Order(StringComparer.Ordinal).ToList(),
                scanner.Enabled));
        }).WithSummary("Size, count and type limits for uploads.");

        attachments.MapGet("/{attachmentId:guid}", async (Guid attachmentId, HttpContext http, AttachmentService service, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await service.GetAsync(http.User.RequireUserId(), attachmentId, ct))))
            .WithSummary("Attachment metadata: your own uploads in any state, others' once ready and visible to you.");

        attachments.MapGet("/{attachmentId:guid}/content", async (
            Guid attachmentId, bool? download, HttpContext http, AttachmentService service, CancellationToken ct) =>
        {
            var userId = http.User.RequireUserId();
            var authorized = await service.AuthorizeReadAsync(userId, attachmentId, ct);
            var etag = new EntityTagHeaderValue($"\"{authorized.Sha256}\"");
            SetDownloadHeaders(http.Response, authorized.FileName, authorized.ContentType, download == true, cacheable: true);
            if (http.Request.Headers.IfNoneMatch.ToString().Contains(etag.Tag.ToString(), StringComparison.Ordinal))
            {
                http.Response.Headers.ETag = etag.ToString();
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            var content = await service.OpenAsync(userId, attachmentId, ct);
            http.Response.Headers.ETag = etag.ToString();
            http.Response.ContentLength = content.Attachment.Size;
            return Results.Stream(content.Content, content.Attachment.ContentType);
        }).WithSummary("Downloads an attachment. Authorized on every request through its conversation; images are shown inline unless download=true.");

        attachments.MapDelete("/{attachmentId:guid}", async (Guid attachmentId, HttpContext http, AttachmentService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(http.User.RequireUserId(), attachmentId, ct);
            return TypedResults.NoContent();
        }).WithSummary("Removes your own unsent upload. Sent files are removed by deleting their message.");

        return v1;
    }

    internal static MessageAttachmentResponse ToMessageAttachment(AttachmentView view) =>
        new(view.Id, view.FileName, view.ContentType, KindName(view.Kind), view.Size);

    internal static string KindName(FileKind kind) => kind == FileKind.Image ? "image" : "file";

    private static AttachmentResponse ToResponse(Attachment a) => new(
        a.Id, a.ConversationId, a.FileName, a.ContentType, KindName(FileTypes.KindOf(a.ContentType)), a.Size, StatusName(a.Status), a.MessageEntryId, a.CreatedAtUtc);

    private static string StatusName(AttachmentStatus status) => status switch
    {
        AttachmentStatus.PendingUpload => "pending_upload",
        AttachmentStatus.Quarantined => "quarantined",
        AttachmentStatus.Ready => "ready",
        AttachmentStatus.Rejected => "rejected",
        _ => "deleted",
    };

    /// <summary>
    /// Downloads never execute: only raster images may be inline; everything else is an attachment.
    /// The sandboxing CSP and nosniff stop browsers from treating content as a page.
    /// </summary>
    internal static void SetDownloadHeaders(HttpResponse response, string fileName, string contentType, bool forceDownload, bool cacheable)
    {
        var inline = !forceDownload && FileTypes.IsInlineImage(contentType);
        var disposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment");
        disposition.SetHttpFileName(fileName);
        response.Headers.ContentDisposition = disposition.ToString();
        response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";

        // Revalidate every time so revoked access takes effect at once; private content never goes to shared caches.
        response.Headers.CacheControl = cacheable ? "private, no-cache" : "no-store";
    }
}

/// <summary>Streams the file part of a <c>multipart/form-data</c> request without buffering the whole body.</summary>
internal static class MultipartUpload
{
    private const long EnvelopeAllowance = 64 * 1024;

    public static async Task<T> ReadFileAsync<T>(HttpContext http, long maxFileSize, Func<string, string?, Stream, Task<T>> handler, CancellationToken cancellationToken)
    {
        var request = http.Request;
        var limit = maxFileSize + EnvelopeAllowance;
        if (request.ContentLength > limit)
        {
            throw new PayloadTooLargeException("attachment_too_large");
        }

        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodyLimit)
        {
            bodyLimit.MaxRequestBodySize = limit;
        }

        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            || HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value is not { Length: > 0 and <= 200 } boundary)
        {
            throw new RequestValidationException("file", "multipart_required");
        }

        try
        {
            var reader = new MultipartReader(boundary, request.Body);
            while (await reader.ReadNextSectionAsync(cancellationToken).ConfigureAwait(false) is { } section)
            {
                if (ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                    && disposition.IsFileDisposition()
                    && string.Equals(HeaderUtilities.RemoveQuotes(disposition.Name).Value, "file", StringComparison.OrdinalIgnoreCase))
                {
                    var name = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value;
                    return await handler(name ?? string.Empty, section.ContentType, section.Body).ConfigureAwait(false);
                }
            }
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            throw new PayloadTooLargeException("attachment_too_large");
        }
        catch (InvalidDataException)
        {
            throw new RequestValidationException("file", "multipart_required");
        }

        throw new RequestValidationException("file", "required");
    }
}
