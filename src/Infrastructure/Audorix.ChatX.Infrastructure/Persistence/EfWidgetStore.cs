using Audorix.ChatX.Application.Widgets;
using Audorix.ChatX.Domain.Conversations;
using Audorix.ChatX.Domain.Users;
using Audorix.ChatX.Domain.Widgets;
using Audorix.ChatX.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Audorix.ChatX.Infrastructure.Persistence;

internal sealed class EfWidgetStore(ChatXDbContext db) : IWidgetStore
{
    public void Add(Widget widget) => db.Widgets.Add(widget);

    public void Add(Visitor visitor) => db.Visitors.Add(visitor);

    public void Add(VisitorSession session) => db.VisitorSessions.Add(session);

    public Task<Widget?> FindAsync(Guid widgetId, CancellationToken cancellationToken) =>
        db.Widgets.FirstOrDefaultAsync(w => w.Id == widgetId, cancellationToken);

    public async Task<IReadOnlyList<Widget>> ListForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.Widgets.Where(w => w.OrganizationId == organizationId).OrderBy(w => w.Name).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Guid>> ListResponderIdsAsync(Guid widgetId, CancellationToken cancellationToken) =>
        await db.WidgetResponders.AsNoTracking().Where(r => r.WidgetId == widgetId).Select(r => r.UserId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task ReplaceRespondersAsync(Guid widgetId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        var current = await db.WidgetResponders.Where(r => r.WidgetId == widgetId).ToListAsync(cancellationToken).ConfigureAwait(false);
        db.WidgetResponders.RemoveRange(current.Where(r => !userIds.Contains(r.UserId)));
        foreach (var userId in userIds.Where(id => current.All(r => r.UserId != id)))
        {
            db.WidgetResponders.Add(WidgetResponder.Create(widgetId, userId));
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<Visitor?> FindVisitorAsync(Guid visitorId, CancellationToken cancellationToken) =>
        db.Visitors.FirstOrDefaultAsync(v => v.Id == visitorId, cancellationToken);

    public Task<Visitor?> FindVisitorBySubjectAsync(Guid widgetId, string subject, CancellationToken cancellationToken) =>
        db.Visitors.FirstOrDefaultAsync(v => v.WidgetId == widgetId && v.ExternalSubject == subject, cancellationToken);

    public Task<VisitorSession?> FindSessionByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        db.VisitorSessions.FirstOrDefaultAsync(s => s.TokenHash == tokenHash, cancellationToken);

    public Task<VisitorSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        db.VisitorSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

    public async Task<Guid?> FindConversationIdAsync(Guid widgetId, Guid visitorUserId, CancellationToken cancellationToken) =>
        await (
            from c in db.Conversations.AsNoTracking()
            where c.WidgetId == widgetId && c.Type == ConversationType.Website
            join p in db.ConversationParticipants.AsNoTracking() on c.Id equals p.ConversationId
            where p.UserId == visitorUserId && p.Status == ParticipantStatus.Active
            select (Guid?)c.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

/// <summary>Widget signing secrets at rest, protected with the installation's Data Protection key ring.</summary>
internal sealed class DataProtectionWidgetSecretProtector(IDataProtectionProvider provider) : IWidgetSecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Audorix.ChatX.Widgets.SigningSecret.v1");

    public string Protect(string secret) => _protector.Protect(secret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}

/// <summary>
/// Stand-in accounts for visitors: no password, an undeliverable <c>.invalid</c> address (Identity
/// requires unique emails), permanently locked out and flagged <see cref="ChatXUser.IsVisitor"/>.
/// </summary>
internal sealed class IdentityVisitorAccounts(UserManager<ChatXUser> users, ChatXDbContext db, TimeProvider time) : IVisitorAccounts
{
    public async Task CreateAsync(Guid userId, string displayName, string language, CancellationToken cancellationToken)
    {
        var user = new ChatXUser
        {
            Id = userId,
            UserName = $"visitor-{userId:N}",
            Email = $"{userId:N}@visitors.chatx.invalid",
            EmailConfirmed = false,
            IsVisitor = true,
            LockoutEnabled = true,
            LockoutEnd = DateTimeOffset.MaxValue,
        };
        db.UserProfiles.Add(UserProfile.Create(userId, displayName, language, time.GetUtcNow()));
        var result = await users.CreateAsync(user).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("The visitor account could not be created: " + string.Join(", ", result.Errors.Select(e => e.Code)));
        }
    }

    public async Task RenameAsync(Guid userId, string displayName, CancellationToken cancellationToken)
    {
        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken).ConfigureAwait(false);
        profile?.Rename(displayName, time.GetUtcNow());
    }
}
