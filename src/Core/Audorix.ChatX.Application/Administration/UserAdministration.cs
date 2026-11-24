using Audorix.ChatX.Application.Common;
using Audorix.ChatX.Application.Identity;
using Audorix.ChatX.Application.Organizations;
using Audorix.ChatX.Application.Sessions;
using Audorix.ChatX.Application.Users;
using Audorix.ChatX.Domain.Authorization;
using Audorix.ChatX.Domain.Organizations;

namespace Audorix.ChatX.Application.Administration;

/// <summary>An account as administrators see it: identity and state, never message content.</summary>
public sealed record AdminUserView(
    Guid Id, string Email, string DisplayName, string Initials, bool EmailConfirmed, bool Disabled, bool LockedOut,
    IReadOnlyList<string> Roles, DateTimeOffset CreatedAtUtc, DateTimeOffset? LastSignInAtUtc, int ActiveSessions, int Organizations);

public sealed record AdminUserPage(IReadOnlyList<AdminUserView> Items, int Total);

public sealed record AdminOrganizationView(Guid Id, string Name, DateTimeOffset CreatedAtUtc, int Members, int Owners);

public sealed record AdminOrganizationPage(IReadOnlyList<AdminOrganizationView> Items, int Total);

public sealed record AdminMemberView(UserSummary User, string Email, OrganizationRole Role, DateTimeOffset JoinedAtUtc);

public sealed record AdminOrganizationDetail(AdminOrganizationView Organization, IReadOnlyList<AdminMemberView> Members);

/// <summary>Read model for administration screens.</summary>
public interface IAdminDirectory
{
    /// <param name="filter"><c>all</c>, <c>enabled</c>, <c>disabled</c>, <c>unconfirmed</c> or a role name (older clients).</param>
    /// <param name="role">Redesign 2026-11: an installation role name, or <c>none</c> for accounts without one.</param>
    /// <param name="sort"><c>nameAsc</c> (default) or <c>nameDesc</c>.</param>
    Task<AdminUserPage> SearchUsersAsync(
        string? query, string? filter, int skip, int take, CancellationToken cancellationToken, string? role = null, string? sort = null);

    Task<AdminUserView?> FindUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <param name="sort"><c>nameAsc</c> (default), <c>nameDesc</c> or <c>members</c> (most first).</param>
    Task<AdminOrganizationPage> SearchOrganizationsAsync(string? query, int skip, int take, CancellationToken cancellationToken, string? sort = null);

    Task<AdminOrganizationDetail?> FindOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>Enabled accounts holding the role.</summary>
    Task<int> CountEnabledInRoleAsync(string role, CancellationToken cancellationToken);
}

/// <summary>
/// Installation administration of accounts (Phase 9, owner decision): view, disable/enable, end
/// sessions, resend or mark confirmation, grant/revoke installation roles. No password setting, no
/// deletion and no access to conversations. Every change is audited. An installation always keeps
/// at least one enabled administrator, and administrators cannot disable themselves.
/// </summary>
public sealed class UserAdministrationService(
    IAdminDirectory directory,
    IUserAccountManager accounts,
    SessionService sessions,
    AccountService accountService,
    AuditService audit)
{
    public const int MaxPageSize = 100;

    public static readonly IReadOnlyList<string> UserSorts = ["nameAsc", "nameDesc"];

    public static readonly IReadOnlyList<string> OrganizationSorts = ["nameAsc", "nameDesc", "members"];

    public Task<AdminUserPage> SearchAsync(
        string? query, string? filter, int skip, int take, CancellationToken cancellationToken, string? role = null, string? sort = null)
    {
        if (take is < 1 or > MaxPageSize || skip < 0)
        {
            throw new RequestValidationException("take", "limit_out_of_range");
        }

        return directory.SearchUsersAsync(query?.Trim(), filter?.Trim(), skip, take, cancellationToken, role?.Trim(), RequireSort(sort, UserSorts));
    }

    /// <summary>A known sort (case-insensitive) or null for the default; anything else is 400 <c>sort_invalid</c>.</summary>
    internal static string? RequireSort(string? sort, IReadOnlyList<string> allowed) =>
        string.IsNullOrWhiteSpace(sort)
            ? null
            : allowed.FirstOrDefault(s => string.Equals(s, sort.Trim(), StringComparison.OrdinalIgnoreCase))
              ?? throw new RequestValidationException("sort", "sort_invalid");

    public async Task<AdminUserView> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        await directory.FindUserAsync(userId, cancellationToken).ConfigureAwait(false) ?? throw new ResourceNotFoundException("user_not_found");

    public async Task<AdminUserView> SetDisabledAsync(Guid actorId, Guid userId, bool disabled, CancellationToken cancellationToken)
    {
        var target = await GetAsync(userId, cancellationToken).ConfigureAwait(false);
        if (disabled && userId == actorId)
        {
            throw new ConflictException("cannot_disable_self");
        }

        if (disabled && target.Roles.Contains(Roles.InstallationAdministrator) && !target.Disabled)
        {
            await RequireAnotherAdministratorAsync(cancellationToken).ConfigureAwait(false);
        }

        await accounts.SetDisabledAsync(userId, disabled, cancellationToken).ConfigureAwait(false);
        if (disabled)
        {
            // Signed out everywhere at once; realtime connections close with the sessions.
            await sessions.RevokeAllAsync(userId, null, cancellationToken).ConfigureAwait(false);
        }

        await audit.RecordAsync(disabled ? AuditActions.UserDisabled : AuditActions.UserEnabled, actorId, "user", userId.ToString(), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return await GetAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RevokeSessionsAsync(Guid actorId, Guid userId, CancellationToken cancellationToken)
    {
        await GetAsync(userId, cancellationToken).ConfigureAwait(false);
        var revoked = await sessions.RevokeAllAsync(userId, null, cancellationToken).ConfigureAwait(false);
        await audit.RecordAsync(AuditActions.UserSessionsRevoked, actorId, "user", userId.ToString(),
            new Dictionary<string, object?> { ["count"] = revoked }, cancellationToken: cancellationToken).ConfigureAwait(false);
        return revoked;
    }

    public async Task<AdminUserView> ConfirmEmailAsync(Guid actorId, Guid userId, CancellationToken cancellationToken)
    {
        var target = await GetAsync(userId, cancellationToken).ConfigureAwait(false);
        if (!target.EmailConfirmed)
        {
            await accounts.MarkEmailConfirmedAsync(userId, cancellationToken).ConfigureAwait(false);
            await audit.RecordAsync(AuditActions.UserEmailConfirmed, actorId, "user", userId.ToString(), cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return await GetAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    public async Task ResendConfirmationAsync(Guid actorId, Guid userId, CancellationToken cancellationToken)
    {
        var target = await GetAsync(userId, cancellationToken).ConfigureAwait(false);
        if (target.EmailConfirmed)
        {
            throw new ConflictException("email_already_confirmed");
        }

        await accountService.SendConfirmationAsAdministratorAsync(userId, cancellationToken).ConfigureAwait(false);
        await audit.RecordAsync(AuditActions.UserConfirmationSent, actorId, "user", userId.ToString(), cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<AdminUserView> GrantRoleAsync(Guid actorId, Guid userId, string? role, CancellationToken cancellationToken)
    {
        var name = RequireRole(role);
        await GetAsync(userId, cancellationToken).ConfigureAwait(false);
        var result = await accounts.AddToRoleAsync(userId, name, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new RequestValidationException(result.Errors);
        }

        await audit.RecordAsync(AuditActions.RoleGranted, actorId, "user", userId.ToString(), new Dictionary<string, object?> { ["role"] = name },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return await GetAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AdminUserView> RevokeRoleAsync(Guid actorId, Guid userId, string? role, CancellationToken cancellationToken)
    {
        var name = RequireRole(role);
        var target = await GetAsync(userId, cancellationToken).ConfigureAwait(false);
        if (!target.Roles.Contains(name))
        {
            return target;
        }

        if (name == Roles.InstallationAdministrator && !target.Disabled)
        {
            await RequireAnotherAdministratorAsync(cancellationToken).ConfigureAwait(false);
        }

        await accounts.RemoveFromRoleAsync(userId, name, cancellationToken).ConfigureAwait(false);
        await audit.RecordAsync(AuditActions.RoleRevoked, actorId, "user", userId.ToString(), new Dictionary<string, object?> { ["role"] = name },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return await GetAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    private static string RequireRole(string? role) =>
        Roles.Active.FirstOrDefault(r => string.Equals(r, role?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new RequestValidationException("role", "role_not_active");

    private async Task RequireAnotherAdministratorAsync(CancellationToken cancellationToken)
    {
        if (await directory.CountEnabledInRoleAsync(Roles.InstallationAdministrator, cancellationToken).ConfigureAwait(false) <= 1)
        {
            throw new ConflictException("last_administrator");
        }
    }
}

/// <summary>
/// Installation administration of organizations: view all, rename, change member roles and remove
/// members, with the same "keep an owner" rule as organization owners have. Audited.
/// </summary>
public sealed class OrganizationAdministrationService(IAdminDirectory directory, OrganizationService organizations, AuditService audit)
{
    public Task<AdminOrganizationPage> SearchAsync(string? query, int skip, int take, CancellationToken cancellationToken, string? sort = null)
    {
        if (take is < 1 or > UserAdministrationService.MaxPageSize || skip < 0)
        {
            throw new RequestValidationException("take", "limit_out_of_range");
        }

        return directory.SearchOrganizationsAsync(query?.Trim(), skip, take, cancellationToken,
            UserAdministrationService.RequireSort(sort, UserAdministrationService.OrganizationSorts));
    }

    public async Task<AdminOrganizationDetail> GetAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await directory.FindOrganizationAsync(organizationId, cancellationToken).ConfigureAwait(false)
        ?? throw new ResourceNotFoundException("organization_not_found");

    public async Task<AdminOrganizationDetail> RenameAsync(Guid actorId, Guid organizationId, string? name, CancellationToken cancellationToken)
    {
        var before = await GetAsync(organizationId, cancellationToken).ConfigureAwait(false);
        await organizations.RenameAsAdministratorAsync(organizationId, name, cancellationToken).ConfigureAwait(false);
        await audit.RecordAsync(AuditActions.OrganizationRenamed, actorId, "organization", organizationId.ToString(),
            new Dictionary<string, object?> { ["from"] = before.Organization.Name, ["to"] = name?.Trim() }, cancellationToken: cancellationToken).ConfigureAwait(false);
        return await GetAsync(organizationId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AdminOrganizationDetail> ChangeMemberRoleAsync(
        Guid actorId, Guid organizationId, Guid userId, OrganizationRole role, CancellationToken cancellationToken)
    {
        await organizations.ChangeRoleAsAdministratorAsync(organizationId, userId, role, cancellationToken).ConfigureAwait(false);
        await audit.RecordAsync(AuditActions.OrganizationMemberRoleChanged, actorId, "organization", organizationId.ToString(),
            new Dictionary<string, object?> { ["user"] = userId.ToString(), ["role"] = role.ToString().ToLowerInvariant() }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return await GetAsync(organizationId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes the member; the caller also ends their organization conversations (see the endpoint).</summary>
    public async Task RemoveMemberAsync(Guid actorId, Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        await organizations.RemoveMemberAsAdministratorAsync(organizationId, userId, cancellationToken).ConfigureAwait(false);
        await audit.RecordAsync(AuditActions.OrganizationMemberRemoved, actorId, "organization", organizationId.ToString(),
            new Dictionary<string, object?> { ["user"] = userId.ToString() }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Live settings persisted in the database (the API layers them into configuration).</summary>
public interface IInstallationSettingsStore
{
    Task<IReadOnlyList<Domain.Administration.InstallationSetting>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Sets (or, with a null value, removes) the given keys in one transaction.</summary>
    Task SaveAsync(IReadOnlyDictionary<string, string?> values, Guid? byUserId, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}
