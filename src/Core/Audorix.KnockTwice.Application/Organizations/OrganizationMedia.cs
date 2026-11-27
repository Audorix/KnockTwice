using Audorix.KnockTwice.Application.Attachments;
using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Users;
using Audorix.KnockTwice.Domain.Organizations;

namespace Audorix.KnockTwice.Application.Organizations;

public sealed record OrganizationImageContent(Stream Content, string ContentType, int Version);

/// <summary>
/// The organization's picture and background image (redesign 2026-11-24, owner mockup). Both arrive
/// already cropped and resized by the client; the server accepts PNG, JPEG or WebP by signature
/// only, at most <see cref="MaxPhotoBytes"/> or <see cref="MaxBannerBytes"/>, under a random storage
/// key. Members see them; owners and administrators change them.
/// </summary>
public sealed class OrganizationMediaService(
    OrganizationService organizations,
    IOrganizationStore store,
    IAttachmentStorage storage,
    TimeProvider time)
{
    public const int MaxPhotoBytes = 1024 * 1024;
    public const int MaxBannerBytes = 2 * 1024 * 1024;

    public static int MaxBytes(OrganizationImage kind) => kind == OrganizationImage.Banner ? MaxBannerBytes : MaxPhotoBytes;

    public async Task<OrganizationView> SetImageAsync(Guid userId, Guid organizationId, OrganizationImage kind, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var (organization, membership) = await organizations.RequireMembershipAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        await organizations.RequirePermissionAsync(userId, organizationId, membership, "organization.manage", OrganizationPermission.Manage, cancellationToken).ConfigureAwait(false);

        var limit = MaxBytes(kind);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                throw new PayloadTooLargeException("photo_too_large");
            }

            buffer.Write(chunk, 0, read);
        }

        var type = ProfileMediaService.DetectImage(buffer.GetBuffer().AsSpan(0, (int)buffer.Length))
            ?? throw new RequestValidationException("file", "photo_type_invalid");
        var now = time.GetUtcNow();
        var key = $"organizations/{AttachmentService.NewStorageKey(now)}";
        buffer.Position = 0;
        try
        {
            await storage.PutAsync(key, buffer, buffer.Length, type, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not StorageUnavailableException)
        {
            throw new StorageUnavailableException("The image could not be stored.", ex);
        }

        var previous = organization.SetImage(kind, key, type, now);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        await TryDeleteAsync(previous).ConfigureAwait(false);
        return new OrganizationView(organization, membership.Role, membership.IsSupportAgent,
            await organizations.GetMyPermissionsAsync(userId, organizationId, membership, cancellationToken).ConfigureAwait(false));
    }

    public async Task<OrganizationView> RemoveImageAsync(Guid userId, Guid organizationId, OrganizationImage kind, CancellationToken cancellationToken)
    {
        var (organization, membership) = await organizations.RequireMembershipAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        await organizations.RequirePermissionAsync(userId, organizationId, membership, "organization.manage", OrganizationPermission.Manage, cancellationToken).ConfigureAwait(false);
        var previous = organization.RemoveImage(kind, time.GetUtcNow());
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        await TryDeleteAsync(previous).ConfigureAwait(false);
        return new OrganizationView(organization, membership.Role, membership.IsSupportAgent,
            await organizations.GetMyPermissionsAsync(userId, organizationId, membership, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>The picture or background image for a member, or null when there is none.</summary>
    public async Task<OrganizationImageContent?> OpenImageAsync(Guid userId, Guid organizationId, OrganizationImage kind, CancellationToken cancellationToken)
    {
        var (organization, _) = await organizations.RequireMembershipAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        var (key, type, version) = kind == OrganizationImage.Banner
            ? (organization.BannerKey, organization.BannerContentType, organization.BannerVersion)
            : (organization.PhotoKey, organization.PhotoContentType, organization.PhotoVersion);
        if (key is null || type is null)
        {
            return null;
        }

        var stream = await storage.OpenReadAsync(key, cancellationToken).ConfigureAwait(false);
        return stream is null ? null : new OrganizationImageContent(stream, type, version);
    }

    /// <summary>The picture or background image for installation administrators (no membership needed), or null.</summary>
    public async Task<OrganizationImageContent?> OpenImageForAdministratorAsync(Guid organizationId, OrganizationImage kind, CancellationToken cancellationToken)
    {
        var organization = await store.FindOrganizationAsync(organizationId, cancellationToken).ConfigureAwait(false);
        if (organization is null)
        {
            return null;
        }

        var (key, type, version) = kind == OrganizationImage.Banner
            ? (organization.BannerKey, organization.BannerContentType, organization.BannerVersion)
            : (organization.PhotoKey, organization.PhotoContentType, organization.PhotoVersion);
        if (key is null || type is null)
        {
            return null;
        }

        var stream = await storage.OpenReadAsync(key, cancellationToken).ConfigureAwait(false);
        return stream is null ? null : new OrganizationImageContent(stream, type, version);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (!await store.TrySaveChangesAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException("conflict");
        }
    }

    private async Task TryDeleteAsync(string? key)
    {
        if (key is null)
        {
            return;
        }

        try
        {
            await storage.DeleteAsync(key, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A leftover file is harmless: nothing points at it any more.
        }
    }
}
