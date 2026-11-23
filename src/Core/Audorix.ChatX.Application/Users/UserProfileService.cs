using Audorix.ChatX.Application.Common;
using Audorix.ChatX.Application.Identity;
using Audorix.ChatX.Application.Localization;
using Audorix.ChatX.Domain.Users;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Application.Users;

/// <summary>Persistence port for user profiles. Profiles are created together with accounts.</summary>
public interface IUserProfileStore
{
    Task<UserProfile?> FindAsync(Guid userId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>The signed-in user's own view of their account, profile and preferences.</summary>
public sealed record CurrentUser(UserAccount Account, UserProfile Profile);

public sealed class UserProfileService(
    IUserProfileStore profiles,
    IUserAccountManager accounts,
    TimeProvider time,
    IOptionsSnapshot<LocalizationSettings> localization)
{
    public async Task<CurrentUser> GetCurrentAsync(Guid userId, CancellationToken cancellationToken)
    {
        var account = await accounts.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("user_not_found");
        var profile = await profiles.FindAsync(userId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("user_not_found");
        return new CurrentUser(account, profile);
    }

    public async Task<CurrentUser> UpdateProfileAsync(Guid userId, string displayName, CancellationToken cancellationToken)
    {
        var current = await GetCurrentAsync(userId, cancellationToken).ConfigureAwait(false);
        current.Profile.Rename(displayName, time.GetUtcNow());
        await profiles.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return current;
    }

    /// <summary>Updates the given preferences; null values are left unchanged.</summary>
    public async Task<CurrentUser> UpdatePreferencesAsync(
        Guid userId, string? uiLanguage, CancellationToken cancellationToken,
        string? theme = null, bool? desktopNotifications = null, bool? notificationPreviews = null, string? emailNotifications = null)
    {
        var current = await GetCurrentAsync(userId, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        if (uiLanguage is not null)
        {
            var normalized = uiLanguage.Trim().ToLowerInvariant();
            if (!localization.Value.EffectiveSupportedLanguages.Contains(normalized))
            {
                throw new RequestValidationException("uiLanguage", "unsupported_language");
            }

            current.Profile.ChangeUiLanguage(normalized, now);
        }

        if (theme is not null)
        {
            if (!UserProfile.Themes.Contains(theme.Trim().ToLowerInvariant()))
            {
                throw new RequestValidationException("theme", "theme_invalid");
            }

            current.Profile.ChangeTheme(theme, now);
        }

        if (desktopNotifications is not null || notificationPreviews is not null)
        {
            current.Profile.ChangeNotifications(
                desktopNotifications ?? current.Profile.DesktopNotifications, notificationPreviews ?? current.Profile.NotificationPreviews, now);
        }

        if (emailNotifications is not null)
        {
            if (!UserProfile.EmailModes.Contains(emailNotifications.Trim().ToLowerInvariant()))
            {
                throw new RequestValidationException("emailNotifications", "email_notifications_invalid");
            }

            current.Profile.ChangeEmailNotifications(emailNotifications, now);
        }

        await profiles.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return current;
    }
}
