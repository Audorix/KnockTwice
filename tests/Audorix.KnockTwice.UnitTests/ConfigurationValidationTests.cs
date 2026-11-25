using Audorix.KnockTwice.Application.Localization;
using Audorix.KnockTwice.Infrastructure.Persistence;
using Audorix.KnockTwice.Persistence.PostgreSql;
using Audorix.KnockTwice.Persistence.Sqlite;

namespace Audorix.KnockTwice.UnitTests;

public sealed class ConfigurationValidationTests
{
    private const string Secret = "S3cr3t-Pa55word!";

    private static DatabaseOptionsValidator CreateValidator() =>
        new([new SqliteDatabaseProvider(Path.GetTempPath()), new PostgreSqlDatabaseProvider()]);

    [Theory]
    [InlineData("Sqlite", "Data Source=chatx.db")]
    [InlineData("sqlite", "Data Source=chatx.db")]
    [InlineData("PostgreSql", "Host=db;Database=chatx;Username=chatx;Password=x")]
    [InlineData("POSTGRESQL", "Host=db;Database=chatx")]
    public void Accepts_explicit_provider_names(string provider, string connectionString)
    {
        var result = CreateValidator().Validate(null, new DatabaseOptions { Provider = provider, ConnectionString = connectionString });
        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_provider_fails_instead_of_falling_back(string? provider)
    {
        var result = CreateValidator().Validate(null, new DatabaseOptions { Provider = provider, ConnectionString = "Data Source=chatx.db" });

        Assert.True(result.Failed);
        Assert.Contains("Database:Provider is required", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Sqlite, PostgreSql", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("Postgres")]
    [InlineData("1")]
    public void Unknown_provider_fails_and_lists_supported_values(string provider)
    {
        var result = CreateValidator().Validate(null, new DatabaseOptions
        {
            Provider = provider,
            ConnectionString = $"Host=db;Database=chatx;Password={Secret}",
        });

        Assert.True(result.Failed);
        Assert.Contains($"'{provider}' is not supported", result.FailureMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Connection_string_pasted_into_provider_setting_is_not_echoed()
    {
        var result = CreateValidator().Validate(null, new DatabaseOptions
        {
            Provider = $"Host=db;Password={Secret}",
            ConnectionString = "Data Source=chatx.db",
        });

        Assert.True(result.Failed);
        Assert.DoesNotContain(Secret, result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("<redacted>", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Sqlite", "Data Source=:memory:")]
    [InlineData("Sqlite", "Mode=Memory;Data Source=x")]
    [InlineData("Sqlite", "Data Source=")]
    [InlineData("Sqlite", "NotAKey=1;Password=" + Secret)]
    [InlineData("PostgreSql", "Host=db;Password=" + Secret)]
    [InlineData("PostgreSql", "Database=chatx;Password=" + Secret)]
    [InlineData("PostgreSql", "Bogus Keyword=" + Secret)]
    [InlineData("PostgreSql", "")]
    public void Invalid_connection_strings_fail_without_leaking_credentials(string provider, string connectionString)
    {
        var result = CreateValidator().Validate(null, new DatabaseOptions { Provider = provider, ConnectionString = connectionString });

        Assert.True(result.Failed);
        Assert.Contains("Database:ConnectionString", result.FailureMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_requires_default_language_among_supported_languages()
    {
        var settings = new LocalizationSettings { DefaultLanguage = "es" };
        settings.SupportedLanguages.Add("en");

        var errors = settings.Validate();

        Assert.Single(errors);
        Assert.Contains("Localization:DefaultLanguage", errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_rejects_languages_without_resources()
    {
        var settings = new LocalizationSettings { DefaultLanguage = "en" };
        settings.SupportedLanguages.Add("en");
        settings.SupportedLanguages.Add("fr");

        var errors = settings.Validate();

        Assert.Contains(errors, e => e.Contains("'fr'", StringComparison.Ordinal));
    }

    [Fact]
    public void Localization_defaults_to_all_available_languages()
    {
        var settings = new LocalizationSettings();

        Assert.Empty(settings.Validate());
        Assert.Equal(["en", "es"], settings.EffectiveSupportedLanguages);
    }
}
