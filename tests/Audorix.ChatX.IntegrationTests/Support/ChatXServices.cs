using Audorix.ChatX.Application;
using Audorix.ChatX.Infrastructure;
using Audorix.ChatX.Infrastructure.Persistence;
using Audorix.ChatX.Persistence.PostgreSql;
using Audorix.ChatX.Persistence.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Audorix.ChatX.IntegrationTests.Support;

/// <summary>
/// Builds the same Application + Infrastructure + provider registrations as the API host,
/// without HTTP. Each call is an independent "process" over the same database, which is
/// how the tests prove data survives reopening.
/// </summary>
internal static class ChatXServices
{
    public static ServiceProvider Build(string provider, string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = provider,
                ["Database:ConnectionString"] = connectionString,
                ["Localization:DefaultLanguage"] = "en",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddOptions<Application.Localization.LocalizationSettings>().BindConfiguration("Localization");
        services.AddChatXApplication();
        services.AddChatXInfrastructure();
        services.AddSingleton<IDatabaseProvider>(new SqliteDatabaseProvider(AppContext.BaseDirectory));
        services.AddSingleton<IDatabaseProvider, PostgreSqlDatabaseProvider>();
        // Account services (Identity, localization) are not registered in this persistence-only
        // container, so validation is limited to scopes; the API tests cover the full graph.
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });
    }
}
