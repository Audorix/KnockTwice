using System.Security.Cryptography;
using Audorix.KnockTwice.Application.Demo;
using Audorix.KnockTwice.Domain.Conversations;
using Audorix.KnockTwice.Domain.Organizations;
using Audorix.KnockTwice.Domain.Users;
using Audorix.KnockTwice.Domain.Widgets;
using Audorix.KnockTwice.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace Audorix.KnockTwice.Infrastructure.Persistence;

/// <summary>
/// Support demo data (owner request of 2026-11-24): the administrator who loads it and a few people
/// per company become support agents; each company gets a "Website chat" and an "In-app" widget
/// (switched off, without allowed sites); every customer is an identified visitor with a stand-in
/// account flagged as demo data. Conversations that already exist are left as they are.
/// </summary>
internal sealed partial class EfDemoDataStore
{
    private const int SupportBatch = 100;

    private async Task EnsureSupportAsync(DemoDataPlan plan, Dictionary<string, Guid> users, DateTimeOffset start, int[] created, CancellationToken cancellationToken)
    {
        var companyIds = plan.SupportDesks.Select(d => DemoDataPlan.DemoId("org:" + d.CompanyKey)).ToList();
        var organizations = await db.Organizations.Where(o => companyIds.Contains(o.Id)).Select(o => o.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var members = await db.OrganizationMembers.Where(m => companyIds.Contains(m.OrganizationId) && m.Status == MembershipStatus.Active)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var active = members.ToDictionary(m => (m.OrganizationId, m.UserId));

        // Support agents: the administrator who loads it and the desk's people (those still members).
        foreach (var desk in plan.SupportDesks)
        {
            var organizationId = DemoDataPlan.DemoId("org:" + desk.CompanyKey);
            foreach (var key in desk.Agents.Prepend(DemoDataPlan.Me))
            {
                if (active.TryGetValue((organizationId, users[key]), out var member) && !member.IsSupportAgent)
                {
                    member.SetSupportAgent(true);
                }
            }
        }

        // Two widgets per company; off and without allowed sites, so nobody outside can use them.
        var widgetIds = plan.SupportDesks.SelectMany(d => new[] { WidgetId(d.CompanyKey, false), WidgetId(d.CompanyKey, true) }).ToList();
        var widgets = await db.Widgets.Where(w => widgetIds.Contains(w.Id)).Select(w => w.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var widgetSet = widgets.ToHashSet();
        foreach (var desk in plan.SupportDesks)
        {
            var organizationId = DemoDataPlan.DemoId("org:" + desk.CompanyKey);
            if (!organizations.Contains(organizationId))
            {
                continue;
            }

            foreach (var inApp in new[] { false, true })
            {
                var id = WidgetId(desk.CompanyKey, inApp);
                if (widgetSet.Add(id))
                {
                    db.Widgets.Add(Widget.Create(id, organizationId, inApp ? "In-app" : "Website chat", users[DemoDataPlan.Me],
                        secrets.Protect(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))), start));
                }
            }
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);

        var ids = plan.Support.Select(s => s.Id).ToList();
        var existing = (await db.Conversations.Where(c => ids.Contains(c.Id)).Select(c => c.Id).ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();
        var visitorUserIds = plan.Support.Select(s => VisitorUserId(s.Key)).ToList();
        var existingVisitors = (await db.Users.Where(u => visitorUserIds.Contains(u.Id)).Select(u => u.Id).ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();
        var caller = users[DemoDataPlan.Me];
        var pending = 0;
        foreach (var item in plan.Support)
        {
            var organizationId = DemoDataPlan.DemoId("org:" + item.CompanyKey);
            var widgetId = WidgetId(item.CompanyKey, item.InApp);
            if (existing.Contains(item.Id) || !widgetSet.Contains(widgetId) || existingVisitors.Contains(VisitorUserId(item.Key)))
            {
                continue; // already there (or its customer was kept after the conversation went)
            }

            // People who left the company take no part: their work goes to the administrator who loads it.
            Guid? AgentId(string? key) => key is null ? null
                : active.ContainsKey((organizationId, users[key])) ? users[key]
                : active.ContainsKey((organizationId, caller)) ? caller : null;

            var assignee = AgentId(item.Assignee);
            if (item.Assignee is not null && assignee is null)
            {
                continue;
            }

            AddSupportConversation(item, organizationId, widgetId, assignee, AgentId, caller, start);
            created[3]++;
            created[4] += item.Messages.Count;
            created[6]++;
            if (++pending >= SupportBatch)
            {
                await SaveAsync(cancellationToken).ConfigureAwait(false);
                pending = 0;
            }
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    private void AddSupportConversation(
        DemoSupport item, Guid organizationId, Guid widgetId, Guid? assignee, Func<string?, Guid?> agentId, Guid caller, DateTimeOffset start)
    {
        DateTimeOffset At(int minutes) => start.AddDays(1).AddMinutes(minutes);
        var opened = At(item.Messages[0].At - 1);

        // The customer: an identified visitor with a stand-in account (removed with the demo data).
        var visitorUserId = VisitorUserId(item.Key);
        db.Users.Add(new ChatXUser
        {
            Id = visitorUserId,
            UserName = $"visitor-{visitorUserId:N}",
            NormalizedUserName = $"VISITOR-{visitorUserId:N}".ToUpperInvariant(),
            Email = $"{visitorUserId:N}@visitors.chatx.invalid",
            NormalizedEmail = $"{visitorUserId:N}@VISITORS.CHATX.INVALID".ToUpperInvariant(),
            EmailConfirmed = false,
            IsVisitor = true,
            IsDemo = true,
            LockoutEnabled = true,
            LockoutEnd = DateTimeOffset.MaxValue,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
        });
        db.UserProfiles.Add(UserProfile.Create(visitorUserId, item.CustomerName, item.CustomerLanguage, opened));
        db.Visitors.Add(Visitor.CreateIdentified(DemoDataPlan.DemoId("visitor:" + item.Key), widgetId, visitorUserId, "demo-" + item.Key,
            item.CustomerName, null, opened));

        var conversation = Conversation.CreateWebsite(item.Id, organizationId, widgetId, item.CustomerName, visitorUserId, opened);
        conversation.MarkAsDemo();
        conversation.SetSubject(item.Subject);
        db.Conversations.Add(conversation);
        var first = ConversationEvent.Record(Guid.CreateVersion7(opened), conversation, ConversationEventKind.Created, visitorUserId, null, null, opened);
        db.ConversationEvents.Add(first);
        var visitor = ConversationParticipant.JoinVisitor(Guid.CreateVersion7(opened), conversation.Id, visitorUserId, first.Sequence, opened);
        db.ConversationParticipants.Add(visitor);

        // The team: the assignee and whoever else wrote, all seeing the whole history.
        var team = new Dictionary<Guid, ConversationParticipant>();
        void Join(Guid userId, DateTimeOffset at)
        {
            if (!team.ContainsKey(userId))
            {
                var participant = ConversationParticipant.Join(Guid.CreateVersion7(at), conversation.Id, userId, ParticipantRole.Member, 1, at);
                db.ConversationParticipants.Add(participant);
                team[userId] = participant;
            }
        }

        long lastTeamMessage = 0;
        for (var i = 0; i < item.Messages.Count; i++)
        {
            var message = item.Messages[i];
            var at = At(message.At);
            var author = message.Agent is null ? visitorUserId : agentId(message.Agent) ?? assignee ?? caller;
            if (i == 1 && assignee is { } who)
            {
                // Taken right after the customer's first message.
                Join(who, at.AddSeconds(-30));
                conversation.Assign(who);
                db.ConversationEvents.Add(ConversationEvent.Record(Guid.CreateVersion7(at), conversation, ConversationEventKind.Assigned, who, who, null,
                    at.AddSeconds(-30)));
            }

            if (author != visitorUserId)
            {
                Join(author, at);
            }

            var entry = ConversationEvent.Post(Guid.CreateVersion7(at), conversation, author, $"demo-{item.Key}-{i}", message.Body, null, 20000, at);
            db.ConversationEvents.Add(entry);
            if (author != visitorUserId)
            {
                lastTeamMessage = entry.Sequence;
            }
        }

        if (assignee is { } owner && !team.ContainsKey(owner))
        {
            Join(owner, At(item.Messages[^1].At));
            conversation.Assign(owner);
        }

        if (item.Status != SupportStatus.Open)
        {
            var at = At(item.Messages[^1].At + 1);
            conversation.SetSupportStatus(item.Status);
            db.ConversationEvents.Add(ConversationEvent.Record(Guid.CreateVersion7(at), conversation, ConversationEventKind.StatusChanged, assignee ?? caller, null,
                item.Status == SupportStatus.Pending ? "pending" : "closed", at));
        }

        foreach (var (note, index) in item.Notes.Select((n, i) => (n, i)))
        {
            var writer = agentId(note.Agent) ?? assignee ?? caller;
            db.SupportNotes.Add(SupportNote.Create(DemoDataPlan.DemoId($"note:{item.Key}:{index}"), conversation, writer, note.Body, At(note.At)));
        }

        // The customer has read everything; the team has read up to its own last reply, so customer
        // messages after it are unread for the administrator who loads it (others have read all).
        visitor.MarkDelivered(conversation.LastSequence, conversation.LastSequence);
        visitor.MarkRead(conversation.LastSequence, conversation.LastSequence);
        foreach (var (userId, participant) in team)
        {
            participant.MarkDelivered(conversation.LastSequence, conversation.LastSequence);
            participant.MarkRead(userId == caller ? Math.Max(lastTeamMessage, participant.LastReadSequence) : conversation.LastSequence, conversation.LastSequence);
        }
    }

    private static Guid WidgetId(string companyKey, bool inApp) => DemoDataPlan.DemoId($"widget:{companyKey}:{(inApp ? "app" : "web")}");

    private static Guid VisitorUserId(string key) => DemoDataPlan.DemoId("visitor-user:" + key);
}
