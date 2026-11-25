using Audorix.KnockTwice.Application.Administration;
using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Conversations;
using Audorix.KnockTwice.Application.Organizations;
using Audorix.KnockTwice.Application.PrivateChats;
using Audorix.KnockTwice.Application.Sessions;
using Audorix.KnockTwice.Domain.Authorization;
using Audorix.KnockTwice.Domain.Organizations;

namespace Audorix.KnockTwice.Application.Identity;

/// <summary>
/// Erases what identifies a person, in one transaction (implemented by Infrastructure). The account
/// and profile rows stay so earlier messages keep an author ("Deleted user"); email, user name,
/// password, external logins, roles, tokens, sessions, contacts, requests, blocks, team and guest
/// records, widget responder entries, support assignments, unsent uploads and visitor records go.
/// </summary>
public interface IAccountErasure
{
    Task EraseAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>Removes the account rows entirely when nothing refers to them (never used accounts); false otherwise.</summary>
    Task<bool> TryRemoveUnusedAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Stand-in accounts of anonymous website visitors last seen before <paramref name="cutoff"/>.</summary>
    Task<IReadOnlyList<Guid>> ListInactiveAnonymousVisitorsAsync(DateTimeOffset cutoff, int limit, CancellationToken cancellationToken);

    /// <summary>Accounts (not visitors, not deleted) whose email was never confirmed, created before <paramref name="cutoff"/>.</summary>
    Task<IReadOnlyList<Guid>> ListStaleUnconfirmedAsync(DateTimeOffset cutoff, int limit, CancellationToken cancellationToken);
}

/// <summary>What stands in the way of deleting an account.</summary>
public sealed record AccountDeletionCheck(IReadOnlyList<Organization> SoleOwnerOf, bool LastAdministrator, bool RequiresPassword)
{
    public bool CanDelete => SoleOwnerOf.Count == 0 && !LastAdministrator;
}

/// <summary>
/// Account deletion (Phase 13, owner decisions of 2026-11-23). People delete their own account
/// after confirming with their password (or, without one, their email address); installation
/// administrators delete accounts too. Messages stay and show "Deleted user". Deletion is refused
/// while the person is the only owner of an organization (nothing is handed over automatically)
/// or the installation's last enabled administrator. Every step can run again, so a deletion
/// interrupted half-way is completed by repeating it.
/// </summary>
public sealed class AccountDeletionService(
    IUserAccountManager accounts,
    IOrganizationStore organizations,
    OrganizationService organizationService,
    ConversationService conversations,
    SessionService sessions,
    PrivateChatService privateChats,
    IAccountErasure erasure,
    IAdminDirectory adminDirectory,
    AuditService audit,
    TimeProvider time)
{
    public async Task<AccountDeletionCheck> CheckAsync(Guid userId, CancellationToken cancellationToken)
    {
        var account = await RequireAccountAsync(userId, cancellationToken).ConfigureAwait(false);
        var soleOwner = new List<Organization>();
        foreach (var (organization, membership) in await organizations.ListActiveMembershipsAsync(userId, cancellationToken).ConfigureAwait(false))
        {
            if (membership.Role == OrganizationRole.Owner
                && !(await organizations.ListActiveMembersAsync(organization.Id, cancellationToken).ConfigureAwait(false))
                    .Any(m => m.Role == OrganizationRole.Owner && m.UserId != userId))
            {
                soleOwner.Add(organization);
            }
        }

        var lastAdministrator = account.Roles.Contains(Roles.InstallationAdministrator)
            && await adminDirectory.CountEnabledInRoleAsync(Roles.InstallationAdministrator, cancellationToken).ConfigureAwait(false) <= 1;
        return new AccountDeletionCheck(soleOwner.OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), lastAdministrator, account.HasPassword);
    }

    /// <summary>The signed-in person deletes their own account.</summary>
    public async Task DeleteOwnAsync(Guid userId, string? password, string? confirmEmail, CancellationToken cancellationToken)
    {
        var account = await RequireAccountAsync(userId, cancellationToken).ConfigureAwait(false);
        if (account.HasPassword)
        {
            if (!await accounts.CheckPasswordAsync(userId, password ?? string.Empty, cancellationToken).ConfigureAwait(false))
            {
                throw new RequestValidationException("password", "password_incorrect");
            }
        }
        else if (!string.Equals(confirmEmail?.Trim(), account.Email, StringComparison.OrdinalIgnoreCase))
        {
            throw new RequestValidationException("confirmEmail", "confirmation_mismatch");
        }

        await DeleteAsync(userId, cancellationToken).ConfigureAwait(false);
        await audit.RecordAsync(AuditActions.AccountDeleted, userId, "user", userId.ToString(), cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>An installation administrator deletes someone else's account (their own goes through <see cref="DeleteOwnAsync"/>).</summary>
    public async Task DeleteByAdministratorAsync(Guid actorId, Guid userId, CancellationToken cancellationToken)
    {
        if (actorId == userId)
        {
            throw new ConflictException("cannot_delete_self");
        }

        await DeleteAsync(userId, cancellationToken).ConfigureAwait(false);
        await audit.RecordAsync(AuditActions.UserDeleted, actorId, "user", userId.ToString(), cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Retention (Phase 13): deletes an account nobody uses, removing it entirely when nothing refers to it.</summary>
    public async Task<bool> DeleteForRetentionAsync(Guid userId, CancellationToken cancellationToken)
    {
        var account = await accounts.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (account is null || account.IsDeleted || !(await CheckAsync(userId, cancellationToken).ConfigureAwait(false)).CanDelete)
        {
            return false;
        }

        await DeleteAsync(userId, cancellationToken).ConfigureAwait(false);
        if (!account.IsVisitor)
        {
            await erasure.TryRemoveUnusedAsync(userId, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private async Task DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var check = await CheckAsync(userId, cancellationToken).ConfigureAwait(false);
        if (check.SoleOwnerOf.Count > 0)
        {
            throw new ConflictException("sole_owner");
        }

        if (check.LastAdministrator)
        {
            throw new ConflictException("last_administrator");
        }

        // Signed out everywhere first: realtime connections close with the sessions, and private
        // chats (memory only) end on both sides.
        await sessions.RevokeAllAsync(userId, null, cancellationToken).ConfigureAwait(false);
        await privateChats.EndForUserAsync(userId, cancellationToken).ConfigureAwait(false);

        // Leave every organization through the normal paths, so others see an ordinary departure.
        foreach (var (organization, _) in await organizations.ListActiveMembershipsAsync(userId, cancellationToken).ConfigureAwait(false))
        {
            await organizationService.RemoveMemberAsAdministratorAsync(organization.Id, userId, cancellationToken).ConfigureAwait(false);
            await conversations.RemoveFromOrganizationAsync(userId, organization.Id, userId, cancellationToken).ConfigureAwait(false);
        }

        await conversations.LeaveEverythingAsync(userId, cancellationToken).ConfigureAwait(false);
        await erasure.EraseAsync(userId, time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
    }

    private async Task<UserAccount> RequireAccountAsync(Guid userId, CancellationToken cancellationToken) =>
        await accounts.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false) is { IsDeleted: false } account
            ? account
            : throw new ResourceNotFoundException("user_not_found");
}
