using System.Buffers.Text;
using System.Net.Mail;
using System.Text;
using Audorix.KnockTwice.Application.Abstractions;
using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Email;
using Audorix.KnockTwice.Application.Localization;
using Audorix.KnockTwice.Application.Sessions;
using Audorix.KnockTwice.Application.Users;
using Audorix.KnockTwice.Domain;
using Audorix.KnockTwice.Domain.Users;
using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Application.Identity;

/// <summary>
/// Account use cases that do not depend on the HTTP sign-in mechanism: registration, email
/// confirmation, password recovery and change, and external account provisioning.
/// Responses never reveal whether an email address has an account (no enumeration):
/// register, resend and forgot-password always report the same accepted outcome.
/// </summary>
public sealed class AccountService(
    IUserAccountManager accounts,
    IUserProfileStore profiles,
    ICaptchaVerifier captcha,
    IEmailSender email,
    AccountEmailComposer composer,
    SessionService sessions,
    TimeProvider time,
    IOptionsSnapshot<AccountSettings> settings,
    IOptions<PublicUrlSettings> urls,
    IOptionsSnapshot<LocalizationSettings> localization,
    IRegistrationInvitations invitations,
    Organizations.OrganizationDomainEnrollmentService domainEnrollment)
{
    public const int EmailMaxLength = 254;
    public const int PasswordMaxLength = 128;

    public async Task RegisterAsync(RegisterCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var mode = settings.Value.EffectiveRegistrationMode;
        if (mode == RegistrationModes.Closed)
        {
            throw new FeatureDisabledException("registration_disabled");
        }

        await RequireCaptchaAsync(command.CaptchaToken, CaptchaActions.Register, command.RemoteIp, cancellationToken).ConfigureAwait(false);

        var errors = new Dictionary<string, string[]>();
        var address = NormalizeEmail(command.Email, errors);
        if (mode == RegistrationModes.Invitation && address is not null
            && !await invitations.HasOpenInvitationAsync(address, cancellationToken).ConfigureAwait(false))
        {
            throw new FeatureDisabledException("registration_invitation_required");
        }
        var displayName = TryNormalizeDisplayName(command.DisplayName, errors);
        if (string.IsNullOrEmpty(command.Password))
        {
            errors["password"] = ["password_required"];
        }
        else if (command.Password.Length > PasswordMaxLength)
        {
            errors["password"] = ["password_too_long"];
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        // Development mode confirms new accounts directly and sends no email at all, so the
        // outcome stays identical for new and existing addresses.
        var autoConfirm = settings.Value.AutoConfirmEmail;
        var language = ResolveLanguage(command.Language);
        var existing = await accounts.FindByEmailAsync(address!, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!autoConfirm)
            {
                await NotifyExistingAccountAsync(existing, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        var now = time.GetUtcNow();
        var id = Guid.CreateVersion7(now);
        var profile = UserProfile.Create(id, displayName!, language, now);
        var result = await accounts.CreateAsync(new NewAccount(id, address!, EmailConfirmed: autoConfirm), profile, command.Password, null, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            if (result.HasError("duplicate_email"))
            {
                // Lost a race with a concurrent registration for the same address.
                var winner = await accounts.FindByEmailAsync(address!, cancellationToken).ConfigureAwait(false);
                if (winner is not null && !autoConfirm)
                {
                    await NotifyExistingAccountAsync(winner, cancellationToken).ConfigureAwait(false);
                }

                return;
            }

            throw new RequestValidationException(result.Errors);
        }

        if (!autoConfirm)
        {
            await SendConfirmationAsync(id, address!, profile, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await domainEnrollment.EnrollIfEligibleAsync(id, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Validates the first administrator's details without touching storage, so the setup wizard
    /// can reject bad input before it migrates a database. Field keys match registration.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> ValidateAdministratorInput(
        string? emailAddress, string? displayName, string? password, int passwordMinimumLength)
    {
        var errors = new Dictionary<string, string[]>();
        NormalizeEmail(emailAddress, errors);
        TryNormalizeDisplayName(displayName, errors);
        if (string.IsNullOrEmpty(password))
        {
            errors["password"] = ["password_required"];
        }
        else if (password.Length > PasswordMaxLength)
        {
            errors["password"] = ["password_too_long"];
        }
        else if (password.Length < passwordMinimumLength)
        {
            errors["password"] = ["password_too_short"];
        }
        else if (password.Distinct().Count() < 4)
        {
            errors["password"] = ["password_requires_unique_chars"];
        }

        return errors;
    }

    /// <summary>
    /// Creates the first installation administrator with a confirmed email address. Refused
    /// when any administrator already exists, so setup can never take over an installation.
    /// </summary>
    public async Task<Guid> CreateInstallationAdministratorAsync(
        string? emailAddress, string? displayName, string? password, string? language, CancellationToken cancellationToken)
    {
        var errors = ValidateAdministratorInput(emailAddress, displayName, password, settings.Value.PasswordMinimumLength);
        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        const string role = Domain.Authorization.Roles.InstallationAdministrator;
        if (await accounts.AnyInRoleAsync(role, cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException("setup_administrator_exists");
        }

        var address = emailAddress!.Trim();
        if (await accounts.FindByEmailAsync(address, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new RequestValidationException("email", "duplicate_email");
        }

        var now = time.GetUtcNow();
        var id = Guid.CreateVersion7(now);
        var profile = UserProfile.Create(id, UserProfile.NormalizeDisplayName(displayName!), ResolveLanguage(language), now);
        var created = await accounts.CreateAsync(new NewAccount(id, address, EmailConfirmed: true), profile, password, null, cancellationToken)
            .ConfigureAwait(false);
        if (!created.Succeeded)
        {
            throw new RequestValidationException(created.Errors);
        }

        var granted = await accounts.AddToRoleAsync(id, role, cancellationToken).ConfigureAwait(false);
        if (!granted.Succeeded)
        {
            throw new RequestValidationException(granted.Errors);
        }

        return id;
    }

    public async Task ResendConfirmationAsync(string emailAddress, string? captchaToken, string? remoteIp, CancellationToken cancellationToken)
    {
        await RequireCaptchaAsync(captchaToken, CaptchaActions.ResendConfirmation, remoteIp, cancellationToken).ConfigureAwait(false);
        var address = NormalizeEmailOrThrow(emailAddress);
        var account = await accounts.FindByEmailAsync(address, cancellationToken).ConfigureAwait(false);
        if (account is { EmailConfirmed: false })
        {
            var profile = await profiles.FindAsync(account.Id, cancellationToken).ConfigureAwait(false);
            if (profile is not null)
            {
                await SendConfirmationAsync(account.Id, account.Email, profile, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Sends a new confirmation link for an unconfirmed account (administrators, no captcha).</summary>
    public async Task SendConfirmationAsAdministratorAsync(Guid userId, CancellationToken cancellationToken)
    {
        var account = await accounts.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("user_not_found");
        var profile = await profiles.FindAsync(userId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("user_not_found");
        if (!account.EmailConfirmed)
        {
            await SendConfirmationAsync(account.Id, account.Email, profile, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task ConfirmEmailAsync(Guid userId, string encodedToken, CancellationToken cancellationToken)
    {
        var token = DecodeToken(encodedToken);
        if (token is null || !await accounts.ConfirmEmailAsync(userId, token, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidTokenException();
        }
        await domainEnrollment.EnrollIfEligibleAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    public async Task ForgotPasswordAsync(string emailAddress, string? captchaToken, string? remoteIp, CancellationToken cancellationToken)
    {
        await RequireCaptchaAsync(captchaToken, CaptchaActions.ForgotPassword, remoteIp, cancellationToken).ConfigureAwait(false);
        var address = NormalizeEmailOrThrow(emailAddress);
        var account = await accounts.FindByEmailAsync(address, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return;
        }

        var profile = await profiles.FindAsync(account.Id, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return;
        }

        var token = await accounts.CreatePasswordResetTokenAsync(account.Id, cancellationToken).ConfigureAwait(false);
        var link = urls.Value.Link("reset-password", ("user", account.Id.ToString()), ("token", EncodeToken(token)));
        await email.SendAsync(
            composer.PasswordReset(account.Email, profile.DisplayName, profile.UiLanguage, link, settings.Value.TokenLifetime),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sets a new password from an emailed link and signs the user out everywhere.</summary>
    public async Task ResetPasswordAsync(Guid userId, string encodedToken, string newPassword, CancellationToken cancellationToken)
    {
        ValidateNewPassword(newPassword, "newPassword");
        var token = DecodeToken(encodedToken) ?? throw new InvalidTokenException();
        var result = await accounts.ResetPasswordAsync(userId, token, newPassword, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            if (result.HasError("invalid_token") || result.HasError("user_not_found"))
            {
                throw new InvalidTokenException();
            }

            throw new RequestValidationException(result.Errors);
        }

        await sessions.RevokeAllAsync(userId, keepSessionId: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Changes the password of a signed-in user and revokes their other sessions.</summary>
    public async Task ChangePasswordAsync(Guid userId, Guid currentSessionId, string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        ValidateNewPassword(newPassword, "newPassword");
        if (string.IsNullOrEmpty(currentPassword))
        {
            throw new RequestValidationException("currentPassword", "password_required");
        }

        var result = await accounts.ChangePasswordAsync(userId, currentPassword, newPassword, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new RequestValidationException(result.Errors);
        }

        await sessions.RevokeAllAsync(userId, currentSessionId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates an account for a first-time external sign-in. The provider has authenticated the
    /// address, so it is stored as confirmed. Existing local accounts are never linked
    /// automatically by email address, which prevents account takeover through a provider.
    /// </summary>
    public async Task<ExternalProvisioningResult> ProvisionExternalAccountAsync(
        ExternalLoginReference login, string? emailAddress, string? language, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(login);
        if (!settings.Value.AllowRegistration)
        {
            return ExternalProvisioningResult.Failed("registration_disabled");
        }

        var errors = new Dictionary<string, string[]>();
        var address = NormalizeEmail(emailAddress, errors);
        if (address is null)
        {
            return ExternalProvisioningResult.Failed("external_email_missing");
        }

        if (await accounts.FindByEmailAsync(address, cancellationToken).ConfigureAwait(false) is not null)
        {
            return ExternalProvisioningResult.Failed("external_account_exists");
        }

        var now = time.GetUtcNow();
        var id = Guid.CreateVersion7(now);
        string displayName;
        try
        {
            displayName = UserProfile.NormalizeDisplayName(string.IsNullOrWhiteSpace(login.DisplayName) ? address.Split('@')[0] : login.DisplayName);
        }
        catch (DomainRuleException)
        {
            displayName = UserProfile.NormalizeDisplayName(address.Split('@')[0]);
        }

        var profile = UserProfile.Create(id, displayName, ResolveLanguage(language), now);
        var result = await accounts.CreateAsync(new NewAccount(id, address, EmailConfirmed: true), profile, null, login, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded)
            return ExternalProvisioningResult.Failed(result.HasError("duplicate_email") ? "external_account_exists" : "external_sign_in_failed");
        await domainEnrollment.EnrollIfEligibleAsync(id, cancellationToken).ConfigureAwait(false);
        return ExternalProvisioningResult.Created(id);
    }

    public static string EncodeToken(string token) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(token));

    public static string? DecodeToken(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded) || encoded.Length > 4096)
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(Base64Url.DecodeFromChars(encoded));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private async Task SendConfirmationAsync(Guid userId, string address, UserProfile profile, CancellationToken cancellationToken)
    {
        var token = await accounts.CreateEmailConfirmationTokenAsync(userId, cancellationToken).ConfigureAwait(false);
        var link = urls.Value.Link("verify-email", ("user", userId.ToString()), ("token", EncodeToken(token)));
        await email.SendAsync(
            composer.EmailConfirmation(address, profile.DisplayName, profile.UiLanguage, link, settings.Value.TokenLifetime),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task NotifyExistingAccountAsync(UserAccount account, CancellationToken cancellationToken)
    {
        var profile = await profiles.FindAsync(account.Id, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return;
        }

        if (!account.EmailConfirmed)
        {
            // Re-registering an unconfirmed address simply resends the confirmation link.
            await SendConfirmationAsync(account.Id, account.Email, profile, cancellationToken).ConfigureAwait(false);
            return;
        }

        await email.SendAsync(
            composer.AlreadyRegistered(account.Email, profile.DisplayName, profile.UiLanguage, urls.Value.Link("sign-in"), urls.Value.Link("forgot-password")),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RequireCaptchaAsync(string? token, string action, string? remoteIp, CancellationToken cancellationToken)
    {
        var outcome = await captcha.VerifyAsync(token, action, remoteIp, cancellationToken).ConfigureAwait(false);
        switch (outcome)
        {
            case CaptchaOutcome.Passed:
                return;
            case CaptchaOutcome.Unavailable:
                throw new CaptchaRejectedException("captcha_unavailable");
            default:
                throw new CaptchaRejectedException("captcha_failed");
        }
    }

    private static void ValidateNewPassword(string password, string field)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new RequestValidationException(field, "password_required");
        }

        if (password.Length > PasswordMaxLength)
        {
            throw new RequestValidationException(field, "password_too_long");
        }
    }

    private string ResolveLanguage(string? requested)
    {
        var normalized = requested?.Trim().ToLowerInvariant();
        var supported = localization.Value.EffectiveSupportedLanguages;
        return normalized is not null && supported.Contains(normalized) ? normalized : localization.Value.DefaultLanguage.Trim().ToLowerInvariant();
    }

    private static string NormalizeEmailOrThrow(string? value)
    {
        var errors = new Dictionary<string, string[]>();
        return NormalizeEmail(value, errors) ?? throw new RequestValidationException(errors);
    }

    private static string? NormalizeEmail(string? value, Dictionary<string, string[]> errors)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            errors["email"] = ["email_required"];
            return null;
        }

        if (trimmed.Length > EmailMaxLength || !MailAddress.TryCreate(trimmed, out var parsed) || parsed.Address != trimmed || !trimmed.Contains('.', StringComparison.Ordinal))
        {
            errors["email"] = ["email_invalid"];
            return null;
        }

        return trimmed;
    }

    private static string? TryNormalizeDisplayName(string? value, Dictionary<string, string[]> errors)
    {
        try
        {
            return UserProfile.NormalizeDisplayName(value ?? string.Empty);
        }
        catch (DomainRuleException ex)
        {
            errors["displayName"] = [ex.Code];
            return null;
        }
    }
}

public sealed record RegisterCommand(
    string? Email,
    string? Password,
    string? DisplayName,
    string? Language,
    string? CaptchaToken,
    string? RemoteIp);

public sealed record ExternalProvisioningResult(bool Succeeded, Guid? UserId, string? ErrorCode)
{
    public static ExternalProvisioningResult Created(Guid userId) => new(true, userId, null);

    public static ExternalProvisioningResult Failed(string code) => new(false, null, code);
}
