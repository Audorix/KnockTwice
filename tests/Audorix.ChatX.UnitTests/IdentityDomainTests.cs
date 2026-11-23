using Audorix.ChatX.Domain;
using Audorix.ChatX.Domain.Authorization;
using Audorix.ChatX.Domain.Sessions;
using Audorix.ChatX.Domain.Users;

namespace Audorix.ChatX.UnitTests;

public sealed class IdentityDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("  Ada   Lovelace ", "Ada Lovelace", "AL")]
    [InlineData("José", "José", "J")]
    [InlineData("maría de la o", "maría de la o", "MO")]
    [InlineData("🙂 Bob", "🙂 Bob", "B")]
    public void Profile_normalizes_display_name_and_derives_initials(string input, string expectedName, string expectedInitials)
    {
        var profile = UserProfile.Create(Guid.NewGuid(), input, "en", Now);

        Assert.Equal(expectedName, profile.DisplayName);
        Assert.Equal(expectedInitials, profile.Initials);
    }

    [Theory]
    [InlineData("", "display_name_required")]
    [InlineData("   ", "display_name_required")]
    [InlineData("bad\u0007name", "display_name_invalid")]
    public void Profile_rejects_invalid_display_names(string input, string code)
    {
        Assert.Equal(code, Assert.Throws<DomainRuleException>(() => UserProfile.Create(Guid.NewGuid(), input, "en", Now)).Code);
    }

    [Fact]
    public void Profile_rejects_overlong_display_name()
    {
        var tooLong = new string('a', UserProfile.DisplayNameMaxLength + 1);
        Assert.Equal("display_name_too_long", Assert.Throws<DomainRuleException>(() => UserProfile.Create(Guid.NewGuid(), tooLong, "en", Now)).Code);
    }

    [Fact]
    public void Session_is_active_until_expiry_and_sliding_touch_extends_it()
    {
        var session = UserSession.Start(Guid.NewGuid(), Guid.NewGuid(), "Firefox on Linux", persistent: false, Now, TimeSpan.FromHours(1));

        Assert.True(session.IsActive(Now.AddMinutes(59)));
        Assert.False(session.IsActive(Now.AddHours(1)));

        session.Touch(Now.AddMinutes(30), TimeSpan.FromHours(1));
        Assert.True(session.IsActive(Now.AddMinutes(89)));
        Assert.Equal(Now.AddMinutes(30), session.LastSeenAtUtc);
    }

    [Fact]
    public void Revoked_session_stays_revoked_and_cannot_be_revived_by_activity()
    {
        var session = UserSession.Start(Guid.NewGuid(), Guid.NewGuid(), "x", persistent: true, Now, TimeSpan.FromDays(14));

        session.Revoke(Now.AddMinutes(1));
        session.Revoke(Now.AddMinutes(5));
        session.Touch(Now.AddMinutes(6), TimeSpan.FromDays(14));

        Assert.Equal(Now.AddMinutes(1), session.RevokedAtUtc);
        Assert.False(session.IsActive(Now.AddMinutes(6)));
        Assert.Equal(Now, session.LastSeenAtUtc);
    }

    [Fact]
    public void Session_description_is_bounded()
    {
        var session = UserSession.Start(Guid.NewGuid(), Guid.NewGuid(), new string('x', 500), false, Now, TimeSpan.FromHours(1));
        Assert.Equal(UserSession.DescriptionMaxLength, session.Description.Length);
    }

    [Fact]
    public void Permissions_come_only_from_active_roles()
    {
        Assert.True(Permissions.IsGrantedBy([Roles.InstallationAdministrator], Permissions.InstallationRead));
        Assert.False(Permissions.IsGrantedBy(["Member", "installationadministrator"], Permissions.InstallationRead));
        Assert.False(Permissions.IsGrantedBy([], Permissions.InstallationRead));
    }
}
