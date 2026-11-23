using System.Text.Json;
using Audorix.ChatX.Api.Setup;
using Audorix.ChatX.Application.Identity;
using Audorix.ChatX.Infrastructure.Persistence;
using Audorix.ChatX.Persistence.PostgreSql;
using Audorix.ChatX.Persistence.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.UnitTests;

public sealed class SetupTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Generated_setup_code_is_grouped_and_verified_case_and_separator_insensitively()
    {
        using var coordinator = new SetupCoordinator(Options.Create(new SetupSettings()));

        Assert.True(coordinator.TokenWasGenerated);
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$", coordinator.DisplayToken);
        Assert.True(coordinator.VerifyToken(coordinator.DisplayToken.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal)));
        Assert.False(coordinator.VerifyToken(null));
        Assert.False(coordinator.VerifyToken("AAAA-AAAA-AAAA"));
        Assert.False(coordinator.VerifyToken(new string('A', 500)));
    }

    [Fact]
    public async Task Settings_file_is_nested_typed_json_that_configuration_reads_back()
    {
        var directory = Path.Combine(Path.GetTempPath(), "chatx-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "nested", "chatx.settings.json");
        try
        {
            await ChatXSettingsFile.WriteAsync(path,
            [
                new("Setup:Completed", true),
                new("Database:Provider", "Sqlite"),
                new("Email:Smtp:Port", 2525),
            ], Ct);

            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path, Ct));
            Assert.Equal(JsonValueKind.True, json.RootElement.GetProperty("Setup").GetProperty("Completed").ValueKind);
            Assert.Equal(2525, json.RootElement.GetProperty("Email").GetProperty("Smtp").GetProperty("Port").GetInt32());
            Assert.False(File.Exists(path + ".tmp"));

            var configuration = new ConfigurationBuilder().AddJsonFile(path).Build();
            Assert.False(ChatXSettingsFile.IsSetupRequired(configuration));
            Assert.Equal("Sqlite", configuration["Database:Provider"]);
            Assert.True(ChatXSettingsFile.IsSetupRequired(new ConfigurationBuilder().Build()));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Providers_build_connection_strings_from_wizard_fields()
    {
        var sqlite = new SqliteDatabaseProvider(Path.GetTempPath()).BuildConnectionString(new DatabaseConnectionParameters(Path: "App_Data/chatx.db"));
        Assert.Equal("Data Source=App_Data/chatx.db", sqlite);

        var postgres = new PostgreSqlDatabaseProvider().BuildConnectionString(
            new DatabaseConnectionParameters(Host: "db", Port: 5433, Database: "chatx", Username: "chatx", Password: "p;w=d", SslMode: "Require"));
        Assert.Empty(new PostgreSqlDatabaseProvider().ValidateConnectionString(postgres));
        var parsed = new Npgsql.NpgsqlConnectionStringBuilder(postgres);
        Assert.Equal(("db", 5433, "chatx", "p;w=d", Npgsql.SslMode.Require), (parsed.Host, parsed.Port, parsed.Database, parsed.Password, parsed.SslMode));
    }

    [Theory]
    [InlineData("root@example.org", "Root", "correct horse battery", null)]
    [InlineData("root@localhost", "Root", "correct horse battery", "email")]
    [InlineData("root@example.org", " ", "correct horse battery", "displayName")]
    [InlineData("root@example.org", "Root", "aaaaaaaaaaaa", "password")]
    [InlineData("root@example.org", "Root", "short", "password")]
    public void Administrator_input_is_validated_without_storage(string email, string name, string password, string? failingField)
    {
        var errors = AccountService.ValidateAdministratorInput(email, name, password, passwordMinimumLength: 10);
        if (failingField is null)
        {
            Assert.Empty(errors);
        }
        else
        {
            Assert.Equal([failingField], errors.Keys);
        }
    }
}
