using Audorix.ChatX.Domain.Installations;
using Microsoft.EntityFrameworkCore;

namespace Audorix.ChatX.Infrastructure.Persistence;

/// <summary>
/// The shared logical model. Provider-specific concerns (migrations, column types) live in
/// <c>Audorix.ChatX.Persistence.Sqlite</c> and <c>Audorix.ChatX.Persistence.PostgreSql</c>.
/// </summary>
public sealed class ChatXDbContext(DbContextOptions<ChatXDbContext> options) : DbContext(options)
{
    public DbSet<Installation> Installations => Set<Installation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ChatXDbContext).Assembly);
    }
}
