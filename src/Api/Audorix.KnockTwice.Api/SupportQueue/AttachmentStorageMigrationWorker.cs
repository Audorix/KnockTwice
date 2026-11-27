using System.Security.Cryptography;
using Audorix.KnockTwice.Api.Hosting;
using Audorix.KnockTwice.Application.Attachments;
using Audorix.KnockTwice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Audorix.KnockTwice.Api.SupportQueue;

internal sealed class AttachmentStorageMigrationWorker(IServiceScopeFactory scopes, TimeProvider time,
    ILogger<AttachmentStorageMigrationWorker> logger) : QuietBackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(30);
    private const int MaxAttempts = 5;

    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try { await ProcessOneAsync(stoppingToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { logger.LogWarning(ex, "Attachment storage migration worker iteration failed ({ExceptionType}).", ex.GetType().Name); }
        }
    }

    private async Task ProcessOneAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatXDbContext>();
        var activeProvider = scope.ServiceProvider.GetRequiredService<IAttachmentStorageProviderRegistry>().DefaultProvider;
        var now = time.GetUtcNow();
        await RecoverInterruptedSwitchesAsync(db, activeProvider, now, ct).ConfigureAwait(false);
        await db.AttachmentStorageMigrationJobs
            .Where(x => x.State == "AwaitingRestart" && x.DestinationProvider == activeProvider)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "CutoverComplete").SetProperty(x => x.UpdatedAtUtc, now), ct)
            .ConfigureAwait(false);
        await db.AttachmentStorageMigrationJobs
            .Where(x => x.State == "RollbackAwaitingRestart" && x.SourceProvider == activeProvider)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "RolledBack").SetProperty(x => x.UpdatedAtUtc, now), ct)
            .ConfigureAwait(false);
        var candidate = await db.AttachmentStorageMigrationJobs.AsNoTracking()
            .Where(x => x.State == "Queued" || (x.State == "Running" && (x.LeaseExpiresAtTicks == null || x.LeaseExpiresAtTicks <= now.UtcTicks)))
            .OrderBy(x => x.Id).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (candidate is null) return;
        var token = Guid.NewGuid();
        var updated = await db.AttachmentStorageMigrationJobs.Where(x => x.Id == candidate &&
                (x.State == "Queued" || (x.State == "Running" && (x.LeaseExpiresAtTicks == null || x.LeaseExpiresAtTicks <= now.UtcTicks))))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "Running")
                .SetProperty(x => x.UpdatedAtUtc, now)
                .SetProperty(x => x.LeaseToken, (Guid?)token)
                .SetProperty(x => x.LeaseExpiresAtTicks, (long?)(now + LeaseDuration).UtcTicks), ct).ConfigureAwait(false);
        if (updated != 1) return;

        while (!ct.IsCancellationRequested)
        {
            var job = await db.AttachmentStorageMigrationJobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == candidate && x.LeaseToken == token, ct).ConfigureAwait(false);
            if (job is null || job.State != "Running") return;
            var fileId = await db.AttachmentStorageMigrationFiles.AsNoTracking()
                .Where(x => x.JobId == candidate && (x.State == "Pending" || x.State == "Copying") && x.AvailableAtTicks <= time.GetUtcNow().UtcTicks)
                .OrderBy(x => x.Id).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (fileId is null) { await CompleteIfFinishedAsync(db, candidate.Value, token, ct).ConfigureAwait(false); return; }
            var itemToken = Guid.NewGuid();
            var claimed = await db.AttachmentStorageMigrationFiles.Where(x => x.Id == fileId && x.JobId == candidate
                    && (x.State == "Pending" || x.State == "Copying") && x.AvailableAtTicks <= time.GetUtcNow().UtcTicks)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "Copying")
                    .SetProperty(x => x.Attempts, x => x.Attempts + 1)
                    .SetProperty(x => x.LeaseToken, (Guid?)itemToken)
                    .SetProperty(x => x.LeaseExpiresAtTicks, (long?)(time.GetUtcNow() + LeaseDuration).UtcTicks), ct).ConfigureAwait(false);
            if (claimed != 1) continue;
            var item = await db.AttachmentStorageMigrationFiles.AsNoTracking().SingleAsync(x => x.Id == fileId && x.LeaseToken == itemToken, ct).ConfigureAwait(false);
            try
            {
                var attachment = await db.Attachments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == item.AttachmentId, ct).ConfigureAwait(false);
                if (attachment is null || attachment.ContentRemoved || attachment.Size != item.Size || attachment.Sha256 != item.Sha256
                    || attachment.StorageKey != item.StorageKey || (attachment.StorageProvider is not null && attachment.StorageProvider != job.SourceProvider))
                {
                    await SetFileStateAsync(db, item.Id, itemToken, "Skipped", null, null, ct).ConfigureAwait(false);
                    continue;
                }
                var registry = scope.ServiceProvider.GetRequiredService<IAttachmentStorageProviderRegistry>();
                var source = registry.Resolve(job.SourceProvider);
                var destination = registry.Resolve(job.DestinationProvider);
                await using var input = await source.OpenReadAsync(item.StorageKey, ct).ConfigureAwait(false) ?? throw new IOException("source_file_missing");
                using var hashing = new HashingReadStream(input);
                await destination.PutAsync(item.StorageKey, hashing, item.Size, item.ContentType, ct).ConfigureAwait(false);
                if (hashing.BytesRead != item.Size || !string.Equals(Convert.ToHexStringLower(hashing.Finish()), item.Sha256, StringComparison.Ordinal))
                    throw new IOException("source_content_mismatch");
                await using var copied = await destination.OpenReadAsync(item.StorageKey, ct).ConfigureAwait(false) ?? throw new IOException("destination_file_missing");
                var (bytes, hash) = await HashAsync(copied, ct).ConfigureAwait(false);
                if (bytes != item.Size || !string.Equals(hash, item.Sha256, StringComparison.Ordinal)) throw new IOException("destination_verification_failed");
                await SetFileStateAsync(db, item.Id, itemToken, "Copied", time.GetUtcNow(), null, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var attempts = item.Attempts;
                var terminal = attempts >= MaxAttempts;
                var error = (ex.GetType().Name + ": " + ex.Message);
                if (error.Length > 1000) error = error[..1000];
                var retryAt = time.GetUtcNow() + TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, attempts)));
                await SetFileStateAsync(db, item.Id, itemToken, terminal ? "Failed" : "Pending", null, error, ct, terminal ? 0 : retryAt.UtcTicks).ConfigureAwait(false);
                logger.LogWarning("Attachment migration file {FileId} failed on attempt {Attempt}.", item.Id, attempts);
                if (!terminal)
                {
                    await db.AttachmentStorageMigrationJobs.Where(x => x.Id == candidate && x.LeaseToken == token && x.State == "Running")
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseExpiresAtTicks, (long?)retryAt.UtcTicks), ct).ConfigureAwait(false);
                    return;
                }
            }
        }
    }

    private static async Task RecoverInterruptedSwitchesAsync(ChatXDbContext db, string activeProvider, DateTimeOffset now, CancellationToken ct)
    {
        var interrupted = await db.AttachmentStorageMigrationJobs.AsNoTracking()
            .Where(x => x.State == "CuttingOver" || x.State == "RollbackCuttingOver")
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var job in interrupted)
        {
            if (job.State == "CuttingOver" && activeProvider == job.SourceProvider)
            {
                await db.AttachmentStorageMigrationJobs.Where(x => x.Id == job.Id && x.State == "CuttingOver")
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "Completed").SetProperty(x => x.UpdatedAtUtc, now), ct).ConfigureAwait(false);
                continue;
            }
            if (job.State == "CuttingOver" && activeProvider == job.DestinationProvider)
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                await MoveProviderReferencesAsync(db, job.SourceProvider, job.DestinationProvider, ct).ConfigureAwait(false);
                await db.AttachmentStorageMigrationJobs.Where(x => x.Id == job.Id && x.State == "CuttingOver")
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "CutoverComplete").SetProperty(x => x.UpdatedAtUtc, now), ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                continue;
            }
            if (job.State == "RollbackCuttingOver" && activeProvider == job.DestinationProvider)
            {
                await db.AttachmentStorageMigrationJobs.Where(x => x.Id == job.Id && x.State == "RollbackCuttingOver")
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "CutoverComplete").SetProperty(x => x.UpdatedAtUtc, now), ct).ConfigureAwait(false);
                continue;
            }
            if (job.State == "RollbackCuttingOver" && activeProvider == job.SourceProvider)
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                await MoveProviderReferencesAsync(db, job.DestinationProvider, job.SourceProvider, ct).ConfigureAwait(false);
                await db.AttachmentStorageMigrationJobs.Where(x => x.Id == job.Id && x.State == "RollbackCuttingOver")
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "RolledBack").SetProperty(x => x.UpdatedAtUtc, now), ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }
        }
    }

    private static Task<int> MoveProviderReferencesAsync(ChatXDbContext db, string from, string to, CancellationToken ct) =>
        db.Attachments.Where(a => (a.StorageProvider == from || a.StorageProvider == null)
                && !a.ContentRemoved && (a.Status == Domain.Attachments.AttachmentStatus.Ready || a.Status == Domain.Attachments.AttachmentStatus.Quarantined))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.StorageProvider, to), ct);

    private async Task CompleteIfFinishedAsync(ChatXDbContext db, Guid jobId, Guid token, CancellationToken ct)
    {
        var remaining = await db.AttachmentStorageMigrationFiles.AnyAsync(x => x.JobId == jobId && (x.State == "Pending" || x.State == "Copying"), ct).ConfigureAwait(false);
        if (remaining)
        {
            var nextRetry = await db.AttachmentStorageMigrationFiles.Where(x => x.JobId == jobId && x.State == "Pending")
                .Select(x => (long?)x.AvailableAtTicks).MinAsync(ct).ConfigureAwait(false);
            if (nextRetry is not null)
                await db.AttachmentStorageMigrationJobs.Where(x => x.Id == jobId && x.LeaseToken == token && x.State == "Running")
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseExpiresAtTicks, nextRetry), ct).ConfigureAwait(false);
            return;
        }
        var failed = await db.AttachmentStorageMigrationFiles.AnyAsync(x => x.JobId == jobId && x.State == "Failed", ct).ConfigureAwait(false);
        await db.AttachmentStorageMigrationJobs.Where(x => x.Id == jobId && x.LeaseToken == token && x.State == "Running")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, failed ? "CompletedWithErrors" : "Completed")
                .SetProperty(x => x.UpdatedAtUtc, time.GetUtcNow())
                .SetProperty(x => x.LeaseToken, (Guid?)null).SetProperty(x => x.LeaseExpiresAtTicks, (long?)null), ct).ConfigureAwait(false);
    }

    private static Task<int> SetFileStateAsync(ChatXDbContext db, Guid id, Guid token, string state, DateTimeOffset? copiedAt,
        string? error, CancellationToken ct, long availableAtTicks = 0) =>
        db.AttachmentStorageMigrationFiles.Where(x => x.Id == id && x.LeaseToken == token)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, state).SetProperty(x => x.CopiedAtUtc, copiedAt)
                .SetProperty(x => x.LastError, error).SetProperty(x => x.AvailableAtTicks, availableAtTicks)
                .SetProperty(x => x.LeaseToken, (Guid?)null).SetProperty(x => x.LeaseExpiresAtTicks, (long?)null), ct);

    private static async Task<(long Bytes, string Hash)> HashAsync(Stream input, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024]; long bytes = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0) { hash.AppendData(buffer, 0, read); bytes += read; }
        return (bytes, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    private sealed class HashingReadStream(Stream inner) : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        public long BytesRead { get; private set; }
        public byte[] Finish() => _hash.GetHashAndReset();
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { var n = inner.Read(buffer, offset, count); _hash.AppendData(buffer, offset, n); BytesRead += n; return n; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) { var n = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false); _hash.AppendData(buffer.Span[..n]); BytesRead += n; return n; }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _hash.Dispose(); base.Dispose(disposing); }
    }
}
