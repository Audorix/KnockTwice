using System.Security.Cryptography;
using System.Text;
using Audorix.ChatX.Application.Administration;
using Audorix.ChatX.Application.Attachments;
using Audorix.ChatX.Application.Common;
using Audorix.ChatX.Application.Conversations;
using Audorix.ChatX.Application.Messaging;
using Audorix.ChatX.Application.Organizations;
using Audorix.ChatX.Application.Users;
using Audorix.ChatX.Domain.Attachments;
using Audorix.ChatX.Domain.Organizations;
using Audorix.ChatX.Domain.Widgets;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Application.Widgets;

public sealed record WidgetView(Widget Widget, IReadOnlyList<UserSummary> Responders);

/// <summary>A widget together with its signing secret, returned only when the secret is created or replaced.</summary>
public sealed record WidgetWithSecret(WidgetView View, string Secret);

public sealed record WidgetChanges(
    string? Name, string? Title, string? WelcomeMessage, string? AccentColor, WidgetPosition? Position, string? DefaultLanguage, bool? AllowAnonymous,
    IReadOnlyList<string>? AllowedOrigins, bool? Enabled, IReadOnlyList<Guid>? ResponderIds);

/// <summary>
/// Widget administration by organization owners and administrators (Phase 10). The signing secret
/// is shown once on creation and on rotation; afterwards only its protected form is stored.
/// Responders must be active members; a widget can only be enabled with an allowed origin and a responder.
/// </summary>
public sealed class WidgetService(
    IWidgetStore store,
    IWidgetSecretProtector protector,
    OrganizationService organizations,
    IOrganizationStore organizationStore,
    IUserDirectory directory,
    AuditService audit,
    TimeProvider time)
{
    public async Task<IReadOnlyList<WidgetView>> ListAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken)
    {
        await RequireManagerAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        var views = new List<WidgetView>();
        foreach (var widget in await store.ListForOrganizationAsync(organizationId, cancellationToken).ConfigureAwait(false))
        {
            views.Add(await ViewAsync(widget, cancellationToken).ConfigureAwait(false));
        }

        return views;
    }

    public async Task<WidgetView> GetAsync(Guid userId, Guid organizationId, Guid widgetId, CancellationToken cancellationToken)
    {
        var widget = await RequireWidgetAsync(userId, organizationId, widgetId, cancellationToken).ConfigureAwait(false);
        return await ViewAsync(widget, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WidgetWithSecret> CreateAsync(Guid userId, Guid organizationId, string? name, CancellationToken cancellationToken)
    {
        await RequireManagerAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        var secret = NewSecret();
        var now = time.GetUtcNow();
        var widget = Widget.Create(Guid.CreateVersion7(now), organizationId, name ?? string.Empty, userId, protector.Protect(secret), now);
        store.Add(widget);
        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await store.ReplaceRespondersAsync(widget.Id, [userId], cancellationToken).ConfigureAwait(false);
        await audit.RecordAsync("widgets.created", userId, "widget", widget.Id.ToString(), new Dictionary<string, object?> { ["organization"] = organizationId.ToString() },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return new WidgetWithSecret(await ViewAsync(widget, cancellationToken).ConfigureAwait(false), secret);
    }

    public async Task<WidgetView> UpdateAsync(Guid userId, Guid organizationId, Guid widgetId, WidgetChanges changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var widget = await RequireWidgetAsync(userId, organizationId, widgetId, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();

        // Every check runs before anything is written, so a refused change leaves the widget as it was.
        var responders = changes.ResponderIds?.Distinct().ToList();
        foreach (var id in responders ?? [])
        {
            if (await organizationStore.FindMemberAsync(organizationId, id, cancellationToken).ConfigureAwait(false) is not { IsActive: true })
            {
                throw new RequestValidationException("responderIds", "responder_not_member");
            }
        }

        var responderCount = responders?.Count ?? (await store.ListResponderIdsAsync(widgetId, cancellationToken).ConfigureAwait(false)).Count;
        if (changes.Enabled == true && responderCount == 0)
        {
            throw new RequestValidationException("responderIds", "widget_responders_required");
        }

        widget.Update(
            changes.Name ?? widget.Name,
            changes.Title ?? widget.Title,
            changes.WelcomeMessage ?? widget.WelcomeMessage,
            changes.AccentColor ?? widget.AccentColor,
            changes.Position ?? widget.Position,
            changes.DefaultLanguage ?? widget.DefaultLanguage,
            changes.AllowAnonymous ?? widget.AllowAnonymous,
            changes.AllowedOrigins ?? widget.Origins,
            now);

        if (changes.Enabled is { } enabled)
        {
            widget.SetEnabled(enabled, now);
        }
        else if (responderCount == 0 && widget.Enabled)
        {
            widget.SetEnabled(false, now); // no one left to answer
        }

        if (responders is not null)
        {
            await store.ReplaceRespondersAsync(widgetId, responders, cancellationToken).ConfigureAwait(false); // also saves the widget
        }
        else
        {
            await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        await audit.RecordAsync("widgets.updated", userId, "widget", widgetId.ToString(), cancellationToken: cancellationToken).ConfigureAwait(false);
        return await ViewAsync(widget, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces the signing secret; tokens signed with the old one stop working at once.</summary>
    public async Task<WidgetWithSecret> RotateSecretAsync(Guid userId, Guid organizationId, Guid widgetId, CancellationToken cancellationToken)
    {
        var widget = await RequireWidgetAsync(userId, organizationId, widgetId, cancellationToken).ConfigureAwait(false);
        var secret = NewSecret();
        widget.ReplaceSecret(protector.Protect(secret), time.GetUtcNow());
        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await audit.RecordAsync("widgets.secret_rotated", userId, "widget", widgetId.ToString(), cancellationToken: cancellationToken).ConfigureAwait(false);
        return new WidgetWithSecret(await ViewAsync(widget, cancellationToken).ConfigureAwait(false), secret);
    }

    private static string NewSecret() => IdentityTokens.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private async Task<WidgetView> ViewAsync(Widget widget, CancellationToken cancellationToken)
    {
        var ids = await store.ListResponderIdsAsync(widget.Id, cancellationToken).ConfigureAwait(false);
        var users = await directory.GetSummariesAsync(ids, cancellationToken).ConfigureAwait(false);
        return new WidgetView(widget, ids.Where(users.ContainsKey).Select(id => users[id]).OrderBy(u => u.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    private async Task RequireManagerAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken)
    {
        var (_, membership) = await organizations.RequireMembershipAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        OrganizationService.RequirePermission(membership, OrganizationPermission.Manage);
    }

    private async Task<Widget> RequireWidgetAsync(Guid userId, Guid organizationId, Guid widgetId, CancellationToken cancellationToken)
    {
        await RequireManagerAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
        var widget = await store.FindAsync(widgetId, cancellationToken).ConfigureAwait(false);
        return widget is not null && widget.OrganizationId == organizationId ? widget : throw new ResourceNotFoundException("widget_not_found");
    }
}

/// <summary>The authenticated visitor behind a session token.</summary>
public sealed record VisitorPrincipal(Guid SessionId, Guid VisitorId, Guid WidgetId, Guid UserId, string DisplayName, bool Identified);

public sealed record VisitorSessionStarted(string Token, DateTimeOffset ExpiresAtUtc, VisitorPrincipal Visitor);

/// <summary>What a visitor sees of its conversation: the history and who answers.</summary>
public sealed record VisitorConversationView(Guid? ConversationId, HistoryPage History, IReadOnlyList<UserSummary> Responders, long LastReadSequence);

/// <summary>
/// The visitor side of a widget (Phase 10, owner decisions): anonymous sessions (if the widget allows
/// them) or identities signed by the host server; one conversation per visitor, created with the
/// first message; per-address and per-session rate limits; attachments only for identified visitors.
/// Everything a visitor does goes through the same conversation, messaging and attachment rules as
/// signed-in users, with the visitor's stand-in account.
/// </summary>
public sealed class VisitorService(
    IWidgetStore store,
    IWidgetSecretProtector protector,
    IVisitorAccounts accounts,
    ConversationService conversations,
    MessagingService messaging,
    AttachmentService attachments,
    VisitorRateLimiter limiter,
    TimeProvider time,
    IOptionsSnapshot<WidgetSettings> settings)
{
    private const string NotFound = "widget_not_found";

    /// <summary>A widget visitors may use right now (exists, enabled, feature on).</summary>
    public async Task<Widget> RequirePublicWidgetAsync(Guid widgetId, CancellationToken cancellationToken)
    {
        if (!settings.Value.Enabled)
        {
            throw new ResourceNotFoundException(NotFound);
        }

        var widget = await store.FindAsync(widgetId, cancellationToken).ConfigureAwait(false);
        return widget is { Enabled: true } ? widget : throw new ResourceNotFoundException(NotFound);
    }

    /// <summary>
    /// Starts a visitor session. With <paramref name="identityToken"/> the host server vouches for the
    /// visitor; without it the visitor is anonymous (if allowed). The embedding page's origin, as
    /// reported by the frame, must be one of the widget's origins (browsers also enforce this with
    /// frame-ancestors; this is the abuse-control layer of ADR-024).
    /// </summary>
    public async Task<VisitorSessionStarted> StartSessionAsync(
        Guid widgetId, string? identityToken, string? parentOrigin, string? language, string? remoteAddress, CancellationToken cancellationToken)
    {
        var widget = await RequirePublicWidgetAsync(widgetId, cancellationToken).ConfigureAwait(false);
        if (!widget.AllowsOrigin(parentOrigin))
        {
            throw new ForbiddenException("widget_origin_not_allowed");
        }

        var options = settings.Value;
        var now = time.GetUtcNow();
        var lang = language?.Trim().ToLowerInvariant() is "es" ? "es" : "en";
        Visitor visitor;
        if (!string.IsNullOrWhiteSpace(identityToken))
        {
            var identity = IdentityTokens.Verify(identityToken, protector.Unprotect(widget.ProtectedSecret), widget.Id, now, options.IdentityTokenMaxLifetime)
                ?? throw new UnauthorizedVisitorException();
            visitor = await store.FindVisitorBySubjectAsync(widget.Id, identity.Subject, cancellationToken).ConfigureAwait(false) ?? await CreateIdentifiedAsync(widget, identity, lang, now, cancellationToken).ConfigureAwait(false);
            if (visitor.DisplayName != identity.Name || visitor.Email != identity.Email)
            {
                visitor.UpdateIdentity(identity.Name, identity.Email, now);
                await accounts.RenameAsync(visitor.UserId, identity.Name, cancellationToken).ConfigureAwait(false);
                if (await store.FindConversationIdAsync(widget.Id, visitor.UserId, cancellationToken).ConfigureAwait(false) is { } conversationId
                    && await conversations.RequireAccessAsync(visitor.UserId, conversationId, cancellationToken).ConfigureAwait(false) is var (conversation, _))
                {
                    conversation.RenameVisitor(identity.Name);
                }
            }
        }
        else
        {
            if (!widget.AllowAnonymous)
            {
                throw new UnauthorizedVisitorException("widget_identity_required");
            }

            if (!limiter.TryAcquire($"session:{remoteAddress}", options.AnonymousSessionsPerHourPerAddress, TimeSpan.FromHours(1)))
            {
                throw new TooManyRequestsException();
            }

            var userId = Guid.CreateVersion7(now);
            var name = $"Visitor {Convert.ToHexString(RandomNumberGenerator.GetBytes(2))}";
            await accounts.CreateAsync(userId, name, lang, cancellationToken).ConfigureAwait(false);
            visitor = Visitor.CreateAnonymous(Guid.CreateVersion7(now), widget.Id, userId, name, now);
            store.Add(visitor);
        }

        var token = IdentityTokens.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var session = VisitorSession.Start(Guid.CreateVersion7(now), visitor.Id, widget.Id, Hash(token), now, options.SessionLifetime);
        store.Add(session);
        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new VisitorSessionStarted(token, session.ExpiresAtUtc,
            new VisitorPrincipal(session.Id, visitor.Id, widget.Id, visitor.UserId, visitor.DisplayName, visitor.IsIdentified));
    }

    /// <summary>The visitor behind a bearer token, or null (unknown, revoked, expired, widget off). Renews the session while used.</summary>
    public async Task<VisitorPrincipal?> AuthenticateAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128 || !settings.Value.Enabled)
        {
            return null;
        }

        var session = await store.FindSessionByHashAsync(Hash(token), cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow();
        if (session is null || !session.IsActive(now))
        {
            return null;
        }

        var widget = await store.FindAsync(session.WidgetId, cancellationToken).ConfigureAwait(false);
        var visitor = await store.FindVisitorAsync(session.VisitorId, cancellationToken).ConfigureAwait(false);
        if (widget is not { Enabled: true } || visitor is null)
        {
            return null;
        }

        if (session.Touch(now, settings.Value.SessionLifetime, settings.Value.SessionTouchInterval))
        {
            visitor.Seen(now);
            await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new VisitorPrincipal(session.Id, visitor.Id, widget.Id, visitor.UserId, visitor.DisplayName, visitor.IsIdentified);
    }

    /// <summary>Whether a visitor session is still valid (checked on every realtime call).</summary>
    public async Task<bool> IsSessionActiveAsync(Guid sessionId, CancellationToken cancellationToken) =>
        settings.Value.Enabled
        && await store.FindSessionAsync(sessionId, cancellationToken).ConfigureAwait(false) is { } session
        && session.IsActive(time.GetUtcNow())
        && await store.FindAsync(session.WidgetId, cancellationToken).ConfigureAwait(false) is { Enabled: true };

    public async Task EndSessionAsync(VisitorPrincipal visitor, string token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        var session = await store.FindSessionByHashAsync(Hash(token), cancellationToken).ConfigureAwait(false);
        if (session is not null && session.Id == visitor.SessionId)
        {
            session.Revoke(time.GetUtcNow());
            await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<VisitorConversationView> GetConversationAsync(VisitorPrincipal visitor, long? beforeSequence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        var id = await store.FindConversationIdAsync(visitor.WidgetId, visitor.UserId, cancellationToken).ConfigureAwait(false);
        if (id is not { } conversationId)
        {
            return new VisitorConversationView(null, new HistoryPage([], null, false), [], 0);
        }

        var history = await conversations.GetHistoryAsync(visitor.UserId, conversationId, beforeSequence, null, cancellationToken).ConfigureAwait(false);
        var detail = await conversations.GetAsync(visitor.UserId, conversationId, cancellationToken).ConfigureAwait(false);
        return new VisitorConversationView(conversationId, history,
            detail.Participants.Where(p => !p.Participant.IsVisitor).Select(p => p.User).ToList(), detail.Summary.Me.LastReadSequence);
    }

    /// <summary>Sends a message, starting the visitor's conversation with the first one.</summary>
    public async Task<(HistoryEntryView Message, bool Created)> SendAsync(
        VisitorPrincipal visitor, string? clientMessageId, string? body, IReadOnlyList<Guid>? attachmentIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        if (!limiter.TryAcquire($"messages:{visitor.SessionId}", settings.Value.MessagesPerMinute, TimeSpan.FromMinutes(1)))
        {
            throw new TooManyRequestsException();
        }

        var conversationId = await EnsureConversationAsync(visitor, cancellationToken).ConfigureAwait(false);
        var result = await messaging.SendAsync(visitor.UserId, conversationId, clientMessageId, body, null, cancellationToken, attachmentIds).ConfigureAwait(false);
        if (result.Created)
        {
            await conversations.ReopenOnVisitorMessageAsync(visitor.UserId, conversationId, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>Signed-in (identified) visitors only, owner decision.</summary>
    public async Task<Attachment> UploadAsync(
        VisitorPrincipal visitor, string? fileName, string? contentType, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        if (!visitor.Identified)
        {
            throw new ForbiddenException("widget_attachments_identified_only");
        }

        var conversationId = await EnsureConversationAsync(visitor, cancellationToken).ConfigureAwait(false);
        return await attachments.UploadAsync(visitor.UserId, conversationId, fileName, contentType, null, content, cancellationToken).ConfigureAwait(false);
    }

    public Task<Guid?> GetConversationIdAsync(VisitorPrincipal visitor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return store.FindConversationIdAsync(visitor.WidgetId, visitor.UserId, cancellationToken);
    }

    public async Task<Guid> RequireConversationIdAsync(VisitorPrincipal visitor, CancellationToken cancellationToken) =>
        await store.FindConversationIdAsync(visitor.WidgetId, visitor.UserId, cancellationToken).ConfigureAwait(false)
        ?? throw new ResourceNotFoundException("conversation_not_found");

    private async Task<Guid> EnsureConversationAsync(VisitorPrincipal visitor, CancellationToken cancellationToken)
    {
        if (await store.FindConversationIdAsync(visitor.WidgetId, visitor.UserId, cancellationToken).ConfigureAwait(false) is { } existing)
        {
            return existing;
        }

        var widget = await RequirePublicWidgetAsync(visitor.WidgetId, cancellationToken).ConfigureAwait(false);
        var responders = await store.ListResponderIdsAsync(widget.Id, cancellationToken).ConfigureAwait(false);
        var conversation = await conversations.CreateWebsiteConversationAsync(widget.OrganizationId, widget.Id, visitor.UserId, visitor.DisplayName, responders, cancellationToken)
            .ConfigureAwait(false);
        return conversation.Id;
    }

    private async Task<Visitor> CreateIdentifiedAsync(Widget widget, VerifiedIdentity identity, string language, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var userId = Guid.CreateVersion7(now);
        await accounts.CreateAsync(userId, identity.Name, language, cancellationToken).ConfigureAwait(false);
        var visitor = Visitor.CreateIdentified(Guid.CreateVersion7(now), widget.Id, userId, identity.Subject, identity.Name, identity.Email, now);
        store.Add(visitor);
        return visitor;
    }

    private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>A visitor credential or identity was missing or invalid (401).</summary>
public sealed class UnauthorizedVisitorException(string code = "widget_identity_invalid") : ApplicationRuleException(code, "The visitor could not be identified.");

/// <summary>A rate limit was reached (429).</summary>
public sealed class TooManyRequestsException() : ApplicationRuleException("too_many_requests", "Too many requests.");
