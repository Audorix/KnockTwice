namespace Audorix.KnockTwice.Infrastructure.Persistence;

public sealed class AttachmentStorageMigrationJob
{
    public Guid Id { get; set; }
    public string SourceProvider { get; set; } = "Local";
    public string DestinationProvider { get; set; } = "S3";
    public string State { get; set; } = "Queued";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public Guid? LeaseToken { get; set; }
    public long? LeaseExpiresAtTicks { get; set; }
    public string? LastError { get; set; }
}

public sealed class AttachmentStorageMigrationFile
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public Guid AttachmentId { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string State { get; set; } = "Pending";
    public int Attempts { get; set; }
    public long AvailableAtTicks { get; set; }
    public Guid? LeaseToken { get; set; }
    public long? LeaseExpiresAtTicks { get; set; }
    public DateTimeOffset? CopiedAtUtc { get; set; }
    public string? LastError { get; set; }
}
