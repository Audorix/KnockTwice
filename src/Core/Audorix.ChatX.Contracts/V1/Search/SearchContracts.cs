using Audorix.ChatX.Contracts.V1.Conversations;

namespace Audorix.ChatX.Contracts.V1.Search;

/// <summary>A message found by search, with the conversation it belongs to.</summary>
public sealed record SearchResultResponse(ConversationSummaryResponse Conversation, ConversationEntryResponse Message);

/// <param name="Items">Newest first.</param>
/// <param name="NextCursor">Pass as <c>cursor</c> for more results; null when there are none.</param>
public sealed record SearchResponse(IReadOnlyList<SearchResultResponse> Items, string? NextCursor);

/// <param name="Provider"><c>Database</c> or <c>OpenSearch</c>.</param>
/// <param name="Pending">Message changes waiting to be indexed.</param>
public sealed record AdminSearchStatusResponse(string Provider, bool Enabled, int Pending);

public sealed record AdminSearchRebuildResponse(int Queued);
