namespace Audorix.ChatX.Contracts.V1.Realtime;

/// <summary>Phase 22: a reconnect cursor. Keep it; after reconnecting ask for <c>GET /api/v1/changes?since=</c>.</summary>
public sealed record ChangeCursorResponse(string Cursor);

/// <param name="Reset">Too much changed (more than 500) or the cursor is too old: reload what you show, then continue from <paramref name="Cursor"/>.</param>
/// <param name="Cursor">The next cursor.</param>
/// <param name="ConversationIds">Conversations with changes you can see (refresh their summaries and read positions).</param>
/// <param name="Entries">The current state of every changed entry you can see (new, edited, deleted, reactions, thread counts), ascending per conversation. Apply them like realtime message events (keep the highest version).</param>
public sealed record ChangeSetResponse(bool Reset, string Cursor, IReadOnlyList<Guid> ConversationIds, IReadOnlyList<ChangedEntryResponse> Entries);

public sealed record ChangedEntryResponse(Guid ConversationId, Conversations.ConversationEntryResponse Entry);
