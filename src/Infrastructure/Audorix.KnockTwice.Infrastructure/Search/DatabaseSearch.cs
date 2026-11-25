using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Audorix.KnockTwice.Application.Search;
using Audorix.KnockTwice.Domain.Attachments;
using Audorix.KnockTwice.Domain.Conversations;
using Audorix.KnockTwice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Audorix.KnockTwice.Infrastructure.Search;

/// <summary>The search queue (Phase 14) in the application database.</summary>
internal sealed class EfSearchBacklog(ChatXDbContext db, TimeProvider time) : ISearchBacklog
{
    public async Task<IReadOnlyList<SearchQueueEntry>> TakeAsync(int limit, CancellationToken cancellationToken) =>
        await db.SearchQueue.AsNoTracking().OrderBy(q => q.Id).Take(limit)
            .Select(q => new SearchQueueEntry(q.Id, q.EntryId)).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task CompleteAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken) =>
        db.SearchQueue.Where(q => ids.Contains(q.Id)).ExecuteDeleteAsync(cancellationToken);

    public async Task<IReadOnlyList<SearchDocument>> LoadDocumentsAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken)
    {
        var entries = await db.ConversationEvents.AsNoTracking()
            .Where(e => entryIds.Contains(e.Id) && e.Kind == ConversationEventKind.Message && e.DeletedAtUtc == null)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var files = await db.Attachments.AsNoTracking()
            .Where(a => a.MessageEntryId != null && entryIds.Contains(a.MessageEntryId.Value) && a.Status == AttachmentStatus.Ready)
            .Select(a => new { EntryId = a.MessageEntryId!.Value, a.FileName })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return entries.Select(e =>
        {
            var names = files.Where(f => f.EntryId == e.Id).Select(f => f.FileName).ToList();
            var text = SearchText.Normalize(string.Join(' ', names.Prepend(e.Body ?? string.Empty)));
            return new SearchDocument(e.Id, e.ConversationId, e.Sequence, e.ActorUserId, e.CreatedAtUtc, names.Count > 0, text);
        }).ToList();
    }

    public Task<int> EnqueueAllAsync(CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync(
            "INSERT INTO search_queue (entry_id, conversation_id, enqueued_at_ticks) " +
            "SELECT id, conversation_id, {0} FROM conversation_events WHERE kind = 'Message' AND deleted_at_utc IS NULL",
            [time.GetUtcNow().UtcTicks], cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken) => db.SearchQueue.CountAsync(cancellationToken);
}

/// <summary>
/// The default search provider (Phase 14): the application database. PostgreSQL matches a
/// generated <c>tsvector</c> column (configuration <c>simple</c>, GIN index) with prefix queries;
/// SQLite an FTS5 table (<c>unicode61</c>, diacritics removed) with prefix queries. The text is
/// normalized by <see cref="SearchText"/> first, so both behave the same.
/// </summary>
internal sealed class DatabaseSearchProvider(ChatXDbContext db) : ISearchProvider
{
    public string Name => SearchSettings.DatabaseProvider;

    private bool IsPostgreSql => db.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true;

    public Task CheckAsync(CancellationToken cancellationToken) => db.MessageSearch.AnyAsync(cancellationToken);

    /// <summary>The tables come from the migration, which also queued every existing message.</summary>
    public Task<bool> EnsureIndexAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    public async Task IndexAsync(IReadOnlyList<SearchDocument> documents, CancellationToken cancellationToken)
    {
        var ids = documents.Select(d => d.EntryId).ToList();
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await db.MessageSearch.Where(r => ids.Contains(r.EntryId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            db.MessageSearch.AddRange(documents.Select(d => new MessageSearchRow
            {
                EntryId = d.EntryId,
                ConversationId = d.ConversationId,
                Sequence = d.Sequence,
                SenderId = d.SenderId,
                CreatedAtTicks = d.CreatedAtUtc.UtcTicks,
                HasFiles = d.HasFiles,
                Content = d.Text,
            }));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
        db.ChangeTracker.Clear();
    }

    public Task RemoveAsync(IReadOnlyList<Guid> entryIds, CancellationToken cancellationToken) =>
        db.MessageSearch.Where(r => entryIds.Contains(r.EntryId)).ExecuteDeleteAsync(cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken) => db.MessageSearch.ExecuteDeleteAsync(cancellationToken);

    public async Task<IReadOnlyList<SearchMatch>> SearchAsync(SearchCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.Terms.Count == 0 || criteria.Scopes.Count == 0)
        {
            return [];
        }

        var postgres = IsPostgreSql;
        var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var command = connection.CreateCommand();
            var sql = new StringBuilder("SELECT s.conversation_id, s.entry_id, s.created_at_ticks");
            AppendMatches(command, sql, criteria, postgres, withFilesFilter: true);

            var (compare, direction) = criteria.OldestFirst ? (">", "ASC") : ("<", "DESC");
            if (criteria.After is { } after)
            {
                Add(command, "afterTicks", after.CreatedAtTicks);
                Add(command, "afterId", postgres ? after.EntryId : Text(after.EntryId));
                sql.Append(CultureInfo.InvariantCulture,
                    $" AND (s.created_at_ticks {compare} @afterTicks OR (s.created_at_ticks = @afterTicks AND s.entry_id {compare} @afterId))");
            }

            Add(command, "limit", criteria.Limit);
            sql.Append(CultureInfo.InvariantCulture, $" ORDER BY s.created_at_ticks {direction}, s.entry_id {direction} LIMIT @limit");
            command.CommandText = sql.ToString();

            var matches = new List<SearchMatch>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                matches.Add(new SearchMatch(ReadGuid(reader, 0), ReadGuid(reader, 1), reader.GetInt64(2)));
            }

            return matches;
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    public async Task<SearchCounts> CountAsync(SearchCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.Terms.Count == 0 || criteria.Scopes.Count == 0)
        {
            return new SearchCounts(0, 0);
        }

        var postgres = IsPostgreSql;
        var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var command = connection.CreateCommand();
            var sql = new StringBuilder("SELECT COUNT(*), COALESCE(SUM(CASE WHEN s.has_files THEN 1 ELSE 0 END), 0)");
            AppendMatches(command, sql, criteria, postgres, withFilesFilter: false);
            command.CommandText = sql.ToString();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            return new SearchCounts(Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture), Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture));
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private async Task<DbConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        return connection;
    }

    /// <summary>The FROM and WHERE of a search: terms, visibility and the filters (not the cursor).</summary>
    private static void AppendMatches(DbCommand command, StringBuilder sql, SearchCriteria criteria, bool postgres, bool withFilesFilter)
    {
        if (postgres)
        {
            // Terms contain only letters and digits (SearchText), so they cannot form operators.
            Add(command, "match", string.Join(" & ", criteria.Terms.Select(t => t + ":*")));
            Add(command, "cids", criteria.Scopes.Select(s => s.ConversationId).ToArray());
            Add(command, "froms", criteria.Scopes.Select(s => s.FromSequence).ToArray());
            sql.Append("""

                FROM message_search s
                JOIN unnest(@cids, @froms) AS sc(cid, fromseq) ON s.conversation_id = sc.cid AND s.sequence >= sc.fromseq
                WHERE s.document @@ to_tsquery('simple', @match)
                """);
        }
        else
        {
            Add(command, "match", string.Join(' ', criteria.Terms.Select(t => "\"" + t + "\"*")));
            Add(command, "scopes", JsonSerializer.Serialize(criteria.Scopes.Select(s => new { c = Text(s.ConversationId), f = s.FromSequence })));
            sql.Append("""

                FROM message_search_fts
                JOIN message_search s ON s.rowid = message_search_fts.rowid
                JOIN json_each(@scopes) j ON s.conversation_id = json_extract(j.value, '$.c') AND s.sequence >= json_extract(j.value, '$.f')
                WHERE message_search_fts MATCH @match
                """);
        }

        if (criteria.SenderId is { } sender)
        {
            Add(command, "sender", postgres ? sender : Text(sender));
            sql.Append(" AND s.sender_id = @sender");
        }

        if (criteria.From is { } from)
        {
            Add(command, "from", from.UtcTicks);
            sql.Append(" AND s.created_at_ticks >= @from");
        }

        if (criteria.To is { } to)
        {
            Add(command, "to", to.UtcTicks);
            sql.Append(" AND s.created_at_ticks <= @to");
        }

        if (withFilesFilter && criteria.HasFiles is { } hasFiles)
        {
            Add(command, "files", postgres ? hasFiles : (hasFiles ? 1 : 0));
            sql.Append(" AND s.has_files = @files");
        }
    }

    /// <summary>EF Core stores GUIDs on SQLite as upper-case text.</summary>
    private static string Text(Guid id) => id.ToString().ToUpperInvariant();

    private static Guid ReadGuid(DbDataReader reader, int ordinal) =>
        reader.GetFieldType(ordinal) == typeof(Guid) ? reader.GetGuid(ordinal) : Guid.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
