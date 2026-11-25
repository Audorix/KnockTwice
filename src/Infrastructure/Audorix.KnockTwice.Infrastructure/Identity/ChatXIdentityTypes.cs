using Microsoft.AspNetCore.Identity;

namespace Audorix.KnockTwice.Infrastructure.Identity;

/// <summary>
/// ASP.NET Core Identity account record (credentials, email, lockout, external logins).
/// Profile data lives in the domain <see cref="Domain.Users.UserProfile"/> with the same identifier.
/// </summary>
public sealed class ChatXUser : IdentityUser<Guid>
{
    /// <summary>
    /// Set while an administrator has disabled the account (Phase 9). The account is also locked
    /// out indefinitely so every Identity sign-in path refuses it.
    /// </summary>
    public DateTimeOffset? DisabledAtUtc { get; set; }

    /// <summary>
    /// A website visitor's stand-in account (Phase 10): it has no email or password, cannot sign
    /// in, is never discoverable and only takes part in its own widget conversations.
    /// </summary>
    public bool IsVisitor { get; set; }

    /// <summary>
    /// Set when the account was deleted (Phase 13): email, user name, password, logins, roles and
    /// tokens are gone and the row only keeps earlier messages attributable to "Deleted user".
    /// </summary>
    public DateTimeOffset? DeletedAtUtc { get; set; }

    /// <summary>A demo person created by the demo data loader (removed when demo data is cleared).</summary>
    public bool IsDemo { get; set; }
}

public sealed class ChatXRole : IdentityRole<Guid>
{
    public ChatXRole()
    {
    }

    public ChatXRole(string roleName)
        : base(roleName)
    {
    }
}
