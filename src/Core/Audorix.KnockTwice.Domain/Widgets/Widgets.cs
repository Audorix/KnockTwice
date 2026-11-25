using System.Text.RegularExpressions;
using Audorix.KnockTwice.Domain.Organizations;

namespace Audorix.KnockTwice.Domain.Widgets;

/// <summary>Where the launcher sits on the host page.</summary>
public enum WidgetPosition
{
    Right = 1,
    Left = 2,
}

/// <summary>
/// A website chat widget of an organization (Phase 10, ADR-024 accepted): public look and texts,
/// the exact origins allowed to embed it, whether anonymous visitors may chat, and the protected
/// secret host servers use to sign visitor identities. Visitor conversations go to its responders.
/// </summary>
public sealed partial class Widget
{
    public const int NameMaxLength = 80;
    public const int TitleMaxLength = 80;
    public const int WelcomeMaxLength = 500;
    public const int MaxOrigins = 20;
    public static readonly IReadOnlyList<string> Languages = ["auto", "en", "es"];

    private Widget(Guid id, Guid organizationId, string name, Guid createdByUserId, DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        Name = name;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        Title = name;
        AccentColor = "#0b6e66";
        DefaultLanguage = "auto";
        AllowedOrigins = string.Empty;
        ProtectedSecret = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    /// <summary>Internal name for administrators.</summary>
    public string Name { get; private set; }

    /// <summary>Heading visitors see.</summary>
    public string Title { get; private set; }

    public string? WelcomeMessage { get; private set; }

    /// <summary><c>#rrggbb</c>.</summary>
    public string AccentColor { get; private set; }

    public WidgetPosition Position { get; private set; } = WidgetPosition.Right;

    /// <summary><c>auto</c> (the visitor's browser), <c>en</c> or <c>es</c>.</summary>
    public string DefaultLanguage { get; private set; }

    public bool Enabled { get; private set; }

    public bool AllowAnonymous { get; private set; } = true;

    /// <summary>Exact origins (<c>https://example.org</c>), one per line.</summary>
    public string AllowedOrigins { get; private set; }

    /// <summary>The signing secret, protected with the installation's Data Protection keys.</summary>
    public string ProtectedSecret { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyList<string> Origins => AllowedOrigins.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    public static Widget Create(Guid id, Guid organizationId, string name, Guid createdByUserId, string protectedSecret, DateTimeOffset nowUtc)
    {
        var widget = new Widget(Require.Id(id), Require.Id(organizationId), Require.Name(name, NameMaxLength, "widget_name"), Require.Id(createdByUserId), Require.Utc(nowUtc));
        widget.ReplaceSecret(protectedSecret, nowUtc);
        return widget;
    }

    public void Update(
        string name, string title, string? welcome, string accentColor, WidgetPosition position, string language, bool allowAnonymous,
        IEnumerable<string> origins, DateTimeOffset nowUtc)
    {
        Name = Require.Name(name, NameMaxLength, "widget_name");
        Title = Require.Name(title, TitleMaxLength, "widget_title");
        var text = welcome?.Trim() ?? string.Empty;
        WelcomeMessage = text.Length == 0 ? null
            : text.Length > WelcomeMaxLength ? throw new DomainRuleException("widget_welcome_too_long", "The welcome message is too long.")
            : text;
        AccentColor = ColorPattern().IsMatch(accentColor ?? string.Empty) ? accentColor!.ToLowerInvariant()
            : throw new DomainRuleException("widget_color_invalid", "The colour must look like #1a2b3c.");
        Position = Require.Defined(position);
        DefaultLanguage = Languages.Contains(language?.Trim().ToLowerInvariant() ?? string.Empty) ? language!.Trim().ToLowerInvariant()
            : throw new DomainRuleException("widget_language_invalid", "The language is not available.");
        AllowAnonymous = allowAnonymous;
        var normalized = (origins ?? []).Select(NormalizeOrigin).Distinct(StringComparer.Ordinal).ToList();
        if (normalized.Count > MaxOrigins)
        {
            throw new DomainRuleException("widget_origins_too_many", $"At most {MaxOrigins} origins are allowed.");
        }

        AllowedOrigins = string.Join('\n', normalized);
        if (Enabled && normalized.Count == 0)
        {
            Enabled = false;
        }

        UpdatedAtUtc = Require.Utc(nowUtc);
    }

    /// <summary>A widget can only be switched on with at least one allowed origin (and, checked by the caller, a responder).</summary>
    public void SetEnabled(bool enabled, DateTimeOffset nowUtc)
    {
        if (enabled && Origins.Count == 0)
        {
            throw new DomainRuleException("widget_origins_required", "Add at least one website address first.");
        }

        Enabled = enabled;
        UpdatedAtUtc = Require.Utc(nowUtc);
    }

    public void ReplaceSecret(string protectedSecret, DateTimeOffset nowUtc)
    {
        ProtectedSecret = string.IsNullOrWhiteSpace(protectedSecret)
            ? throw new DomainRuleException("value_invalid", "A protected secret is required.")
            : protectedSecret;
        UpdatedAtUtc = Require.Utc(nowUtc);
    }

    public bool AllowsOrigin(string? origin) =>
        TryNormalizeOrigin(origin, out var normalized) && Origins.Contains(normalized, StringComparer.Ordinal);

    /// <summary>Scheme, host and port only, lower case, default ports omitted: <c>https://shop.example.org</c>.</summary>
    public static string NormalizeOrigin(string origin) =>
        TryNormalizeOrigin(origin, out var normalized)
            ? normalized
            : throw new DomainRuleException("widget_origin_invalid", "Enter website addresses like https://example.org.");

    public static bool TryNormalizeOrigin(string? origin, out string normalized)
    {
        normalized = string.Empty;
        if (!Uri.TryCreate(origin?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")
            || (uri.AbsolutePath is not ("/" or "") ) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo) || uri.Host.Contains('*', StringComparison.Ordinal))
        {
            return false;
        }

        normalized = uri.IsDefaultPort ? $"{uri.Scheme}://{uri.IdnHost.ToLowerInvariant()}" : $"{uri.Scheme}://{uri.IdnHost.ToLowerInvariant()}:{uri.Port}";
        return true;
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorPattern();
}

/// <summary>An organization member who answers the widget's visitor conversations.</summary>
public sealed class WidgetResponder
{
    private WidgetResponder(Guid widgetId, Guid userId)
    {
        WidgetId = widgetId;
        UserId = userId;
    }

    public Guid WidgetId { get; private set; }

    public Guid UserId { get; private set; }

    public static WidgetResponder Create(Guid widgetId, Guid userId) => new(Require.Id(widgetId), Require.Id(userId));
}

/// <summary>
/// A website visitor of one widget. Anonymous visitors exist only through their session token;
/// identified visitors carry the subject, name and email the host server signed. Each visitor has a
/// stand-in account (<see cref="UserId"/>) so it can take part in its conversation.
/// </summary>
public sealed class Visitor
{
    public const int SubjectMaxLength = 128;

    private Visitor(Guid id, Guid widgetId, Guid userId, string displayName, DateTimeOffset createdAtUtc)
    {
        Id = id;
        WidgetId = widgetId;
        UserId = userId;
        DisplayName = displayName;
        CreatedAtUtc = createdAtUtc;
        LastSeenAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid WidgetId { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>The host's stable id for a signed-in visitor; null for anonymous visitors.</summary>
    public string? ExternalSubject { get; private set; }

    public string DisplayName { get; private set; }

    /// <summary>Only from a verified identity token; never typed by the visitor.</summary>
    public string? Email { get; private set; }

    public bool IsIdentified => ExternalSubject is not null;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset LastSeenAtUtc { get; private set; }

    public static Visitor CreateAnonymous(Guid id, Guid widgetId, Guid userId, string displayName, DateTimeOffset nowUtc) =>
        new(Require.Id(id), Require.Id(widgetId), Require.Id(userId), Require.Name(displayName, 80, "display_name"), Require.Utc(nowUtc));

    public static Visitor CreateIdentified(Guid id, Guid widgetId, Guid userId, string subject, string displayName, string? email, DateTimeOffset nowUtc)
    {
        var visitor = new Visitor(Require.Id(id), Require.Id(widgetId), Require.Id(userId), Require.Name(displayName, 80, "display_name"), Require.Utc(nowUtc));
        visitor.ExternalSubject = RequireSubject(subject);
        visitor.Email = email;
        return visitor;
    }

    /// <summary>Applies the latest verified identity details.</summary>
    public void UpdateIdentity(string displayName, string? email, DateTimeOffset nowUtc)
    {
        DisplayName = Require.Name(displayName, 80, "display_name");
        Email = email;
        LastSeenAtUtc = Require.Utc(nowUtc);
    }

    public void Seen(DateTimeOffset nowUtc) => LastSeenAtUtc = Require.Utc(nowUtc);

    private static string RequireSubject(string subject) =>
        !string.IsNullOrWhiteSpace(subject) && subject.Length <= SubjectMaxLength && !subject.Any(char.IsControl)
            ? subject
            : throw new DomainRuleException("widget_identity_invalid", "The visitor subject is invalid.");
}

/// <summary>
/// A visitor's bearer session (ADR-024: short-lived, narrowly scoped, separate from first-party
/// cookies). Only a SHA-256 hash of the token is stored. Sliding expiry while used.
/// </summary>
public sealed class VisitorSession
{
    private VisitorSession(Guid id, Guid visitorId, Guid widgetId, string tokenHash, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc)
    {
        Id = id;
        VisitorId = visitorId;
        WidgetId = widgetId;
        TokenHash = tokenHash;
        CreatedAtUtc = createdAtUtc;
        LastSeenAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid VisitorId { get; private set; }

    public Guid WidgetId { get; private set; }

    public string TokenHash { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset LastSeenAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public bool IsActive(DateTimeOffset nowUtc) => RevokedAtUtc is null && nowUtc < ExpiresAtUtc;

    public static VisitorSession Start(Guid id, Guid visitorId, Guid widgetId, string tokenHash, DateTimeOffset nowUtc, TimeSpan lifetime) =>
        new(Require.Id(id), Require.Id(visitorId), Require.Id(widgetId), tokenHash, Require.Utc(nowUtc), Require.Utc(nowUtc) + lifetime);

    /// <summary>Extends the session while it is used; returns whether anything changed.</summary>
    public bool Touch(DateTimeOffset nowUtc, TimeSpan lifetime, TimeSpan interval)
    {
        if (nowUtc - LastSeenAtUtc < interval)
        {
            return false;
        }

        LastSeenAtUtc = Require.Utc(nowUtc);
        ExpiresAtUtc = nowUtc + lifetime;
        return true;
    }

    public void Revoke(DateTimeOffset nowUtc) => RevokedAtUtc ??= Require.Utc(nowUtc);
}
