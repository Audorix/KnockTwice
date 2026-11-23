using Audorix.ChatX.Application.Identity;
using Audorix.ChatX.Domain.Authorization;
using Audorix.ChatX.Domain.Users;
using Audorix.ChatX.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Audorix.ChatX.Infrastructure.Identity;

/// <summary>
/// <see cref="IUserAccountManager"/> on top of ASP.NET Core Identity's <see cref="UserManager{TUser}"/>.
/// Identity error codes are translated to stable snake_case codes grouped by request field.
/// </summary>
internal sealed class IdentityUserAccountManager(
    UserManager<ChatXUser> users,
    RoleManager<ChatXRole> roles,
    ChatXDbContext db) : IUserAccountManager
{
    public async Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        return user is null ? null : await ToAccountAsync(user).ConfigureAwait(false);
    }

    public async Task<UserAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(email).ConfigureAwait(false);
        return user is null ? null : await ToAccountAsync(user).ConfigureAwait(false);
    }

    public async Task<AccountOperationResult> CreateAsync(
        NewAccount account,
        UserProfile profile,
        string? password,
        ExternalLoginReference? externalLogin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(profile);
        var user = new ChatXUser
        {
            Id = account.Id,
            UserName = account.Email,
            Email = account.Email,
            EmailConfirmed = account.EmailConfirmed,
        };

        // Account, profile and external login commit together or not at all.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        db.UserProfiles.Add(profile);
        try
        {
            var created = password is null
                ? await users.CreateAsync(user).ConfigureAwait(false)
                : await users.CreateAsync(user, password).ConfigureAwait(false);
            if (!created.Succeeded)
            {
                Detach(profile);
                return Translate(created, "password");
            }

            if (externalLogin is not null)
            {
                var linked = await users.AddLoginAsync(user, new UserLoginInfo(externalLogin.Provider, externalLogin.ProviderKey, externalLogin.Provider))
                    .ConfigureAwait(false);
                if (!linked.Succeeded)
                {
                    Detach(profile);
                    return Translate(linked, "externalLogin");
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return AccountOperationResult.Success;
        }
        catch (DbUpdateException)
        {
            // A unique index on the normalized email/user name rejected a concurrent duplicate.
            Detach(profile);
            db.ChangeTracker.Clear();
            return AccountOperationResult.Failure("email", "duplicate_email");
        }
    }

    public async Task<string> CreateEmailConfirmationTokenAsync(Guid userId, CancellationToken cancellationToken) =>
        await users.GenerateEmailConfirmationTokenAsync(await RequireAsync(userId).ConfigureAwait(false)).ConfigureAwait(false);

    public async Task<bool> ConfirmEmailAsync(Guid userId, string token, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        return user is not null && (await users.ConfirmEmailAsync(user, token).ConfigureAwait(false)).Succeeded;
    }

    public async Task<string> CreatePasswordResetTokenAsync(Guid userId, CancellationToken cancellationToken) =>
        await users.GeneratePasswordResetTokenAsync(await RequireAsync(userId).ConfigureAwait(false)).ConfigureAwait(false);

    public async Task<AccountOperationResult> ResetPasswordAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        if (user is null)
        {
            return AccountOperationResult.Failure("token", "user_not_found");
        }

        var result = Translate(await users.ResetPasswordAsync(user, token, newPassword).ConfigureAwait(false), "newPassword");
        if (result.Succeeded && !user.EmailConfirmed)
        {
            // Completing a reset proves control of the mailbox.
            user.EmailConfirmed = true;
            await users.UpdateAsync(user).ConfigureAwait(false);
        }

        return result;
    }

    public async Task<AccountOperationResult> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        var user = await RequireAsync(userId).ConfigureAwait(false);
        if (!await users.HasPasswordAsync(user).ConfigureAwait(false))
        {
            return AccountOperationResult.Failure("currentPassword", "password_not_set");
        }

        return Translate(await users.ChangePasswordAsync(user, currentPassword, newPassword).ConfigureAwait(false), "newPassword");
    }

    public async Task<AccountOperationResult> AddToRoleAsync(Guid userId, string role, CancellationToken cancellationToken)
    {
        if (!Roles.Active.Contains(role, StringComparer.Ordinal))
        {
            return AccountOperationResult.Failure("role", "role_not_active");
        }

        var user = await RequireAsync(userId).ConfigureAwait(false);
        if (!await roles.RoleExistsAsync(role).ConfigureAwait(false))
        {
            var createdRole = await roles.CreateAsync(new ChatXRole(role)).ConfigureAwait(false);
            if (!createdRole.Succeeded)
            {
                return Translate(createdRole, "role");
            }
        }

        if (await users.IsInRoleAsync(user, role).ConfigureAwait(false))
        {
            return AccountOperationResult.Success;
        }

        return Translate(await users.AddToRoleAsync(user, role).ConfigureAwait(false), "role");
    }

    public async Task<bool> AnyInRoleAsync(string role, CancellationToken cancellationToken) =>
        await roles.RoleExistsAsync(role).ConfigureAwait(false)
        && (await users.GetUsersInRoleAsync(role).ConfigureAwait(false)).Count > 0;

    public async Task RemoveFromRoleAsync(Guid userId, string role, CancellationToken cancellationToken)
    {
        var user = await RequireAsync(userId).ConfigureAwait(false);
        if (await users.IsInRoleAsync(user, role).ConfigureAwait(false))
        {
            Ensure(await users.RemoveFromRoleAsync(user, role).ConfigureAwait(false));
        }
    }

    public async Task SetDisabledAsync(Guid userId, bool disabled, CancellationToken cancellationToken)
    {
        var user = await RequireAsync(userId).ConfigureAwait(false);
        if (disabled)
        {
            user.DisabledAtUtc ??= DateTimeOffset.UtcNow;
            user.LockoutEnabled = true;
            user.LockoutEnd = DateTimeOffset.MaxValue;
        }
        else
        {
            user.DisabledAtUtc = null;
            user.LockoutEnd = null;
            user.AccessFailedCount = 0;
        }

        Ensure(await users.UpdateAsync(user).ConfigureAwait(false));

        // A new security stamp invalidates authentication cookies at their next validation.
        Ensure(await users.UpdateSecurityStampAsync(user).ConfigureAwait(false));
    }

    public async Task MarkEmailConfirmedAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await RequireAsync(userId).ConfigureAwait(false);
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            Ensure(await users.UpdateAsync(user).ConfigureAwait(false));
        }
    }

    public async Task<bool> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        return user is { DeletedAtUtc: null } && !string.IsNullOrEmpty(password) && await users.CheckPasswordAsync(user, password).ConfigureAwait(false);
    }

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("The account update failed: " + string.Join(", ", result.Errors.Select(e => e.Code)));
        }
    }

    private async Task<ChatXUser> RequireAsync(Guid userId) =>
        await users.FindByIdAsync(userId.ToString()).ConfigureAwait(false)
        ?? throw new InvalidOperationException("The user does not exist.");

    private async Task<UserAccount> ToAccountAsync(ChatXUser user)
    {
        var logins = await users.GetLoginsAsync(user).ConfigureAwait(false);
        var userRoles = await users.GetRolesAsync(user).ConfigureAwait(false);
        return new UserAccount(
            user.Id,
            user.Email ?? string.Empty,
            user.EmailConfirmed,
            user.PasswordHash is not null,
            logins.Select(l => l.LoginProvider).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
            userRoles.Order(StringComparer.Ordinal).ToList(),
            user.DeletedAtUtc is not null,
            user.IsVisitor);
    }

    private void Detach(UserProfile profile)
    {
        var entry = db.Entry(profile);
        if (entry.State != EntityState.Detached)
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <summary>Maps Identity error codes to stable API codes and request fields.</summary>
    internal static AccountOperationResult Translate(IdentityResult result, string passwordField)
    {
        if (result.Succeeded)
        {
            return AccountOperationResult.Success;
        }

        var errors = new Dictionary<string, List<string>>();
        foreach (var error in result.Errors)
        {
            var (field, code) = error.Code switch
            {
                nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName) => ("email", "duplicate_email"),
                nameof(IdentityErrorDescriber.InvalidEmail) or nameof(IdentityErrorDescriber.InvalidUserName) => ("email", "email_invalid"),
                nameof(IdentityErrorDescriber.PasswordMismatch) => ("currentPassword", "password_incorrect"),
                nameof(IdentityErrorDescriber.InvalidToken) => ("token", "invalid_token"),
                nameof(IdentityErrorDescriber.PasswordTooShort) => (passwordField, "password_too_short"),
                nameof(IdentityErrorDescriber.PasswordRequiresUniqueChars) => (passwordField, "password_requires_unique_chars"),
                nameof(IdentityErrorDescriber.PasswordRequiresDigit) => (passwordField, "password_requires_digit"),
                nameof(IdentityErrorDescriber.PasswordRequiresLower) => (passwordField, "password_requires_lower"),
                nameof(IdentityErrorDescriber.PasswordRequiresUpper) => (passwordField, "password_requires_upper"),
                nameof(IdentityErrorDescriber.PasswordRequiresNonAlphanumeric) => (passwordField, "password_requires_non_alphanumeric"),
                nameof(IdentityErrorDescriber.LoginAlreadyAssociated) => ("externalLogin", "external_login_in_use"),
                nameof(IdentityErrorDescriber.UserAlreadyInRole) => ("role", "already_in_role"),
                _ => ("general", "account_operation_failed"),
            };

            if (!errors.TryGetValue(field, out var codes))
            {
                errors[field] = codes = [];
            }

            if (!codes.Contains(code))
            {
                codes.Add(code);
            }
        }

        return new AccountOperationResult(false, errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
    }
}
