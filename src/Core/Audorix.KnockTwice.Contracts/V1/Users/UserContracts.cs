namespace Audorix.KnockTwice.Contracts.V1.Users;

/// <summary>The signed-in user's own account, profile and preferences.</summary>
public sealed record CurrentUserResponse(
    Guid Id,
    string Email,
    bool EmailConfirmed,
    string DisplayName,
    AvatarResponse Avatar,
    UserPreferencesResponse Preferences,
    bool HasPassword,
    IReadOnlyList<string> ExternalProviders,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    string? Handle = null);

/// <summary>Another user as shown to the caller. Never includes the email address.</summary>
/// <param name="Deleted">The account was deleted; show a localized "Deleted user" instead of <paramref name="DisplayName"/>.</param>
public sealed record UserSummaryResponse(Guid Id, string DisplayName, AvatarResponse Avatar, bool Deleted = false);

/// <summary>What deleting the signed-in account involves (Phase 13).</summary>
/// <param name="CanDelete">False while <paramref name="BlockingOrganizations"/> is not empty or the account is the last administrator.</param>
/// <param name="BlockingOrganizations">Organizations the user is the only owner of: hand them over first.</param>
/// <param name="LastAdministrator">The account is the installation's only enabled administrator.</param>
/// <param name="RequiresPassword">Confirm with the password; otherwise (external sign-in only) type the email address.</param>
public sealed record AccountDeletionCheckResponse(
    bool CanDelete, IReadOnlyList<DeletionBlockerResponse> BlockingOrganizations, bool LastAdministrator, bool RequiresPassword);

public sealed record DeletionBlockerResponse(Guid OrganizationId, string Name);

/// <summary>Confirms deleting the signed-in account: the current password, or the account email for external-only accounts.</summary>
public sealed record DeleteAccountRequest(string? Password, string? ConfirmEmail);

/// <summary>Generated avatar. Image upload arrives with the storage module (Phase 7).</summary>
/// <param name="PhotoUrl">The profile photo (signed-in people only; the address changes with every new photo), or null for initials.</param>
public sealed record AvatarResponse(string Initials, string? PhotoUrl = null);

/// <param name="Platform"><c>website</c>, <c>github</c>, <c>linkedin</c>, <c>youtube</c>, <c>instagram</c> or <c>x</c>.</param>
/// <param name="Visibility"><c>contacts</c> or <c>only_me</c>.</param>
public sealed record ProfileLinkResponse(string Platform, string Url, string Visibility);

/// <param name="Links">In order; replaces all links (at most 10).</param>
public sealed record ReplaceProfileLinksRequest(IReadOnlyList<ProfileLinkRequest>? Links);

public sealed record ProfileLinkRequest(string? Platform, string? Url, string? Visibility);

/// <param name="Theme"><c>system</c>, <c>light</c> or <c>dark</c>.</param>
/// <param name="DesktopNotifications">Browser notifications switched on (each browser also asks for permission).</param>
/// <param name="NotificationPreviews">Desktop notifications may include message text.</param>
/// <param name="EmailNotifications"><c>batched</c> (default: soon after messages stay unread, at most hourly), <c>daily</c> or <c>off</c> (Phase 15).</param>
/// <param name="ShareEmailWithContacts">Contacts see the email address in your profile drawer (off by default).</param>
public sealed record UserPreferencesResponse(
    string UiLanguage, string Theme = "system", bool DesktopNotifications = false, bool NotificationPreviews = true, string EmailNotifications = "batched",
    bool ShareEmailWithContacts = false);

/// <param name="Handle">New Display ID (unique; 3-30 of a-z, 0-9, dots and underscores; a leading @ is ignored); null keeps it.</param>
public sealed record UpdateProfileRequest(string? DisplayName, string? Handle = null);

/// <summary>Null fields are left unchanged.</summary>
public sealed record UpdatePreferencesRequest(
    string? UiLanguage, string? Theme = null, bool? DesktopNotifications = null, bool? NotificationPreviews = null, string? EmailNotifications = null,
    bool? ShareEmailWithContacts = null);

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
