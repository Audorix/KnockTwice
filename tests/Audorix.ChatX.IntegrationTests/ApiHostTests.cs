using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Audorix.ChatX.Contracts.V1.Health;
using Audorix.ChatX.Contracts.V1.Meta;
using Audorix.ChatX.Infrastructure.Persistence;
using Audorix.ChatX.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Audorix.ChatX.IntegrationTests;

public sealed class ApiHostTests
{
    private const string Secret = "Sup3r-Secret-Pa55";

    // Port 1 on loopback refuses connections immediately: a deterministic "database down".
    private const string UnreachablePostgres = "Host=127.0.0.1;Port=1;Database=chatx;Username=chatx;Password=" + Secret + ";Timeout=2";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, string?> Settings(string provider, string connectionString) => new()
    {
        ["Database:Provider"] = provider,
        ["Database:ConnectionString"] = connectionString,
    };

    [Fact]
    public async Task Liveness_succeeds_while_database_is_unavailable_but_readiness_does_not()
    {
        await using var factory = new ChatXApiFactory(Settings("PostgreSql", UnreachablePostgres));
        using var client = factory.CreateClient();

        var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative), Ct);
        var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Ct);
        var readyBody = await ready.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        var report = JsonSerializer.Deserialize<HealthReportResponse>(readyBody, JsonSerializerOptions.Web)!;
        Assert.Equal("unhealthy", report.Status);
        Assert.Equal([new HealthCheckEntry("database", "unhealthy")], report.Checks);

        // No infrastructure details or credentials in the public response.
        Assert.DoesNotContain("127.0.0.1", readyBody, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, readyBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", readyBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("no-store", ready.Headers.CacheControl?.ToString());
        Assert.False(ready.Headers.Contains("Server"));
    }

    [Fact]
    public async Task Readiness_reports_schema_not_ready_until_migrations_are_applied()
    {
        await using var database = new SqliteTestDatabase();
        await using var factory = new ChatXApiFactory(Settings("Sqlite", database.ConnectionString));
        using var client = factory.CreateClient();

        var before = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, before.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync(Ct);
        }

        var after = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        var report = await after.Content.ReadFromJsonAsync<HealthReportResponse>(Ct);
        Assert.Equal("healthy", report!.Status);
    }

    [Fact]
    public async Task Unknown_route_returns_problem_document_with_stable_code_and_localized_title()
    {
        await using var database = new SqliteTestDatabase();
        await using var factory = new ChatXApiFactory(Settings("Sqlite", database.ConnectionString));
        using var client = factory.CreateClient();

        using var english = new HttpRequestMessage(HttpMethod.Get, "/api/v1/does-not-exist");
        using var spanish = new HttpRequestMessage(HttpMethod.Get, "/api/v1/does-not-exist");
        spanish.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("es-MX"));

        var en = await client.SendAsync(english, Ct);
        var es = await client.SendAsync(spanish, Ct);

        Assert.Equal(HttpStatusCode.NotFound, en.StatusCode);
        Assert.Equal("application/problem+json", en.Content.Headers.ContentType?.MediaType);
        using var enDoc = JsonDocument.Parse(await en.Content.ReadAsStringAsync(Ct));
        using var esDoc = JsonDocument.Parse(await es.Content.ReadAsStringAsync(Ct));
        Assert.Equal("not_found", enDoc.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(enDoc.RootElement.GetProperty("traceId").GetString()));
        Assert.Equal("The requested resource was not found.", enDoc.RootElement.GetProperty("title").GetString());
        Assert.Equal("not_found", esDoc.RootElement.GetProperty("code").GetString());
        Assert.Equal("No se encontró el recurso solicitado.", esDoc.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Unhandled_exception_in_production_hides_exception_details()
    {
        await using var database = new SqliteTestDatabase();
        await using var factory = new ChatXApiFactory(Settings("Sqlite", database.ConnectionString));
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri(ChatXApiFactory.ThrowPath, UriKind.Relative), Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("internal_error", doc.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(ChatXApiFactory.ThrownMessage, body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Metadata_endpoint_is_versioned_and_discloses_no_build_details()
    {
        await using var database = new SqliteTestDatabase();
        await using var factory = new ChatXApiFactory(Settings("Sqlite", database.ConnectionString));
        using var client = factory.CreateClient();

        var meta = await client.GetFromJsonAsync<ApiMetadataResponse>(new Uri("/api/v1/meta", UriKind.Relative), Ct);

        Assert.Equal(new[] { "en", "es" }, meta!.SupportedLanguages);
        Assert.Equal("v1", meta.ApiVersion);
        Assert.Equal("foundation", meta.Stage);
    }

    [Fact]
    public async Task OpenApi_document_is_served_in_development_only()
    {
        await using var database = new SqliteTestDatabase();
        await using var development = new ChatXApiFactory(Settings("Sqlite", database.ConnectionString), "Development");
        await using var production = new ChatXApiFactory(Settings("Sqlite", database.ConnectionString));

        var devResponse = await development.CreateClient().GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Ct);
        var prodResponse = await production.CreateClient().GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, devResponse.StatusCode);
        var document = await devResponse.Content.ReadAsStringAsync(Ct);
        Assert.Contains("/api/v1/meta", document, StringComparison.Ordinal);
        Assert.Contains("Audorix.ChatX API", document, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, prodResponse.StatusCode);
    }

    [Fact]
    public async Task Swagger_ui_is_served_in_development_only_and_points_at_the_openapi_document()
    {
        await using var database = new SqliteTestDatabase();
        await using var development = new ChatXApiFactory(Settings("Sqlite", database.ConnectionString), "Development");
        await using var production = new ChatXApiFactory(Settings("Sqlite", database.ConnectionString));
        using var devClient = development.CreateClient();

        var page = await devClient.GetAsync(new Uri("/swagger/index.html", UriKind.Relative), Ct);
        var initializer = await devClient.GetStringAsync(new Uri("/swagger/index.js", UriKind.Relative), Ct);
        var prodPage = await production.CreateClient().GetAsync(new Uri("/swagger/index.html", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("/openapi/v1.json", initializer, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, prodPage.StatusCode);
    }

    // Invalid provider configuration is verified against the real executable in CommandLineTests
    // (exit code and console output) and exhaustively in ConfigurationValidationTests.
}
