namespace Audorix.ChatX.Application.Identity;

/// <summary>Account and session policy bound from the <c>Identity</c> configuration section.</summary>
public sealed class AccountSettings
{
    public const string SectionName = "Identity";

    /// <summary>Whether visitors may create local accounts. External sign-in can still create accounts when a provider is enabled.</summary>
    public bool AllowRegistration { get; set; } = true;

    /// <summary>
    /// Who may register (Phase 9): <c>open</c> (anyone), <c>invitation</c> (only addresses with an
    /// open organization invitation) or <c>closed</c>. <see cref="AllowRegistration"/> = false also closes it.
    /// </summary>
    public string RegistrationMode { get; set; } = RegistrationModes.Open;

    /// <summary>The mode in force, taking <see cref="AllowRegistration"/> into account.</summary>
    public string EffectiveRegistrationMode =>
        !AllowRegistration ? RegistrationModes.Closed : RegistrationModes.Normalize(RegistrationMode) ?? RegistrationModes.Open;

    /// <summary>Block password sign-in until the email address is confirmed.</summary>
    public bool RequireConfirmedEmail { get; set; } = true;

    /// <summary>
    /// Development mode: new local accounts are created with a confirmed email address and no
    /// confirmation email is sent. Never enable this on an installation reachable by others.
    /// </summary>
    public bool AutoConfirmEmail { get; set; }

    public int PasswordMinimumLength { get; set; } = 10;

    public int MaxFailedAccessAttempts { get; set; } = 5;

    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Idle lifetime of a browser-session sign-in ("remember me" unchecked).</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(12);

    /// <summary>Idle lifetime of a persistent sign-in ("remember me" checked).</summary>
    public TimeSpan PersistentSessionLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>Lifetime of email confirmation and password reset links.</summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(24);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (PasswordMinimumLength is < 8 or > 128)
        {
            errors.Add("Identity:PasswordMinimumLength must be between 8 and 128.");
        }

        if (MaxFailedAccessAttempts is < 1 or > 100)
        {
            errors.Add("Identity:MaxFailedAccessAttempts must be between 1 and 100.");
        }

        if (LockoutDuration <= TimeSpan.Zero)
        {
            errors.Add("Identity:LockoutDuration must be positive.");
        }

        if (SessionLifetime <= TimeSpan.Zero || PersistentSessionLifetime <= TimeSpan.Zero)
        {
            errors.Add("Identity:SessionLifetime and Identity:PersistentSessionLifetime must be positive.");
        }

        if (TokenLifetime <= TimeSpan.Zero || TokenLifetime > TimeSpan.FromDays(7))
        {
            errors.Add("Identity:TokenLifetime must be positive and at most 7 days.");
        }

        if (RegistrationModes.Normalize(RegistrationMode) is null)
        {
            errors.Add("Identity:RegistrationMode must be open, invitation or closed.");
        }

        return errors;
    }
}

public static class RegistrationModes
{
    public const string Open = "open";
    public const string Invitation = "invitation";
    public const string Closed = "closed";

    public static string? Normalize(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        null or "" or Open => Open,
        Invitation => Invitation,
        Closed => Closed,
        _ => null,
    };
}

/// <summary>Public URL of the web application, used to build links in emails and OAuth redirects.</summary>
public sealed class PublicUrlSettings
{
    public const string SectionName = "App";

    /// <summary>Absolute origin (and optional path) where users open the web app, e.g. <c>https://chat.example.org</c>.</summary>
    public string? PublicBaseUrl { get; set; }

    public Uri BaseUri => new(PublicBaseUrl!.TrimEnd('/') + "/", UriKind.Absolute);

    public IReadOnlyList<string> Validate()
    {
        if (!Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return ["App:PublicBaseUrl must be an absolute http(s) URL without query or fragment (for example https://chat.example.org)."];
        }

        return [];
    }

    /// <summary>Builds an absolute link to a web-app route with escaped query values.</summary>
    public Uri Link(string relativePath, params (string Key, string Value)[] query)
    {
        var path = relativePath.TrimStart('/');
        var queryString = string.Join('&', query.Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value)}"));
        return new Uri(BaseUri, queryString.Length == 0 ? path : $"{path}?{queryString}");
    }
}
