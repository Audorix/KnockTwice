using Audorix.ChatX.Contracts.V1.Conversations;
using Audorix.ChatX.Contracts.V1.Users;

namespace Audorix.ChatX.Contracts.V1.Widgets;

/// <summary>A website widget as its organization's managers see it. The signing secret is never included.</summary>
/// <param name="Position"><c>right</c> or <c>left</c>.</param>
/// <param name="DefaultLanguage"><c>auto</c>, <c>en</c> or <c>es</c>.</param>
/// <param name="AllowedOrigins">Exact origins allowed to embed the widget, e.g. <c>https://shop.example.org</c>.</param>
public sealed record WidgetResponse(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string Title,
    string? WelcomeMessage,
    string AccentColor,
    string Position,
    string DefaultLanguage,
    bool AllowAnonymous,
    bool Enabled,
    IReadOnlyList<string> AllowedOrigins,
    IReadOnlyList<UserSummaryResponse> Responders,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>Returned once when a widget is created or its secret replaced. Store the secret on your server only.</summary>
public sealed record WidgetSecretResponse(WidgetResponse Widget, string SigningSecret);

public sealed record CreateWidgetRequest(string? Name);

/// <summary>Null fields stay unchanged.</summary>
public sealed record UpdateWidgetRequest(
    string? Name,
    string? Title,
    string? WelcomeMessage,
    string? AccentColor,
    string? Position,
    string? DefaultLanguage,
    bool? AllowAnonymous,
    IReadOnlyList<string>? AllowedOrigins,
    bool? Enabled,
    IReadOnlyList<Guid>? ResponderIds);

/// <summary>Public widget configuration for the embedded chat. Contains no secrets.</summary>
public sealed record WidgetConfigResponse(
    Guid Id, string Title, string? WelcomeMessage, string AccentColor, string Position, string DefaultLanguage, bool AllowAnonymous, long MaxFileSize);

/// <summary>Origins allowed to frame the widget (used by the web server for <c>frame-ancestors</c>).</summary>
public sealed record WidgetFramePolicyResponse(IReadOnlyList<string> FrameAncestors);

/// <param name="IdentityToken">A JWT your server signed with the widget's secret (HS256; sub, name, email, aud = widget id, iat, exp). Omit for an anonymous visitor.</param>
/// <param name="ParentOrigin">The origin of the page embedding the widget, as the browser reports it to the frame.</param>
public sealed record VisitorSessionRequest(string? IdentityToken, string? ParentOrigin, string? Language);

/// <summary>A visitor session. Send <paramref name="Token"/> as <c>Authorization: Bearer</c>.</summary>
public sealed record VisitorSessionResponse(string Token, DateTimeOffset ExpiresAtUtc, Guid VisitorId, string DisplayName, bool Identified);

/// <summary>The visitor's conversation: history (ascending), who answers, and the visitor's read position.</summary>
public sealed record VisitorConversationResponse(
    Guid? ConversationId,
    Guid VisitorUserId,
    IReadOnlyList<ConversationEntryResponse> Items,
    string? NextCursor,
    bool HasMore,
    IReadOnlyList<UserSummaryResponse> Responders,
    long LastReadSequence);

public sealed record VisitorMessageRequest(string? ClientMessageId, string? Body, IReadOnlyList<Guid>? AttachmentIds);
