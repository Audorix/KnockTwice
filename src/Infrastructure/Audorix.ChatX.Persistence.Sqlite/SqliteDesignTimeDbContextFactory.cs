using Audorix.ChatX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Audorix.ChatX.Persistence.Sqlite;

/// <summary>
/// Used only by <c>dotnet ef</c> when this project is the startup project. It never opens
/// the database for <c>migrations add</c>, and does not need PostgreSQL to be available.
/// </summary>
internal sealed class SqliteDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ChatXDbContext>
{
    public ChatXDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CHATX_DESIGN_SQLITE")
            ?? "Data Source=chatx-design-time.db";
        var builder = new DbContextOptionsBuilder<ChatXDbContext>();
        new SqliteDatabaseProvider(Directory.GetCurrentDirectory()).Configure(builder, connectionString);
        return new ChatXDbContext(builder.Options);
    }
}
