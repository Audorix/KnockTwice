using System.Globalization;
using System.Net;

namespace Audorix.KnockTwice.Domain.Organizations;

/// <summary>Proof that an organization controls a DNS domain. Enrollment is a separate opt-in.</summary>
public sealed class VerifiedOrganizationDomain
{
    public const int DomainMaxLength = 253;
    private VerifiedOrganizationDomain() { }

    private VerifiedOrganizationDomain(Guid id, Guid organizationId, string domain, string token, DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        Domain = domain;
        VerificationToken = token;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Domain { get; private set; } = string.Empty;
    public string VerificationToken { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? VerifiedAtUtc { get; private set; }
    public bool IsVerified => VerifiedAtUtc is not null;

    public static VerifiedOrganizationDomain Create(Guid id, Guid organizationId, string? domain, string token, DateTimeOffset nowUtc)
    {
        var normalized = NormalizeDomain(domain);
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
            throw new DomainRuleException("domain_token_invalid", "A verification token is required.");
        return new(Require.Id(id), Require.Id(organizationId), normalized, token, Require.Utc(nowUtc));
    }

    public static string NormalizeDomain(string? value)
    {
        var input = value?.Trim().TrimEnd('.') ?? string.Empty;
        if (input.Length is 0 or > DomainMaxLength || input.Contains('/') || input.Contains('@') || IPAddress.TryParse(input, out _))
            throw new DomainRuleException("domain_invalid", "Enter a valid DNS domain name.");
        try
        {
            var ascii = new IdnMapping().GetAscii(input).ToLowerInvariant();
            var labels = ascii.Split('.');
            if (labels.Length < 2 || labels.Any(label => label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-' ||
                label.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-'))))
                throw new DomainRuleException("domain_invalid", "Enter a valid DNS domain name.");
            return ascii;
        }
        catch (ArgumentException)
        {
            throw new DomainRuleException("domain_invalid", "Enter a valid DNS domain name.");
        }
    }

    public void MarkVerified(DateTimeOffset nowUtc) => VerifiedAtUtc ??= Require.Utc(nowUtc);
}
