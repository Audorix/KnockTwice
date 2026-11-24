namespace Audorix.ChatX.Contracts.V1.Admin;

/// <param name="Created">What this run added (all zero when everything already existed).</param>
/// <param name="Total">The demo data set's size.</param>
/// <param name="UserEmails">Demo accounts; all use <paramref name="Password"/>.</param>
/// <param name="Password">The shared demo password (demo data is only available where enabled for development).</param>
public sealed record DemoDataResponse(DemoDataCountsResponse Created, DemoDataCountsResponse Total, IReadOnlyList<string> UserEmails, string Password);

/// <param name="Activity">Mentions, saved messages, reminders and scheduled messages in demo conversations.</param>
/// <param name="Support">Support conversations from demo customers (also counted in <paramref name="Conversations"/>).</param>
public sealed record DemoDataCountsResponse(int Users, int Organizations, int Contacts, int Conversations, int Messages, int Activity, int Support = 0);

/// <param name="Removed">The demo data that was removed.</param>
public sealed record DemoDataClearedResponse(DemoDataCountsResponse Removed);
