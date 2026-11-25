namespace Audorix.KnockTwice.Contracts.V1.Admin;

/// <param name="Created">What this run added (all zero when everything already existed).</param>
/// <param name="Total">The demo data set's size.</param>
/// <param name="UserEmails">Demo accounts; all use <paramref name="Password"/>.</param>
/// <param name="Password">The shared demo password (demo data is only available where enabled for development).</param>
public sealed record DemoDataResponse(DemoDataCountsResponse Created, DemoDataCountsResponse Total, IReadOnlyList<string> UserEmails, string Password);

/// <param name="Activity">Mentions, saved messages, reminders and scheduled messages in demo conversations.</param>
/// <param name="Support">Support conversations from demo customers (also counted in <paramref name="Conversations"/>).</param>
/// <param name="Photos">Demo people with a profile photo (from the configured demo avatars folder).</param>
public sealed record DemoDataCountsResponse(int Users, int Organizations, int Contacts, int Conversations, int Messages, int Activity, int Support = 0, int Photos = 0);

/// <param name="Removed">The demo data that was removed.</param>
public sealed record DemoDataClearedResponse(DemoDataCountsResponse Removed);

/// <summary>A demo data load running in the background (2026-11-25), with progress per part.</summary>
/// <param name="State"><c>running</c>, <c>completed</c> or <c>failed</c>.</param>
/// <param name="Steps">The parts in loading order: <c>people</c>, <c>companies</c>, <c>contacts</c>, <c>chats</c>,
/// <c>channels</c>, <c>messages</c>, <c>activity</c>, <c>support</c> and, with a demo avatars folder, <c>photos</c>.</param>
/// <param name="Result">What the load added and the demo accounts, once completed.</param>
/// <param name="ErrorCode">Why the load stopped, once failed.</param>
public sealed record DemoDataJobResponse(
    Guid Id, string State, DateTimeOffset StartedAtUtc, DateTimeOffset? FinishedAtUtc, IReadOnlyList<DemoDataStepResponse> Steps,
    DemoDataResponse? Result, string? ErrorCode);

/// <param name="Done">Items processed so far: added, or found already there.</param>
public sealed record DemoDataStepResponse(string Key, int Done, int Total);
