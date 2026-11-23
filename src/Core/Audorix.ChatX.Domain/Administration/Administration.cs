using Audorix.ChatX.Domain.Organizations;

namespace Audorix.ChatX.Domain.Administration;

/// <summary>
/// One audit record (Phase 9, owner decision: administrative and security events, kept one year by
/// default). It names who did what to which target, from where and with what outcome. It never
/// contains message text, file content, tokens, passwords or secret configuration values.
/// </summary>
public sealed class AuditEntry
{
    public const int ActionMaxLength = 64;
    public const int DetailsMaxLength = 2000;

    private AuditEntry(Guid id, DateTimeOffset occurredAtUtc, string action, bool succeeded)
    {
        Id = id;
        OccurredAtUtc = occurredAtUtc;
        Action = action;
        Succeeded = succeeded;
    }

    public Guid Id { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    /// <summary>Dotted, stable name such as <c>auth.sign_in</c> or <c>admin.user.disabled</c>.</summary>
    public string Action { get; private set; }

    public bool Succeeded { get; private set; }

    /// <summary>Who acted; null for anonymous or unknown callers (e.g. a failed sign-in for no account).</summary>
    public Guid? ActorUserId { get; private set; }

    /// <summary><c>user</c>, <c>organization</c>, <c>setting</c>, <c>session</c>…</summary>
    public string? TargetType { get; private set; }

    public string? TargetId { get; private set; }

    public string? IpAddress { get; private set; }

    /// <summary>Small JSON object of non-sensitive facts (setting names, role names, reasons).</summary>
    public string? Details { get; private set; }

    public static AuditEntry Create(
        Guid id, DateTimeOffset occurredAtUtc, string action, bool succeeded, Guid? actorUserId, string? targetType, string? targetId, string? ipAddress, string? details)
    {
        var name = action?.Trim() ?? string.Empty;
        if (name.Length is 0 or > ActionMaxLength || name.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '_')))
        {
            throw new DomainRuleException("value_invalid", "An audit action is a dotted lower-case name.");
        }

        return new AuditEntry(Require.Id(id), Require.Utc(occurredAtUtc), name, succeeded)
        {
            ActorUserId = actorUserId,
            TargetType = Truncate(targetType, 32),
            TargetId = Truncate(targetId, 128),
            IpAddress = Truncate(ipAddress, 45),
            Details = Truncate(details, DetailsMaxLength),
        };
    }

    private static string? Truncate(string? value, int length) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= length ? value : value[..length];
}

/// <summary>
/// A live installation setting stored in the database (Phase 9, owner decision): feature switches,
/// limits and languages that administrators change without a restart. Environment variables and
/// command-line arguments still take precedence over these values.
/// </summary>
public sealed class InstallationSetting
{
    public const int KeyMaxLength = 200;
    public const int ValueMaxLength = 4000;

    private InstallationSetting(string key, string? value, DateTimeOffset updatedAtUtc, Guid? updatedByUserId)
    {
        Key = key;
        Value = value;
        UpdatedAtUtc = updatedAtUtc;
        UpdatedByUserId = updatedByUserId;
    }

    /// <summary>Configuration key, e.g. <c>Attachments:MaxFileSize</c>.</summary>
    public string Key { get; private set; }

    /// <summary>The value as configuration text; lists are stored as a JSON array.</summary>
    public string? Value { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid? UpdatedByUserId { get; private set; }

    public static InstallationSetting Create(string key, string? value, DateTimeOffset nowUtc, Guid? byUserId)
    {
        var setting = new InstallationSetting(RequireKey(key), null, Require.Utc(nowUtc), byUserId);
        setting.Change(value, nowUtc, byUserId);
        return setting;
    }

    public void Change(string? value, DateTimeOffset nowUtc, Guid? byUserId)
    {
        if (value is { Length: > ValueMaxLength })
        {
            throw new DomainRuleException("value_invalid", "The setting value is too long.");
        }

        Value = value;
        UpdatedAtUtc = Require.Utc(nowUtc);
        UpdatedByUserId = byUserId;
    }

    private static string RequireKey(string key) =>
        !string.IsNullOrWhiteSpace(key) && key.Length <= KeyMaxLength && !key.Any(char.IsWhiteSpace)
            ? key
            : throw new DomainRuleException("value_invalid", "A setting key is required.");
}
