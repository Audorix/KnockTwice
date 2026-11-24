using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Audorix.ChatX.Application.Search;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Infrastructure.Search;

/// <summary>OpenSearch connection (Phase 14), bound from <c>Search:OpenSearch</c>.</summary>
public sealed class OpenSearchOptions
{
    public const string SectionName = "Search:OpenSearch";

    /// <summary>Cluster URL, for example <c>https://search.internal:9200</c>.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Index name; one index per installation.</summary>
    public string IndexName { get; set; } = "chatx-messages";

    /// <summary>Basic authentication (OpenSearch security plugin). Leave empty for an unsecured development cluster.</summary>
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}

internal sealed class OpenSearchOptionsValidator(IOptions<SearchSettings> search) : IValidateOptions<OpenSearchOptions>
{
    public ValidateOptionsResult Validate(string? name, OpenSearchOptions options)
    {
        if (SearchSettings.Normalize(search.Value.Provider) != SearchSettings.OpenSearchProvider)
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();
        if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            errors.Add("Search:OpenSearch:Url must be an absolute http(s) URL.");
        }

        if (string.IsNullOrWhiteSpace(options.IndexName) || options.IndexName.Length > 200 || options.IndexName.Any(char.IsUpper)
            || options.IndexName.StartsWith('_') || options.IndexName.StartsWith('-') || options.IndexName.Any(c => " \"*\\<|,>/?#:".Contains(c, StringComparison.Ordinal)))
        {
            errors.Add("Search:OpenSearch:IndexName must be a valid lower-case index name.");
        }

        if (string.IsNullOrWhiteSpace(options.Username) != string.IsNullOrWhiteSpace(options.Password))
        {
            errors.Add("Search:OpenSearch:Username and Password must be set together.");
        }

        if (options.Timeout <= TimeSpan.Zero)
        {
            errors.Add("Search:OpenSearch:Timeout must be positive.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

/// <summary>
/// OpenSearch as search provider (Phase 14), through its REST API (no client library). One index,
/// documents keyed by message id; the text field uses a lower-case, ASCII-folding analyzer and the
/// already normalized text, and every query term is matched as a prefix. Who may see what arrives
/// as filters (conversation and history position); results are sorted newest first and paged with
/// <c>search_after</c>. Any transport or server failure becomes <see cref="SearchUnavailableException"/>.
/// </summary>
internal sealed class OpenSearchProvider(HttpClient http, IOptions<OpenSearchOptions> options) : ISearchProvider
{
    public const string HttpClientName = "opensearch";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Name => SearchSettings.OpenSearchProvider;

    private string Index => Uri.EscapeDataString(options.Value.IndexName);

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, string.Empty, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> EnsureIndexAsync(CancellationToken cancellationToken)
    {
        using (var head = await SendAsync(HttpMethod.Head, Index, null, cancellationToken, HttpStatusCode.NotFound).ConfigureAwait(false))
        {
            if (head.StatusCode != HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        var definition = new JsonObject
        {
            ["settings"] = new JsonObject
            {
                ["analysis"] = new JsonObject
                {
                    ["analyzer"] = new JsonObject
                    {
                        ["chatx_text"] = new JsonObject
                        {
                            ["type"] = "custom",
                            ["tokenizer"] = "standard",
                            ["filter"] = new JsonArray("lowercase", "asciifolding"),
                        },
                    },
                },
            },
            ["mappings"] = new JsonObject
            {
                ["dynamic"] = "strict",
                ["properties"] = new JsonObject
                {
                    ["entryId"] = Field("keyword"),
                    ["conversationId"] = Field("keyword"),
                    ["senderId"] = Field("keyword"),
                    ["sequence"] = Field("long"),
                    ["createdAtTicks"] = Field("long"),
                    ["hasFiles"] = Field("boolean"),
                    ["text"] = new JsonObject { ["type"] = "text", ["analyzer"] = "chatx_text" },
                },
            },
        };

        // Another instance may create it at the same moment: "already exists" is fine.
        using var created = await SendAsync(HttpMethod.Put, Index, definition, cancellationToken, HttpStatusCode.BadRequest).ConfigureAwait(false);
        if (created.StatusCode == HttpStatusCode.BadRequest)
        {
            var body = await created.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!body.Contains("resource_already_exists_exception", StringComparison.Ordinal))
            {
                throw new SearchUnavailableException("OpenSearch refused the index definition.");
            }

            return false;
        }

        return true;
    }

    public async Task IndexAsync(IReadOnlyList<SearchDocument> documents, CancellationToken cancellationToken)
    {
        var bulk = new StringBuilder();
        foreach (var d in documents)
        {
            bulk.Append(JsonSerializer.Serialize(new { index = new { _index = options.Value.IndexName, _id = d.EntryId.ToString() } }, Json)).Append('\n');
            bulk.Append(JsonSerializer.Serialize(new
            {
                entryId = d.EntryId.ToString(),
                conversationId = d.ConversationId.ToString(),
                senderId = d.SenderId.ToString(),
                sequence = d.Sequence,
                createdAtTicks = d.CreatedAtUtc.UtcTicks,
                hasFiles = d.HasFiles,
                text = d.Text,
            }, Json)).Append('\n');
        }

        await BulkAsync(bulk, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(IReadOnlyList<Guid> entryIds, CancellationToken cancellationToken)
    {
        var bulk = new StringBuilder();
        foreach (var id in entryIds)
        {
            bulk.Append(JsonSerializer.Serialize(new { delete = new { _index = options.Value.IndexName, _id = id.ToString() } }, Json)).Append('\n');
        }

        await BulkAsync(bulk, cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        var query = new JsonObject { ["query"] = new JsonObject { ["match_all"] = new JsonObject() } };
        using var response = await SendAsync(HttpMethod.Post, $"{Index}/_delete_by_query?conflicts=proceed&refresh=true", query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SearchMatch>> SearchAsync(SearchCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.Terms.Count == 0 || criteria.Scopes.Count == 0)
        {
            return [];
        }

        using var response = await SendAsync(HttpMethod.Post, $"{Index}/_search", BuildQuery(criteria), cancellationToken).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken).ConfigureAwait(false)
            ?? throw new SearchUnavailableException("OpenSearch returned no result.");
        var matches = new List<SearchMatch>();
        foreach (var hit in result["hits"]?["hits"]?.AsArray() ?? [])
        {
            var source = hit?["_source"];
            if (source is null)
            {
                continue;
            }

            matches.Add(new SearchMatch(
                Guid.Parse(source["conversationId"]!.GetValue<string>()),
                Guid.Parse(source["entryId"]!.GetValue<string>()),
                source["createdAtTicks"]!.GetValue<long>()));
        }

        return matches;
    }

    public async Task<SearchCounts> CountAsync(SearchCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.Terms.Count == 0 || criteria.Scopes.Count == 0)
        {
            return new SearchCounts(0, 0);
        }

        using var response = await SendAsync(HttpMethod.Post, $"{Index}/_search", BuildCountQuery(criteria), cancellationToken).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken).ConfigureAwait(false)
            ?? throw new SearchUnavailableException("OpenSearch returned no result.");
        return new SearchCounts(
            result["hits"]?["total"]?["value"]?.GetValue<int>() ?? 0,
            result["aggregations"]?["withFiles"]?["doc_count"]?.GetValue<int>() ?? 0);
    }

    /// <summary>The same matches without the files filter, cursor or results: an exact total and how many have files.</summary>
    internal static JsonObject BuildCountQuery(SearchCriteria criteria)
    {
        var query = BuildQuery(criteria with { HasFiles = null, After = null });
        query.Remove("_source");
        query.Remove("sort");
        query["size"] = 0;
        query["track_total_hits"] = true;
        query["aggs"] = new JsonObject
        {
            ["withFiles"] = new JsonObject { ["filter"] = new JsonObject { ["term"] = new JsonObject { ["hasFiles"] = true } } },
        };
        return query;
    }

    /// <summary>
    /// Every term must match a word of the text as a prefix. Visibility is a filter: conversations
    /// whose whole history is visible in one <c>terms</c> clause, the others each with their first
    /// visible position.
    /// </summary>
    internal static JsonObject BuildQuery(SearchCriteria criteria)
    {
        var must = new JsonArray();
        foreach (var term in criteria.Terms)
        {
            must.Add(new JsonObject { ["prefix"] = new JsonObject { ["text"] = new JsonObject { ["value"] = term } } });
        }

        var visibility = new JsonArray();
        var whole = criteria.Scopes.Where(s => s.FromSequence <= 1).Select(s => (JsonNode)s.ConversationId.ToString()).ToArray();
        if (whole.Length > 0)
        {
            visibility.Add(new JsonObject { ["terms"] = new JsonObject { ["conversationId"] = new JsonArray(whole) } });
        }

        foreach (var scope in criteria.Scopes.Where(s => s.FromSequence > 1))
        {
            visibility.Add(new JsonObject
            {
                ["bool"] = new JsonObject
                {
                    ["filter"] = new JsonArray(
                        new JsonObject { ["term"] = new JsonObject { ["conversationId"] = scope.ConversationId.ToString() } },
                        new JsonObject { ["range"] = new JsonObject { ["sequence"] = new JsonObject { ["gte"] = scope.FromSequence } } }),
                },
            });
        }

        var filter = new JsonArray { new JsonObject { ["bool"] = new JsonObject { ["should"] = visibility, ["minimum_should_match"] = 1 } } };
        if (criteria.SenderId is { } sender)
        {
            filter.Add(new JsonObject { ["term"] = new JsonObject { ["senderId"] = sender.ToString() } });
        }

        if (criteria.From is not null || criteria.To is not null)
        {
            var range = new JsonObject();
            if (criteria.From is { } from)
            {
                range["gte"] = from.UtcTicks;
            }

            if (criteria.To is { } to)
            {
                range["lte"] = to.UtcTicks;
            }

            filter.Add(new JsonObject { ["range"] = new JsonObject { ["createdAtTicks"] = range } });
        }

        if (criteria.HasFiles is { } hasFiles)
        {
            filter.Add(new JsonObject { ["term"] = new JsonObject { ["hasFiles"] = hasFiles } });
        }

        var order = criteria.OldestFirst ? "asc" : "desc";
        var query = new JsonObject
        {
            ["size"] = criteria.Limit,
            ["_source"] = new JsonArray("conversationId", "entryId", "createdAtTicks"),
            ["track_total_hits"] = false,
            ["query"] = new JsonObject { ["bool"] = new JsonObject { ["must"] = must, ["filter"] = filter } },
            ["sort"] = new JsonArray(
                new JsonObject { ["createdAtTicks"] = order },
                new JsonObject { ["entryId"] = order }),
        };
        if (criteria.After is { } after)
        {
            query["search_after"] = new JsonArray(after.CreatedAtTicks, after.EntryId.ToString());
        }

        return query;
    }

    private async Task BulkAsync(StringBuilder bulk, CancellationToken cancellationToken)
    {
        if (bulk.Length == 0)
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "_bulk")
        {
            Content = new StringContent(bulk.ToString(), Encoding.UTF8, "application/x-ndjson"),
        };
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken).ConfigureAwait(false);
        if (result?["errors"]?.GetValue<bool>() == true)
        {
            // A delete of a document that was never indexed reports 404; anything else is a failure.
            var failed = (result["items"]?.AsArray() ?? []).Any(item =>
                item?.AsObject().FirstOrDefault().Value?["status"]?.GetValue<int>() is { } status && status >= 300 && status != 404);
            if (failed)
            {
                throw new SearchUnavailableException("OpenSearch rejected part of a bulk request.");
            }
        }
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken, params HttpStatusCode[] accepted)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        return SendAsync(request, cancellationToken, accepted);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken, params HttpStatusCode[] accepted)
    {
        var settings = options.Value;
        if (!string.IsNullOrEmpty(settings.Username))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.Username}:{settings.Password}")));
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new SearchUnavailableException("OpenSearch could not be reached.", ex);
        }
        finally
        {
            request.Dispose();
        }

        if (response.IsSuccessStatusCode || accepted.Contains(response.StatusCode))
        {
            return response;
        }

        var status = (int)response.StatusCode;
        response.Dispose();
        throw new SearchUnavailableException($"OpenSearch answered {status}.");
    }

    private static JsonObject Field(string type) => new() { ["type"] = type };
}
