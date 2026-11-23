using Audorix.ChatX.Api.Identity;
using Audorix.ChatX.Application.Common;
using Audorix.ChatX.Application.Realtime;
using Audorix.ChatX.Contracts.V1.Realtime;

namespace Audorix.ChatX.Api.Endpoints;

/// <summary><c>/api/v1/changes</c>: reconnect catch-up (Phase 22, second half of ADR-022).</summary>
internal static class ChangeEndpoints
{
    public static RouteGroupBuilder MapChangeEndpoints(this RouteGroupBuilder v1)
    {
        var changes = v1.MapGroup("/changes").RequireAuthorization().WithTags("Realtime");
        changes.MapGet("/cursor", async (ChangeFeedService feed, CancellationToken ct) =>
            TypedResults.Ok(new ChangeCursorResponse(Encode(await feed.CurrentAsync(ct)))))
            .WithSummary("The current change cursor. Take one after loading what you show; use it after reconnecting.");
        changes.MapGet(string.Empty, async (string? since, HttpContext http, ChangeFeedService feed, CancellationToken ct) =>
        {
            var set = await feed.ListAsync(http.User.RequireUserId(), Decode(since), ct);
            return TypedResults.Ok(new ChangeSetResponse(
                set.Reset,
                Encode(set.Cursor),
                set.ConversationIds,
                set.Entries.Select(e => new ChangedEntryResponse(e.Entry.ConversationId, ConversationEndpoints.ToEntry(e))).ToList()));
        }).WithSummary("What changed since a cursor (at most 500 changes, kept 24 hours): the current state of each changed entry you can see, or reset: true to reload.");
        return v1;
    }

    // Cursors reuse the opaque history cursor format, shifted by one because an empty log is 0.
    private static string Encode(long id) => ConversationEndpoints.EncodeCursor(id + 1);

    private static long Decode(string? cursor) =>
        ConversationEndpoints.DecodeCursor(cursor) is { } value ? value - 1 : throw new RequestValidationException("since", "cursor_invalid");
}
