using System.Security.Cryptography;
using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Domain.Organizations;

namespace Audorix.KnockTwice.Application.Organizations;

public sealed record OrganizationSsoSignInSettings(
    Guid OrganizationId, string OrganizationName, string Issuer, string ClientId, string ProtectedClientSecret,
    bool AutomaticEnrollmentEnabled, IReadOnlyList<string> VerifiedDomains);

/// <summary>Owner/admin workflows for proving organization domain ownership with DNS TXT.</summary>
public sealed class OrganizationDomainService(
    IOrganizationStore store,
    OrganizationService organizations,
    IDomainTxtRecordLookup txtLookup,
    TimeProvider time)
{
    public async Task<IReadOnlyList<VerifiedOrganizationDomain>> ListAsync(Guid userId, Guid organizationId, CancellationToken ct)
    {
        await RequireManagerAsync(userId, organizationId, ct).ConfigureAwait(false);
        return await store.ListVerifiedDomainsAsync(organizationId, ct).ConfigureAwait(false);
    }

    public async Task<VerifiedOrganizationDomain> AddAsync(Guid userId, Guid organizationId, string? domain, CancellationToken ct)
    {
        await RequireManagerAsync(userId, organizationId, ct).ConfigureAwait(false);
        var normalized = VerifiedOrganizationDomain.NormalizeDomain(domain);
        if (await store.FindDomainByNameAsync(normalized, ct).ConfigureAwait(false) is not null)
            throw new ConflictException("domain_already_claimed");

        var bytes = RandomNumberGenerator.GetBytes(24);
        var token = Convert.ToHexString(bytes).ToLowerInvariant();
        var row = VerifiedOrganizationDomain.Create(Guid.CreateVersion7(time.GetUtcNow()), organizationId, normalized, token, time.GetUtcNow());
        store.Add(row);
        if (!await store.TrySaveChangesAsync(ct).ConfigureAwait(false)) throw new ConflictException("domain_already_claimed");
        return row;
    }

    public async Task<VerifiedOrganizationDomain> VerifyAsync(Guid userId, Guid organizationId, Guid domainId, CancellationToken ct)
    {
        await RequireManagerAsync(userId, organizationId, ct).ConfigureAwait(false);
        var row = await store.FindVerifiedDomainAsync(organizationId, domainId, ct).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("organization_domain_not_found");
        var records = await txtLookup.LookupAsync("_knocktwice-verification." + row.Domain, ct).ConfigureAwait(false);
        if (!records.Any(record => string.Equals(record.Trim().Trim('"'), $"knocktwice-verification={row.VerificationToken}", StringComparison.Ordinal)))
            throw new ConflictException("domain_verification_record_missing");
        row.MarkVerified(time.GetUtcNow());
        if (!await store.TrySaveChangesAsync(ct).ConfigureAwait(false)) throw new ConflictException("conflict");
        return row;
    }

    public async Task RemoveAsync(Guid userId, Guid organizationId, Guid domainId, CancellationToken ct)
    {
        await RequireManagerAsync(userId, organizationId, ct).ConfigureAwait(false);
        var row = await store.FindVerifiedDomainAsync(organizationId, domainId, ct).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("organization_domain_not_found");
        var organization = await store.FindOrganizationAsync(organizationId, ct).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("organization_not_found");
        if (row.IsVerified && (await store.ListVerifiedDomainsAsync(organizationId, ct).ConfigureAwait(false)).Count(d => d.IsVerified) == 1)
        {
            if (organization.AutomaticEnrollmentEnabled) throw new ConflictException("disable_automatic_enrollment_first");
            if (organization.SsoConfigured) throw new ConflictException("remove_organization_sso_configuration_first");
        }
        store.Remove(row);
        if (!await store.TrySaveChangesAsync(ct).ConfigureAwait(false)) throw new ConflictException("conflict");
    }

    public async Task<bool> GetAutomaticEnrollmentAsync(Guid userId, Guid organizationId, CancellationToken ct)
    {
        await RequireManagerAsync(userId, organizationId, ct).ConfigureAwait(false);
        var organization = await store.FindOrganizationAsync(organizationId, ct).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("organization_not_found");
        return organization.AutomaticEnrollmentEnabled;
    }

    public async Task<bool> SetAutomaticEnrollmentAsync(Guid userId, Guid organizationId, bool enabled, CancellationToken ct)
    {
        await RequireManagerAsync(userId, organizationId, ct).ConfigureAwait(false);
        var organization = await store.FindOrganizationAsync(organizationId, ct).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("organization_not_found");
        var hasVerifiedDomain = (await store.ListVerifiedDomainsAsync(organizationId, ct).ConfigureAwait(false)).Any(d => d.IsVerified);
        organization.ConfigureAutomaticEnrollment(enabled, hasVerifiedDomain, time.GetUtcNow());
        if (!await store.TrySaveChangesAsync(ct).ConfigureAwait(false)) throw new ConflictException("conflict");
        return organization.AutomaticEnrollmentEnabled;
    }

    public async Task<Organization> GetSsoAsync(Guid userId, Guid organizationId, CancellationToken ct)
    {
        await RequireManagerAsync(userId, organizationId, ct).ConfigureAwait(false);
        return await store.FindOrganizationAsync(organizationId, ct).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("organization_not_found");
    }

    public async Task<OrganizationSsoSignInSettings?> GetSsoForSignInAsync(Guid organizationId, CancellationToken ct)
    {
        var organization = await store.FindOrganizationAsync(organizationId, ct).ConfigureAwait(false);
        if (organization is null || !organization.SsoConfigured) return null;
        var domains = (await store.ListVerifiedDomainsAsync(organizationId, ct).ConfigureAwait(false))
            .Where(domain => domain.IsVerified).Select(domain => domain.Domain).ToArray();
        if (domains.Length == 0) return null;
        return new OrganizationSsoSignInSettings(organization.Id, organization.Name, organization.SsoIssuer!,
            organization.SsoClientId!, organization.ProtectedSsoClientSecret!, organization.AutomaticEnrollmentEnabled, domains);
    }

    public async Task<Organization> SetSsoAsync(Guid userId, Guid organizationId, string? issuer, string? clientId, string? protectedClientSecret, CancellationToken ct)
    {
        await RequireManagerAsync(userId, organizationId, ct).ConfigureAwait(false);
        var organization = await store.FindOrganizationAsync(organizationId, ct).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("organization_not_found");
        var hasVerifiedDomain = (await store.ListVerifiedDomainsAsync(organizationId, ct).ConfigureAwait(false)).Any(d => d.IsVerified);
        organization.ConfigureSso(issuer, clientId, protectedClientSecret, hasVerifiedDomain, time.GetUtcNow());
        if (!await store.TrySaveChangesAsync(ct).ConfigureAwait(false)) throw new ConflictException("conflict");
        return organization;
    }

    public async Task ClearSsoAsync(Guid userId, Guid organizationId, CancellationToken ct)
    {
        await RequireManagerAsync(userId, organizationId, ct).ConfigureAwait(false);
        var organization = await store.FindOrganizationAsync(organizationId, ct).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("organization_not_found");
        organization.ClearSsoConfiguration(time.GetUtcNow());
        if (!await store.TrySaveChangesAsync(ct).ConfigureAwait(false)) throw new ConflictException("conflict");
    }

    private async Task RequireManagerAsync(Guid userId, Guid organizationId, CancellationToken ct)
    {
        var (_, membership) = await organizations.RequireMembershipAsync(userId, organizationId, ct).ConfigureAwait(false);
        await organizations.RequirePermissionAsync(userId, organizationId, membership, "organization.manage", OrganizationPermission.Manage, ct).ConfigureAwait(false);
    }
}
