using Audorix.KnockTwice.Application.Administration;
using Audorix.KnockTwice.Application.Attachments;
using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Conversations;
using Audorix.KnockTwice.Application.Identity;
using Audorix.KnockTwice.Application.Realtime;
using Audorix.KnockTwice.Domain.Organizations;

namespace Audorix.KnockTwice.Application.Organizations;

/// <summary>A conversation of an organization that is about to be deleted, with who can see it now.</summary>
public sealed record OrganizationConversation(Guid ConversationId, long LastSequence, IReadOnlyList<Guid> ParticipantIds);

/// <summary>Removes an organization and everything it owns (implemented by Infrastructure).</summary>
public interface IOrganizationRemoval
{
    Task<IReadOnlyList<OrganizationConversation>> ListConversationsAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>Storage keys of every file in the organization's conversations whose content still exists.</summary>
    Task<IReadOnlyList<string>> ListAttachmentKeysAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>Stand-in accounts of the website visitors of the organization's widgets.</summary>
    Task<IReadOnlyList<Guid>> ListVisitorUserIdsAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes, in one transaction, the organization's history, files records and conversations,
    /// then the organization with its members, invitations, workspaces, teams, guests and widgets.
    /// </summary>
    Task DeleteAsync(Guid organizationId, CancellationToken cancellationToken);
}

/// <summary>
/// Deleting an organization (Phase 13). Owners delete their organization after typing its name;
/// installation administrators can too. Everything the organization owns goes: conversations and
/// channels with their messages and files, workspaces, teams, guests, invitations, widgets and
/// their website visitors. It cannot be undone. Files are removed from storage first; if storage
/// is unavailable nothing is deleted.
/// </summary>
public sealed class OrganizationDeletionService(
    IOrganizationStore organizations,
    IOrganizationRemoval removal,
    IAttachmentStorage storage,
    IAccountErasure erasure,
    IRealtimePublisher publisher,
    RealtimeService realtime,
    AuditService audit,
    TimeProvider time)
{
    /// <summary>An owner deletes their organization; <paramref name="confirmName"/> must repeat its name.</summary>
    public async Task DeleteAsOwnerAsync(Guid userId, Guid organizationId, string? confirmName, CancellationToken cancellationToken)
    {
        var membership = await organizations.FindMemberAsync(organizationId, userId, cancellationToken).ConfigureAwait(false);
        var organization = membership is { IsActive: true }
            ? await organizations.FindOrganizationAsync(organizationId, cancellationToken).ConfigureAwait(false)
            : null;
        if (organization is null)
        {
            throw new ResourceNotFoundException("organization_not_found");
        }

        if (membership!.Role != OrganizationRole.Owner)
        {
            throw new ForbiddenException();
        }

        RequireName(organization, confirmName);
        await DeleteAsync(organization, cancellationToken).ConfigureAwait(false);
        await audit.RecordAsync(AuditActions.OrganizationDeleted, userId, "organization", organizationId.ToString(),
            new Dictionary<string, object?> { ["name"] = organization.Name }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsAdministratorAsync(Guid actorId, Guid organizationId, string? confirmName, CancellationToken cancellationToken)
    {
        var organization = await organizations.FindOrganizationAsync(organizationId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("organization_not_found");
        RequireName(organization, confirmName);
        await DeleteAsync(organization, cancellationToken).ConfigureAwait(false);
        await audit.RecordAsync(AuditActions.AdminOrganizationDeleted, actorId, "organization", organizationId.ToString(),
            new Dictionary<string, object?> { ["name"] = organization.Name }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static void RequireName(Organization organization, string? confirmName)
    {
        if (!string.Equals(confirmName?.Trim(), organization.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new RequestValidationException("confirmName", "confirmation_mismatch");
        }
    }

    private async Task DeleteAsync(Organization organization, CancellationToken cancellationToken)
    {
        var conversations = await removal.ListConversationsAsync(organization.Id, cancellationToken).ConfigureAwait(false);
        var visitors = await removal.ListVisitorUserIdsAsync(organization.Id, cancellationToken).ConfigureAwait(false);
        // A storage failure stops here (503 attachment_storage_unavailable) with nothing deleted in the database.
        foreach (var key in await removal.ListAttachmentKeysAsync(organization.Id, cancellationToken).ConfigureAwait(false))
        {
            await storage.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
        }

        // The organization's picture and background image go with it.
        foreach (var key in new[] { organization.PhotoKey, organization.BannerKey }.OfType<string>())
        {
            await storage.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
        }

        await removal.DeleteAsync(organization.Id, cancellationToken).ConfigureAwait(false);

        // Visitors only existed for this organization's widgets: erase their stand-in accounts.
        var now = time.GetUtcNow();
        foreach (var visitorUserId in visitors)
        {
            await erasure.EraseAsync(visitorUserId, now, cancellationToken).ConfigureAwait(false);
            await erasure.TryRemoveUnusedAsync(visitorUserId, cancellationToken).ConfigureAwait(false);
        }

        foreach (var conversation in conversations)
        {
            foreach (var userId in conversation.ParticipantIds)
            {
                await realtime.RevokeConversationAccessAsync(userId, conversation.ConversationId, cancellationToken).ConfigureAwait(false);
            }

            await publisher.PublishToUsersAsync(conversation.ParticipantIds,
                new ConversationUpdated(conversation.ConversationId, "removed", conversation.LastSequence), cancellationToken).ConfigureAwait(false);
        }
    }
}
