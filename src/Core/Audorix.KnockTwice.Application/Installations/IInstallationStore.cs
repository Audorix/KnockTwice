using Audorix.KnockTwice.Domain.Installations;

namespace Audorix.KnockTwice.Application.Installations;

/// <summary>
/// Persistence port for the single installation record. Implemented by Infrastructure.
/// </summary>
public interface IInstallationStore
{
    /// <summary>Returns the tracked installation record, or <see langword="null"/> before initialization.</summary>
    Task<Installation?> FindAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persists a new installation record. Returns <see langword="false"/> when another
    /// record already exists (the store enforces a single row with a unique constraint).
    /// </summary>
    Task<bool> TryAddAsync(Installation installation, CancellationToken cancellationToken);

    /// <summary>Saves changes to a record previously returned by <see cref="FindAsync"/>.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
