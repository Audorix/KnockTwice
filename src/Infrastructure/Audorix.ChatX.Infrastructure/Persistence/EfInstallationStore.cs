using Audorix.ChatX.Application.Installations;
using Audorix.ChatX.Domain.Installations;
using Audorix.ChatX.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Audorix.ChatX.Infrastructure.Persistence;

internal sealed class EfInstallationStore(ChatXDbContext db) : IInstallationStore
{
    public Task<Installation?> FindAsync(CancellationToken cancellationToken) =>
        db.Installations.SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> TryAddAsync(Installation installation, CancellationToken cancellationToken)
    {
        if (await db.Installations.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var entry = db.Installations.Add(installation);
        entry.Property<int>(InstallationConfiguration.SingletonColumn).CurrentValue = 1;
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException)
        {
            // Lost a race against another initializer: the unique singleton index rejected the row.
            entry.State = EntityState.Detached;
            if (await db.Installations.AsNoTracking().AnyAsync(cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            throw;
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
