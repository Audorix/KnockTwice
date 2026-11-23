using System.Text.Json;
using Audorix.ChatX.Domain.Administration;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Application.Administration;

/// <summary>Audit log policy (owner decision: one year by default), bound from <c>Audit</c>.</summary>
public sealed class AuditSettings
{
    public const string SectionName = "Audit";

    public int RetentionDays { get; set; } = 365;

    public IReadOnlyList<string> Validate() =>
        RetentionDays is < 7 or > 3650 ? ["Audit:RetentionDays must be between 7 and 3650."] : [];
}

/// <summary>Request facts the audit log records (supplied by the host).</summary>
public interface IAuditContext
{
    string? IpAddress { get; }
}

public sealed record AuditQuery(string? Action, Guid? ActorUserId, string? TargetId, bool? Succeeded, DateTimeOffset? Before, int Limit);

public interface IAuditStore
{
    void Add(AuditEntry entry);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken);

    /// <summary>Deletes up to <paramref name="batch"/> entries older than <paramref name="cutoff"/>; returns how many.</summary>
    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, int batch, CancellationToken cancellationToken);
}

/// <summary>Well-known audit actions. Names are part of the operator-facing log; add, never rename.</summary>
public static class AuditActions
{
    public const string SignIn = "auth.sign_in";
    public const string SignOut = "auth.sign_out";
    public const string Registered = "auth.registered";
    public const string EmailConfirmed = "auth.email_confirmed";
    public const string PasswordChanged = "auth.password_changed";
    public const string PasswordReset = "auth.password_reset";
    public const string SessionRevoked = "auth.session_revoked";
    public const string OtherSessionsRevoked = "auth.other_sessions_revoked";
    public const string UserBlocked = "contacts.blocked";
    public const string UserUnblocked = "contacts.unblocked";

    public const string SettingsChanged = "admin.settings.changed";
    public const string ProviderChanged = "admin.provider.changed";
    public const string ProviderTested = "admin.provider.tested";
    public const string UserDisabled = "admin.user.disabled";
    public const string UserEnabled = "admin.user.enabled";
    public const string UserSessionsRevoked = "admin.user.sessions_revoked";
    public const string UserEmailConfirmed = "admin.user.email_confirmed";
    public const string UserConfirmationSent = "admin.user.confirmation_sent";
    public const string RoleGranted = "admin.user.role_granted";
    public const string RoleRevoked = "admin.user.role_revoked";
    public const string OrganizationRenamed = "admin.organization.renamed";
    public const string OrganizationMemberRoleChanged = "admin.organization.member_role_changed";
    public const string OrganizationMemberRemoved = "admin.organization.member_removed";
    public const string DemoDataPopulated = "admin.demo_data.populated";
    public const string DemoDataCleared = "admin.demo_data.cleared";

    // Phase 13: account deletion and data retention.
    public const string AccountDeleted = "auth.account_deleted";
    public const string UserDeleted = "admin.user.deleted";
    public const string RetentionApplied = "retention.applied";
    public const string OrganizationDeleted = "organization.deleted";
    public const string AdminOrganizationDeleted = "admin.organization.deleted";

    // Phase 14: search.
    public const string SearchRebuilt = "admin.search.rebuilt";
}

/// <summary>
/// Writes and reads the audit log. Details are reduced to short, non-sensitive facts: values whose
/// names suggest secrets are dropped and strings are truncated, so message text, tokens, passwords
/// and secret configuration values never reach the log.
/// </summary>
public sealed class AuditService(IAuditStore store, IAuditContext context, TimeProvider time, IOptions<AuditSettings> settings)
{
    private static readonly string[] SensitiveNames = ["password", "secret", "token", "apikey", "accesskey", "body", "content", "connectionstring"];

    public async Task RecordAsync(
        string action, Guid? actorUserId, string? targetType = null, string? targetId = null,
        IReadOnlyDictionary<string, object?>? details = null, bool succeeded = true, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        store.Add(AuditEntry.Create(Guid.CreateVersion7(now), now, action, succeeded, actorUserId, targetType, targetId, context.IpAddress, Serialize(details)));
        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return store.QueryAsync(query with { Limit = Math.Clamp(query.Limit, 1, 200) }, cancellationToken);
    }

    /// <summary>Removes entries past the retention period; returns how many.</summary>
    public async Task<int> PurgeAsync(CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow() - TimeSpan.FromDays(settings.Value.RetentionDays);
        var total = 0;
        int deleted;
        do
        {
            deleted = await store.DeleteOlderThanAsync(cutoff, 1000, cancellationToken).ConfigureAwait(false);
            total += deleted;
        }
        while (deleted == 1000);

        return total;
    }

    /// <summary>A compact JSON object without sensitive or oversized values.</summary>
    public static string? Serialize(IReadOnlyDictionary<string, object?>? details)
    {
        if (details is null || details.Count == 0)
        {
            return null;
        }

        var safe = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in details)
        {
            var lowered = key.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            if (SensitiveNames.Any(lowered.Contains))
            {
                continue;
            }

            safe[key] = value switch
            {
                string s => s.Length <= 200 ? s : s[..200] + "…",
                IEnumerable<string> list => list.Take(50).Select(v => v.Length <= 100 ? v : v[..100]).ToList(),
                _ => value,
            };
        }

        var json = JsonSerializer.Serialize(safe);
        return json.Length <= AuditEntry.DetailsMaxLength ? json : json[..AuditEntry.DetailsMaxLength];
    }
}
