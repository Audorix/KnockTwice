using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Audorix.KnockTwice.IntegrationTests.Support;
using Npgsql;
using static Audorix.KnockTwice.IntegrationTests.Support.ApiSession;

namespace Audorix.KnockTwice.IntegrationTests;

/// <summary>
/// The setup wizard API: setup mode gating, the setup code, validation that writes nothing,
/// and a complete setup whose settings file the normal host then starts from and whose
/// administrator can sign in, on both providers.
/// </summary>
public sealed class SetupTests : IDisposable
{
    private const string Token = "TEST-SETUP-CODE-0001";
    private const string AdminPassword = "correct horse battery";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "chatx-tests", Guid.NewGuid().ToString("N"));

    public SetupTests() => Directory.CreateDirectory(_directory);

    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string SettingsPath => Path.Combine(_directory, "chatx.settings.json");

    [Fact]
    public async Task Setup_mode_answers_only_setup_and_health_and_requires_the_setup_code()
    {
        await using var api = SetupHost();
        using var client = api.CreateHttpsClient();

        var status = await JsonAsync(await client.GetAsync(new Uri("/api/v1/setup/status", UriKind.Relative), Ct));
        Assert.True(status.GetProperty("required").GetBoolean());

        var other = await client.GetAsync(new Uri("/api/v1/auth/config", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, other.StatusCode);
        Assert.Equal("setup_required", await ErrorCodeAsync(other));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/health/live", UriKind.Relative), Ct)).StatusCode);
        var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal("setup_required", (await JsonAsync(ready)).GetProperty("status").GetString());

        var noToken = await client.GetAsync(new Uri("/api/v1/setup/configuration", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
        Assert.Equal("setup_token_invalid", await ErrorCodeAsync(noToken));
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, "/api/v1/setup/token", new { }, token: "WRONG-CODE-0000")).StatusCode);

        // Codes are accepted case-insensitively and without separators.
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, "/api/v1/setup/token", new { }, token: "test setup code 0001")).StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/setup/configuration");
        request.Headers.Add("X-Setup-Token", Token);
        var configuration = await JsonAsync(await client.SendAsync(request, Ct));
        Assert.False(configuration.GetProperty("database").GetProperty("locked").GetBoolean());
        Assert.Equal(10, configuration.GetProperty("passwordMinimumLength").GetInt32());
        Assert.DoesNotContain("password\"", configuration.GetRawText().ToLowerInvariant(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Swagger_describes_the_setup_api_in_development_setup_mode_only()
    {
        await using var development = SetupHost(new() { ["OpenApi:Enabled"] = "true" });
        using var client = development.CreateHttpsClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/swagger/index.html", UriKind.Relative), Ct)).StatusCode);
        var document = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), Ct);
        Assert.Contains("/api/v1/setup/complete", document, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/v1/auth", document, StringComparison.Ordinal);

        await using var production = SetupHost();
        using var productionClient = production.CreateHttpsClient();
        Assert.Equal(HttpStatusCode.NotFound, (await productionClient.GetAsync(new Uri("/swagger/index.html", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task Invalid_input_is_reported_per_field_and_writes_nothing()
    {
        await using var api = SetupHost();
        using var client = api.CreateHttpsClient();

        var response = await SendAsync(client, "/api/v1/setup/complete", Request(
            new { provider = "Sqlite", sqlite = new { path = "" } },
            administrator: new { email = "not-an-email", displayName = "Root", password = "short" },
            publicBaseUrl: "ftp://chat"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await JsonAsync(response)).GetProperty("errors");
        Assert.Equal("required", errors.GetProperty("database.path")[0].GetString());
        Assert.Equal("url_invalid", errors.GetProperty("options.publicBaseUrl")[0].GetString());
        Assert.False(File.Exists(SettingsPath));

        // Administrator fields are checked before any database is touched.
        var dbPath = Path.Combine(_directory, "never.db");
        var admin = await SendAsync(client, "/api/v1/setup/complete", Request(
            new { provider = "Sqlite", sqlite = new { path = dbPath } },
            administrator: new { email = "not-an-email", displayName = "Root", password = "short" }));
        var adminErrors = (await JsonAsync(admin)).GetProperty("errors");
        Assert.Equal("email_invalid", adminErrors.GetProperty("administrator.email")[0].GetString());
        Assert.Equal("password_too_short", adminErrors.GetProperty("administrator.password")[0].GetString());
        Assert.False(File.Exists(dbPath));
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public async Task Unreachable_postgresql_server_is_reported_without_provider_details()
    {
        await using var api = SetupHost();
        using var client = api.CreateHttpsClient();

        var response = await SendAsync(client, "/api/v1/setup/database/test", new
        {
            provider = "PostgreSql",
            postgreSql = new { host = "127.0.0.1", port = 1, database = "chatx", username = "chatx", password = "Secret-Pa55" },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Equal("setup_database_unavailable", await ErrorCodeAsync(response));
        Assert.Contains("unreachable", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret-Pa55", body, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", body, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Complete_setup_migrates_creates_the_administrator_and_the_normal_host_starts_from_the_settings_file(string provider)
    {
        await using var target = await SetupTarget.CreateAsync(provider, _directory);
        await using (var api = SetupHost())
        {
            using var client = api.CreateHttpsClient();
            var test = await SendAsync(client, "/api/v1/setup/database/test", target.Input);
            AssertStatus(HttpStatusCode.OK, test, await test.Content.ReadAsStringAsync(Ct));
            Assert.Equal("will_be_created", (await JsonAsync(test)).GetProperty("result").GetString());

            var complete = await SendAsync(client, "/api/v1/setup/complete", Request(target.Input, developmentMode: true));
            AssertStatus(HttpStatusCode.OK, complete, await complete.Content.ReadAsStringAsync(Ct));
            Assert.Equal("root@example.org", (await JsonAsync(complete)).GetProperty("administratorEmail").GetString());

            // The same process now reports completion and refuses a second run.
            var status = await JsonAsync(await client.GetAsync(new Uri("/api/v1/setup/status", UriKind.Relative), Ct));
            Assert.False(status.GetProperty("required").GetBoolean());
            var again = await SendAsync(client, "/api/v1/setup/complete", Request(target.Input));
            Assert.Equal("setup_already_completed", await ErrorCodeAsync(again));
        }

        Assert.False(File.Exists(Path.Combine(_directory, "setup-code.txt")), "The setup code file must be removed after setup.");
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(SettingsPath, Ct));
        var root = settings.RootElement;
        Assert.True(root.GetProperty("Setup").GetProperty("Completed").GetBoolean());
        Assert.Equal(provider, root.GetProperty("Database").GetProperty("Provider").GetString());
        Assert.False(root.GetProperty("Identity").GetProperty("RequireConfirmedEmail").GetBoolean());
        Assert.True(root.GetProperty("Identity").GetProperty("AutoConfirmEmail").GetBoolean());
        Assert.Equal("localhost", root.GetProperty("Email").GetProperty("Smtp").GetProperty("Host").GetString());
        Assert.False(root.GetProperty("Setup").TryGetProperty("Token", out _));
        Assert.DoesNotContain(AdminPassword, settings.RootElement.GetRawText(), StringComparison.Ordinal);

        // Normal mode, configured only by the settings file the wizard wrote.
        await using var normal = new ChatXApiFactory(new Dictionary<string, string?>
        {
            ["Setup:Completed"] = null,
            ["Setup:SettingsPath"] = SettingsPath,
            ["App:PublicBaseUrl"] = null,
        });
        using var browser = new ApiSession(normal.CreateHttpsClient());
        var normalStatus = await JsonAsync(await browser.Client.GetAsync(new Uri("/api/v1/setup/status", UriKind.Relative), Ct));
        Assert.False(normalStatus.GetProperty("required").GetBoolean());

        AssertStatus(HttpStatusCode.OK, await browser.LoginAsync("root@example.org", AdminPassword));
        var installation = await browser.Client.GetAsync(new Uri("/api/v1/admin/installation", UriKind.Relative), Ct);
        AssertStatus(HttpStatusCode.OK, installation, await installation.Content.ReadAsStringAsync(Ct));

        // Development mode: a new registration is confirmed immediately and sends no email.
        using var visitor = new ApiSession(normal.CreateHttpsClient());
        var register = await visitor.PostAsync("/api/v1/auth/register",
            new { email = "dev@example.org", password = AdminPassword, displayName = "Dev User", language = "en", captchaToken = "t" });
        AssertStatus(HttpStatusCode.Accepted, register);
        Assert.Empty(normal.Emails.Sent);
        AssertStatus(HttpStatusCode.OK, await visitor.LoginAsync("dev@example.org", AdminPassword));
        var config = await JsonAsync(await visitor.Client.GetAsync(new Uri("/api/v1/auth/config", UriKind.Relative), Ct));
        Assert.True(config.GetProperty("autoConfirmEmail").GetBoolean());
    }

    [Fact]
    public async Task Setup_refuses_a_database_that_already_has_an_administrator()
    {
        await using var target = await SetupTarget.CreateAsync("Sqlite", _directory);
        await using (var first = SetupHost())
        {
            using var client = first.CreateHttpsClient();
            AssertStatus(HttpStatusCode.OK, await SendAsync(client, "/api/v1/setup/complete", Request(target.Input)));
        }

        File.Delete(SettingsPath);
        await using var second = SetupHost();
        using var again = second.CreateHttpsClient();
        var response = await SendAsync(again, "/api/v1/setup/complete", Request(target.Input,
            administrator: new { email = "other@example.org", displayName = "Other", password = AdminPassword }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("setup_administrator_exists", await ErrorCodeAsync(response));
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public async Task Database_configured_by_a_higher_precedence_source_is_locked()
    {
        await using var target = await SetupTarget.CreateAsync("Sqlite", _directory);
        await using var api = SetupHost(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:ConnectionString"] = $"Data Source={Path.Combine(_directory, "locked.db")}",
        });
        using var client = api.CreateHttpsClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/setup/configuration");
        request.Headers.Add("X-Setup-Token", Token);
        var configuration = await JsonAsync(await client.SendAsync(request, Ct));
        Assert.True(configuration.GetProperty("database").GetProperty("locked").GetBoolean());
        Assert.Equal("Sqlite", configuration.GetProperty("database").GetProperty("provider").GetString());

        // The wizard's database input is ignored; the locked database is used and not written.
        AssertStatus(HttpStatusCode.OK, await SendAsync(client, "/api/v1/setup/complete", Request(target.Input)));
        Assert.True(File.Exists(Path.Combine(_directory, "locked.db")));
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(SettingsPath, Ct));
        Assert.False(settings.RootElement.TryGetProperty("Database", out _));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    private ChatXApiFactory SetupHost(Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Setup:Completed"] = "false",
            ["Setup:Token"] = Token,
            ["Setup:RestartAfterCompletion"] = "false",
            ["Setup:SettingsPath"] = SettingsPath,
            ["App:PublicBaseUrl"] = null,
        };
        foreach (var (key, value) in extra ?? [])
        {
            settings[key] = value;
        }

        return new ChatXApiFactory(settings);
    }

    private static object Request(
        object database,
        object? administrator = null,
        string publicBaseUrl = "https://chat.test",
        bool developmentMode = false) => new
    {
        database,
        email = new
        {
            provider = "Smtp",
            fromAddress = "no-reply@chat.test",
            fromName = "ChatX",
            smtp = new { host = "localhost", port = 1025, security = "None" },
        },
        administrator = administrator ?? new { email = "root@example.org", displayName = "Root Admin", password = AdminPassword },
        options = new { developmentMode, publicBaseUrl, defaultLanguage = "en", captchaProvider = "None" },
    };

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, object body, string token = Token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Setup-Token", token);
        return await client.SendAsync(request, Ct);
    }

    /// <summary>A database the wizard creates: a new SQLite file, or a not-yet-existing PostgreSQL database that is dropped afterwards.</summary>
    private sealed class SetupTarget : IAsyncDisposable
    {
        private readonly string? _postgresAdmin;
        private readonly string? _postgresDatabase;

        private SetupTarget(object input, string? postgresAdmin = null, string? postgresDatabase = null)
        {
            Input = input;
            _postgresAdmin = postgresAdmin;
            _postgresDatabase = postgresDatabase;
        }

        public object Input { get; }

        public static Task<SetupTarget> CreateAsync(string provider, string directory)
        {
            if (provider == "Sqlite")
            {
                return Task.FromResult(new SetupTarget(new { provider, sqlite = new { path = Path.Combine(directory, "setup.db") } }));
            }

            var admin = PostgreSqlTestDatabase.AdminConnectionString;
            if (string.IsNullOrWhiteSpace(admin))
            {
                Assert.Skip($"PostgreSQL integration tests need {TestDatabase.PostgresEnvironmentVariable}.");
            }

            var parsed = new NpgsqlConnectionStringBuilder(admin);
            var name = "chatx_test_" + Guid.NewGuid().ToString("N");
            return Task.FromResult(new SetupTarget(
                new
                {
                    provider,
                    postgreSql = new { host = parsed.Host, port = parsed.Port, database = name, username = parsed.Username, password = parsed.Password, sslMode = "Prefer" },
                },
                admin,
                name));
        }

        public async ValueTask DisposeAsync()
        {
            if (_postgresAdmin is null)
            {
                return;
            }

            NpgsqlConnection.ClearAllPools();
            await using var connection = new NpgsqlConnection(_postgresAdmin);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_postgresDatabase}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
