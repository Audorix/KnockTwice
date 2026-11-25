using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Identity;
using Audorix.KnockTwice.Application.Localization;
using Audorix.KnockTwice.Domain.Users;
using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Application.Users;

/// <summary>Persistence port for user profiles. Profiles are created together with accounts.</summary>
public interface IUserProfileStore
{
    Task<UserProfile?> FindAsync(Guid userId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Saves; false (and nothing saved) when a unique value such as a Display ID is already taken.</summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Display IDs in use by people other than <paramref name="exceptUserId"/> that start with <paramref name="prefix"/>.</summary>
    Task<IReadOnlyList<string>> ListHandlesStartingWithAsync(string prefix, Guid exceptUserId, CancellationToken cancellationToken);
}

/// <summary>The signed-in user's own view of their account, profile and preferences.</summary>
public sealed record CurrentUser(UserAccount Account, UserProfile Profile);

/// <param name="Email">Null unless the person shares their email address with contacts.</param>
public sealed record ContactDetails(string? Handle, string? Email);

public sealed class UserProfileService(
    IUserProfileStore profiles,
    IUserAccountManager accounts,
    TimeProvider time,
    IOptionsSnapshot<LocalizationSettings> localization,
    Contacts.IContactStore contacts)
{
    /// <summary>
    /// What a contact's drawer shows (2026-11-24): their Display ID, and their email address only when
    /// they share it with contacts. Only for contacts; anyone else is "not found".
    /// </summary>
    public async Task<ContactDetails> GetContactDetailsAsync(Guid viewerId, Guid userId, CancellationToken cancellationToken)
    {
        if (viewerId == userId || await contacts.FindContactAsync(Domain.Contacts.UserPair.Of(viewerId, userId), cancellationToken).ConfigureAwait(false) is null)
        {
            throw new ResourceNotFoundException("contact_not_found");
        }

        var profile = await EnsureHandleAsync(userId, cancellationToken).ConfigureAwait(false);
        if (profile is null || profile.IsDeleted)
        {
            throw new ResourceNotFoundException("contact_not_found");
        }

        var email = profile.ShareEmailWithContacts
            ? (await accounts.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false))?.Email
            : null;
        return new ContactDetails(profile.Handle, email);
    }

    public async Task<CurrentUser> GetCurrentAsync(Guid userId, CancellationToken cancellationToken)
    {
        var account = await accounts.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("user_not_found");
        var profile = await EnsureHandleAsync(userId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("user_not_found");
        return new CurrentUser(account, profile);
    }

    /// <summary>Changes the display name and, when given, the Display ID (<paramref name="handle"/>, unique).</summary>
    public async Task<CurrentUser> UpdateProfileAsync(Guid userId, string displayName, CancellationToken cancellationToken, string? handle = null)
    {
        var current = await GetCurrentAsync(userId, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        current.Profile.Rename(displayName, now);
        var handleChanged = false;
        if (handle is not null)
        {
            string normalized;
            try
            {
                normalized = UserProfile.NormalizeHandle(handle);
            }
            catch (Domain.DomainRuleException)
            {
                throw new RequestValidationException("handle", "handle_invalid");
            }

            if (normalized != current.Profile.Handle)
            {
                current.Profile.ChangeHandle(normalized, now);
                handleChanged = true;
            }
        }

        // Only a new Display ID can collide with someone else's; any other failed save is a plain conflict.
        if (!await profiles.TrySaveChangesAsync(cancellationToken).ConfigureAwait(false))
        {
            throw handleChanged ? new RequestValidationException("handle", "handle_taken") : new ConflictException("conflict");
        }

        return current;
    }

    /// <summary>
    /// The profile, with a Display ID made from the name when it has none yet ("aaronmedina", then
    /// "aaronmedina2" and so on when taken). A simultaneous claim of the same ID is retried.
    /// Deleted accounts get none. Null when there is no such profile.
    /// </summary>
    public async Task<UserProfile?> EnsureHandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var profile = await profiles.FindAsync(userId, cancellationToken).ConfigureAwait(false);
            if (profile is null || profile.Handle is not null || profile.IsDeleted)
            {
                return profile;
            }

            var suggestion = UserProfile.SuggestHandle(profile.DisplayName);
            var taken = (await profiles.ListHandlesStartingWithAsync(suggestion, userId, cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
            var handle = suggestion;
            for (var n = 2; taken.Contains(handle); n++)
            {
                handle = suggestion + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            profile.ChangeHandle(handle, time.GetUtcNow());
            if (await profiles.TrySaveChangesAsync(cancellationToken).ConfigureAwait(false) || attempt >= 4)
            {
                return await profiles.FindAsync(userId, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Updates the given preferences; null values are left unchanged.</summary>
    public async Task<CurrentUser> UpdatePreferencesAsync(
        Guid userId, string? uiLanguage, CancellationToken cancellationToken,
        string? theme = null, bool? desktopNotifications = null, bool? notificationPreviews = null, string? emailNotifications = null,
        bool? shareEmailWithContacts = null)
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

        if (shareEmailWithContacts is { } share)
        {
            current.Profile.ChangeEmailSharing(share, now);
        }

        await profiles.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return current;
    }
}
