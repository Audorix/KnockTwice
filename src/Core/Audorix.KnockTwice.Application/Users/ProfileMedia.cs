using Audorix.KnockTwice.Application.Attachments;
using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Contacts;
using Audorix.KnockTwice.Domain;
using Audorix.KnockTwice.Domain.Contacts;
using Audorix.KnockTwice.Domain.Users;

namespace Audorix.KnockTwice.Application.Users;

/// <summary>Persistence port for profile links.</summary>
public interface IProfileLinkStore
{
    /// <summary>The person's links in their order.</summary>
    Task<IReadOnlyList<ProfileLink>> ListAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Replaces all of the person's links with <paramref name="links"/> in one save.</summary>
    Task ReplaceAsync(Guid userId, IReadOnlyList<ProfileLink> links, CancellationToken cancellationToken);
}

/// <param name="Platform"><c>website</c>, <c>github</c>, <c>linkedin</c>, <c>youtube</c>, <c>instagram</c> or <c>x</c>.</param>
/// <param name="Visibility"><c>contacts</c> or <c>only_me</c>.</param>
public sealed record ProfileLinkInput(string? Platform, string? Url, string? Visibility);

public sealed record ProfilePhotoContent(Stream Content, string ContentType, int Version);

/// <summary>
/// Profile photo and links (redesign 2026-11, owner mockups "My profile"). The photo arrives
/// already cropped and resized by the client; the server only accepts PNG, JPEG or WebP by their
/// signature, at most <see cref="MaxPhotoBytes"/>, under a random storage key. Any signed-in person
/// may see a photo (it stands in for the initials wherever the name shows). Links are seen by the
/// person and, when marked so, by their contacts.
/// </summary>
public sealed class ProfileMediaService(
    IUserProfileStore profiles,
    IProfileLinkStore links,
    IAttachmentStorage storage,
    IContactStore contacts,
    TimeProvider time)
{
    public const int MaxPhotoBytes = 1024 * 1024;

    public static readonly IReadOnlyDictionary<string, ProfileLinkPlatform> Platforms = new Dictionary<string, ProfileLinkPlatform>(StringComparer.Ordinal)
    {
        ["website"] = ProfileLinkPlatform.Website,
        ["github"] = ProfileLinkPlatform.GitHub,
        ["linkedin"] = ProfileLinkPlatform.LinkedIn,
        ["youtube"] = ProfileLinkPlatform.YouTube,
        ["instagram"] = ProfileLinkPlatform.Instagram,
        ["x"] = ProfileLinkPlatform.X,
    };

    public static string PlatformName(ProfileLinkPlatform platform) => Platforms.First(p => p.Value == platform).Key;

    public static string VisibilityName(ProfileLinkVisibility visibility) => visibility == ProfileLinkVisibility.OnlyMe ? "only_me" : "contacts";

    // ---- Photo -----------------------------------------------------------------------------------

    public async Task<UserProfile> SetPhotoAsync(Guid userId, Stream content, long? declaredLength, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (declaredLength > MaxPhotoBytes)
        {
            throw new PayloadTooLargeException("photo_too_large");
        }

        var profile = await RequireProfileAsync(userId, cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxPhotoBytes)
            {
                throw new PayloadTooLargeException("photo_too_large");
            }

            buffer.Write(chunk, 0, read);
        }

        var type = DetectImage(buffer.GetBuffer().AsSpan(0, (int)buffer.Length))
            ?? throw new RequestValidationException("file", "photo_type_invalid");
        var now = time.GetUtcNow();
        var key = $"photos/{AttachmentService.NewStorageKey(now)}";
        buffer.Position = 0;
        try
        {
            await storage.PutAsync(key, buffer, buffer.Length, type, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not StorageUnavailableException)
        {
            throw new StorageUnavailableException("The photo could not be stored.", ex);
        }

        var previous = profile.SetPhoto(key, type, now);
        await profiles.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await TryDeleteAsync(previous).ConfigureAwait(false);
        return profile;
    }

    public async Task<UserProfile> RemovePhotoAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await RequireProfileAsync(userId, cancellationToken).ConfigureAwait(false);
        var previous = profile.RemovePhoto(time.GetUtcNow());
        await profiles.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await TryDeleteAsync(previous).ConfigureAwait(false);
        return profile;
    }

    /// <summary>The person's photo, or null when they have none (or the account was deleted).</summary>
    public async Task<ProfilePhotoContent?> OpenPhotoAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await profiles.FindAsync(userId, cancellationToken).ConfigureAwait(false);
        if (profile is not { IsDeleted: false, PhotoKey: { } key, PhotoContentType: { } type })
        {
            return null;
        }

        var stream = await storage.OpenReadAsync(key, cancellationToken).ConfigureAwait(false);
        return stream is null ? null : new ProfilePhotoContent(stream, type, profile.PhotoVersion);
    }

    /// <summary>PNG, JPEG or WebP by signature; anything else is refused.</summary>
    public static string? DetectImage(ReadOnlySpan<byte> bytes) => bytes switch
    {
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => "image/png",
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        _ => null,
    };

    // ---- Links -----------------------------------------------------------------------------------

    public async Task<IReadOnlyList<ProfileLink>> ListOwnLinksAsync(Guid userId, CancellationToken cancellationToken)
    {
        await RequireProfileAsync(userId, cancellationToken).ConfigureAwait(false);
        return await links.ListAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces the person's links, in the given order. Every row is checked; problems are reported per row.</summary>
    public async Task<IReadOnlyList<ProfileLink>> ReplaceLinksAsync(Guid userId, IReadOnlyList<ProfileLinkInput>? inputs, CancellationToken cancellationToken)
    {
        await RequireProfileAsync(userId, cancellationToken).ConfigureAwait(false);
        var rows = inputs ?? [];
        if (rows.Count > ProfileLink.MaxPerUser)
        {
            throw new RequestValidationException("links", "links_too_many");
        }

        var errors = new Dictionary<string, string[]>();
        var created = new List<ProfileLink>();
        var now = time.GetUtcNow();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (!Platforms.TryGetValue(row.Platform?.Trim().ToLowerInvariant() ?? string.Empty, out var platform))
            {
                errors[$"links[{i}].platform"] = ["link_platform_invalid"];
                continue;
            }

            ProfileLinkVisibility? visibility = row.Visibility?.Trim().ToLowerInvariant() switch
            {
                null or "" or "contacts" => ProfileLinkVisibility.Contacts,
                "only_me" => ProfileLinkVisibility.OnlyMe,
                _ => null,
            };
            if (visibility is null)
            {
                errors[$"links[{i}].visibility"] = ["link_visibility_invalid"];
                continue;
            }

            try
            {
                created.Add(ProfileLink.Create(Guid.CreateVersion7(now), userId, platform, row.Url, visibility.Value, i));
            }
            catch (DomainRuleException ex)
            {
                errors[$"links[{i}].url"] = [ex.Code];
            }
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        await links.ReplaceAsync(userId, created, cancellationToken).ConfigureAwait(false);
        return created;
    }

    /// <summary>
    /// The links <paramref name="viewerId"/> may see on <paramref name="userId"/>'s profile: all of
    /// them for the person themself, the ones for contacts for a contact, none for anyone else.
    /// </summary>
    public async Task<IReadOnlyList<ProfileLink>> ListVisibleLinksAsync(Guid viewerId, Guid userId, CancellationToken cancellationToken)
    {
        if (viewerId == userId)
        {
            return await links.ListAsync(userId, cancellationToken).ConfigureAwait(false);
        }

        if (await contacts.FindContactAsync(UserPair.Of(viewerId, userId), cancellationToken).ConfigureAwait(false) is null)
        {
            return [];
        }

        return (await links.ListAsync(userId, cancellationToken).ConfigureAwait(false)).Where(l => l.Visibility == ProfileLinkVisibility.Contacts).ToList();
    }

    private async Task<UserProfile> RequireProfileAsync(Guid userId, CancellationToken cancellationToken) =>
        await profiles.FindAsync(userId, cancellationToken).ConfigureAwait(false) is { IsDeleted: false } profile
            ? profile
            : throw new ResourceNotFoundException("user_not_found");

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
