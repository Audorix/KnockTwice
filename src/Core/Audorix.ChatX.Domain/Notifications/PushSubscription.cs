namespace Audorix.ChatX.Domain.Notifications;

/// <summary>
/// A browser's Web Push subscription (Phase 15, RFC 8030/8291): the push service endpoint and the
/// keys the payload is encrypted for. One row per browser; the endpoint is unique. Removed when the
/// person switches push off in that browser, signs out there, or the push service reports it gone.
/// </summary>
public sealed class PushSubscription
{
    public const int EndpointMaxLength = 2000;
    public const int KeyMaxLength = 200;

    /// <summary>After this many failed deliveries in a row the subscription is dropped.</summary>
    public const int MaxFailures = 5;

    private PushSubscription(Guid id, Guid userId, string endpoint, string p256dh, string auth, DateTimeOffset createdAtUtc)
    {
        Id = id;
        UserId = userId;
        Endpoint = endpoint;
        P256dh = p256dh;
        Auth = auth;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>
    /// The sign-in session that subscribed. Push stops when that session ends (signed out, revoked
    /// or expired), so a device signed out remotely gets no more notifications.
    /// </summary>
    public Guid SessionId { get; private set; }

    /// <summary>The push service URL (an https URL of the browser vendor's push service).</summary>
    public string Endpoint { get; private set; }

    /// <summary>The browser's P-256 public key, base64url.</summary>
    public string P256dh { get; private set; }

    /// <summary>The 16-byte authentication secret, base64url.</summary>
    public string Auth { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? LastSuccessAtUtc { get; private set; }

    public int Failures { get; private set; }

    public static PushSubscription Create(Guid id, Guid userId, Guid sessionId, string endpoint, string p256dh, string auth, DateTimeOffset nowUtc)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps || endpoint.Length > EndpointMaxLength)
        {
            throw new DomainRuleException("push_endpoint_invalid", "The push endpoint must be an https URL.");
        }

        if (!IsKey(p256dh, 87) || !IsKey(auth, 22))
        {
            throw new DomainRuleException("push_keys_invalid", "The subscription keys are not valid.");
        }

        return new(id, userId, endpoint, p256dh, auth, nowUtc) { SessionId = sessionId };
    }

    /// <summary>The same browser subscribed again (keys can change, for example after a reset).</summary>
    public void Renew(Guid userId, Guid sessionId, string p256dh, string auth)
    {
        SessionId = sessionId;
        if (!IsKey(p256dh, 87) || !IsKey(auth, 22))
        {
            throw new DomainRuleException("push_keys_invalid", "The subscription keys are not valid.");
        }

        UserId = userId;
        P256dh = p256dh;
        Auth = auth;
        Failures = 0;
    }

    public void Delivered(DateTimeOffset nowUtc)
    {
        LastSuccessAtUtc = nowUtc;
        Failures = 0;
    }

    /// <summary>Counts a failed delivery; true when the subscription should be dropped.</summary>
    public bool Failed() => ++Failures >= MaxFailures;

    /// <summary>Base64url text of roughly the expected length (65-byte key: 87 characters; 16-byte secret: 22).</summary>
    private static bool IsKey(string? value, int length) =>
        value is not null && value.Length >= length - 2 && value.Length <= Math.Min(KeyMaxLength, length + 4)
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '=' or '+' or '/');
}
