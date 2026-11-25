using Audorix.KnockTwice.Contracts.V1.Conversations;

namespace Audorix.KnockTwice.Contracts.V1.Search;

/// <summary>A message found by search, with the conversation it belongs to.</summary>
public sealed record SearchResultResponse(ConversationSummaryResponse Conversation, ConversationEntryResponse Message);

/// <param name="Items">Newest first, or oldest first with <c>order=oldest</c>.</param>
/// <param name="NextCursor">Pass as <c>cursor</c> (with the same filters and order) for more results; null when there are none.</param>
/// <param name="Counts">First page only: how many messages match in all, ignoring <c>hasFiles</c>.</param>
public sealed record SearchResponse(IReadOnlyList<SearchResultResponse> Items, string? NextCursor, SearchCountsResponse? Counts = null);

/// <param name="Total">All matching messages.</param>
/// <param name="WithFiles">Matching messages with files (the rest have none).</param>
public sealed record SearchCountsResponse(int Total, int WithFiles);

/// <param name="Provider"><c>Database</c> or <c>OpenSearch</c>.</param>
/// <param name="Pending">Message changes waiting to be indexed.</param>
public sealed record AdminSearchStatusResponse(string Provider, bool Enabled, int Pending);

public sealed record AdminSearchRebuildResponse(int Queued);
