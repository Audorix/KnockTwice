using Audorix.ChatX.Contracts.V1.Users;

namespace Audorix.ChatX.Contracts.V1.Contacts;

/// <param name="SharedOrganizations">
/// Organizations both you and the contact are active members of, by name (contacts list; null in
/// the blocked list).
/// </param>
public sealed record ContactResponse(UserSummaryResponse User, DateTimeOffset SinceUtc, IReadOnlyList<SharedOrganizationResponse>? SharedOrganizations = null);

public sealed record SharedOrganizationResponse(Guid Id, string Name);

/// <summary>What a contact's profile drawer shows (2026-11-24).</summary>
/// <param name="Handle">Display ID, shown as <c>@handle</c>.</param>
/// <param name="Email">The contact's email address; null when they keep it private.</param>
/// <param name="EmailShared">Whether the contact shares their email address with contacts.</param>
public sealed record ContactDetailsResponse(string? Handle, string? Email, bool EmailShared);

/// <param name="Direction"><c>incoming</c> or <c>outgoing</c>.</param>
public sealed record ContactRequestResponse(Guid Id, string Direction, UserSummaryResponse User, DateTimeOffset CreatedAtUtc);

public sealed record SendContactRequest(Guid UserId);

/// <param name="Status"><c>pending</c>, or <c>accepted</c> when the other user had already asked.</param>
public sealed record SendContactRequestResponse(string Status, ContactRequestResponse? Request);
