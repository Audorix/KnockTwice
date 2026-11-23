using Audorix.ChatX.Contracts.V1.Users;

namespace Audorix.ChatX.Contracts.V1.Contacts;

public sealed record ContactResponse(UserSummaryResponse User, DateTimeOffset SinceUtc);

/// <param name="Direction"><c>incoming</c> or <c>outgoing</c>.</param>
public sealed record ContactRequestResponse(Guid Id, string Direction, UserSummaryResponse User, DateTimeOffset CreatedAtUtc);

public sealed record SendContactRequest(Guid UserId);

/// <param name="Status"><c>pending</c>, or <c>accepted</c> when the other user had already asked.</param>
public sealed record SendContactRequestResponse(string Status, ContactRequestResponse? Request);
