using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Audorix.ChatX.Api.Endpoints;
using Audorix.ChatX.Api.Identity;
using Audorix.ChatX.Application.Administration;
using Audorix.ChatX.Application.Common;
using Audorix.ChatX.Application.Search;
using Audorix.ChatX.Contracts.V1.Search;
using Audorix.ChatX.Domain.Authorization;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Api.Search;

/// <summary>Message search (Phase 14): the API, the administration of the index and the indexing job.</summary>
internal static class SearchSetup
{
    public static IServiceCollection AddChatXSearchIndexing(this IServiceCollection services)
    {
        services.AddHostedService<SearchIndexingService>();
        return services;
    }

    public static RouteGroupBuilder MapSearchEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet("/search", async (
            string? q, Guid? conversationId, Guid? senderId, DateTimeOffset? from, DateTimeOffset? to, bool? hasFiles, string? cursor, int? limit,
            HttpContext http, SearchService search, CancellationToken ct) =>
        {
            var page = await search.SearchAsync(http.User.RequireUserId(),
                new SearchRequest(q, conversationId, senderId, from, to, hasFiles, DecodeCursor(cursor), limit), ct);
            return TypedResults.Ok(new SearchResponse(
                page.Items.Select(i => new SearchResultResponse(ConversationEndpoints.ToSummary(i.Conversation), ConversationEndpoints.ToEntry(i.Message))).ToList(),
                page.Next is { } next ? EncodeCursor(next) : null));
        })
            .RequireAuthorization()
            .WithTags("Search")
            .WithSummary("Searches the messages the caller can read (from their join), newest first. Every word of q matches as a prefix, case- and accent-insensitive; message text and attachment file names. Optional filters: conversationId, senderId, from/to (UTC), hasFiles.");

        var admin = v1.MapGroup("/admin/search").WithTags("Administration");
        admin.MapGet(string.Empty, async (SearchService search, CancellationToken ct) =>
        {
            var status = await search.StatusAsync(ct);
            return TypedResults.Ok(new AdminSearchStatusResponse(status.Provider, status.Enabled, status.Pending));
        }).RequireAuthorization(Permissions.InstallationRead).WithSummary("Search provider in use and the number of message changes waiting to be indexed.");
        admin.MapPost("/rebuild", async (HttpContext http, SearchService search, AuditService audit, CancellationToken ct) =>
        {
            var queued = await search.RebuildAsync(ct);
            await audit.RecordAsync(AuditActions.SearchRebuilt, http.User.RequireUserId(), "search", null,
                new Dictionary<string, object?> { ["queued"] = queued }, cancellationToken: ct);
            return TypedResults.Ok(new AdminSearchRebuildResponse(queued));
        }).RequireAuthorization(Permissions.InstallationManage).WithSummary("Empties the search index and indexes every message again (after switching providers, or to repair the index).");
        return v1;
    }

    private const string CursorPrefix = "s1:";

    internal static string EncodeCursor(SearchCursor cursor) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes($"{CursorPrefix}{cursor.CreatedAtTicks.ToString(CultureInfo.InvariantCulture)}:{cursor.EntryId:N}"));

    internal static SearchCursor? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return null;
        }

        try
        {
            var text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
            var parts = text.StartsWith(CursorPrefix, StringComparison.Ordinal) ? text[CursorPrefix.Length..].Split(':') : [];
            if (parts.Length == 2 && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && Guid.TryParseExact(parts[1], "N", out var entryId))
            {
                return new SearchCursor(ticks, entryId);
            }
        }
        catch (FormatException)
        {
        }

        throw new RequestValidationException("cursor", "cursor_invalid");
    }
}

/// <summary>
/// Keeps the search index up to date (Phase 14): prepares the index at start (a new, empty index
/// gets every message queued), then applies queued message changes every <c>Search:IndexInterval</c>.
/// When the provider is unavailable the changes wait in the queue.
/// </summary>
internal sealed class SearchIndexingService(
    IServiceScopeFactory scopes, IOptions<SearchSettings> settings, TimeProvider time, ILogger<SearchIndexingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var prepared = false;
        var failing = false;
        using var timer = new PeriodicTimer(settings.Value.IndexInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var indexer = scope.ServiceProvider.GetRequiredService<SearchIndexer>();
                if (!prepared)
                {
                    await indexer.PrepareAsync(stoppingToken).ConfigureAwait(false);
                    prepared = true;
                }

                var result = await indexer.RunAsync(200, stoppingToken).ConfigureAwait(false);
                if (failing)
                {
                    logger.LogInformation("Search indexing works again.");
                    failing = false;
                }

                if (result.Indexed + result.Removed > 1000)
                {
                    logger.LogInformation("Search index: {Indexed} message(s) indexed, {Removed} removed.", result.Indexed, result.Removed);
                }
            }
            catch (SearchUnavailableException ex)
            {
                if (!failing)
                {
                    logger.LogWarning("Search indexing paused: {Reason} Changes wait in the queue.", ex.Message);
                    failing = true;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Database unavailable or not migrated yet: try again on the next tick.
                logger.LogDebug("Search indexing skipped ({ExceptionType}).", ex.GetType().Name);
            }
        }
    }
}
