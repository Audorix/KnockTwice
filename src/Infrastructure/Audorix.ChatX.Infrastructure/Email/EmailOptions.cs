using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Infrastructure.Email;

/// <summary>
/// Outbound email settings bound from the <c>Email</c> section. <see cref="Provider"/> selects the
/// adapter: <c>Smtp</c> (default; a local SMTP server such as Mailpit on localhost:1025) or
/// <c>Postmark</c>. Secrets (SMTP password, Postmark server token) come from environment
/// variables or a secret store.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Provider { get; set; } = EmailProviders.Smtp;

    public string? FromAddress { get; set; } = "no-reply@chatx.localhost";

    public string FromName { get; set; } = "Knock Twice";

    public SmtpEmailOptions Smtp { get; set; } = new();

    public PostmarkEmailOptions Postmark { get; set; } = new();
}

public sealed class SmtpEmailOptions
{
    public string? Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    /// <summary><c>None</c>, <c>StartTls</c>, <c>StartTlsWhenAvailable</c>, <c>SslOnConnect</c> or <c>Auto</c>.</summary>
    public string Security { get; set; } = "None";

    public string? Username { get; set; }

    public string? Password { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}

public sealed class PostmarkEmailOptions
{
    /// <summary>Postmark server API token (secret).</summary>
    public string? ServerToken { get; set; }

    public string MessageStream { get; set; } = "outbound";

    public Uri BaseUrl { get; set; } = new("https://api.postmarkapp.com/");
}

public static class EmailProviders
{
    public const string Smtp = "Smtp";
    public const string Postmark = "Postmark";

    public static IReadOnlyList<string> Names { get; } = [Smtp, Postmark];

    public static string? Normalize(string? value) =>
        Names.FirstOrDefault(n => string.Equals(n, value?.Trim(), StringComparison.OrdinalIgnoreCase));
}

internal sealed class EmailOptionsValidator : IValidateOptions<EmailOptions>
{
    private static readonly string[] SmtpSecurityModes = ["None", "StartTls", "StartTlsWhenAvailable", "SslOnConnect", "Auto"];

    public ValidateOptionsResult Validate(string? name, EmailOptions options)
    {
        var errors = new List<string>();
        var provider = EmailProviders.Normalize(options.Provider);
        if (provider is null)
        {
            errors.Add($"Email:Provider is not supported. Supported values: {string.Join(", ", EmailProviders.Names)}.");
        }

        if (string.IsNullOrWhiteSpace(options.FromAddress) || !MailAddress.TryCreate(options.FromAddress, out _))
        {
            errors.Add("Email:FromAddress must be a valid email address.");
        }

        if (provider == EmailProviders.Smtp)
        {
            if (string.IsNullOrWhiteSpace(options.Smtp.Host))
            {
                errors.Add("Email:Smtp:Host is required.");
            }

            if (options.Smtp.Port is < 1 or > 65535)
            {
                errors.Add("Email:Smtp:Port must be between 1 and 65535.");
            }

            if (!SmtpSecurityModes.Contains(options.Smtp.Security, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"Email:Smtp:Security must be one of: {string.Join(", ", SmtpSecurityModes)}.");
            }

            if (string.IsNullOrEmpty(options.Smtp.Username) != string.IsNullOrEmpty(options.Smtp.Password))
            {
                errors.Add("Email:Smtp:Username and Email:Smtp:Password must be set together.");
            }
        }

        if (provider == EmailProviders.Postmark)
        {
            if (string.IsNullOrWhiteSpace(options.Postmark.ServerToken))
            {
                errors.Add("Email:Postmark:ServerToken is required when Email:Provider is Postmark (set Email__Postmark__ServerToken).");
            }

            if (options.Postmark.BaseUrl is not { IsAbsoluteUri: true, Scheme: "https" })
            {
                errors.Add("Email:Postmark:BaseUrl must be an absolute https URL.");
            }

            if (string.IsNullOrWhiteSpace(options.Postmark.MessageStream))
            {
                errors.Add("Email:Postmark:MessageStream is required.");
            }
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
