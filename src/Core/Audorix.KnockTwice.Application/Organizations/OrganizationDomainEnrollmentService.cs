using Audorix.KnockTwice.Application.Identity;
using Audorix.KnockTwice.Domain;
using Audorix.KnockTwice.Domain.Organizations;

namespace Audorix.KnockTwice.Application.Organizations;

/// <summary>Enrolls confirmed accounts only when an organization owner explicitly enabled a verified domain.</summary>
public sealed class OrganizationDomainEnrollmentService(
    IUserAccountManager accounts,
    IOrganizationStore store,
    TimeProvider time)
{
    public async Task<bool> HasActiveMembershipAsync(Guid organizationId, Guid userId, CancellationToken ct)
    {
        var member = await store.FindMemberAsync(organizationId, userId, ct).ConfigureAwait(false);
        return member?.IsActive == true;
    }

    public async Task EnrollIfEligibleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var account = await accounts.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (account is not { EmailConfirmed: true, IsDeleted: false, IsVisitor: false }) return;
        var at = account.Email.LastIndexOf('@');
        if (at <= 0 || at == account.Email.Length - 1) return;

        string domain;
        try { domain = VerifiedOrganizationDomain.NormalizeDomain(account.Email[(at + 1)..]); }
        catch (DomainRuleException) { return; }

        var organizations = await store.ListAutomaticEnrollmentOrganizationsAsync(domain, cancellationToken).ConfigureAwait(false);
        foreach (var organization in organizations)
        {
            var now = time.GetUtcNow();
            var member = await store.FindMemberAsync(organization.Id, userId, cancellationToken).ConfigureAwait(false);
            if (member is { IsActive: true }) continue;
            if (member is null)
            {
                member = OrganizationMember.Join(Guid.CreateVersion7(now), organization.Id, userId, OrganizationRole.Member, now);
                store.Add(member);
            }
            else member.Reactivate(OrganizationRole.Member, now);

            // Automatic enrollment grants access only to the first (default) workspace.
            var workspace = (await store.ListWorkspacesAsync(organization.Id, cancellationToken).ConfigureAwait(false))
                .OrderBy(w => w.CreatedAtUtc).ThenBy(w => w.Id).FirstOrDefault();
            if (workspace is not null)
            {
                var workspaceMember = await store.FindWorkspaceMemberAsync(workspace.Id, userId, cancellationToken).ConfigureAwait(false);
                if (workspaceMember is null)
                    store.Add(WorkspaceMember.Join(Guid.CreateVersion7(now), workspace, member, WorkspaceRole.Member, now));
                else if (!workspaceMember.IsActive)
                    workspaceMember.Reactivate(member, WorkspaceRole.Member, now);
            }

            // Concurrent login attempts are idempotent; a uniqueness race does not block sign-in.
            await store.TrySaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
