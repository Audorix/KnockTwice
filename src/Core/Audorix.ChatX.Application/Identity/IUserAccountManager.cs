using Audorix.ChatX.Domain.Users;

namespace Audorix.ChatX.Application.Identity;

/// <summary>
/// Account port implemented by Infrastructure on top of ASP.NET Core Identity. It owns
/// credentials, email confirmation, one-time tokens, external logins and role membership.
/// </summary>
public interface IUserAccountManager
{
    Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the account and its profile atomically. <paramref name="password"/> is null for
    /// accounts created through an external provider, which then supply <paramref name="externalLogin"/>.
    /// </summary>
    Task<AccountOperationResult> CreateAsync(
        NewAccount account,
        UserProfile profile,
        string? password,
        ExternalLoginReference? externalLogin,
        CancellationToken cancellationToken);

    Task<string> CreateEmailConfirmationTokenAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken);

    Task<string> CreatePasswordResetTokenAsync(Guid userId, CancellationToken cancellationToken);

    Task<AccountOperationResult> ResetPasswordAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken);

    Task<AccountOperationResult> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken);

    /// <summary>Adds the user to an active role (see <see cref="Domain.Authorization.Roles.Active"/>), creating the role if needed.</summary>
    Task<AccountOperationResult> AddToRoleAsync(Guid userId, string role, CancellationToken cancellationToken);

    /// <summary>Whether at least one account holds <paramref name="role"/>.</summary>
    Task<bool> AnyInRoleAsync(string role, CancellationToken cancellationToken);

    Task RemoveFromRoleAsync(Guid userId, string role, CancellationToken cancellationToken);

    /// <summary>A disabled account cannot sign in (password or external) until enabled again.</summary>
    Task SetDisabledAsync(Guid userId, bool disabled, CancellationToken cancellationToken);

    /// <summary>Administrators may confirm an address on the user's behalf.</summary>
    Task MarkEmailConfirmedAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Whether <paramref name="password"/> is the account's current password (no lockout counting).</summary>
    Task<bool> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken);
}

/// <summary>Whether an address may register while registration is invitation-only.</summary>
public interface IRegistrationInvitations
{
    Task<bool> HasOpenInvitationAsync(string email, CancellationToken cancellationToken);
}

public sealed record NewAccount(Guid Id, string Email, bool EmailConfirmed);

/// <param name="Provider">Authentication scheme name, e.g. <c>Google</c>.</param>
/// <param name="ProviderKey">Stable subject identifier issued by the provider.</param>
/// <param name="DisplayName">Provider display name.</param>
public sealed record ExternalLoginReference(string Provider, string ProviderKey, string DisplayName);

public sealed record UserAccount(
    Guid Id,
    string Email,
    bool EmailConfirmed,
    bool HasPassword,
    IReadOnlyList<string> ExternalProviders,
    IReadOnlyList<string> Roles,
    bool IsDeleted = false,
    bool IsVisitor = false);

/// <summary>
/// Outcome of an account operation. <see cref="Errors"/> holds stable codes such as
/// <c>password_too_short</c> or <c>duplicate_email</c>, grouped by field.
/// </summary>
public sealed record AccountOperationResult(bool Succeeded, IReadOnlyDictionary<string, string[]> Errors)
{
    public static AccountOperationResult Success { get; } = new(true, new Dictionary<string, string[]>());

    public static AccountOperationResult Failure(string field, params string[] codes) =>
        new(false, new Dictionary<string, string[]> { [field] = codes });

    public bool HasError(string code) => Errors.Values.Any(codes => codes.Contains(code, StringComparer.Ordinal));
}
