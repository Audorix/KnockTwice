using Audorix.ChatX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Audorix.ChatX.Persistence.PostgreSql;

/// <summary>
/// Used only by <c>dotnet ef</c> when this project is the startup project. <c>migrations add</c>
/// does not connect, so no PostgreSQL server (and no SQLite) is required to generate migrations.
/// The placeholder connection string contains no credentials.
/// </summary>
internal sealed class PostgreSqlDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ChatXDbContext>
{
    public ChatXDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CHATX_DESIGN_POSTGRES")
            ?? "Host=localhost;Database=chatx_design_time";
        var builder = new DbContextOptionsBuilder<ChatXDbContext>();
        new PostgreSqlDatabaseProvider().Configure(builder, connectionString);
        return new ChatXDbContext(builder.Options);
    }
}
