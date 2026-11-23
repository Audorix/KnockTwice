namespace Audorix.ChatX.Api.Identity;

/// <summary>
/// OAuth/OpenID Connect providers bound from <c>Authentication</c>. A provider is enabled only
/// when both its client id and secret are configured; disabled providers do not affect local sign-in.
/// Register redirect URIs as <c>{public origin}/api/v1/auth/external/signin-google</c> and
/// <c>{public origin}/api/v1/auth/external/signin-microsoft</c>.
/// </summary>
public sealed class ExternalAuthenticationSettings
{
    public const string SectionName = "Authentication";

    public OAuthProviderSettings Google { get; set; } = new();

    public OAuthProviderSettings Microsoft { get; set; } = new();

    public IReadOnlyList<string> EnabledProviders =>
        ExternalProviders.All.Where(p => p.Name switch
        {
            "google" => Google.IsEnabled,
            "microsoft" => Microsoft.IsEnabled,
            _ => false,
        }).Select(p => p.Name).ToList();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Google.IsPartiallyConfigured)
        {
            errors.Add("Authentication:Google:ClientId and Authentication:Google:ClientSecret must be set together.");
        }

        if (Microsoft.IsPartiallyConfigured)
        {
            errors.Add("Authentication:Microsoft:ClientId and Authentication:Microsoft:ClientSecret must be set together.");
        }

        return errors;
    }
}

public sealed class OAuthProviderSettings
{
    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    internal bool IsPartiallyConfigured => string.IsNullOrWhiteSpace(ClientId) != string.IsNullOrWhiteSpace(ClientSecret);
}

public sealed record ExternalProvider(string Name, string Scheme, string CallbackPath);

public static class ExternalProviders
{
    public static ExternalProvider Google { get; } = new("google", "Google", "/api/v1/auth/external/signin-google");

    public static ExternalProvider Microsoft { get; } = new("microsoft", "Microsoft", "/api/v1/auth/external/signin-microsoft");

    public static IReadOnlyList<ExternalProvider> All { get; } = [Google, Microsoft];

    public static ExternalProvider? Find(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public static ExternalProvider? FindByScheme(string? scheme) =>
        All.FirstOrDefault(p => string.Equals(p.Scheme, scheme, StringComparison.Ordinal));
}
