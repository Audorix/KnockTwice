using Audorix.KnockTwice.Application.Attachments;
using Microsoft.EntityFrameworkCore;

namespace Audorix.KnockTwice.Infrastructure.Persistence;

internal sealed class EfStorageMigrationGate(ChatXDbContext db) : IStorageMigrationGate
{
    private static readonly string[] ClosedStates = ["CuttingOver", "AwaitingRestart", "RollbackCuttingOver", "RollbackAwaitingRestart"];

    public Task<bool> IsClosedAsync(CancellationToken cancellationToken) =>
        db.AttachmentStorageMigrationJobs.AsNoTracking().AnyAsync(job => ClosedStates.Contains(job.State), cancellationToken);
}
