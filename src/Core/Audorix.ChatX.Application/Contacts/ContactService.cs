using Audorix.ChatX.Application.Common;
using Audorix.ChatX.Application.Users;
using Audorix.ChatX.Domain.Contacts;

namespace Audorix.ChatX.Application.Contacts;

/// <summary>Persistence port for contacts, contact requests and blocks.</summary>
public interface IContactStore
{
    Task<Contact?> FindContactAsync(UserPair pair, CancellationToken cancellationToken);

    Task<IReadOnlyList<Contact>> ListContactsAsync(Guid userId, CancellationToken cancellationToken);

    Task<ContactRequest?> FindRequestAsync(Guid requestId, CancellationToken cancellationToken);

    Task<ContactRequest?> FindPendingRequestAsync(UserPair pair, CancellationToken cancellationToken);

    Task<IReadOnlyList<ContactRequest>> ListPendingRequestsAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserBlock?> FindBlockAsync(Guid blockerId, Guid blockedId, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserBlock>> ListBlocksByAsync(Guid blockerId, CancellationToken cancellationToken);

    /// <summary>Users that <paramref name="userId"/> blocked or was blocked by.</summary>
    Task<IReadOnlySet<Guid>> ListBlockPartnersAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> AreContactsOrPendingAsync(Guid userId, Guid otherUserId, CancellationToken cancellationToken);

    /// <summary>
    /// For each of <paramref name="otherUserIds"/>, the organizations both they and
    /// <paramref name="userId"/> are active members of (people without one are absent).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<SharedOrganization>>> ListSharedOrganizationsAsync(
        Guid userId, IReadOnlyCollection<Guid> otherUserIds, CancellationToken cancellationToken);

    void Add(Contact contact);

    void Add(ContactRequest request);

    void Add(UserBlock block);

    void Remove(Contact contact);

    void Remove(UserBlock block);

    /// <summary>Saves; returns false (and discards pending changes) when a uniqueness rule rejected a concurrent duplicate.</summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken);
}

/// <param name="SharedOrganizations">Organizations both people are active members of (contacts list only).</param>
public sealed record ContactView(UserSummary User, DateTimeOffset SinceUtc, IReadOnlyList<SharedOrganization>? SharedOrganizations = null);

public sealed record SharedOrganization(Guid Id, string Name);

public enum RequestDirection
{
    Incoming = 1,
    Outgoing = 2,
}

public sealed record ContactRequestView(Guid Id, RequestDirection Direction, UserSummary User, DateTimeOffset CreatedAtUtc);

public sealed record SendRequestResult(bool BecameContacts, ContactRequestView? Request);

/// <summary>
/// Contacts are personal relationships, independent of organization membership (Phase 3
/// acceptance). Blocking rules: a block stops requests in both directions, removes the contact and
/// closes pending requests; the blocked user is told "user not found", never that they were blocked.
/// </summary>
public sealed class ContactService(IContactStore store, IUserDirectory directory, TimeProvider time)
{
    public async Task<IReadOnlyList<ContactView>> ListContactsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var contacts = await store.ListContactsAsync(userId, cancellationToken).ConfigureAwait(false);
        var others = contacts.Select(c => c.Pair.Other(userId)).ToList();
        var users = await directory.GetSummariesAsync(others, cancellationToken).ConfigureAwait(false);
        var shared = await store.ListSharedOrganizationsAsync(userId, others, cancellationToken).ConfigureAwait(false);
        return contacts
            .Where(c => users.ContainsKey(c.Pair.Other(userId)))
            .Select(c => new ContactView(users[c.Pair.Other(userId)], c.CreatedAtUtc, shared.GetValueOrDefault(c.Pair.Other(userId)) ?? []))
            .OrderBy(c => c.User.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<ContactRequestView>> ListRequestsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var requests = await store.ListPendingRequestsAsync(userId, cancellationToken).ConfigureAwait(false);
        var users = await directory.GetSummariesAsync(requests.Select(r => Other(r, userId)).ToList(), cancellationToken).ConfigureAwait(false);
        return requests
            .Where(r => users.ContainsKey(Other(r, userId)))
            .Select(r => ToView(r, userId, users[Other(r, userId)]))
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToList();
    }

    public async Task<SendRequestResult> SendRequestAsync(Guid userId, Guid targetUserId, CancellationToken cancellationToken)
    {
        var pair = UserPair.Of(userId, targetUserId);
        if (!await directory.IsActiveUserAsync(targetUserId, cancellationToken).ConfigureAwait(false)
            || await store.FindBlockAsync(targetUserId, userId, cancellationToken).ConfigureAwait(false) is not null)
        {
            // Being blocked is indistinguishable from the user not existing.
            throw new ResourceNotFoundException("user_not_found");
        }

        if (await store.FindBlockAsync(userId, targetUserId, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new ConflictException("user_blocked");
        }

        if (await store.FindContactAsync(pair, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new ConflictException("already_contacts");
        }

        var now = time.GetUtcNow();
        var pending = await store.FindPendingRequestAsync(pair, cancellationToken).ConfigureAwait(false);
        if (pending is not null)
        {
            if (pending.RequesterId == userId)
            {
                return new SendRequestResult(false, ToView(pending, userId, await SummaryAsync(targetUserId, cancellationToken).ConfigureAwait(false)));
            }

            // They already asked us: sending a request back means accepting theirs.
            pending.Accept(userId, now);
            store.Add(Contact.Create(Guid.CreateVersion7(now), pair, now));
            await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
            return new SendRequestResult(true, null);
        }

        var request = ContactRequest.Create(Guid.CreateVersion7(now), userId, targetUserId, now);
        store.Add(request);
        await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
        return new SendRequestResult(false, ToView(request, userId, await SummaryAsync(targetUserId, cancellationToken).ConfigureAwait(false)));
    }

    public async Task AcceptAsync(Guid userId, Guid requestId, CancellationToken cancellationToken)
    {
        var request = await RequireRequestAsync(userId, requestId, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        request.Accept(userId, now);
        var pair = UserPair.Of(request.RequesterId, request.RecipientId);
        if (await store.FindContactAsync(pair, cancellationToken).ConfigureAwait(false) is null)
        {
            store.Add(Contact.Create(Guid.CreateVersion7(now), pair, now));
        }

        await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeclineAsync(Guid userId, Guid requestId, CancellationToken cancellationToken)
    {
        var request = await RequireRequestAsync(userId, requestId, cancellationToken).ConfigureAwait(false);
        request.Decline(userId, time.GetUtcNow());
        await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CancelAsync(Guid userId, Guid requestId, CancellationToken cancellationToken)
    {
        var request = await RequireRequestAsync(userId, requestId, cancellationToken).ConfigureAwait(false);
        request.Cancel(userId, time.GetUtcNow());
        await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveContactAsync(Guid userId, Guid otherUserId, CancellationToken cancellationToken)
    {
        var contact = await store.FindContactAsync(UserPair.Of(userId, otherUserId), cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("contact_not_found");
        store.Remove(contact);
        await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task BlockAsync(Guid userId, Guid targetUserId, CancellationToken cancellationToken)
    {
        var pair = UserPair.Of(userId, targetUserId);
        if ((await directory.GetSummariesAsync([targetUserId], cancellationToken).ConfigureAwait(false)).Count == 0)
        {
            throw new ResourceNotFoundException("user_not_found");
        }

        if (await store.FindBlockAsync(userId, targetUserId, cancellationToken).ConfigureAwait(false) is not null)
        {
            return; // idempotent
        }

        var now = time.GetUtcNow();
        store.Add(UserBlock.Create(Guid.CreateVersion7(now), userId, targetUserId, now));
        if (await store.FindContactAsync(pair, cancellationToken).ConfigureAwait(false) is { } contact)
        {
            store.Remove(contact);
        }

        (await store.FindPendingRequestAsync(pair, cancellationToken).ConfigureAwait(false))?.CloseBecauseOfBlock(now);
        if (!await store.TrySaveChangesAsync(cancellationToken).ConfigureAwait(false))
        {
            return; // a concurrent identical block won
        }
    }

    public async Task UnblockAsync(Guid userId, Guid targetUserId, CancellationToken cancellationToken)
    {
        if (await store.FindBlockAsync(userId, targetUserId, cancellationToken).ConfigureAwait(false) is { } block)
        {
            store.Remove(block);
            await SaveOrConflictAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<ContactView>> ListBlockedAsync(Guid userId, CancellationToken cancellationToken)
    {
        var blocks = await store.ListBlocksByAsync(userId, cancellationToken).ConfigureAwait(false);
        var users = await directory.GetSummariesAsync(blocks.Select(b => b.BlockedId).ToList(), cancellationToken).ConfigureAwait(false);
        return blocks.Where(b => users.ContainsKey(b.BlockedId)).Select(b => new ContactView(users[b.BlockedId], b.CreatedAtUtc)).ToList();
    }

    private async Task<ContactRequest> RequireRequestAsync(Guid userId, Guid requestId, CancellationToken cancellationToken)
    {
        var request = await store.FindRequestAsync(requestId, cancellationToken).ConfigureAwait(false);
        return request is not null && (request.RequesterId == userId || request.RecipientId == userId)
            ? request
            : throw new ResourceNotFoundException("contact_request_not_found");
    }

    private async Task<UserSummary> SummaryAsync(Guid userId, CancellationToken cancellationToken) =>
        (await directory.GetSummariesAsync([userId], cancellationToken).ConfigureAwait(false))[userId];

    private async Task SaveOrConflictAsync(CancellationToken cancellationToken)
    {
        if (!await store.TrySaveChangesAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException("conflict");
        }
    }

    private static Guid Other(ContactRequest request, Guid userId) => request.RequesterId == userId ? request.RecipientId : request.RequesterId;

    private static ContactRequestView ToView(ContactRequest request, Guid userId, UserSummary other) =>
        new(request.Id, request.RequesterId == userId ? RequestDirection.Outgoing : RequestDirection.Incoming, other, request.CreatedAtUtc);
}
