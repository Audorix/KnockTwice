using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Audorix.ChatX.Domain.Widgets;

namespace Audorix.ChatX.Application.Widgets;

/// <summary>Embedded chat policy (owner decisions of 2026-11-23, "balanced"), bound from <c>Widgets</c>.</summary>
public sealed class WidgetSettings
{
    public const string SectionName = "Widgets";

    public bool Enabled { get; set; } = true;

    /// <summary>A visitor session lasts this long and is renewed while it is used.</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Renewal is written at most this often.</summary>
    public TimeSpan SessionTouchInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Signed identity tokens may be valid at most this long (exp − iat).</summary>
    public TimeSpan IdentityTokenMaxLifetime { get; set; } = TimeSpan.FromHours(1);

    public int AnonymousSessionsPerHourPerAddress { get; set; } = 5;

    public int MessagesPerMinute { get; set; } = 20;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (SessionLifetime < TimeSpan.FromMinutes(5) || SessionLifetime > TimeSpan.FromDays(365) || SessionTouchInterval <= TimeSpan.Zero)
        {
            errors.Add("Widgets:SessionLifetime must be 5 minutes to 365 days and SessionTouchInterval positive.");
        }

        if (IdentityTokenMaxLifetime < TimeSpan.FromMinutes(1) || IdentityTokenMaxLifetime > TimeSpan.FromDays(1))
        {
            errors.Add("Widgets:IdentityTokenMaxLifetime must be between 1 minute and 1 day.");
        }

        if (AnonymousSessionsPerHourPerAddress is < 1 or > 10000 || MessagesPerMinute is < 1 or > 1000)
        {
            errors.Add("Widgets:AnonymousSessionsPerHourPerAddress must be 1-10000 and MessagesPerMinute 1-1000.");
        }

        return errors;
    }
}

public interface IWidgetStore
{
    void Add(Widget widget);

    void Add(Visitor visitor);

    void Add(VisitorSession session);

    Task<Widget?> FindAsync(Guid widgetId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Widget>> ListForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ListResponderIdsAsync(Guid widgetId, CancellationToken cancellationToken);

    Task ReplaceRespondersAsync(Guid widgetId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    Task<Visitor?> FindVisitorAsync(Guid visitorId, CancellationToken cancellationToken);

    Task<Visitor?> FindVisitorBySubjectAsync(Guid widgetId, string subject, CancellationToken cancellationToken);

    Task<VisitorSession?> FindSessionByHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task<VisitorSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>The visitor's website conversation, if it has one.</summary>
    Task<Guid?> FindConversationIdAsync(Guid widgetId, Guid visitorUserId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Protects widget signing secrets at rest (Data Protection in production).</summary>
public interface IWidgetSecretProtector
{
    string Protect(string secret);

    string Unprotect(string protectedSecret);
}

/// <summary>Creates the stand-in accounts visitors take part in conversations with.</summary>
public interface IVisitorAccounts
{
    Task CreateAsync(Guid userId, string displayName, string language, CancellationToken cancellationToken);

    Task RenameAsync(Guid userId, string displayName, CancellationToken cancellationToken);
}

/// <summary>Claims of a verified host-signed identity token.</summary>
public sealed record VerifiedIdentity(string Subject, string Name, string? Email);

/// <summary>
/// Verifies identity tokens signed by a host server (owner decision: HS256 JWT with the widget's
/// secret). Requirements: header <c>alg</c> exactly HS256; constant-time signature check;
/// <c>aud</c> equal to the widget id; <c>sub</c> present; <c>exp</c> in the future, <c>iat</c> not
/// in the future, and a total validity no longer than the configured maximum. Anything else fails.
/// </summary>
public static class IdentityTokens
{
    public static VerifiedIdentity? Verify(string? token, string secret, Guid widgetId, DateTimeOffset now, TimeSpan maxLifetime)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096)
        {
            return null;
        }

        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            using var header = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            if (!header.RootElement.TryGetProperty("alg", out var alg) || alg.GetString() != "HS256")
            {
                return null;
            }

            var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"));
            if (!CryptographicOperations.FixedTimeEquals(expected, Base64UrlDecode(parts[2])))
            {
                return null;
            }

            using var payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            var claims = payload.RootElement;
            var audience = claims.TryGetProperty("aud", out var aud) ? aud.ValueKind switch
            {
                JsonValueKind.String => [aud.GetString()],
                JsonValueKind.Array => aud.EnumerateArray().Select(a => a.GetString()).ToArray(),
                _ => [],
            } : [];
            if (!audience.Contains(widgetId.ToString(), StringComparer.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!claims.TryGetProperty("exp", out var expClaim) || !expClaim.TryGetInt64(out var exp)
                || !claims.TryGetProperty("iat", out var iatClaim) || !iatClaim.TryGetInt64(out var iat))
            {
                return null;
            }

            var skew = TimeSpan.FromMinutes(1);
            var issued = DateTimeOffset.FromUnixTimeSeconds(iat);
            var expires = DateTimeOffset.FromUnixTimeSeconds(exp);
            if (expires <= now || issued > now + skew || expires - issued > maxLifetime)
            {
                return null;
            }

            var subject = claims.TryGetProperty("sub", out var sub) && sub.ValueKind == JsonValueKind.String ? sub.GetString()?.Trim() : null;
            if (string.IsNullOrEmpty(subject) || subject.Length > Visitor.SubjectMaxLength)
            {
                return null;
            }

            var name = claims.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()?.Trim() : null;
            var email = claims.TryGetProperty("email", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString()?.Trim() : null;
            return new VerifiedIdentity(subject, string.IsNullOrEmpty(name) ? subject : name[..Math.Min(name.Length, 80)], string.IsNullOrEmpty(email) ? null : email[..Math.Min(email.Length, 254)]);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Creates a token the way a host server would (for tests and the documentation's example).</summary>
    public static string Sign(string secret, Guid widgetId, string subject, string? name, string? email, DateTimeOffset issuedAt, TimeSpan lifetime)
    {
        var header = Base64UrlEncode("""{"alg":"HS256","typ":"JWT"}"""u8.ToArray());
        var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
        {
            ["sub"] = subject,
            ["name"] = name,
            ["email"] = email,
            ["aud"] = widgetId.ToString(),
            ["iat"] = issuedAt.ToUnixTimeSeconds(),
            ["exp"] = (issuedAt + lifetime).ToUnixTimeSeconds(),
        }));
        var signature = Base64UrlEncode(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.ASCII.GetBytes($"{header}.{payload}")));
        return $"{header}.{payload}.{signature}";
    }

    public static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            0 => string.Empty,
            _ => throw new FormatException("Invalid base64url."),
        };
        return Convert.FromBase64String(padded);
    }
}

/// <summary>
/// Visitor abuse limits (Phase 10). Phase 21: counted through <see cref="Common.IRateCounter"/>,
/// so with several API instances the limits are shared (Redis) instead of per instance.
/// </summary>
public sealed class VisitorRateLimiter(Common.IRateCounter counter)
{
    /// <summary>Counts an event for <paramref name="key"/>; false when the limit within <paramref name="window"/> is reached.</summary>
    public bool TryAcquire(string key, int limit, TimeSpan window) => counter.TryAcquire($"visitor:{key}", limit, window);
}
