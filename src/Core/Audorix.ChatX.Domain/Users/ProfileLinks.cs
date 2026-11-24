namespace Audorix.ChatX.Domain.Users;

/// <summary>Where a profile link points (redesign 2026-11). <see cref="Website"/> takes any address.</summary>
public enum ProfileLinkPlatform
{
    Website = 1,
    GitHub = 2,
    LinkedIn = 3,
    YouTube = 4,
    Instagram = 5,
    X = 6,
}

/// <summary>Who sees a profile link: the person's contacts, or only the person.</summary>
public enum ProfileLinkVisibility
{
    Contacts = 1,
    OnlyMe = 2,
}

/// <summary>
/// A link on a person's profile (redesign 2026-11, owner mockup "Media &amp; links"): a profile or
/// channel address, no account connection. Only http(s) addresses; platform links must point to
/// that platform, so a "GitHub" link cannot lead somewhere else.
/// </summary>
public sealed class ProfileLink
{
    public const int UrlMaxLength = 300;
    public const int MaxPerUser = 10;

    private static readonly Dictionary<ProfileLinkPlatform, string[]> Hosts = new()
    {
        [ProfileLinkPlatform.GitHub] = ["github.com"],
        [ProfileLinkPlatform.LinkedIn] = ["linkedin.com"],
        [ProfileLinkPlatform.YouTube] = ["youtube.com", "youtu.be"],
        [ProfileLinkPlatform.Instagram] = ["instagram.com"],
        [ProfileLinkPlatform.X] = ["x.com", "twitter.com"],
    };

    private ProfileLink(Guid id, Guid userId, ProfileLinkPlatform platform, string url, ProfileLinkVisibility visibility, int position)
    {
        Id = id;
        UserId = userId;
        Platform = platform;
        Url = url;
        Visibility = visibility;
        Position = position;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public ProfileLinkPlatform Platform { get; private set; }

    public string Url { get; private set; }

    public ProfileLinkVisibility Visibility { get; private set; }

    /// <summary>Order on the profile, from 0.</summary>
    public int Position { get; private set; }

    public static ProfileLink Create(Guid id, Guid userId, ProfileLinkPlatform platform, string? url, ProfileLinkVisibility visibility, int position)
    {
        if (id == Guid.Empty || userId == Guid.Empty)
        {
            throw new DomainRuleException("value_invalid", "Identifiers are required.");
        }

        if (!Enum.IsDefined(platform) || !Enum.IsDefined(visibility) || position is < 0 or >= MaxPerUser)
        {
            throw new DomainRuleException("link_invalid", "The link is not valid.");
        }

        return new ProfileLink(id, userId, platform, NormalizeUrl(platform, url), visibility, position);
    }

    /// <summary>An absolute http(s) address without credentials; for a platform, on that platform's site.</summary>
    public static string NormalizeUrl(ProfileLinkPlatform platform, string? url)
    {
        var text = url?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            throw new DomainRuleException("link_url_required", "Enter the address of the profile or site.");
        }

        if (text.Length > UrlMaxLength || text.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))
            || !Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")
            || !string.IsNullOrEmpty(uri.UserInfo) || uri.Host.Length == 0)
        {
            throw new DomainRuleException("link_url_invalid", "Enter a full address that starts with https://.");
        }

        if (Hosts.TryGetValue(platform, out var hosts))
        {
            var host = uri.IdnHost.ToLowerInvariant();
            if (!hosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal)))
            {
                throw new DomainRuleException("link_url_platform", "The address does not belong to the chosen platform.");
            }
        }

        return uri.AbsoluteUri;
    }
}
