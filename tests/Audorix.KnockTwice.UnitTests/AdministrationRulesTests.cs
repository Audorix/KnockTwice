using Audorix.KnockTwice.Application.Administration;
using Audorix.KnockTwice.Application.Identity;
using Audorix.KnockTwice.Domain;
using Audorix.KnockTwice.Domain.Administration;
using Audorix.KnockTwice.Domain.Authorization;

namespace Audorix.KnockTwice.UnitTests;

public sealed class AdministrationRulesTests
{
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
        Assert.All(Permissions.All, p => Assert.True(Permissions.IsGrantedBy([Roles.InstallationAdministrator], p)));
        Assert.Empty(Permissions.ForRole("Unknown"));
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
