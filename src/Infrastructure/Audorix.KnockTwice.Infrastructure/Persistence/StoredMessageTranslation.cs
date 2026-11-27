namespace Audorix.KnockTwice.Infrastructure.Persistence;

/// <summary>Saved translation snapshot. The source message remains authoritative and unchanged.</summary>
public sealed class StoredMessageTranslation
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public Guid ConversationId { get; set; }
    public string TargetLanguage { get; set; } = string.Empty;
    public string SourceSha256 { get; set; } = string.Empty;
    public string OriginalText { get; set; } = string.Empty;
    public string TranslatedText { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
