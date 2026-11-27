using Audorix.KnockTwice.Application.Administration;
using Audorix.KnockTwice.Application.Identity;
using Audorix.KnockTwice.Domain;
using Audorix.KnockTwice.Domain.Administration;
using Audorix.KnockTwice.Domain.Authorization;

namespace Audorix.KnockTwice.UnitTests;

public sealed class AdministrationRulesTests
{
    [Fact]
    public void Permission_catalog_has_unique_scoped_keys_and_matches_the_active_authorization_set()
    {
        Assert.Equal(PermissionCatalog.All.Count, PermissionCatalog.All.Select(permission => permission.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            Permissions.All.Order(StringComparer.Ordinal),
            PermissionCatalog.Available.Select(permission => permission.Key).Order(StringComparer.Ordinal));
        Assert.All(PermissionCatalog.All, permission =>
        {
            Assert.Contains(permission.Scope, new[] { PermissionScopes.Installation, PermissionScopes.Organization, PermissionScopes.Workspace });
            Assert.Contains(permission.State, new[] { PermissionCatalog.Implemented, PermissionCatalog.Planned });
            Assert.False(string.IsNullOrWhiteSpace(permission.Description));
        });
    }

    [Fact]
    public void Organization_permission_is_cataloged_but_not_granted_by_installation_roles()
    {
        var permission = Assert.Single(PermissionCatalog.All, item => item.Key == "private_chats.use");

        Assert.Equal(PermissionCatalog.Implemented, permission.State);
        Assert.Equal(PermissionScopes.Organization, permission.Scope);
        Assert.False(Permissions.IsGrantedBy([Roles.InstallationAdministrator], permission.Key));
        Assert.False(Permissions.IsGrantedBy([Roles.Auditor], permission.Key));
    }

    [Fact]
    public void Granular_installation_permissions_keep_legacy_access_until_configured_then_use_exact_grants()
    {
        Assert.True(Permissions.IsEffectivelyGranted([Roles.InstallationAdministrator], "settings.read", [], [], []));
        Assert.False(Permissions.IsEffectivelyGranted([Roles.InstallationAdministrator], "settings.read", [], [], ["settings.read"]));
        Assert.True(Permissions.IsEffectivelyGranted([], "settings.read", ["settings.read"], [], ["settings.read"]));
        Assert.False(Permissions.IsEffectivelyGranted([Roles.InstallationAdministrator], "settings.read", ["settings.read"], ["settings.read"], ["settings.read"]));
    }

    [Fact]
    public void Audit_details_never_keep_secrets_message_text_or_oversized_values()
    {
        var json = AuditService.Serialize(new Dictionary<string, object?>
        {
            ["settings"] = new[] { "Email:Smtp:Password", "Email:FromName" },
            ["password"] = "hunter2",
            ["serverToken"] = "pm-123",
            ["secret_key"] = "abc",
            ["body"] = "a private message",
            ["reason"] = new string('x', 500),
            ["count"] = 3,
        })!;

        Assert.Contains("Email:Smtp:Password", json, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", json, StringComparison.Ordinal);
        Assert.DoesNotContain("pm-123", json, StringComparison.Ordinal);
        Assert.DoesNotContain("abc", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private message", json, StringComparison.Ordinal);
        Assert.True(json.Length < 400);
        Assert.Contains("\"count\":3", json, StringComparison.Ordinal);
        Assert.Null(AuditService.Serialize(null));
    }

    [Theory]
    [InlineData("auth.sign_in")]
    [InlineData("admin.user.role_granted")]
    public void Audit_actions_are_dotted_lower_case_names(string action) =>
        Assert.Equal(action, AuditEntry.Create(Guid.NewGuid(), DateTimeOffset.UtcNow, action, true, null, null, null, null, null).Action);

    [Theory]
    [InlineData("")]
    [InlineData("Auth.SignIn")]
    [InlineData("auth sign in")]
    public void Malformed_audit_actions_are_rejected(string action) =>
        Assert.Throws<DomainRuleException>(() => AuditEntry.Create(Guid.NewGuid(), DateTimeOffset.UtcNow, action, true, null, null, null, null, null));

    [Fact]
    public void Auditors_read_but_never_manage()
    {
        Assert.True(Permissions.IsGrantedBy([Roles.Auditor], Permissions.AuditRead));
        Assert.True(Permissions.IsGrantedBy([Roles.Auditor], Permissions.UsersRead));
        Assert.False(Permissions.IsGrantedBy([Roles.Auditor], Permissions.UsersManage));
        Assert.False(Permissions.IsGrantedBy([Roles.Auditor], Permissions.InstallationManage));
        Assert.True(Permissions.IsEffectivelyGranted([Roles.Auditor], "settings.read", [], [], []));
        Assert.False(Permissions.IsEffectivelyGranted([Roles.Auditor], "settings.manage", [], [], []));
        Assert.All(Permissions.All, p => Assert.True(Permissions.IsEffectivelyGranted([Roles.InstallationAdministrator], p, [], [], [])));
        Assert.Empty(Permissions.ForRole("Unknown"));
    }

    [Fact]
    public void Custom_access_roles_accept_only_implemented_installation_permissions()
    {
        var now = DateTimeOffset.UtcNow;
        var role = AccessRole.Create(Guid.NewGuid(), "Support Readers", "Can review users", Guid.NewGuid(), now);
        role.SetPermissions([new RolePermissionDecision("users.read", PermissionEffect.Allow)]);
        Assert.Equal([("users.read", PermissionEffect.Allow)], role.Permissions.Select(item => (item.PermissionKey, item.Effect)));

        var wrongScope = Assert.Throws<DomainRuleException>(() => role.SetPermissions([new RolePermissionDecision("private_chats.use", PermissionEffect.Deny)]));
        Assert.Equal("permission_not_assignable", wrongScope.Code);
        var organizationId = Guid.NewGuid();
        var orgRole = AccessRole.Create(Guid.NewGuid(), "Private Chat Users", null, Guid.NewGuid(), now, PermissionScopes.Organization, organizationId);
        orgRole.SetPermissions([new RolePermissionDecision("private_chats.use", PermissionEffect.Allow)]);
        Assert.Equal(PermissionScopes.Organization, orgRole.Scope);
        Assert.Equal(organizationId, orgRole.OrganizationId);
        Assert.Throws<DomainRuleException>(() => AccessRole.Create(Guid.NewGuid(), "Bad scope", null, Guid.NewGuid(), now, PermissionScopes.Organization));
        var duplicate = Assert.Throws<DomainRuleException>(() => role.SetPermissions([
            new RolePermissionDecision("users.read", PermissionEffect.Allow), new RolePermissionDecision("users.read", PermissionEffect.Deny)]));
        Assert.Equal("permission_duplicate", duplicate.Code);
        Assert.Throws<DomainRuleException>(() => AccessRole.Create(Guid.NewGuid(), "Auditor", null, Guid.NewGuid(), now));
        Assert.Throws<DomainRuleException>(() => AccessRole.Create(Guid.NewGuid(), "Role & Admin", null, Guid.NewGuid(), now));
    }

    [Fact]
    public void Custom_access_groups_normalize_names_and_reject_empty_or_controlled_names()
    {
        var group = AccessGroup.Create(Guid.NewGuid(), "  Support   Agents ", null, Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Equal("Support   Agents", group.Name);
        Assert.Equal("SUPPORT   AGENTS", group.NormalizedName);
        Assert.Throws<DomainRuleException>(() => AccessGroup.Create(Guid.NewGuid(), "", null, Guid.NewGuid(), DateTimeOffset.UtcNow));
        Assert.Throws<DomainRuleException>(() => AccessGroup.Create(Guid.NewGuid(), "Bad\nGroup", null, Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(true, "open", "open")]
    [InlineData(true, "INVITATION", "invitation")]
    [InlineData(true, null, "open")]
    [InlineData(false, "open", "closed")]
    public void Registration_mode_respects_the_older_switch(bool allow, string? mode, string expected) =>
        Assert.Equal(expected, new AccountSettings { AllowRegistration = allow, RegistrationMode = mode! }.EffectiveRegistrationMode);

    [Fact]
    public void Unknown_registration_modes_are_invalid() =>
        Assert.Contains(new AccountSettings { RegistrationMode = "sometimes" }.Validate(), e => e.Contains("RegistrationMode", StringComparison.Ordinal));
}
