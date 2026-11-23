namespace Audorix.ChatX.Contracts.V1.Admin;

/// <param name="Created">What this run added (all zero when everything already existed).</param>
/// <param name="Total">The demo data set's size.</param>
/// <param name="UserEmails">Demo accounts; all use <paramref name="Password"/>.</param>
/// <param name="Password">The shared demo password (demo data is only available where enabled for development).</param>
public sealed record DemoDataResponse(DemoDataCountsResponse Created, DemoDataCountsResponse Total, IReadOnlyList<string> UserEmails, string Password);

public sealed record DemoDataCountsResponse(int Users, int Organizations, int Contacts, int Conversations, int Messages);

/// <param name="Removed">The demo data that was removed.</param>
public sealed record DemoDataClearedResponse(DemoDataCountsResponse Removed);
