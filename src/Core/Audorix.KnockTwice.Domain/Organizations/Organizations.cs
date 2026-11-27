namespace Audorix.KnockTwice.Domain.Organizations;

/// <summary>
/// An organization: the tenant boundary for business data (ADR-008). Organization-owned resources
/// are only visible through an active <see cref="OrganizationMember"/> record.
/// </summary>
public sealed class Organization
{
    public const int NameMaxLength = 100;

    private Organization(Guid id, string name, Guid createdByUserId, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    // ---- Organization-specific configuration (Phase 11). Applies to this organization only.

    /// <summary>Whether guests may be invited to channels and keep their access. Switching it off suspends every guest.</summary>
    public bool GuestsEnabled { get; private set; } = true;

    /// <summary>Who may create channels in the organization's workspaces.</summary>
    public ChannelCreationPolicy ChannelCreation { get; private set; } = ChannelCreationPolicy.Members;

    /// <summary>When enabled, private chats started in this organization's context require a shared team.</summary>
    public bool PrivateChatsRequireSharedTeam { get; private set; }

    /// <summary>Verified-domain matching users may join this organization automatically. Always opt-in.</summary>
    public bool AutomaticEnrollmentEnabled { get; private set; }

    /// <summary>OpenID Connect settings are configured per organization; the client secret is protected before persistence.</summary>
    public string? SsoIssuer { get; private set; }
    public string? SsoClientId { get; private set; }
    public string? ProtectedSsoClientSecret { get; private set; }
    public bool SsoConfigured => !string.IsNullOrWhiteSpace(SsoIssuer) && !string.IsNullOrWhiteSpace(SsoClientId) && !string.IsNullOrWhiteSpace(ProtectedSsoClientSecret);

    public void Configure(bool? guestsEnabled, ChannelCreationPolicy? channelCreation, bool? privateChatsRequireSharedTeam, DateTimeOffset nowUtc)
    {
        GuestsEnabled = guestsEnabled ?? GuestsEnabled;
        ChannelCreation = channelCreation is { } policy ? Require.Defined(policy) : ChannelCreation;
        PrivateChatsRequireSharedTeam = privateChatsRequireSharedTeam ?? PrivateChatsRequireSharedTeam;
        UpdatedAtUtc = Require.Utc(nowUtc);
    }

    public void ConfigureAutomaticEnrollment(bool enabled, bool hasVerifiedDomain, DateTimeOffset nowUtc)
    {
        if (enabled && !hasVerifiedDomain)
            throw new DomainRuleException("automatic_enrollment_requires_verified_domain", "Automatic enrollment requires a verified domain.");
        AutomaticEnrollmentEnabled = enabled;
        UpdatedAtUtc = Require.Utc(nowUtc);
    }

    public void ConfigureSso(string? issuer, string? clientId, string? protectedClientSecret, bool hasVerifiedDomain, DateTimeOffset nowUtc)
    {
        if (!hasVerifiedDomain)
            throw new DomainRuleException("organization_sso_requires_verified_domain", "Organization SSO requires a verified domain.");
        if (string.IsNullOrWhiteSpace(issuer) || !Uri.TryCreate(issuer.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(uri.Host) || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new DomainRuleException("organization_sso_issuer_invalid", "The SSO issuer must be a valid HTTPS URL.");
        if (string.IsNullOrWhiteSpace(clientId) || clientId.Trim().Length > 256 || string.IsNullOrWhiteSpace(protectedClientSecret))
            throw new DomainRuleException("organization_sso_credentials_required", "An OIDC client ID and secret are required.");

        SsoIssuer = string.IsNullOrWhiteSpace(issuer) ? null : issuer.Trim().TrimEnd('/');
        SsoClientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim();
        ProtectedSsoClientSecret = protectedClientSecret;
        UpdatedAtUtc = Require.Utc(nowUtc);
    }

    public void ClearSsoConfiguration(DateTimeOffset nowUtc)
    {
        (SsoIssuer, SsoClientId, ProtectedSsoClientSecret) = (null, null, null);
        UpdatedAtUtc = Require.Utc(nowUtc);
    }

    public static Organization Create(Guid id, string name, Guid createdByUserId, DateTimeOffset nowUtc) =>
        new(Require.Id(id), Require.Name(name, NameMaxLength, "organization_name"), Require.Id(createdByUserId), Require.Utc(nowUtc));

    public void Rename(string name, DateTimeOffset nowUtc)
    {
        Name = Require.Name(name, NameMaxLength, "organization_name");
        UpdatedAtUtc = Require.Utc(nowUtc);
    }

    /// <summary>Created by the demo data loader; removed with everything in it when demo data is cleared.</summary>
    public bool IsDemo { get; private set; }

    public void MarkAsDemo() => IsDemo = true;

    // ---- Picture and background image (redesign 2026-11-24, owner mockup "Organization profile").

    /// <summary>Storage key of the square picture shown in lists and headers; null for the initials.</summary>
    public string? PhotoKey { get; private set; }

    public string? PhotoContentType { get; private set; }

    /// <summary>Increases with every change, so picture addresses can be cached for good.</summary>
    public int PhotoVersion { get; private set; }

    /// <summary>Storage key of the wide background image of the organization's header; null for none.</summary>
    public string? BannerKey { get; private set; }

    public string? BannerContentType { get; private set; }

    public int BannerVersion { get; private set; }

    /// <summary>Uses a new picture or background image; returns the key of the one it replaces (to delete), if any.</summary>
    public string? SetImage(OrganizationImage kind, string key, string contentType, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(contentType))
        {
            throw new DomainRuleException("image_invalid", "A stored image is required.");
        }

        string? previous;
        if (Require.Defined(kind) == OrganizationImage.Photo)
        {
            previous = PhotoKey;
            (PhotoKey, PhotoContentType) = (key, contentType);
            PhotoVersion++;
        }
        else
        {
            previous = BannerKey;
            (BannerKey, BannerContentType) = (key, contentType);
            BannerVersion++;
        }

        UpdatedAtUtc = Require.Utc(nowUtc);
        return previous;
    }

    /// <summary>Removes the picture or background image; returns the removed key, if any.</summary>
    public string? RemoveImage(OrganizationImage kind, DateTimeOffset nowUtc)
    {
        string? previous;
        if (Require.Defined(kind) == OrganizationImage.Photo)
        {
            previous = PhotoKey;
            if (previous is null)
            {
                return null;
            }

            (PhotoKey, PhotoContentType) = (null, null);
            PhotoVersion++;
        }
        else
        {
            previous = BannerKey;
            if (previous is null)
            {
                return null;
            }

            (BannerKey, BannerContentType) = (null, null);
            BannerVersion++;
        }

        UpdatedAtUtc = Require.Utc(nowUtc);
        return previous;
    }
}

/// <summary>The organization's two images.</summary>
public enum OrganizationImage
{
    /// <summary>Square picture (like a profile photo).</summary>
    Photo = 1,

    /// <summary>Wide background image of the header.</summary>
    Banner = 2,
}

/// <summary>Who may create channels (organization setting, Phase 11).</summary>
public enum ChannelCreationPolicy
{
    /// <summary>Any member of the workspace.</summary>
    Members = 1,

    /// <summary>Workspace administrators and organization owners/administrators only.</summary>
    Administrators = 2,
}

/// <summary>Organization-scoped roles. Distinct from installation roles held in Identity.</summary>
public enum OrganizationRole
{
    Member = 1,
    Administrator = 2,
    Owner = 3,
}

public enum MembershipStatus
{
    Active = 1,
    Revoked = 2,
}

/// <summary>
/// Membership of a user in an organization. Unique per organization/user; a revoked membership
/// is reactivated rather than duplicated. Revocation is the only way out (no hard delete), so
/// history-dependent rules in later phases can rely on it.
/// </summary>
public sealed class OrganizationMember
{
    private OrganizationMember(Guid id, Guid organizationId, Guid userId, OrganizationRole role, DateTimeOffset joinedAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        UserId = userId;
        Role = role;
        Status = MembershipStatus.Active;
        JoinedAtUtc = joinedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid UserId { get; private set; }

    public OrganizationRole Role { get; private set; }

    public MembershipStatus Status { get; private set; }

    public DateTimeOffset JoinedAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public bool IsActive => Status == MembershipStatus.Active;

    /// <summary>
    /// The Support Agent role (Phase 11): works the organization's website-chat inbox. It combines
    /// with the member/administrator/owner role instead of replacing it.
    /// </summary>
    public bool IsSupportAgent { get; private set; }

    public void SetSupportAgent(bool isAgent)
    {
        Require.ActiveMembership(IsActive);
        IsSupportAgent = isAgent;
    }

    public static OrganizationMember Join(Guid id, Guid organizationId, Guid userId, OrganizationRole role, DateTimeOffset nowUtc) =>
        new(Require.Id(id), Require.Id(organizationId), Require.Id(userId), Require.Defined(role), Require.Utc(nowUtc));

    public void ChangeRole(OrganizationRole role)
    {
        Require.ActiveMembership(IsActive);
        Role = Require.Defined(role);
    }

    public void Revoke(DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return;
        }

        Status = MembershipStatus.Revoked;
        RevokedAtUtc = Require.Utc(nowUtc);
        IsSupportAgent = false;
    }

    public void Reactivate(OrganizationRole role, DateTimeOffset nowUtc)
    {
        if (IsActive)
        {
            throw new DomainRuleException("already_member", "The user is already an active member.");
        }

        Status = MembershipStatus.Active;
        Role = Require.Defined(role);
        JoinedAtUtc = Require.Utc(nowUtc);
        RevokedAtUtc = null;
    }

    public bool Can(OrganizationPermission permission) => IsActive && OrganizationPermissions.IsGranted(Role, permission);
}

public enum InvitationStatus
{
    Pending = 1,
    Accepted = 2,
    Declined = 3,
    Revoked = 4,
}

/// <summary>
/// An invitation addressed to an email address, not to a user id. Creating one therefore never
/// reveals whether the address has an account. It is visible to the account whose confirmed email
/// matches, and joining requires that user's acceptance.
/// </summary>
public sealed class OrganizationInvitation
{
    public const int EmailMaxLength = 254;
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    private OrganizationInvitation(
        Guid id, Guid organizationId, string email, string normalizedEmail, OrganizationRole role, Guid invitedByUserId, DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        Email = email;
        NormalizedEmail = normalizedEmail;
        Role = role;
        InvitedByUserId = invitedByUserId;
        Status = InvitationStatus.Pending;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = createdAtUtc + Lifetime;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public string Email { get; private set; }

    public string NormalizedEmail { get; private set; }

    public OrganizationRole Role { get; private set; }

    public Guid InvitedByUserId { get; private set; }

    public InvitationStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? RespondedAtUtc { get; private set; }

    public static OrganizationInvitation Create(
        Guid id, Guid organizationId, string email, OrganizationRole role, Guid invitedByUserId, DateTimeOffset nowUtc)
    {
        var trimmed = email?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > EmailMaxLength || !trimmed.Contains('@', StringComparison.Ordinal))
        {
            throw new DomainRuleException("email_invalid", "A valid email address is required.");
        }

        if (role == OrganizationRole.Owner)
        {
            throw new DomainRuleException("invitation_role_invalid", "Ownership is granted by changing an existing member's role.");
        }

        return new OrganizationInvitation(
            Require.Id(id), Require.Id(organizationId), trimmed, Normalize(trimmed), Require.Defined(role), Require.Id(invitedByUserId), Require.Utc(nowUtc));
    }

    public static string Normalize(string email) => email.Trim().ToUpperInvariant();

    public bool IsOpen(DateTimeOffset nowUtc) => Status == InvitationStatus.Pending && nowUtc < ExpiresAtUtc;

    public void Accept(DateTimeOffset nowUtc) => Respond(InvitationStatus.Accepted, nowUtc);

    public void Decline(DateTimeOffset nowUtc) => Respond(InvitationStatus.Declined, nowUtc);

    public void Revoke(DateTimeOffset nowUtc) => Respond(InvitationStatus.Revoked, nowUtc);

    private void Respond(InvitationStatus status, DateTimeOffset nowUtc)
    {
        if (!IsOpen(Require.Utc(nowUtc)))
        {
            throw new DomainRuleException("invitation_not_open", "The invitation is no longer open.");
        }

        Status = status;
        RespondedAtUtc = nowUtc;
    }
}

/// <summary>A workspace always belongs to exactly one organization (Architecture §17.2).</summary>
public sealed class Workspace
{
    public const int NameMaxLength = 80;

    private Workspace(Guid id, Guid organizationId, string name, DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        Name = name;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public string Name { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Workspace Create(Guid id, Guid organizationId, string name, DateTimeOffset nowUtc) =>
        new(Require.Id(id), Require.Id(organizationId), Require.Name(name, NameMaxLength, "workspace_name"), Require.Utc(nowUtc));

    public void Rename(string name) => Name = Require.Name(name, NameMaxLength, "workspace_name");
}

public enum WorkspaceRole
{
    Member = 1,
    Administrator = 2,
}

/// <summary>
/// Workspace membership. Carries the organization id so the invariant "workspace members are
/// members of the workspace's organization" can be checked and enforced with a composite key.
/// </summary>
public sealed class WorkspaceMember
{
    private WorkspaceMember(Guid id, Guid organizationId, Guid workspaceId, Guid userId, WorkspaceRole role, DateTimeOffset joinedAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        WorkspaceId = workspaceId;
        UserId = userId;
        Role = role;
        Status = MembershipStatus.Active;
        JoinedAtUtc = joinedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public Guid UserId { get; private set; }

    public WorkspaceRole Role { get; private set; }

    public MembershipStatus Status { get; private set; }

    public DateTimeOffset JoinedAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public bool IsActive => Status == MembershipStatus.Active;

    /// <summary>Joins a workspace; requires the user's active membership in the same organization.</summary>
    public static WorkspaceMember Join(Guid id, Workspace workspace, OrganizationMember organizationMembership, WorkspaceRole role, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(organizationMembership);
        if (!organizationMembership.IsActive || organizationMembership.OrganizationId != workspace.OrganizationId)
        {
            throw new DomainRuleException("not_organization_member", "Workspace members must be active members of the workspace's organization.");
        }

        return new WorkspaceMember(Require.Id(id), workspace.OrganizationId, workspace.Id, organizationMembership.UserId, Require.Defined(role), Require.Utc(nowUtc));
    }

    public void Reactivate(OrganizationMember organizationMembership, WorkspaceRole role, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(organizationMembership);
        if (IsActive)
        {
            throw new DomainRuleException("already_member", "The user is already an active workspace member.");
        }

        if (!organizationMembership.IsActive || organizationMembership.OrganizationId != OrganizationId || organizationMembership.UserId != UserId)
        {
            throw new DomainRuleException("not_organization_member", "Workspace members must be active members of the workspace's organization.");
        }

        Status = MembershipStatus.Active;
        Role = Require.Defined(role);
        JoinedAtUtc = Require.Utc(nowUtc);
        RevokedAtUtc = null;
    }

    public void ChangeRole(WorkspaceRole role)
    {
        Require.ActiveMembership(IsActive);
        Role = Require.Defined(role);
    }

    public void Revoke(DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return;
        }

        Status = MembershipStatus.Revoked;
        RevokedAtUtc = Require.Utc(nowUtc);
    }
}

#pragma warning disable CA1711 // "Permission" is the domain term (Architecture §10), not a type-kind suffix.
public enum OrganizationPermission
#pragma warning restore CA1711
{
    /// <summary>Rename and configure the organization.</summary>
    Manage = 1,

    /// <summary>Invite members, revoke invitations, remove members (except owners).</summary>
    ManageMembers = 2,

    /// <summary>Change member roles, including granting and removing ownership.</summary>
    ManageRoles = 3,

    /// <summary>Create workspaces and manage any workspace's membership.</summary>
    ManageWorkspaces = 4,
}

/// <summary>Roles are collections of permissions (Architecture §10).</summary>
public static class OrganizationPermissions
{
    public static bool IsGranted(OrganizationRole role, OrganizationPermission permission) => permission switch
    {
        OrganizationPermission.ManageRoles => role == OrganizationRole.Owner,
        OrganizationPermission.Manage or OrganizationPermission.ManageMembers or OrganizationPermission.ManageWorkspaces =>
            role is OrganizationRole.Owner or OrganizationRole.Administrator,
        _ => false,
    };
}

internal static class Require
{
    public static Guid Id(Guid id) =>
        id != Guid.Empty ? id : throw new DomainRuleException("identifier_required", "An identifier is required.");

    public static DateTimeOffset Utc(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero ? value : throw new DomainRuleException("timestamp_not_utc", "Timestamps must be expressed in UTC.");

    public static T Defined<T>(T value)
        where T : struct, Enum =>
        Enum.IsDefined(value) ? value : throw new DomainRuleException("value_invalid", "The value is not valid.");

    public static string Name(string value, int maxLength, string codePrefix)
    {
        var normalized = string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0)
        {
            throw new DomainRuleException($"{codePrefix}_required", "A name is required.");
        }

        if (normalized.Length > maxLength)
        {
            throw new DomainRuleException($"{codePrefix}_too_long", $"The name may contain at most {maxLength} characters.");
        }

        if (normalized.Any(char.IsControl))
        {
            throw new DomainRuleException($"{codePrefix}_invalid", "The name contains invalid characters.");
        }

        return normalized;
    }

    public static void ActiveMembership(bool isActive)
    {
        if (!isActive)
        {
            throw new DomainRuleException("membership_not_active", "The membership is not active.");
        }
    }
}
