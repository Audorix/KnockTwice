using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Audorix.ChatX.Application.Search;
using Audorix.ChatX.Infrastructure.Search;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.UnitTests;

/// <summary>Search text normalization and the OpenSearch provider's requests (Phase 14).</summary>
public sealed class SearchRulesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("El Café, ¿AHORA?", "el cafe ahora")]
    [InlineData("  presupuesto-2026  ", "presupuesto 2026")]
    [InlineData("Ñandú über façade", "nandu uber facade")]
    [InlineData("a.b@c.org", "a b c org")]
    [InlineData("", "")]
    public void Text_is_lower_case_accent_free_words(string input, string expected) =>
        Assert.Equal(expected, SearchText.Normalize(input));

    [Fact]
    public void Query_terms_are_distinct_words_without_operators()
    {
        Assert.Equal(["presup", "cafe"], SearchText.Terms("presup* CAFÉ presup"));
        Assert.Equal(["a", "b"], SearchText.Terms("\"a\" & b:*"));
        Assert.Equal(SearchText.MaximumTerms, SearchText.Terms(string.Join(' ', Enumerable.Range(0, 30).Select(i => $"w{i}"))).Count);
    }

    [Fact]
    public void OpenSearch_query_filters_by_visible_history_and_pages_newest_first()
    {
        var whole = Guid.NewGuid();
        var joined = Guid.NewGuid();
        var sender = Guid.NewGuid();
        var after = new SearchCursor(123, Guid.NewGuid());
        var query = OpenSearchProvider.BuildQuery(new SearchCriteria(
            ["presup", "cafe"], [new SearchScope(whole, 1), new SearchScope(joined, 42)], sender,
            null, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), true, after, 21));

        var must = query["query"]!["bool"]!["must"]!.AsArray();
        Assert.Equal(["presup", "cafe"], must.Select(m => m!["prefix"]!["text"]!["value"]!.GetValue<string>()));
        var filters = query["query"]!["bool"]!["filter"]!.AsArray();
        var visibility = filters[0]!["bool"]!["should"]!.AsArray();
        Assert.Equal(whole.ToString(), visibility[0]!["terms"]!["conversationId"]![0]!.GetValue<string>());
        Assert.Equal(42, visibility[1]!["bool"]!["filter"]![1]!["range"]!["sequence"]!["gte"]!.GetValue<long>());
        Assert.Equal(1, filters[0]!["bool"]!["minimum_should_match"]!.GetValue<int>());
        Assert.Contains(filters, f => f!["term"]?["senderId"]?.GetValue<string>() == sender.ToString());
        Assert.Contains(filters, f => f!["term"]?["hasFiles"]?.GetValue<bool>() == true);
        Assert.Contains(filters, f => f!["range"]?["createdAtTicks"]?["lte"] is not null);
        Assert.Equal(21, query["size"]!.GetValue<int>());
        Assert.Equal("desc", query["sort"]![0]!["createdAtTicks"]!.GetValue<string>());
        Assert.Equal(123, query["search_after"]![0]!.GetValue<long>());
    }

    [Fact]
    public void OpenSearch_orders_oldest_first_and_counts_without_the_files_filter()
    {
        var criteria = new SearchCriteria(["release"], [new SearchScope(Guid.NewGuid(), 1)], null, null, null, false,
            new SearchCursor(5, Guid.NewGuid()), 21, OldestFirst: true);
        Assert.Equal("asc", OpenSearchProvider.BuildQuery(criteria)["sort"]![0]!["createdAtTicks"]!.GetValue<string>());

        var count = OpenSearchProvider.BuildCountQuery(criteria);
        Assert.Equal(0, count["size"]!.GetValue<int>());
        Assert.True(count["track_total_hits"]!.GetValue<bool>());
        Assert.Null(count["search_after"]);
        Assert.Null(count["sort"]);
        Assert.DoesNotContain(count["query"]!["bool"]!["filter"]!.AsArray(), f => f!["term"]?["hasFiles"] is not null);
        Assert.True(count["aggs"]!["withFiles"]!["filter"]!["term"]!["hasFiles"]!.GetValue<bool>());
    }

    [Fact]
    public async Task OpenSearch_creates_the_index_once_indexes_in_bulk_and_reports_outages()
    {
        var handler = new RecordingHandler();
        var provider = new OpenSearchProvider(new HttpClient(handler) { BaseAddress = new Uri("https://search.test/") },
            Options.Create(new OpenSearchOptions { Url = "https://search.test", IndexName = "chatx-messages", Username = "chatx", Password = "pw" }));

        handler.Next = HttpStatusCode.NotFound;
        handler.Then = HttpStatusCode.OK;
        Assert.True(await provider.EnsureIndexAsync(Ct));
        Assert.Equal(["HEAD chatx-messages", "PUT chatx-messages"], handler.Requests.Select(r => r.Line));
        var definition = JsonNode.Parse(handler.Requests[1].Body)!;
        Assert.Equal("strict", definition["mappings"]!["dynamic"]!.GetValue<string>());
        Assert.Equal("Basic", handler.Requests[0].Authorization);

        handler.Requests.Clear();
        handler.Next = handler.Then = HttpStatusCode.OK;
        handler.Response = """{"errors":false,"items":[]}""";
        var id = Guid.NewGuid();
        await provider.IndexAsync([new SearchDocument(id, Guid.NewGuid(), 3, Guid.NewGuid(), DateTimeOffset.UtcNow, false, "hola mundo")], Ct);
        var bulk = handler.Requests.Single();
        Assert.Equal("POST _bulk", bulk.Line);
        var lines = bulk.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(id.ToString(), JsonNode.Parse(lines[0])!["index"]!["_id"]!.GetValue<string>());
        Assert.Equal("hola mundo", JsonNode.Parse(lines[1])!["text"]!.GetValue<string>());

        // A delete of something never indexed (404) is fine; other item failures are not.
        handler.Response = """{"errors":true,"items":[{"delete":{"status":404}}]}""";
        await provider.RemoveAsync([id], Ct);
        handler.Response = """{"errors":true,"items":[{"index":{"status":429}}]}""";
        await Assert.ThrowsAsync<SearchUnavailableException>(() => provider.RemoveAsync([id], Ct));

        handler.Next = handler.Then = HttpStatusCode.ServiceUnavailable;
        await Assert.ThrowsAsync<SearchUnavailableException>(() => provider.CheckAsync(Ct));
        handler.Fail = true;
        await Assert.ThrowsAsync<SearchUnavailableException>(() => provider.CheckAsync(Ct));
    }

    [Fact]
    public void OpenSearch_settings_are_validated_only_when_selected()
    {
        var off = new OpenSearchOptionsValidator(Options.Create(new SearchSettings { Provider = "Database" }));
        Assert.True(off.Validate(null, new OpenSearchOptions()).Succeeded);
        var on = new OpenSearchOptionsValidator(Options.Create(new SearchSettings { Provider = "opensearch" }));
        Assert.False(on.Validate(null, new OpenSearchOptions { Url = "not a url" }).Succeeded);
        Assert.False(on.Validate(null, new OpenSearchOptions { Url = "https://s.test", IndexName = "Upper" }).Succeeded);
        Assert.False(on.Validate(null, new OpenSearchOptions { Url = "https://s.test", Username = "only" }).Succeeded);
        Assert.True(on.Validate(null, new OpenSearchOptions { Url = "https://s.test" }).Succeeded);
        Assert.NotEmpty(new SearchSettings { Provider = "Elastic" }.Validate());
    }

    private sealed record Recorded(string Line, string Body, string? Authorization);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<Recorded> Requests { get; } = [];

        public HttpStatusCode Next { get; set; } = HttpStatusCode.OK;

        public HttpStatusCode Then { get; set; } = HttpStatusCode.OK;

        public string Response { get; set; } = "{}";

        public bool Fail { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new HttpRequestException("connection refused");
            }

            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Recorded($"{request.Method} {request.RequestUri!.AbsolutePath.TrimStart('/')}", body, request.Headers.Authorization?.Scheme));
            var status = Requests.Count == 1 ? Next : Then;
            return new HttpResponseMessage(status) { Content = new StringContent(Response, Encoding.UTF8, "application/json") };
        }
    }
}
