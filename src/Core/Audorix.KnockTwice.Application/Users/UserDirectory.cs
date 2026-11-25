using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Contacts;
using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Application.Users;

/// <summary>Public face of a user shown to other users. Never contains the email address.</summary>
/// <param name="Deleted">The account was deleted; the name is a neutral placeholder (Phase 13).</param>
/// <param name="PhotoVersion">The profile photo's version, or 0 when the person shows initials (redesign 2026-11).</param>
public sealed record UserSummary(Guid Id, string DisplayName, string Initials, bool Deleted = false, int PhotoVersion = 0);

/// <summary>Read model over accounts, profiles and organization membership (implemented by Infrastructure).</summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<Guid, UserSummary>> GetSummariesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>The id of the account with this exact email whose address is confirmed, otherwise null.</summary>
    Task<Guid?> FindConfirmedUserIdByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>True when the user exists and can take part in interactions (email confirmed, not locked out).</summary>
    Task<bool> IsActiveUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Users sharing at least one active organization with <paramref name="userId"/> whose display name contains <paramref name="text"/>.</summary>
    Task<IReadOnlyList<UserSummary>> SearchColleaguesAsync(Guid userId, string text, int limit, CancellationToken cancellationToken);

    Task<bool> SharesActiveOrganizationAsync(Guid userId, Guid otherUserId, CancellationToken cancellationToken);
}

/// <summary>
/// User discovery defaults (decision register "Public user discovery"). Both are on by default and
/// never expose email addresses: exact-email lookup finds one confirmed account; name search only
/// covers people who share an active organization with the caller.
/// </summary>
public sealed class DiscoverySettings
{
    public const string SectionName = "Discovery";

    public bool AllowEmailLookup { get; set; } = true;

    public bool AllowOrganizationDirectory { get; set; } = true;
}

public sealed class UserDirectoryService(IUserDirectory directory, IContactStore contacts, IOptionsSnapshot<DiscoverySettings> settings)
{
    public const int MinimumQueryLength = 2;
    public const int MaximumQueryLength = 100;
    public const int ResultLimit = 20;

    public async Task<IReadOnlyList<UserSummary>> SearchAsync(Guid userId, string? query, CancellationToken cancellationToken)
    {
        var text = query?.Trim() ?? string.Empty;
        if (text.Length < MinimumQueryLength)
        {
            throw new RequestValidationException("q", "query_too_short");
        }

        if (text.Length > MaximumQueryLength)
        {
            throw new RequestValidationException("q", "query_too_long");
        }

        var hidden = await contacts.ListBlockPartnersAsync(userId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<UserSummary> found;
        if (text.Contains('@', StringComparison.Ordinal))
        {
            if (!settings.Value.AllowEmailLookup)
            {
                return [];
            }

            var id = await directory.FindConfirmedUserIdByEmailAsync(text, cancellationToken).ConfigureAwait(false);
            found = id is { } match ? (await directory.GetSummariesAsync([match], cancellationToken).ConfigureAwait(false)).Values.ToList() : [];
        }
        else
        {
            if (!settings.Value.AllowOrganizationDirectory)
            {
                return [];
            }

            found = await directory.SearchColleaguesAsync(userId, text, ResultLimit + hidden.Count, cancellationToken).ConfigureAwait(false);
        }

        return found.Where(u => u.Id != userId && !hidden.Contains(u.Id)).Take(ResultLimit).ToList();
    }

    /// <summary>
    /// A user's summary, visible only with a reason to know them: themselves, a contact, a pending
    /// request either way, or a shared active organization. Blocked users see "not found".
    /// </summary>
    public async Task<UserSummary> GetAsync(Guid userId, Guid targetUserId, CancellationToken cancellationToken)
    {
        var visible = targetUserId == userId;
        if (!visible)
        {
            var blocks = await contacts.ListBlockPartnersAsync(userId, cancellationToken).ConfigureAwait(false);
            visible = !blocks.Contains(targetUserId) && (
                await contacts.AreContactsOrPendingAsync(userId, targetUserId, cancellationToken).ConfigureAwait(false)
                || await directory.SharesActiveOrganizationAsync(userId, targetUserId, cancellationToken).ConfigureAwait(false));
        }

        if (visible && (await directory.GetSummariesAsync([targetUserId], cancellationToken).ConfigureAwait(false)).TryGetValue(targetUserId, out var summary))
        {
            return summary;
        }

        throw new ResourceNotFoundException("user_not_found");
    }
}
