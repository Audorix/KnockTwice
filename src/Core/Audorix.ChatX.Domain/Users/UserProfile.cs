using System.Globalization;
using System.Text.RegularExpressions;

namespace Audorix.ChatX.Domain.Users;

/// <summary>
/// User-owned profile and preferences (ADR-008 user-owned data). The account itself
/// (credentials, email, external logins) is managed by ASP.NET Core Identity; this entity
/// shares the account's identifier.
/// </summary>
public sealed partial class UserProfile
{
    public const int DisplayNameMaxLength = 80;

    private UserProfile(Guid userId, string displayName, string uiLanguage, DateTimeOffset createdAtUtc)
    {
        UserId = userId;
        DisplayName = displayName;
        UiLanguage = uiLanguage;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid UserId { get; private set; }

    public string DisplayName { get; private set; }

    /// <summary>
    /// Interface language. ADR-009 keeps UI, notification and message language separate; until a
    /// distinct notification-language preference exists, account emails use this language.
    /// </summary>
    public string UiLanguage { get; private set; }

    /// <summary>Colour theme: <c>system</c> (follow the device), <c>light</c> or <c>dark</c> (Phase 8, owner decision).</summary>
    public string Theme { get; private set; } = SystemTheme;

    /// <summary>The user switched on browser desktop notifications (each browser still asks for permission).</summary>
    public bool DesktopNotifications { get; private set; }

    /// <summary>Desktop notifications may show message text; when false they only say who wrote.</summary>
    public bool NotificationPreviews { get; private set; } = true;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Set when the account was deleted (Phase 13, owner decision): the profile row stays so
    /// earlier messages keep an author, but it holds nothing personal any more.
    /// </summary>
    public DateTimeOffset? DeletedAtUtc { get; private set; }

    public bool IsDeleted => DeletedAtUtc is not null;

    /// <summary>Stored name of a deleted account; clients show their own localized label.</summary>
    public const string DeletedDisplayName = "Deleted user";

    public const string SystemTheme = "system";

    public static readonly IReadOnlyList<string> Themes = [SystemTheme, "light", "dark"];

    /// <summary>Up to two initials used for the generated avatar.</summary>
    public string Initials => ComputeInitials(DisplayName);

    public static UserProfile Create(Guid userId, string displayName, string uiLanguage, DateTimeOffset nowUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainRuleException("user_id_required", "A user identifier is required.");
        }

        return new UserProfile(userId, NormalizeDisplayName(displayName), NormalizeLanguage(uiLanguage), EnsureUtc(nowUtc));
    }

    public void Rename(string displayName, DateTimeOffset nowUtc)
    {
        DisplayName = NormalizeDisplayName(displayName);
        Touch(nowUtc);
    }

    public void ChangeUiLanguage(string uiLanguage, DateTimeOffset nowUtc)
    {
        UiLanguage = NormalizeLanguage(uiLanguage);
        Touch(nowUtc);
    }

    public void ChangeTheme(string theme, DateTimeOffset nowUtc)
    {
        var normalized = theme?.Trim().ToLowerInvariant() ?? string.Empty;
        Theme = Themes.Contains(normalized)
            ? normalized
            : throw new DomainRuleException("theme_invalid", "The theme must be system, light or dark.");
        Touch(nowUtc);
    }

    public void ChangeNotifications(bool desktop, bool previews, DateTimeOffset nowUtc)
    {
        DesktopNotifications = desktop;
        NotificationPreviews = previews;
        Touch(nowUtc);
    }

    // ---- Email notifications (Phase 15, owner decision: batched by default) ----------------------

    /// <summary>An email shortly after messages stay unread (at most one per interval).</summary>
    public const string EmailBatched = "batched";

    /// <summary>One email per day with what is still unread.</summary>
    public const string EmailDaily = "daily";

    public const string EmailOff = "off";

    public static readonly IReadOnlyList<string> EmailModes = [EmailBatched, EmailDaily, EmailOff];

    public string EmailNotifications { get; private set; } = EmailBatched;

    /// <summary>When the last notification email was sent (for the interval between emails).</summary>
    public DateTimeOffset? LastNotificationEmailAtUtc { get; private set; }

    public void ChangeEmailNotifications(string mode, DateTimeOffset nowUtc)
    {
        var normalized = mode?.Trim().ToLowerInvariant() ?? string.Empty;
        EmailNotifications = EmailModes.Contains(normalized)
            ? normalized
            : throw new DomainRuleException("email_notifications_invalid", "Email notifications must be batched, daily or off.");
        Touch(nowUtc);
    }

    public void NotificationEmailSent(DateTimeOffset nowUtc) => LastNotificationEmailAtUtc = EnsureUtc(nowUtc);

    /// <summary>Erases the person: a neutral name, default preferences, and the deletion time. Idempotent.</summary>
    public void Erase(DateTimeOffset nowUtc)
    {
        if (IsDeleted)
        {
            return;
        }

        var utc = EnsureUtc(nowUtc);
        DisplayName = DeletedDisplayName;
        Theme = SystemTheme;
        EmailNotifications = EmailOff;
        DesktopNotifications = false;
        NotificationPreviews = true;
        DeletedAtUtc = utc;
        Touch(utc);
    }

    public static string NormalizeDisplayName(string displayName)
    {
        var normalized = WhitespaceRun().Replace(displayName?.Trim() ?? string.Empty, " ");
        if (normalized.Length == 0)
        {
            throw new DomainRuleException("display_name_required", "A display name is required.");
        }

        if (normalized.Length > DisplayNameMaxLength)
        {
            throw new DomainRuleException("display_name_too_long", $"The display name may contain at most {DisplayNameMaxLength} characters.");
        }

        if (normalized.Any(char.IsControl))
        {
            throw new DomainRuleException("display_name_invalid", "The display name contains invalid characters.");
        }

        return normalized;
    }

    private static string NormalizeLanguage(string language)
    {
        var normalized = language?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!LanguageCode().IsMatch(normalized))
        {
            throw new DomainRuleException("invalid_language_code", "The language must be a two- or three-letter ISO 639 code.");
        }

        return normalized;
    }

    private static string ComputeInitials(string displayName)
    {
        var words = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => char.IsLetterOrDigit(w, 0))
            .ToList();
        var initials = words.Count switch
        {
            0 => string.Empty,
            1 => FirstTextElement(words[0]),
            _ => FirstTextElement(words[0]) + FirstTextElement(words[^1]),
        };
        return initials.ToUpper(CultureInfo.InvariantCulture);
    }

    private static string FirstTextElement(string word) =>
        StringInfo.GetNextTextElementLength(word) is var length and > 0 ? word[..length] : string.Empty;

    private void Touch(DateTimeOffset nowUtc)
    {
        var utc = EnsureUtc(nowUtc);
        UpdatedAtUtc = utc > UpdatedAtUtc ? utc : UpdatedAtUtc;
    }

    private static DateTimeOffset EnsureUtc(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero
            ? value
            : throw new DomainRuleException("timestamp_not_utc", "Timestamps must be expressed in UTC.");

    [GeneratedRegex("^[a-z]{2,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageCode();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRun();
}
