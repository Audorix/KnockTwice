using System.Net.Mail;
using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Conversations;
using Audorix.KnockTwice.Application.Identity;
using Audorix.KnockTwice.Application.Users;
using Audorix.KnockTwice.Domain.Conversations;
using Audorix.KnockTwice.Domain.Organizations;

namespace Audorix.KnockTwice.Application.Organizations;

public sealed record GuestInvitationView(GuestInvitation Invitation, string ChannelName, UserSummary? InvitedBy);

public sealed record MyGuestInvitationView(GuestInvitation Invitation, string OrganizationName, string ChannelName, UserSummary? InvitedBy);

public sealed record GuestView(OrganizationGuest Guest, UserSummary User, IReadOnlyList<Conversation> Channels);

/// <summary>
/// Guest access (Phase 11, owner decision "channel-scoped guests"). Owners and administrators invite
/// an email address to one channel; the invitee accepts like an organization invitation. A guest
/// sees only the channels they were invited to (history from their join), cannot browse channels,
/// open direct conversations, see the member directory or manage anything. An organization can
/// switch guests off, which suspends every guest's access at once.
/// </summary>
public sealed class GuestService(
    IOrganizationStore store,
    OrganizationService organizations,
    ConversationService conversations,
    IConversationStore conversationStore,
    IUserAccountManager accounts,
    IUserDirectory directory,
    TimeProvider time)
{
    /// <summary>Invites an email address into a channel. The answer is the same whether or not the address has an account.</summary>
    public async Task<GuestInvitation> InviteAsync(Guid userId, Guid organizationId, Guid channelId, string? email, CancellationToken cancellationToken)
    {
        var (organization, membership) = await organizations.RequireMembershipAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        OrganizationService.RequirePermission(membership, OrganizationPermission.ManageMembers);
        if (!organization.GuestsEnabled)
        {
            throw new FeatureDisabledException("guests_disabled");
        }

        await conversations.RequireOrganizationChannelAsync(organizationId, channelId, cancellationToken).ConfigureAwait(false);
        var address = email?.Trim() ?? string.Empty;
        if (!MailAddress.TryCreate(address, out var parsed) || parsed.Address != address || !address.Contains('.', StringComparison.Ordinal))
        {
            throw new RequestValidationException("email", "email_invalid");
        }

        var now = time.GetUtcNow();
        var normalized = OrganizationInvitation.Normalize(address);
        if ((await store.ListPendingGuestInvitationsAsync(organizationId, cancellationToken).ConfigureAwait(false))
            .Any(i => i.NormalizedEmail == normalized && i.ConversationId == channelId && i.IsOpen(now)))
        {
            throw new ConflictException("invitation_pending");
        }

        var invitation = GuestInvitation.Create(Guid.CreateVersion7(now), organizationId, channelId, address, userId, now);
        store.Add(invitation);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        return invitation;
    }

    public async Task<IReadOnlyList<GuestInvitationView>> ListInvitationsAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken)
    {
        var (_, membership) = await organizations.RequireMembershipAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        OrganizationService.RequirePermission(membership, OrganizationPermission.ManageMembers);
        var now = time.GetUtcNow();
        var invitations = (await store.ListPendingGuestInvitationsAsync(organizationId, cancellationToken).ConfigureAwait(false)).Where(i => i.IsOpen(now)).ToList();
        var inviters = await directory.GetSummariesAsync(invitations.Select(i => i.InvitedByUserId).Distinct().ToList(), cancellationToken).ConfigureAwait(false);
        var views = new List<GuestInvitationView>();
        foreach (var invitation in invitations)
        {
            var channel = await conversationStore.FindAsync(invitation.ConversationId, cancellationToken).ConfigureAwait(false);
            views.Add(new GuestInvitationView(invitation, channel?.Title ?? string.Empty, inviters.GetValueOrDefault(invitation.InvitedByUserId)));
        }

        return views.OrderBy(v => v.Invitation.Email, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task RevokeInvitationAsync(Guid userId, Guid organizationId, Guid invitationId, CancellationToken cancellationToken)
    {
        var (_, membership) = await organizations.RequireMembershipAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        OrganizationService.RequirePermission(membership, OrganizationPermission.ManageMembers);
        var invitation = await store.FindGuestInvitationAsync(invitationId, cancellationToken).ConfigureAwait(false);
        if (invitation is null || invitation.OrganizationId != organizationId || !invitation.IsOpen(time.GetUtcNow()))
        {
            throw new ResourceNotFoundException("invitation_not_found");
        }

        invitation.Revoke(time.GetUtcNow());
        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Open guest invitations addressed to the caller's confirmed email address.</summary>
    public async Task<IReadOnlyList<MyGuestInvitationView>> ListMineAsync(Guid userId, CancellationToken cancellationToken)
    {
        var account = await accounts.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (account is not { EmailConfirmed: true })
        {
            return [];
        }

        var now = time.GetUtcNow();
        var invitations = (await store.ListPendingGuestInvitationsForEmailAsync(OrganizationInvitation.Normalize(account.Email), cancellationToken).ConfigureAwait(false))
            .Where(i => i.IsOpen(now)).ToList();
        var inviters = await directory.GetSummariesAsync(invitations.Select(i => i.InvitedByUserId).Distinct().ToList(), cancellationToken).ConfigureAwait(false);
        var views = new List<MyGuestInvitationView>();
        foreach (var invitation in invitations)
        {
            var organization = await store.FindOrganizationAsync(invitation.OrganizationId, cancellationToken).ConfigureAwait(false);
            var channel = await conversationStore.FindAsync(invitation.ConversationId, cancellationToken).ConfigureAwait(false);
            if (organization is { GuestsEnabled: true } && channel is not null)
            {
                views.Add(new MyGuestInvitationView(invitation, organization.Name, channel.Title ?? string.Empty, inviters.GetValueOrDefault(invitation.InvitedByUserId)));
            }
        }

        return views;
    }

    /// <summary>Accepts: the caller becomes a guest of the organization and joins the channel. Members join channels as members, not guests.</summary>
    public async Task<Guid> AcceptAsync(Guid userId, Guid invitationId, CancellationToken cancellationToken)
    {
        var invitation = await RequireOwnInvitationAsync(userId, invitationId, cancellationToken).ConfigureAwait(false);
        var organization = await store.FindOrganizationAsync(invitation.OrganizationId, cancellationToken).ConfigureAwait(false);
        if (organization is not { GuestsEnabled: true })
        {
            throw new FeatureDisabledException("guests_disabled");
        }

        if (await store.FindMemberAsync(organization.Id, userId, cancellationToken).ConfigureAwait(false) is { IsActive: true })
        {
            throw new ConflictException("already_member");
        }

        var now = time.GetUtcNow();
        var guest = await store.FindGuestAsync(organization.Id, userId, cancellationToken).ConfigureAwait(false);
        if (guest is null)
        {
            store.Add(OrganizationGuest.Join(Guid.CreateVersion7(now), organization.Id, userId, invitation.InvitedByUserId, now));
        }
        else
        {
            guest.Reactivate(invitation.InvitedByUserId, now);
        }

        invitation.Accept(now);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        await conversations.AddGuestToChannelAsync(invitation.InvitedByUserId, invitation.ConversationId, userId, cancellationToken).ConfigureAwait(false);
        return invitation.ConversationId;
    }

    public async Task DeclineAsync(Guid userId, Guid invitationId, CancellationToken cancellationToken)
    {
        var invitation = await RequireOwnInvitationAsync(userId, invitationId, cancellationToken).ConfigureAwait(false);
        invitation.Decline(time.GetUtcNow());
        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Active guests and the channels they take part in (owners and administrators).</summary>
    public async Task<IReadOnlyList<GuestView>> ListGuestsAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken)
    {
        var (_, membership) = await organizations.RequireMembershipAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        OrganizationService.RequirePermission(membership, OrganizationPermission.ManageMembers);
        var guests = await store.ListActiveGuestsAsync(organizationId, cancellationToken).ConfigureAwait(false);
        var users = await directory.GetSummariesAsync(guests.Select(g => g.UserId).ToList(), cancellationToken).ConfigureAwait(false);
        var views = new List<GuestView>();
        foreach (var guest in guests.Where(g => users.ContainsKey(g.UserId)))
        {
            var channels = new List<Conversation>();
            foreach (var id in await conversationStore.ListActiveConversationIdsInOrganizationAsync(organizationId, guest.UserId, cancellationToken).ConfigureAwait(false))
            {
                if (await conversationStore.FindAsync(id, cancellationToken).ConfigureAwait(false) is { Type: ConversationType.Channel } channel)
                {
                    channels.Add(channel);
                }
            }

            views.Add(new GuestView(guest, users[guest.UserId], channels));
        }

        return views.OrderBy(v => v.User.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Ends a guest's access to the organization and removes them from all its channels.</summary>
    public async Task RemoveGuestAsync(Guid userId, Guid organizationId, Guid guestUserId, CancellationToken cancellationToken)
    {
        var (_, membership) = await organizations.RequireMembershipAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        OrganizationService.RequirePermission(membership, OrganizationPermission.ManageMembers);
        var guest = await store.FindGuestAsync(organizationId, guestUserId, cancellationToken).ConfigureAwait(false);
        if (guest is not { IsActive: true })
        {
            throw new ResourceNotFoundException("guest_not_found");
        }

        guest.Revoke(time.GetUtcNow());
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        await conversations.RemoveFromOrganizationAsync(userId, organizationId, guestUserId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GuestInvitation> RequireOwnInvitationAsync(Guid userId, Guid invitationId, CancellationToken cancellationToken)
    {
        var account = await accounts.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        var invitation = await store.FindGuestInvitationAsync(invitationId, cancellationToken).ConfigureAwait(false);
        if (account is not { EmailConfirmed: true } || invitation is null
            || invitation.NormalizedEmail != OrganizationInvitation.Normalize(account.Email)
            || !invitation.IsOpen(time.GetUtcNow()))
        {
            throw new ResourceNotFoundException("invitation_not_found");
        }

        return invitation;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (!await store.TrySaveChangesAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException("conflict");
        }
    }
}
