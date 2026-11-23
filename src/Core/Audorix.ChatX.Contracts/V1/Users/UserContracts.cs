namespace Audorix.ChatX.Contracts.V1.Users;

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
    IReadOnlyList<string> Permissions);

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
public sealed record AvatarResponse(string Initials);

/// <param name="Theme"><c>system</c>, <c>light</c> or <c>dark</c>.</param>
/// <param name="DesktopNotifications">Browser notifications switched on (each browser also asks for permission).</param>
/// <param name="NotificationPreviews">Desktop notifications may include message text.</param>
/// <param name="EmailNotifications"><c>batched</c> (default: soon after messages stay unread, at most hourly), <c>daily</c> or <c>off</c> (Phase 15).</param>
public sealed record UserPreferencesResponse(
    string UiLanguage, string Theme = "system", bool DesktopNotifications = false, bool NotificationPreviews = true, string EmailNotifications = "batched");

public sealed record UpdateProfileRequest(string? DisplayName);

/// <summary>Null fields are left unchanged.</summary>
public sealed record UpdatePreferencesRequest(
    string? UiLanguage, string? Theme = null, bool? DesktopNotifications = null, bool? NotificationPreviews = null, string? EmailNotifications = null);

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
