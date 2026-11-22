using Audorix.ChatX.Application.Installations;
using Audorix.ChatX.Domain.Installations;
using Audorix.ChatX.Infrastructure.Persistence;
using Audorix.ChatX.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Audorix.ChatX.IntegrationTests;

/// <summary>
/// Migration and CRUD behaviour against real SQLite and PostgreSQL databases.
/// PostgreSQL cases are skipped (reported as skipped, not passed) when no server is configured.
/// </summary>
public sealed class PersistenceTests
{
    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Migrations_initialize_installation_and_changes_survive_reopening(string provider)
    {
        await using var database = await TestDatabase.CreateAsync(provider);

        Guid id;
        DateTimeOffset initializedAt;
        await using (var services = ChatXServices.Build(provider, database.ConnectionString))
        await using (var scope = services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync(Ct);
            var db = scope.ServiceProvider.GetRequiredService<ChatXDbContext>();

            Assert.True(result.InstallationCreated);
            Assert.Equal(db.Database.GetMigrations(), result.AppliedMigrations);

            var changed = await scope.ServiceProvider.GetRequiredService<InstallationService>().ChangeDefaultLanguageAsync("es", Ct);
            id = changed.Id;
            initializedAt = changed.InitializedAtUtc;
        }

        // A completely new service provider and connection: nothing is cached in memory.
        await using (var services = ChatXServices.Build(provider, database.ConnectionString))
        await using (var scope = services.CreateAsyncScope())
        {
            var reloaded = await scope.ServiceProvider.GetRequiredService<InstallationService>().FindAsync(Ct);

            Assert.NotNull(reloaded);
            Assert.Equal(id, reloaded.Id);
            Assert.Equal("es", reloaded.DefaultLanguage);
            Assert.Equal(initializedAt.ToUnixTimeMilliseconds(), reloaded.InitializedAtUtc.ToUnixTimeMilliseconds());
            Assert.True(reloaded.UpdatedAtUtc >= reloaded.InitializedAtUtc);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Rerunning_migrations_applies_nothing_and_does_not_duplicate_initialization(string provider)
    {
        await using var database = await TestDatabase.CreateAsync(provider);

        Guid firstId;
        await using (var services = ChatXServices.Build(provider, database.ConnectionString))
        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync(Ct);
            firstId = (await scope.ServiceProvider.GetRequiredService<InstallationService>().FindAsync(Ct))!.Id;
        }

        await using (var services = ChatXServices.Build(provider, database.ConnectionString))
        await using (var scope = services.CreateAsyncScope())
        {
            var second = await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync(Ct);
            var db = scope.ServiceProvider.GetRequiredService<ChatXDbContext>();

            Assert.Empty(second.AppliedMigrations);
            Assert.False(second.InstallationCreated);
            Assert.Empty(await db.Database.GetPendingMigrationsAsync(Ct));
            Assert.Equal(1, await db.Installations.CountAsync(Ct));
            Assert.Equal(firstId, (await db.Installations.SingleAsync(Ct)).Id);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Database_constraint_permits_only_one_installation_row(string provider)
    {
        await using var database = await TestDatabase.CreateAsync(provider);
        await using var services = ChatXServices.Build(provider, database.ConnectionString);
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync(Ct);
        var db = scope.ServiceProvider.GetRequiredService<ChatXDbContext>();

        // Bypass the store's pre-check to prove the database itself rejects a second row.
        var intruder = db.Installations.Add(Installation.Initialize(Guid.NewGuid(), "en", DateTimeOffset.UtcNow));
        intruder.Property<int>("Singleton").CurrentValue = 1;
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));

        intruder.State = EntityState.Detached;
        Assert.Equal(1, await db.Installations.AsNoTracking().CountAsync(Ct));
    }

    [Theory]
    [InlineData("Sqlite", "Microsoft.EntityFrameworkCore.Sqlite", "Audorix.ChatX.Persistence.Sqlite")]
    [InlineData("PostgreSql", "Npgsql.EntityFrameworkCore.PostgreSQL", "Audorix.ChatX.Persistence.PostgreSql")]
    public async Task Configured_provider_selects_its_own_migrations_assembly(string provider, string efProvider, string migrationsAssembly)
    {
        // No connection is opened: provider selection and migration discovery are metadata only.
        var connectionString = provider == "Sqlite"
            ? $"Data Source={Path.Combine(Path.GetTempPath(), "chatx-unused.db")}"
            : "Host=127.0.0.1;Port=1;Database=chatx_unused";
        await using var services = ChatXServices.Build(provider, connectionString);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatXDbContext>();

        Assert.Equal(efProvider, db.Database.ProviderName);
        var assembly = db.GetService<IMigrationsAssembly>();
        Assert.Equal(migrationsAssembly, assembly.Assembly.GetName().Name);
        Assert.NotEmpty(assembly.Migrations);
        Assert.All(assembly.Migrations.Values, type => Assert.Equal(migrationsAssembly, type.Assembly.GetName().Name));
        Assert.NotNull(assembly.ModelSnapshot);
        Assert.Equal(migrationsAssembly, assembly.ModelSnapshot!.GetType().Assembly.GetName().Name);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Model_snapshot_has_no_pending_changes(string provider)
    {
        // Detects a model change committed without a migration for either provider.
        var connectionString = provider == "Sqlite"
            ? $"Data Source={Path.Combine(Path.GetTempPath(), "chatx-unused.db")}"
            : "Host=127.0.0.1;Port=1;Database=chatx_unused";
        await using var services = ChatXServices.Build(provider, connectionString);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatXDbContext>();

        Assert.False(db.Database.HasPendingModelChanges(), $"{provider} model has changes without a migration.");
    }
}
