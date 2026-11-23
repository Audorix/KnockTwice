namespace Audorix.ChatX.Domain.Sessions;

/// <summary>
/// A server-side record of one signed-in browser session. The authentication cookie carries
/// only the session identifier; revoking the record ends the session on its next request,
/// independently of the cookie's own expiry.
/// </summary>
public sealed class UserSession
{
    public const int DescriptionMaxLength = 120;

    private UserSession(Guid id, Guid userId, string description, bool persistent, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc)
    {
        Id = id;
        UserId = userId;
        Description = description;
        Persistent = persistent;
        CreatedAtUtc = createdAtUtc;
        LastSeenAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>Coarse client description (for example "Firefox on Windows"); never the raw user agent or IP.</summary>
    public string Description { get; private set; }

    /// <summary>Whether the user chose to stay signed in across browser restarts.</summary>
    public bool Persistent { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset LastSeenAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public static UserSession Start(Guid id, Guid userId, string description, bool persistent, DateTimeOffset nowUtc, TimeSpan lifetime)
    {
        if (id == Guid.Empty || userId == Guid.Empty)
        {
            throw new DomainRuleException("session_identity_required", "Session and user identifiers are required.");
        }

        if (lifetime <= TimeSpan.Zero)
        {
            throw new DomainRuleException("session_lifetime_invalid", "A session lifetime must be positive.");
        }

        EnsureUtc(nowUtc);
        var text = string.IsNullOrWhiteSpace(description) ? "Unknown client" : description.Trim();
        if (text.Length > DescriptionMaxLength)
        {
            text = text[..DescriptionMaxLength];
        }

        return new UserSession(id, userId, text, persistent, nowUtc, nowUtc + lifetime);
    }

    public bool IsActive(DateTimeOffset nowUtc) => RevokedAtUtc is null && nowUtc < ExpiresAtUtc;

    /// <summary>Records activity and slides the expiry forward. Has no effect on an inactive session.</summary>
    public void Touch(DateTimeOffset nowUtc, TimeSpan lifetime)
    {
        EnsureUtc(nowUtc);
        if (!IsActive(nowUtc))
        {
            return;
        }

        if (nowUtc > LastSeenAtUtc)
        {
            LastSeenAtUtc = nowUtc;
        }

        var expiry = nowUtc + lifetime;
        if (expiry > ExpiresAtUtc)
        {
            ExpiresAtUtc = expiry;
        }
    }

    /// <summary>Revocation is permanent and idempotent: the first revocation time is kept.</summary>
    public void Revoke(DateTimeOffset nowUtc)
    {
        EnsureUtc(nowUtc);
        RevokedAtUtc ??= nowUtc;
    }

    private static void EnsureUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleException("timestamp_not_utc", "Timestamps must be expressed in UTC.");
        }
    }
}
