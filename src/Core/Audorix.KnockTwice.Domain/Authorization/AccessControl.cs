namespace Audorix.KnockTwice.Domain.Authorization;

public enum PermissionEffect { Allow = 1, Deny = 2 }

public sealed record RolePermissionDecision(string Key, PermissionEffect Effect);

/// <summary>A custom role bound to exactly one installation, organization, or workspace scope.</summary>
public sealed class AccessRole
{
    public const int NameMaxLength = 80;
    public const int DescriptionMaxLength = 300;

    private AccessRole() { }

    private AccessRole(Guid id, string name, string normalizedName, string description, string scope, Guid? organizationId, Guid? workspaceId, Guid createdByUserId, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        NormalizedName = normalizedName;
        Description = description;
        Scope = scope;
        OrganizationId = organizationId;
        WorkspaceId = workspaceId;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
        Permissions = [];
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Scope { get; private set; } = PermissionScopes.Installation;
    public Guid? OrganizationId { get; private set; }
    public Guid? WorkspaceId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public List<AccessRoleGrant> Permissions { get; private set; } = [];

    public static AccessRole Create(Guid id, string? name, string? description, Guid createdByUserId, DateTimeOffset nowUtc,
        string scope = PermissionScopes.Installation, Guid? organizationId = null, Guid? workspaceId = null)
    {
        ValidateScope(scope, organizationId, workspaceId);
        var normalized = NormalizeName(name);
        if (Roles.Active.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            throw new DomainRuleException("role_name_reserved", "Built-in role names cannot be used for custom roles.");
        }

        return new AccessRole(RequiredId(id), name!.Trim(), normalized, NormalizeDescription(description), scope, organizationId, workspaceId, RequiredId(createdByUserId), RequiredUtc(nowUtc));
    }

    public void Update(string? name, string? description, IEnumerable<RolePermissionDecision>? permissions)
    {
        var normalized = NormalizeName(name);
        if (Roles.Active.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            throw new DomainRuleException("role_name_reserved", "Built-in role names cannot be used for custom roles.");
        }

        Name = name!.Trim();
        NormalizedName = normalized;
        Description = NormalizeDescription(description);
        SetPermissions(permissions);
    }

    public void SetPermissions(IEnumerable<RolePermissionDecision>? permissions)
    {
        var entries = (permissions ?? []).Select(permission => new RolePermissionDecision(permission.Key?.Trim() ?? string.Empty, permission.Effect))
            .ToList();
        if (entries.GroupBy(permission => permission.Key, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new DomainRuleException("permission_duplicate", "A role may define each permission only once.");
        }

        var invalid = entries.FirstOrDefault(permission => PermissionCatalog.Find(permission.Key) is not { State: PermissionCatalog.Implemented } definition
            || !string.Equals(definition.Scope, Scope, StringComparison.Ordinal)
            || permission.Effect is not (PermissionEffect.Allow or PermissionEffect.Deny));
        if (invalid is not null)
        {
            throw new DomainRuleException("permission_not_assignable", "The permission or decision is invalid, planned, or outside the role's scope.");
        }

        Permissions.Clear();
        Permissions.AddRange(entries.Select(permission => AccessRoleGrant.Create(Id, permission.Key, permission.Effect)));
    }

    private static void ValidateScope(string scope, Guid? organizationId, Guid? workspaceId)
    {
        var valid = scope switch
        {
            PermissionScopes.Installation => organizationId is null && workspaceId is null,
            PermissionScopes.Organization => organizationId is not null && organizationId != Guid.Empty && workspaceId is null,
            PermissionScopes.Workspace => organizationId is not null && organizationId != Guid.Empty && workspaceId is not null && workspaceId != Guid.Empty,
            _ => false,
        };
        if (!valid) throw new DomainRuleException("access_scope_invalid", "Scope must match the supplied organization and workspace.");
    }

    private static Guid RequiredId(Guid id) => id != Guid.Empty ? id : throw new DomainRuleException("identifier_required", "An identifier is required.");
    private static DateTimeOffset RequiredUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero ? value : throw new DomainRuleException("timestamp_not_utc", "Timestamps must be expressed in UTC.");

    private static string NormalizeName(string? name)
    {
        var value = name?.Trim() ?? string.Empty;
        if (value.Length is 0 or > NameMaxLength || value.Any(c => !(char.IsLetterOrDigit(c) || c is ' ' or '-' or '_')))
        {
            throw new DomainRuleException("role_name_invalid", "Role names must use letters, numbers, spaces, hyphens or underscores.");
        }

        return value.ToUpperInvariant();
    }

    private static string NormalizeDescription(string? description)
    {
        var value = description?.Trim() ?? string.Empty;
        if (value.Length > DescriptionMaxLength)
        {
            throw new DomainRuleException("description_too_long", "The role description is too long.");
        }

        return value;
    }
}

public sealed class AccessRoleGrant
{
    private AccessRoleGrant() { }
    private AccessRoleGrant(Guid roleId, string permissionKey, PermissionEffect effect) { RoleId = roleId; PermissionKey = permissionKey; Effect = effect; }
    public Guid RoleId { get; private set; }
    public string PermissionKey { get; private set; } = string.Empty;
    public PermissionEffect Effect { get; private set; }
    public static AccessRoleGrant Create(Guid roleId, string permissionKey, PermissionEffect effect) => new(roleId, permissionKey, effect);
}

/// <summary>Installation-level group used to assign custom installation roles to multiple accounts.</summary>
public sealed class AccessGroup
{
    public const int NameMaxLength = 80;
    public const int DescriptionMaxLength = 300;

    private AccessGroup() { }
    private AccessGroup(Guid id, string name, string normalizedName, string description, string scope, Guid? organizationId, Guid? workspaceId, Guid createdByUserId, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        NormalizedName = normalizedName;
        Description = description;
        Scope = scope;
        OrganizationId = organizationId;
        WorkspaceId = workspaceId;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Scope { get; private set; } = PermissionScopes.Installation;
    public Guid? OrganizationId { get; private set; }
    public Guid? WorkspaceId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static AccessGroup Create(Guid id, string? name, string? description, Guid createdByUserId, DateTimeOffset nowUtc,
        string scope = PermissionScopes.Installation, Guid? organizationId = null, Guid? workspaceId = null)
    {
        var valid = scope switch
        {
            PermissionScopes.Installation => organizationId is null && workspaceId is null,
            PermissionScopes.Organization => organizationId is not null && organizationId != Guid.Empty && workspaceId is null,
            PermissionScopes.Workspace => organizationId is not null && organizationId != Guid.Empty && workspaceId is not null && workspaceId != Guid.Empty,
            _ => false,
        };
        if (!valid) throw new DomainRuleException("access_scope_invalid", "Scope must match the supplied organization and workspace.");
        var (display, normalized) = NormalizeName(name);
        return new AccessGroup(RequiredId(id), display, normalized, NormalizeDescription(description), scope, organizationId, workspaceId, RequiredId(createdByUserId), RequiredUtc(nowUtc));
    }

    public void Update(string? name, string? description)
    {
        var (display, normalized) = NormalizeName(name);
        Name = display;
        NormalizedName = normalized;
        Description = NormalizeDescription(description);
    }

    private static (string Display, string Normalized) NormalizeName(string? name)
    {
        var value = name?.Trim() ?? string.Empty;
        if (value.Length is 0 or > NameMaxLength || value.Any(char.IsControl))
        {
            throw new DomainRuleException("group_name_invalid", "A group name is required and must be 80 characters or fewer.");
        }

        return (value, value.ToUpperInvariant());
    }

    private static Guid RequiredId(Guid id) => id != Guid.Empty ? id : throw new DomainRuleException("identifier_required", "An identifier is required.");
    private static DateTimeOffset RequiredUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero ? value : throw new DomainRuleException("timestamp_not_utc", "Timestamps must be expressed in UTC.");

    private static string NormalizeDescription(string? description)
    {
        var value = description?.Trim() ?? string.Empty;
        if (value.Length > DescriptionMaxLength)
        {
            throw new DomainRuleException("description_too_long", "The group description is too long.");
        }

        return value;
    }
}

public sealed class AccessGroupMember
{
    private AccessGroupMember() { }
    private AccessGroupMember(Guid groupId, Guid userId, DateTimeOffset addedAtUtc, Guid addedByUserId)
    { GroupId = groupId; UserId = userId; AddedAtUtc = addedAtUtc; AddedByUserId = addedByUserId; }
    public Guid GroupId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset AddedAtUtc { get; private set; }
    public Guid AddedByUserId { get; private set; }
    public static AccessGroupMember Create(Guid groupId, Guid userId, DateTimeOffset nowUtc, Guid addedByUserId) =>
        new(RequiredId(groupId), RequiredId(userId), RequiredUtc(nowUtc), RequiredId(addedByUserId));
    private static Guid RequiredId(Guid id) => id != Guid.Empty ? id : throw new DomainRuleException("identifier_required", "An identifier is required.");
    private static DateTimeOffset RequiredUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero ? value : throw new DomainRuleException("timestamp_not_utc", "Timestamps must be expressed in UTC.");
}

public sealed class AccessRoleGroupAssignment
{
    private AccessRoleGroupAssignment() { }
    private AccessRoleGroupAssignment(Guid roleId, Guid groupId, DateTimeOffset assignedAtUtc, Guid assignedByUserId)
    { RoleId = roleId; GroupId = groupId; AssignedAtUtc = assignedAtUtc; AssignedByUserId = assignedByUserId; }
    public Guid RoleId { get; private set; }
    public Guid GroupId { get; private set; }
    public DateTimeOffset AssignedAtUtc { get; private set; }
    public Guid AssignedByUserId { get; private set; }
    public static AccessRoleGroupAssignment Create(Guid roleId, Guid groupId, DateTimeOffset nowUtc, Guid assignedByUserId) =>
        new(RequiredId(roleId), RequiredId(groupId), RequiredUtc(nowUtc), RequiredId(assignedByUserId));
    private static Guid RequiredId(Guid id) => id != Guid.Empty ? id : throw new DomainRuleException("identifier_required", "An identifier is required.");
    private static DateTimeOffset RequiredUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero ? value : throw new DomainRuleException("timestamp_not_utc", "Timestamps must be expressed in UTC.");
}
