using Audorix.ChatX.Application.Demo;
using Audorix.ChatX.Domain.Contacts;
using Audorix.ChatX.Domain.Conversations;
using Audorix.ChatX.Domain.Organizations;
using Audorix.ChatX.Domain.Users;
using Audorix.ChatX.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Audorix.ChatX.Infrastructure.Persistence;

/// <summary>
/// Bulk demo data (owner request of 2026-11-23). Loading reads what exists in a few set queries
/// and inserts only what is missing, in batches; clearing removes demo-flagged records with set
/// operations in one transaction. Demo accounts are created directly (not through the sign-up path)
/// with one shared password hash, which deliberately bypasses the password policy: demo data only
/// exists where <c>DemoData:Enabled</c> is on.
/// </summary>
internal sealed partial class EfDemoDataStore(ChatXDbContext db, Application.Widgets.IWidgetSecretProtector secrets) : IDemoDataStore
{
    private const int Batch = 1000;

    public async Task<DemoDataCounts> PopulateAsync(DemoDataPlan plan, Guid callerId, string password, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var created = new int[7];
        var start = nowUtc.AddDays(-DemoDataPlan.PeriodDays - 1);
        var users = await EnsureUsersAsync(plan, password, start, created, cancellationToken).ConfigureAwait(false);
        users[DemoDataPlan.Me] = callerId;

        await EnsureCompaniesAsync(plan, users, start, created, cancellationToken).ConfigureAwait(false);
        await EnsureContactsAsync(plan, users, start, created, cancellationToken).ConfigureAwait(false);
        var conversations = await EnsureConversationsAsync(plan, users, start, created, cancellationToken).ConfigureAwait(false);
        foreach (var (key, channel) in await EnsureChannelsAsync(plan, users, start, created, cancellationToken).ConfigureAwait(false))
        {
            conversations[key] = channel;
        }

        await EnsureMessagesAsync(plan, users, conversations, start, created, cancellationToken).ConfigureAwait(false);
        await EnsureActivityAsync(plan, users, conversations, nowUtc, created, cancellationToken).ConfigureAwait(false);
        await EnsureSupportAsync(plan, users, start, created, cancellationToken).ConfigureAwait(false);
        return new DemoDataCounts(created[0], created[1], created[2], created[3], created[4], created[5], created[6]);
    }

    // ---- People ------------------------------------------------------------------------------

    private async Task<Dictionary<string, Guid>> EnsureUsersAsync(DemoDataPlan plan, string password, DateTimeOffset start, int[] created, CancellationToken cancellationToken)
    {
        var normalized = plan.People.Select(p => p.Email.ToUpperInvariant()).ToList();
        var existing = await db.Users.Where(u => u.NormalizedEmail != null && normalized.Contains(u.NormalizedEmail))
            .ToDictionaryAsync(u => u.NormalizedEmail!, cancellationToken).ConfigureAwait(false);

        // One hash for every demo account: hashing is deliberately slow, and they share the password anyway.
        var hash = new PasswordHasher<ChatXUser>().HashPassword(new ChatXUser(), password);
        var users = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var person in plan.People)
        {
            if (existing.TryGetValue(person.Email.ToUpperInvariant(), out var user))
            {
                // Earlier demo versions used another password; demo accounts always share the current one.
                if (user.DeletedAtUtc is null && (user.PasswordHash != hash || !user.IsDemo))
                {
                    user.IsDemo = true;
                    user.PasswordHash = hash;
                    user.SecurityStamp = Guid.NewGuid().ToString("N");
                }

                users[person.Key] = user.Id;
                continue;
            }

            db.Users.Add(new ChatXUser
            {
                Id = person.Id,
                UserName = person.Email,
                NormalizedUserName = person.Email.ToUpperInvariant(),
                Email = person.Email,
                NormalizedEmail = person.Email.ToUpperInvariant(),
                EmailConfirmed = true,
                PasswordHash = hash,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                LockoutEnabled = true,
                IsDemo = true,
            });
            db.UserProfiles.Add(UserProfile.Create(person.Id, person.Name, person.Language, start));
            users[person.Key] = person.Id;
            created[0]++;
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);

        // Demo people show their email address to their contacts (contact drawer, 2026-11-24), also
        // when they were loaded before that setting existed.
        var demoIds = users.Values.ToList();
        await db.UserProfiles.Where(p => demoIds.Contains(p.UserId) && !p.ShareEmailWithContacts && p.DeletedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ShareEmailWithContacts, true), cancellationToken).ConfigureAwait(false);
        return users;
    }

    // ---- Companies ---------------------------------------------------------------------------

    private async Task EnsureCompaniesAsync(DemoDataPlan plan, Dictionary<string, Guid> users, DateTimeOffset start, int[] created, CancellationToken cancellationToken)
    {
        var ids = plan.Companies.Select(c => c.Id).ToList();
        var existing = await db.Organizations.Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken).ConfigureAwait(false);
        var memberships = (await db.OrganizationMembers.Where(m => ids.Contains(m.OrganizationId)).ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(m => (m.OrganizationId, m.UserId));
        var generalIds = plan.Companies.Select(c => DemoDataPlan.DemoId($"workspace:{c.Key}:general")).ToList();
        var generals = await db.Workspaces.Where(w => generalIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, cancellationToken).ConfigureAwait(false);
        foreach (var company in plan.Companies)
        {
            if (existing.TryGetValue(company.Id, out var organization))
            {
                organization.MarkAsDemo();
            }
            else
            {
                organization = Organization.Create(company.Id, company.Name, users[company.Owner], start);
                organization.MarkAsDemo();
                db.Organizations.Add(organization);
                created[1]++;
            }

            var isNew = !existing.ContainsKey(company.Id);
            var added = new Dictionary<string, OrganizationMember>(StringComparer.Ordinal);
            foreach (var (key, role) in company.Members)
            {
                // Someone who left (or was removed) keeps their record and is not put back.
                if (!users.TryGetValue(key, out var userId) || memberships.ContainsKey((company.Id, userId)))
                {
                    continue;
                }

                var member = OrganizationMember.Join(Guid.CreateVersion7(start), company.Id, userId, role switch
                {
                    DemoRole.Owner => OrganizationRole.Owner,
                    DemoRole.Administrator => OrganizationRole.Administrator,
                    _ => OrganizationRole.Member,
                }, start);
                db.OrganizationMembers.Add(member);
                memberships[(company.Id, userId)] = member;
                added[key] = member;
            }

            if (!isNew && added.Count > 0 && generals.TryGetValue(DemoDataPlan.DemoId($"workspace:{company.Key}:general"), out var existingGeneral))
            {
                // People added to an existing company (2026-11-24: everyone belongs to one) join its General workspace.
                foreach (var member in added.Values)
                {
                    db.WorkspaceMembers.Add(WorkspaceMember.Join(Guid.CreateVersion7(start), existingGeneral, member, WorkspaceRole.Member, start));
                }
            }

            if (isNew)
            {
                var general = Workspace.Create(DemoDataPlan.DemoId($"workspace:{company.Key}:general"), company.Id, "General", start);
                var team = Workspace.Create(DemoDataPlan.DemoId($"workspace:{company.Key}:team"), company.Id, company.TeamWorkspace, start);
                db.Workspaces.AddRange(general, team);
                foreach (var (key, member) in added)
                {
                    db.WorkspaceMembers.Add(WorkspaceMember.Join(Guid.CreateVersion7(start), general, member,
                        member.Role == OrganizationRole.Member ? WorkspaceRole.Member : WorkspaceRole.Administrator, start));
                    if (company.TeamMembers.Contains(key))
                    {
                        db.WorkspaceMembers.Add(WorkspaceMember.Join(Guid.CreateVersion7(start), team, member,
                            key == company.Owner ? WorkspaceRole.Administrator : WorkspaceRole.Member, start));
                    }
                }
            }
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    // ---- Contacts ----------------------------------------------------------------------------

    private async Task EnsureContactsAsync(DemoDataPlan plan, Dictionary<string, Guid> users, DateTimeOffset start, int[] created, CancellationToken cancellationToken)
    {
        var people = users.Values.ToList();
        var contacts = (await db.Contacts.Where(c => people.Contains(c.UserLowId) || people.Contains(c.UserHighId))
                .Select(c => new { c.UserLowId, c.UserHighId }).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(c => (c.UserLowId, c.UserHighId)).ToHashSet();
        var blocked = (await db.UserBlocks.Where(b => people.Contains(b.BlockerId) || people.Contains(b.BlockedId))
                .Select(b => new { b.BlockerId, b.BlockedId }).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(b => UserPair.Of(b.BlockerId, b.BlockedId)).Select(p => (p.Low, p.High)).ToHashSet();
        foreach (var (a, b) in plan.Contacts)
        {
            var pair = UserPair.Of(users[a], users[b]);
            if (contacts.Contains((pair.Low, pair.High)) || blocked.Contains((pair.Low, pair.High)))
            {
                continue;
            }

            db.Contacts.Add(Contact.Create(Guid.CreateVersion7(start), pair, start));
            contacts.Add((pair.Low, pair.High));
            created[2]++;
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    // ---- Groups and direct conversations ------------------------------------------------------

    private async Task<Dictionary<string, Conversation>> EnsureConversationsAsync(
        DemoDataPlan plan, Dictionary<string, Guid> users, DateTimeOffset start, int[] created, CancellationToken cancellationToken)
    {
        var groupIds = plan.Groups.Select(g => g.Id).ToList();
        var directKeys = plan.Directs.ToDictionary(d => Conversation.DirectKeyFor(ConversationScope.Personal, null, users[d.A], users[d.B]), d => d);
        var keyList = directKeys.Keys.ToList();
        var existing = await db.Conversations
            .Where(c => groupIds.Contains(c.Id) || (c.DirectKey != null && keyList.Contains(c.DirectKey)))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = existing.Select(c => c.Id).ToList();
        var participants = (await db.ConversationParticipants.Where(p => ids.Contains(p.ConversationId)).Select(p => new { p.ConversationId, p.UserId })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(p => (p.ConversationId, p.UserId)).ToHashSet();
        var companyIds = plan.Companies.Select(c => c.Id).ToList();
        var activeMembers = (await db.OrganizationMembers.Where(m => companyIds.Contains(m.OrganizationId) && m.Status == MembershipStatus.Active)
                .Select(m => new { m.OrganizationId, m.UserId }).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(m => (m.OrganizationId, m.UserId)).ToHashSet();

        var demoPeople = users.Where(u => u.Key != DemoDataPlan.Me).Select(u => u.Value).ToHashSet();
        var result = new Dictionary<string, Conversation>(StringComparer.Ordinal);
        foreach (var group in plan.Groups)
        {
            var organizationId = group.CompanyKey is null ? (Guid?)null : DemoDataPlan.DemoId("org:" + group.CompanyKey);
            var owner = users[group.Owner];
            var conversation = existing.FirstOrDefault(c => c.Id == group.Id);
            if (conversation is null)
            {
                if (organizationId is { } org && !activeMembers.Contains((org, owner)))
                {
                    continue; // the owner left the company: the group is not created
                }

                conversation = Conversation.CreateGroup(group.Id, organizationId is null ? ConversationScope.Personal : ConversationScope.Organization,
                    organizationId, group.Title, owner, start);
                conversation.MarkAsDemo();
                var first = ConversationEvent.Record(Guid.CreateVersion7(start), conversation, ConversationEventKind.Created, owner, null, group.Title, start);
                db.Conversations.Add(conversation);
                db.ConversationEvents.Add(first);
                db.ConversationParticipants.Add(ConversationParticipant.Join(Guid.CreateVersion7(start), conversation.Id, owner, ParticipantRole.Owner, first.Sequence, start));
                participants.Add((conversation.Id, owner));
                if (group.Description is not null)
                {
                    conversation.Describe(group.Description);
                    db.ConversationEvents.Add(ConversationEvent.Record(Guid.CreateVersion7(start), conversation, ConversationEventKind.DescriptionChanged, owner, null,
                        group.Description, start));
                }

                created[3]++;
            }
            else
            {
                conversation.MarkAsDemo();
            }

            foreach (var member in group.Members.Select(m => users[m]).Distinct())
            {
                // Only people who never took part; organization groups only take active members.
                if (participants.Contains((conversation.Id, member)) || (organizationId is { } org && !activeMembers.Contains((org, member))))
                {
                    continue;
                }

                var added = ConversationEvent.Record(Guid.CreateVersion7(start), conversation, ConversationEventKind.MemberAdded, conversation.CreatedByUserId, member, null, start);
                db.ConversationEvents.Add(added);
                db.ConversationParticipants.Add(ConversationParticipant.Join(Guid.CreateVersion7(start), conversation.Id, member, ParticipantRole.Member, added.Sequence, start));
                participants.Add((conversation.Id, member));
            }

            result[group.Key] = conversation;
        }

        foreach (var (directKey, direct) in directKeys)
        {
            var conversation = existing.FirstOrDefault(c => c.DirectKey == directKey);
            if (conversation is null)
            {
                var (a, b) = (users[direct.A], users[direct.B]);
                conversation = Conversation.CreateDirect(DemoDataPlan.DemoId("conversation:" + direct.Key), ConversationScope.Personal, null, a, b, start);
                conversation.MarkAsDemo();
                var first = ConversationEvent.Record(Guid.CreateVersion7(start), conversation, ConversationEventKind.Created, a, null, null, start);
                db.Conversations.Add(conversation);
                db.ConversationEvents.Add(first);
                db.ConversationParticipants.Add(ConversationParticipant.Join(Guid.CreateVersion7(start), conversation.Id, a, ParticipantRole.Member, first.Sequence, start));
                db.ConversationParticipants.Add(ConversationParticipant.Join(Guid.CreateVersion7(start), conversation.Id, b, ParticipantRole.Member, first.Sequence, start));
                created[3]++;
            }
            else if (!conversation.IsDemo && demoPeople.Contains(conversation.CreatedByUserId))
            {
                conversation.MarkAsDemo(); // started by a demo account in an earlier version
            }

            // A direct conversation a real person started stays theirs: no demo messages in it.
            if (conversation.IsDemo)
            {
                result[direct.Key] = conversation;
            }
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    // ---- Channels ----------------------------------------------------------------------------

    private async Task<Dictionary<string, Conversation>> EnsureChannelsAsync(
        DemoDataPlan plan, Dictionary<string, Guid> users, DateTimeOffset start, int[] created, CancellationToken cancellationToken)
    {
        static Guid WorkspaceOf(DemoChannel c) => DemoDataPlan.DemoId($"workspace:{c.CompanyKey}:{(c.TeamWorkspace ? "team" : "general")}");

        var workspaceIds = plan.Channels.Select(WorkspaceOf).Distinct().ToList();
        var workspaces = await db.Workspaces.Where(w => workspaceIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, cancellationToken).ConfigureAwait(false);
        var workspaceMembers = (await db.WorkspaceMembers.Where(m => workspaceIds.Contains(m.WorkspaceId) && m.Status == MembershipStatus.Active)
                .Select(m => new { m.WorkspaceId, m.UserId }).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(m => (m.WorkspaceId, m.UserId)).ToHashSet();
        var channelIds = plan.Channels.Select(c => c.Id).ToList();
        var existing = await db.Conversations.Where(c => channelIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken).ConfigureAwait(false);
        var takenNames = (await db.Conversations.Where(c => c.WorkspaceId != null && workspaceIds.Contains(c.WorkspaceId.Value) && c.ChannelKey != null)
                .Select(c => c.ChannelKey!).ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToHashSet(StringComparer.Ordinal);
        var ids = existing.Keys.ToList();
        var participants = (await db.ConversationParticipants.Where(p => ids.Contains(p.ConversationId)).Select(p => new { p.ConversationId, p.UserId })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(p => (p.ConversationId, p.UserId)).ToHashSet();

        var result = new Dictionary<string, Conversation>(StringComparer.Ordinal);
        foreach (var channel in plan.Channels)
        {
            var workspaceId = WorkspaceOf(channel);
            if (!workspaces.TryGetValue(workspaceId, out var workspace))
            {
                continue; // the company's workspace no longer exists
            }

            var manager = users[channel.Manager];
            if (!existing.TryGetValue(channel.Id, out var conversation))
            {
                // Only workspace members create channels, and names stay unique (a real person may have taken one).
                if (!workspaceMembers.Contains((workspaceId, manager)) || takenNames.Contains(Conversation.ChannelKeyFor(workspaceId, channel.Name)))
                {
                    continue;
                }

                conversation = Conversation.CreateChannel(channel.Id, workspace.OrganizationId, workspaceId, channel.Name,
                    channel.Private ? ChannelVisibility.Private : ChannelVisibility.Public, manager, start);
                conversation.MarkAsDemo();
                takenNames.Add(conversation.ChannelKey!);
                var first = ConversationEvent.Record(Guid.CreateVersion7(start), conversation, ConversationEventKind.Created, manager, null, conversation.Title, start);
                db.Conversations.Add(conversation);
                db.ConversationEvents.Add(first);
                db.ConversationParticipants.Add(ConversationParticipant.Join(Guid.CreateVersion7(start), conversation.Id, manager, ParticipantRole.Administrator, first.Sequence, start));
                participants.Add((conversation.Id, manager));
                created[3]++;
            }

            foreach (var member in channel.Members.Select(m => users[m]).Distinct())
            {
                // Only people who never took part, and only workspace members.
                if (participants.Contains((conversation.Id, member)) || !workspaceMembers.Contains((workspaceId, member)))
                {
                    continue;
                }

                var added = ConversationEvent.Record(Guid.CreateVersion7(start), conversation, ConversationEventKind.MemberAdded, conversation.CreatedByUserId, member, null, start);
                db.ConversationEvents.Add(added);
                db.ConversationParticipants.Add(ConversationParticipant.Join(Guid.CreateVersion7(start), conversation.Id, member, ParticipantRole.Member, added.Sequence, start));
                participants.Add((conversation.Id, member));
            }

            result[channel.Key] = conversation;
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    // ---- Messages ----------------------------------------------------------------------------

    private async Task EnsureMessagesAsync(
        DemoDataPlan plan, Dictionary<string, Guid> users, Dictionary<string, Conversation> conversations, DateTimeOffset start, int[] created,
        CancellationToken cancellationToken)
    {
        var ids = conversations.Values.Select(c => c.Id).ToList();
        var sent = (await db.ConversationEvents.Where(e => ids.Contains(e.ConversationId) && e.ClientMessageId != null)
                .Select(e => new { e.ConversationId, e.ActorUserId, e.ClientMessageId }).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(e => (e.ConversationId, e.ActorUserId, e.ClientMessageId!)).ToHashSet();
        var participants = await db.ConversationParticipants.Where(p => ids.Contains(p.ConversationId)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var active = participants.Where(p => p.IsActive).Select(p => (p.ConversationId, p.UserId)).ToHashSet();
        var caller = users[DemoDataPlan.Me];
        var callerMention = "@" + (await db.UserProfiles.Where(p => p.UserId == caller).Select(p => p.DisplayName).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? "me");

        var pending = 0;
        var touched = new HashSet<Guid>();
        var unreadFrom = new Dictionary<(Guid ConversationId, Guid UserId), long>();
        var unreadAfter = DemoDataPlan.PeriodDays * 24 * 60 - DemoDataPlan.UnreadMinutes;
        foreach (var message in plan.Messages.OrderBy(m => m.At))
        {
            if (!conversations.TryGetValue(message.ConversationKey, out var conversation))
            {
                continue;
            }

            var author = users[message.Author];
            if (!active.Contains((conversation.Id, author)) || sent.Contains((conversation.Id, author, message.ClientId)))
            {
                continue;
            }

            var at = start.AddDays(1).AddMinutes(message.At);
            var entry = ConversationEvent.Post(Guid.CreateVersion7(at), conversation, author, message.ClientId,
                message.Body.Replace(DemoDataPlan.MeToken, callerMention, StringComparison.Ordinal), null, 20000, at);
            db.ConversationEvents.Add(entry);
            foreach (var mentioned in (message.Mentions ?? []).Select(k => users[k]).Distinct())
            {
                // Only people who take part (someone may have left); recent mentions stay unread.
                if (mentioned == author || !active.Contains((conversation.Id, mentioned)))
                {
                    continue;
                }

                db.MessageMentions.Add(MessageMention.Create(Guid.CreateVersion7(at), entry, mentioned, MentionKind.User));
                created[5]++;
                if (message.At >= unreadAfter && !unreadFrom.ContainsKey((conversation.Id, mentioned)))
                {
                    unreadFrom[(conversation.Id, mentioned)] = entry.Sequence;
                }
            }

            sent.Add((conversation.Id, author, message.ClientId));
            touched.Add(conversation.Id);
            created[4]++;
            if (++pending >= Batch)
            {
                await SaveAsync(cancellationToken).ConfigureAwait(false);
                pending = 0;
            }
        }

        // Demo people have read everything but their mentions of the last two days; the person who
        // loads it also has the last few messages of each conversation unread.
        var byId = conversations.Values.DistinctBy(c => c.Id).ToDictionary(c => c.Id);
        foreach (var participant in participants.Where(p => p.IsActive && touched.Contains(p.ConversationId)))
        {
            var conversation = byId[participant.ConversationId];
            var target = participant.UserId == caller ? conversation.LastSequence - (int)(conversation.Id.GetHashCode() & 3) : conversation.LastSequence;
            if (unreadFrom.TryGetValue((conversation.Id, participant.UserId), out var firstUnread))
            {
                target = Math.Min(target, firstUnread - 1);
            }

            participant.MarkDelivered(conversation.LastSequence, conversation.LastSequence);
            participant.MarkRead(Math.Max(target, participant.LastReadSequence), conversation.LastSequence);
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    // ---- Saved messages, reminders and scheduled messages (2026-11-24) ---------------------------

    private async Task EnsureActivityAsync(
        DemoDataPlan plan, Dictionary<string, Guid> users, Dictionary<string, Conversation> conversations, DateTimeOffset nowUtc, int[] created,
        CancellationToken cancellationToken)
    {
        var ids = conversations.Values.Select(c => c.Id).Distinct().ToList();
        var wanted = plan.Saved.Select(s => s.ClientId).Concat(plan.Reminders.Select(r => r.ClientId)).ToHashSet(StringComparer.Ordinal);
        var messages = (await db.ConversationEvents.AsNoTracking()
                .Where(e => ids.Contains(e.ConversationId) && e.ClientMessageId != null && e.Kind == ConversationEventKind.Message && e.DeletedAtUtc == null)
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Where(e => wanted.Contains(e.ClientMessageId!))
            .ToDictionary(e => (e.ConversationId, e.ClientMessageId!));
        var active = (await db.ConversationParticipants.Where(p => ids.Contains(p.ConversationId) && p.Status == ParticipantStatus.Active)
                .Select(p => new { p.ConversationId, p.UserId }).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(p => (p.ConversationId, p.UserId)).ToHashSet();
        var people = users.Values.ToList();
        var saved = (await db.SavedMessages.Where(s => people.Contains(s.UserId)).Select(s => new { s.UserId, s.EntryId })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(s => (s.UserId, s.EntryId)).ToHashSet();
        var reminderIds = plan.Reminders.Select(r => DemoDataPlan.DemoId("reminder:" + r.Key)).ToList();
        var reminders = (await db.Reminders.Where(r => reminderIds.Contains(r.Id)).Select(r => r.Id).ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();
        var scheduledIds = plan.Scheduled.Select(s => DemoDataPlan.DemoId("scheduled:" + s.Key)).ToList();
        var scheduled = (await db.ScheduledMessages.Where(s => scheduledIds.Contains(s.Id)).Select(s => s.Id).ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();

        ConversationEvent? Find(string conversationKey, string clientId, Guid userId) =>
            conversations.TryGetValue(conversationKey, out var conversation) && active.Contains((conversation.Id, userId))
                && messages.TryGetValue((conversation.Id, clientId), out var message) ? message : null;

        foreach (var item in plan.Saved)
        {
            var userId = users[item.Person];
            if (Find(item.ConversationKey, item.ClientId, userId) is { } message && saved.Add((userId, message.Id)))
            {
                db.SavedMessages.Add(SavedMessage.Create(DemoDataPlan.DemoId($"saved:{item.Person}:{item.ConversationKey}:{item.ClientId}"), message, userId,
                    message.CreatedAtUtc.AddMinutes(30) < nowUtc ? message.CreatedAtUtc.AddMinutes(30) : nowUtc));
                created[5]++;
            }
        }

        foreach (var item in plan.Reminders)
        {
            var id = DemoDataPlan.DemoId("reminder:" + item.Key);
            var userId = users[item.Person];
            if (!reminders.Contains(id) && Find(item.ConversationKey, item.ClientId, userId) is { } message)
            {
                db.Reminders.Add(Reminder.Create(id, message, userId, nowUtc.AddHours(item.HoursAhead), nowUtc));
                created[5]++;
            }
        }

        foreach (var item in plan.Scheduled)
        {
            var id = DemoDataPlan.DemoId("scheduled:" + item.Key);
            var userId = users[item.Person];
            if (!scheduled.Contains(id) && conversations.TryGetValue(item.ConversationKey, out var conversation) && active.Contains((conversation.Id, userId)))
            {
                db.ScheduledMessages.Add(ScheduledMessage.Create(id, userId, conversation.Id, item.Body, null, null, false, [], nowUtc.AddHours(item.HoursAhead), nowUtc));
                created[5]++;
            }
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // ---- Counting and clearing ---------------------------------------------------------------

    public async Task<DemoDataCounts> CountAsync(CancellationToken cancellationToken)
    {
        var demoUsers = db.Users.Where(u => u.IsDemo).Select(u => u.Id);
        var demoConversations = db.Conversations.Where(c => c.IsDemo).Select(c => c.Id);
        return new DemoDataCounts(
            await db.Users.CountAsync(u => u.IsDemo && !u.IsVisitor, cancellationToken).ConfigureAwait(false),
            await db.Organizations.CountAsync(o => o.IsDemo, cancellationToken).ConfigureAwait(false),
            await db.Contacts.CountAsync(c => demoUsers.Contains(c.UserLowId) || demoUsers.Contains(c.UserHighId), cancellationToken).ConfigureAwait(false),
            await db.Conversations.CountAsync(c => c.IsDemo, cancellationToken).ConfigureAwait(false),
            await db.ConversationEvents.CountAsync(e => e.Kind == ConversationEventKind.Message && demoConversations.Contains(e.ConversationId), cancellationToken)
                .ConfigureAwait(false),
            await db.MessageMentions.CountAsync(m => demoConversations.Contains(m.ConversationId), cancellationToken).ConfigureAwait(false)
            + await db.SavedMessages.CountAsync(s => demoConversations.Contains(s.ConversationId), cancellationToken).ConfigureAwait(false)
            + await db.Reminders.CountAsync(r => demoConversations.Contains(r.ConversationId), cancellationToken).ConfigureAwait(false)
            + await db.ScheduledMessages.CountAsync(s => demoConversations.Contains(s.ConversationId), cancellationToken).ConfigureAwait(false),
            await db.Conversations.CountAsync(c => c.IsDemo && c.Type == ConversationType.Website, cancellationToken).ConfigureAwait(false),
            await db.UserProfiles.CountAsync(p => p.PhotoKey != null && demoUsers.Contains(p.UserId), cancellationToken).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<string>> ListAttachmentKeysAsync(CancellationToken cancellationToken)
    {
        var (users, _, conversations) = Scope();
        var files = await db.Attachments
            .Where(a => !a.ContentRemoved && (conversations.Contains(a.ConversationId) || users.Contains(a.OwnerUserId)))
            .Select(a => a.StorageKey).ToListAsync(cancellationToken).ConfigureAwait(false);
        // Demo people's profile photos go with them, and so do the pictures of removed organizations.
        var photos = await db.UserProfiles.Where(p => p.PhotoKey != null && users.Contains(p.UserId))
            .Select(p => p.PhotoKey!).ToListAsync(cancellationToken).ConfigureAwait(false);
        var (_, organizations, _) = Scope();
        var organizationImages = await db.Organizations.Where(o => organizations.Contains(o.Id) && (o.PhotoKey != null || o.BannerKey != null))
            .Select(o => new { o.PhotoKey, o.BannerKey }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. files, .. photos, .. organizationImages.SelectMany(o => new[] { o.PhotoKey, o.BannerKey }).OfType<string>()];
    }

    /// <summary>
    /// What clearing removes: demo accounts; demo organizations and organizations a demo account
    /// created; demo conversations, conversations a demo account created and every conversation of
    /// those organizations.
    /// </summary>
    private (IQueryable<Guid> Users, IQueryable<Guid> Organizations, IQueryable<Guid> Conversations) Scope()
    {
        var users = db.Users.Where(u => u.IsDemo).Select(u => u.Id);
        var organizations = db.Organizations.Where(o => o.IsDemo || users.Contains(o.CreatedByUserId)).Select(o => o.Id);
        var conversations = db.Conversations
            .Where(c => c.IsDemo || users.Contains(c.CreatedByUserId) || (c.OrganizationId != null && organizations.Contains(c.OrganizationId.Value)))
            .Select(c => c.Id);
        return (users, organizations, conversations);
    }

    public async Task<DemoDataCounts> ClearAsync(CancellationToken cancellationToken)
    {
        var before = await CountAsync(cancellationToken).ConfigureAwait(false);
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            // Materialize the scope first: the deletions below change what the queries would return.
            var (userQuery, organizationQuery, conversationQuery) = Scope();
            var users = await userQuery.ToListAsync(cancellationToken).ConfigureAwait(false);
            var organizations = await organizationQuery.ToListAsync(cancellationToken).ConfigureAwait(false);
            var conversations = await conversationQuery.ToListAsync(cancellationToken).ConfigureAwait(false);

            // History: everything in the removed conversations, and whatever demo people wrote or were
            // the subject of elsewhere. A real person's message a demo moderator deleted stays a tombstone.
            var events = db.ConversationEvents.Where(e => conversations.Contains(e.ConversationId)
                || users.Contains(e.ActorUserId) || (e.SubjectUserId != null && users.Contains(e.SubjectUserId.Value)));
            await EfMessageRetentionStore.QueueSearchRemovalsAsync(db, events, cancellationToken).ConfigureAwait(false);
            await db.ConversationEvents.Where(e => e.DeletedByUserId != null && users.Contains(e.DeletedByUserId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.DeletedByUserId, e => (Guid?)e.ActorUserId), cancellationToken).ConfigureAwait(false);
            var eventIds = events.Select(e => e.Id);
            await db.ConversationEvents.Where(e => e.ReplyToEntryId != null && eventIds.Contains(e.ReplyToEntryId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ReplyToEntryId, (Guid?)null), cancellationToken).ConfigureAwait(false);
            await db.ConversationEvents.Where(e => e.ThreadRootId != null && eventIds.Contains(e.ThreadRootId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ThreadRootId, (Guid?)null).SetProperty(e => e.ShownInConversation, false), cancellationToken)
                .ConfigureAwait(false);
            await db.Attachments.Where(a => conversations.Contains(a.ConversationId) || users.Contains(a.OwnerUserId)
                    || (a.MessageEntryId != null && eventIds.Contains(a.MessageEntryId.Value)))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.OutboxMessages.Where(o => conversations.Contains(o.ConversationId) || eventIds.Contains(o.EntryId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await events.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

            await db.ChannelTeams.Where(l => conversations.Contains(l.ConversationId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.GuestInvitations.Where(i => conversations.Contains(i.ConversationId) || organizations.Contains(i.OrganizationId) || users.Contains(i.InvitedByUserId))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.OrganizationGuests.Where(g => organizations.Contains(g.OrganizationId) || users.Contains(g.InvitedByUserId))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.OrganizationInvitations.Where(i => organizations.Contains(i.OrganizationId) || users.Contains(i.InvitedByUserId))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.ConversationParticipants.Where(p => conversations.Contains(p.ConversationId) || users.Contains(p.UserId))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.Conversations.Where(c => conversations.Contains(c.Id)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.Widgets.Where(w => organizations.Contains(w.OrganizationId) || users.Contains(w.CreatedByUserId)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

            // Members, workspaces and teams follow the organizations by cascade; profiles, sessions,
            // contacts, requests, blocks and memberships follow the accounts.
            await db.Organizations.Where(o => organizations.Contains(o.Id)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.Users.Where(u => users.Contains(u.Id)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        db.ChangeTracker.Clear();
        return before;
    }
}
