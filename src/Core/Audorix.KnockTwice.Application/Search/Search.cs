using System.Globalization;
using System.Text;
using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Conversations;
using Audorix.KnockTwice.Application.Messaging;
using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Application.Search;

/// <summary>
/// Message search (Phase 14, owner decision: provider based, database full-text by default,
/// OpenSearch as an alternative), bound from <c>Search</c>.
/// </summary>
public sealed class SearchSettings
{
    public const string SectionName = "Search";
    public const string DatabaseProvider = "Database";
    public const string OpenSearchProvider = "OpenSearch";

    /// <summary>Feature switch (live): when off, the search API answers 403 <c>search_disabled</c>. Indexing continues.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary><c>Database</c> (SQLite FTS5 / PostgreSQL full-text) or <c>OpenSearch</c>. Needs a restart.</summary>
    public string Provider { get; set; } = DatabaseProvider;

    /// <summary>How often the indexer looks for new, edited or deleted messages.</summary>
    public TimeSpan IndexInterval { get; set; } = TimeSpan.FromSeconds(2);

    public static string? Normalize(string? provider) => provider?.Trim() switch
    {
        null or "" => DatabaseProvider,
        var p when string.Equals(p, DatabaseProvider, StringComparison.OrdinalIgnoreCase) => DatabaseProvider,
        var p when string.Equals(p, OpenSearchProvider, StringComparison.OrdinalIgnoreCase) => OpenSearchProvider,
        _ => null,
    };

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Normalize(Provider) is null)
        {
            errors.Add($"Search:Provider '{Provider}' is not supported. Use Database or OpenSearch.");
        }

        if (IndexInterval < TimeSpan.FromMilliseconds(100) || IndexInterval > TimeSpan.FromMinutes(10))
        {
            errors.Add("Search:IndexInterval must be between 100 milliseconds and 10 minutes.");
        }

        return errors;
    }
}

/// <summary>
/// Text as the index sees it (owner decision: prefixes, accent-blind, the same for every language):
/// lower case, diacritics removed, everything that is not a letter or digit becomes a space.
/// Both the indexed text and the query terms go through it, so every provider matches the same way.
/// </summary>
public static class SearchText
{
    public const int MinimumQueryLength = 2;
    public const int MaximumQueryLength = 200;
    public const int MaximumTerms = 10;

    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var space = true;
        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                space = false;
            }
            else if (!space)
            {
                builder.Append(' ');
                space = true;
            }
        }

        return builder.ToString().TrimEnd().Normalize(NormalizationForm.FormC);
    }

    /// <summary>The distinct words of a query, each matched as a prefix; all of them must match.</summary>
    public static IReadOnlyList<string> Terms(string? query) =>
        Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).Take(MaximumTerms).ToList();
}

/// <summary>What the index holds for one message: its text and attachment file names, normalized.</summary>
public sealed record SearchDocument(
    Guid EntryId, Guid ConversationId, long Sequence, Guid SenderId, DateTimeOffset CreatedAtUtc, bool HasFiles, string Text);

/// <summary>A conversation the caller may search, from the history position they may see.</summary>
public sealed record SearchScope(Guid ConversationId, long FromSequence);

/// <summary>Results are newest first (or oldest first); a cursor continues after the last result of a page.</summary>
public sealed record SearchCursor(long CreatedAtTicks, Guid EntryId);

/// <param name="Terms">Normalized words; every one must match a word of the message as a prefix.</param>
/// <param name="OldestFirst">Ascending order (redesign 2026-11); the cursor then continues with later results.</param>
public sealed record SearchCriteria(
    IReadOnlyList<string> Terms,
    IReadOnlyList<SearchScope> Scopes,
    Guid? SenderId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    bool? HasFiles,
    SearchCursor? After,
    int Limit,
    bool OldestFirst = false);

public sealed record SearchMatch(Guid ConversationId, Guid EntryId, long CreatedAtTicks);

/// <summary>How many indexed messages match, all of them and those with files (redesign 2026-11).</summary>
public sealed record SearchCounts(int Total, int WithFiles);

/// <summary>
/// A search engine behind ChatX (Phase 14). Implementations: the database (default) and
/// OpenSearch. Providers only store and match; who may see what is decided by
/// <see cref="SearchService"/> and passed in as <see cref="SearchCriteria.Scopes"/>.
/// </summary>
public interface ISearchProvider
{
    string Name { get; }

    /// <summary>Checks that the engine answers, without changing anything (provider test in the administration).</summary>
    Task CheckAsync(CancellationToken cancellationToken);

    /// <summary>Prepares the index; true when it was created empty (everything must be indexed again).</summary>
    Task<bool> EnsureIndexAsync(CancellationToken cancellationToken);

    /// <summary>Adds or replaces documents.</summary>
    Task IndexAsync(IReadOnlyList<SearchDocument> documents, CancellationToken cancellationToken);

    Task RemoveAsync(IReadOnlyList<Guid> entryIds, CancellationToken cancellationToken);

    /// <summary>Removes every document (before a rebuild).</summary>
    Task ClearAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SearchMatch>> SearchAsync(SearchCriteria criteria, CancellationToken cancellationToken);

    /// <summary>
    /// Counts the matches of <paramref name="criteria"/>, ignoring its <see cref="SearchCriteria.HasFiles"/>,
    /// cursor and limit: all of them and those with files.
    /// </summary>
    Task<SearchCounts> CountAsync(SearchCriteria criteria, CancellationToken cancellationToken);
}

/// <summary>The search engine could not be reached or refused the request. Mapped to 503 <c>search_unavailable</c>.</summary>
public sealed class SearchUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record SearchQueueEntry(long Id, Guid EntryId);

/// <summary>
/// Messages whose index entry must be written or removed (implemented by Infrastructure). Every
/// created, edited or deleted message is queued in the same transaction as the change.
/// </summary>
public interface ISearchBacklog
{
    Task<IReadOnlyList<SearchQueueEntry>> TakeAsync(int limit, CancellationToken cancellationToken);

    Task CompleteAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken);

    /// <summary>The documents of the entries that are live messages; entries not returned are removed from the index.</summary>
    Task<IReadOnlyList<SearchDocument>> LoadDocumentsAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken);

    /// <summary>Queues every live message (rebuild). Returns how many.</summary>
    Task<int> EnqueueAllAsync(CancellationToken cancellationToken);

    Task<int> CountAsync(CancellationToken cancellationToken);
}

public sealed record SearchRequest(
    string? Query, Guid? ConversationId, Guid? SenderId, DateTimeOffset? From, DateTimeOffset? To, bool? HasFiles, SearchCursor? After, int? Limit,
    bool OldestFirst = false);

public sealed record SearchResultView(ConversationSummaryView Conversation, HistoryEntryView Message);

/// <param name="Counts">On the first page only (no cursor): how many messages match in all.</param>
public sealed record SearchPage(IReadOnlyList<SearchResultView> Items, SearchCursor? Next, SearchCounts? Counts = null);

public sealed record SearchStatus(string Provider, bool Enabled, int Pending);

/// <summary>
/// Message search (Phase 14). Searches only what the caller can read right now: conversations they
/// take part in with the same access rules as the conversation list (organization, workspace and
/// guest rules), from the history position they joined at, never deleted messages. Every match
/// is checked again against the database before it is returned, so a stale index cannot leak.
/// </summary>
public sealed class SearchService(
    ISearchProvider provider,
    ISearchBacklog queue,
    ConversationService conversations,
    IConversationStore store,
    MessagingService messaging,
    IOptionsSnapshot<SearchSettings> settings)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    public async Task<SearchPage> SearchAsync(Guid userId, SearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!settings.Value.Enabled)
        {
            throw new FeatureDisabledException("search_disabled");
        }

        var text = request.Query?.Trim() ?? string.Empty;
        var terms = SearchText.Terms(text);
        if (text.Length > SearchText.MaximumQueryLength)
        {
            throw new RequestValidationException("q", "query_too_long");
        }

        if (text.Length < SearchText.MinimumQueryLength || terms.Count == 0)
        {
            throw new RequestValidationException("q", "query_too_short");
        }

        var limit = request.Limit ?? DefaultPageSize;
        if (limit is < 1 or > MaxPageSize)
        {
            throw new RequestValidationException("limit", "limit_out_of_range");
        }

        if (request.From is { } from && request.To is { } to && from > to)
        {
            throw new RequestValidationException("to", "date_range_invalid");
        }

        var visible = (await conversations.ListAsync(userId, cancellationToken).ConfigureAwait(false))
            .Where(c => request.ConversationId is not { } only || c.Conversation.Id == only)
            .ToDictionary(c => c.Conversation.Id);
        if (visible.Count == 0)
        {
            return new SearchPage([], null, request.After is null ? new SearchCounts(0, 0) : null);
        }

        var scopes = visible.Values.Select(c => new SearchScope(c.Conversation.Id, c.Me.VisibleFromSequence)).ToList();
        var criteria = new SearchCriteria(terms, scopes, request.SenderId, request.From, request.To, request.HasFiles, request.After, limit + 1, request.OldestFirst);
        IReadOnlyList<SearchMatch> matches;
        SearchCounts? counts = null;
        try
        {
            matches = await provider.SearchAsync(criteria, cancellationToken).ConfigureAwait(false);
            if (request.After is null)
            {
                // Counted from the index: a message deleted a moment ago may still count for a few seconds.
                counts = await provider.CountAsync(criteria, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (SearchUnavailableException)
        {
            throw new DependencyUnavailableException("search_unavailable");
        }

        var page = matches.Take(limit).ToList();
        var entries = (await store.FindEntriesAsync(page.Select(m => m.EntryId).ToList(), cancellationToken).ConfigureAwait(false))
            .ToDictionary(e => e.Id);
        var live = page
            .Select(m => entries.GetValueOrDefault(m.EntryId))
            .Where(e => e is { IsMessage: true, IsDeleted: false }
                        && visible.TryGetValue(e.ConversationId, out var c) && e.Sequence >= c.Me.VisibleFromSequence)
            .Select(e => e!)
            .ToList();
        // With the caller's own state (saved), for the bookmark on each result (redesign 2026-11).
        var views = await conversations.WithCallerStateAsync(userId, await messaging.ViewsAsync(live, cancellationToken).ConfigureAwait(false), cancellationToken)
            .ConfigureAwait(false);
        var items = views.Select(v => new SearchResultView(visible[v.Entry.ConversationId], v)).ToList();
        var next = matches.Count > limit ? new SearchCursor(page[^1].CreatedAtTicks, page[^1].EntryId) : null;
        return new SearchPage(items, next, counts);
    }

    public async Task<SearchStatus> StatusAsync(CancellationToken cancellationToken) =>
        new(provider.Name, settings.Value.Enabled, await queue.CountAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>Empties the index and queues every message again (after switching providers, or to repair it).</summary>
    public async Task<int> RebuildAsync(CancellationToken cancellationToken)
    {
        try
        {
            await provider.EnsureIndexAsync(cancellationToken).ConfigureAwait(false);
            await provider.ClearAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SearchUnavailableException)
        {
            throw new DependencyUnavailableException("search_unavailable");
        }

        return await queue.EnqueueAllAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed record IndexingResult(int Indexed, int Removed)
{
    public bool Any => Indexed + Removed > 0;
}

/// <summary>
/// Brings the index up to date from the queue. Entries are completed only after the provider
/// accepted them, so an unavailable search engine just delays indexing.
/// </summary>
public sealed class SearchIndexer(ISearchProvider provider, ISearchBacklog queue)
{
    /// <summary>Prepares the index at startup; a newly created (empty) index gets every message queued.</summary>
    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        if (await provider.EnsureIndexAsync(cancellationToken).ConfigureAwait(false))
        {
            await queue.EnqueueAllAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IndexingResult> RunAsync(int batch, CancellationToken cancellationToken)
    {
        var indexed = 0;
        var removed = 0;
        while (true)
        {
            var taken = await queue.TakeAsync(batch, cancellationToken).ConfigureAwait(false);
            if (taken.Count == 0)
            {
                return new IndexingResult(indexed, removed);
            }

            var ids = taken.Select(t => t.EntryId).Distinct().ToList();
            var documents = await queue.LoadDocumentsAsync(ids, cancellationToken).ConfigureAwait(false);
            var gone = ids.Except(documents.Select(d => d.EntryId)).ToList();
            if (documents.Count > 0)
            {
                await provider.IndexAsync(documents, cancellationToken).ConfigureAwait(false);
            }

            if (gone.Count > 0)
            {
                await provider.RemoveAsync(gone, cancellationToken).ConfigureAwait(false);
            }

            await queue.CompleteAsync(taken.Select(t => t.Id).ToList(), cancellationToken).ConfigureAwait(false);
            indexed += documents.Count;
            removed += gone.Count;
            if (taken.Count < batch)
            {
                return new IndexingResult(indexed, removed);
            }
        }
    }
}
