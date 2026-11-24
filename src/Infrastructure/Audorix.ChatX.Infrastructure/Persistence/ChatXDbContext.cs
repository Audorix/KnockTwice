using System.Text;
using Audorix.ChatX.Domain.Installations;
using Audorix.ChatX.Domain.Sessions;
using Audorix.ChatX.Domain.Users;
using Audorix.ChatX.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Audorix.ChatX.Infrastructure.Persistence;

/// <summary>
/// The shared logical model, including ASP.NET Core Identity tables and Data Protection keys.
/// Provider-specific concerns (migrations, column types) live in
/// <c>Audorix.ChatX.Persistence.Sqlite</c> and <c>Audorix.ChatX.Persistence.PostgreSql</c>.
/// All tables, columns, keys and indexes use lower-case snake_case names on both providers.
/// </summary>
public sealed class ChatXDbContext(DbContextOptions<ChatXDbContext> options)
    : IdentityDbContext<ChatXUser, ChatXRole, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<Installation> Installations => Set<Installation>();

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    public DbSet<Domain.Users.ProfileLink> ProfileLinks => Set<Domain.Users.ProfileLink>();

    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<Domain.Organizations.Organization> Organizations => Set<Domain.Organizations.Organization>();

    public DbSet<Domain.Organizations.OrganizationMember> OrganizationMembers => Set<Domain.Organizations.OrganizationMember>();

    public DbSet<Domain.Organizations.OrganizationInvitation> OrganizationInvitations => Set<Domain.Organizations.OrganizationInvitation>();

    public DbSet<Domain.Organizations.Workspace> Workspaces => Set<Domain.Organizations.Workspace>();

    public DbSet<Domain.Organizations.WorkspaceMember> WorkspaceMembers => Set<Domain.Organizations.WorkspaceMember>();

    public DbSet<Domain.Organizations.Team> Teams => Set<Domain.Organizations.Team>();

    public DbSet<Domain.Organizations.TeamMember> TeamMembers => Set<Domain.Organizations.TeamMember>();

    public DbSet<Domain.Organizations.ChannelTeam> ChannelTeams => Set<Domain.Organizations.ChannelTeam>();

    public DbSet<Domain.Organizations.OrganizationGuest> OrganizationGuests => Set<Domain.Organizations.OrganizationGuest>();

    public DbSet<Domain.Organizations.GuestInvitation> GuestInvitations => Set<Domain.Organizations.GuestInvitation>();

    public DbSet<Domain.Contacts.Contact> Contacts => Set<Domain.Contacts.Contact>();

    public DbSet<Domain.Contacts.ContactRequest> ContactRequests => Set<Domain.Contacts.ContactRequest>();

    public DbSet<Domain.Contacts.UserBlock> UserBlocks => Set<Domain.Contacts.UserBlock>();

    public DbSet<Domain.Conversations.Conversation> Conversations => Set<Domain.Conversations.Conversation>();

    public DbSet<Domain.Conversations.ConversationParticipant> ConversationParticipants => Set<Domain.Conversations.ConversationParticipant>();

    public DbSet<Domain.Conversations.ConversationEvent> ConversationEvents => Set<Domain.Conversations.ConversationEvent>();

    public DbSet<Domain.Conversations.SupportNote> SupportNotes => Set<Domain.Conversations.SupportNote>();

    public DbSet<Application.Messaging.OutboxMessage> OutboxMessages => Set<Application.Messaging.OutboxMessage>();

    public DbSet<Domain.Attachments.Attachment> Attachments => Set<Domain.Attachments.Attachment>();

    public DbSet<Domain.Administration.AuditEntry> AuditEntries => Set<Domain.Administration.AuditEntry>();

    public DbSet<Domain.Administration.InstallationSetting> InstallationSettings => Set<Domain.Administration.InstallationSetting>();

    public DbSet<Domain.Widgets.Widget> Widgets => Set<Domain.Widgets.Widget>();

    public DbSet<Domain.Widgets.WidgetResponder> WidgetResponders => Set<Domain.Widgets.WidgetResponder>();

    public DbSet<Domain.Widgets.Visitor> Visitors => Set<Domain.Widgets.Visitor>();

    public DbSet<Domain.Widgets.VisitorSession> VisitorSessions => Set<Domain.Widgets.VisitorSession>();

    /// <summary>Data Protection key ring, shared by all API instances using this database.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>
    /// Shadow property of history entries: the creation time as UTC ticks (Phase 13), so message
    /// retention can compare ages in the database on SQLite as well. Set on insert.
    /// </summary>
    public const string CreatedAtTicks = "CreatedAtTicks";

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampCreationTicks();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampCreationTicks();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public DbSet<Search.SearchQueueItem> SearchQueue => Set<Search.SearchQueueItem>();

    public DbSet<Search.MessageSearchRow> MessageSearch => Set<Search.MessageSearchRow>();

    public DbSet<Domain.Conversations.MessageReaction> MessageReactions => Set<Domain.Conversations.MessageReaction>();

    public DbSet<Domain.Conversations.ThreadFollow> ThreadFollows => Set<Domain.Conversations.ThreadFollow>();

    public DbSet<Domain.Conversations.MessageMention> MessageMentions => Set<Domain.Conversations.MessageMention>();

    public DbSet<Domain.Conversations.MessagePin> MessagePins => Set<Domain.Conversations.MessagePin>();

    public DbSet<Domain.Conversations.SavedMessage> SavedMessages => Set<Domain.Conversations.SavedMessage>();

    public DbSet<Domain.Conversations.MessageDraft> MessageDrafts => Set<Domain.Conversations.MessageDraft>();

    public DbSet<Domain.Conversations.ScheduledMessage> ScheduledMessages => Set<Domain.Conversations.ScheduledMessage>();

    public DbSet<Domain.Conversations.ScheduledMessageFile> ScheduledMessageFiles => Set<Domain.Conversations.ScheduledMessageFile>();

    public DbSet<Domain.Conversations.Reminder> Reminders => Set<Domain.Conversations.Reminder>();

    public DbSet<Notifications.PushQueueItem> PushQueue => Set<Notifications.PushQueueItem>();

    public DbSet<ChangeLogItem> ChangeLog => Set<ChangeLogItem>();

    public DbSet<Notifications.WebPushKeys> WebPushKeys => Set<Notifications.WebPushKeys>();

    public DbSet<Domain.Notifications.PushSubscription> PushSubscriptions => Set<Domain.Notifications.PushSubscription>();

    /// <summary>
    /// Creation ticks for new history entries, and a search queue row (Phase 14) for every message
    /// that is created, edited or deleted, in the same transaction as the change.
    /// </summary>
    private void StampCreationTicks()
    {
        var queued = new List<Search.SearchQueueItem>();
        var pushes = new List<Notifications.PushQueueItem>();
        var changes = new Dictionary<Guid, ChangeLogItem>();
        var now = DateTimeOffset.UtcNow;

        // Change log (Phase 22): every new or changed history entry, and entries whose reactions
        // changed, so reconnecting clients can catch up. Old history (demo data) is not logged.
        void Changed(Guid conversationId, Guid entryId) =>
            changes.TryAdd(entryId, new ChangeLogItem { ConversationId = conversationId, EntryId = entryId, RecordedTicks = now.UtcTicks });

        foreach (var entry in ChangeTracker.Entries<Domain.Conversations.ConversationEvent>())
        {
            if (entry.State == EntityState.Modified || (entry.State == EntityState.Added && now - entry.Entity.CreatedAtUtc < TimeSpan.FromMinutes(5)))
            {
                Changed(entry.Entity.ConversationId, entry.Entity.Id);
            }

            if (entry.State == EntityState.Added)
            {
                entry.Property(CreatedAtTicks).CurrentValue = entry.Entity.CreatedAtUtc.UtcTicks;

                // New messages are pushed (Phase 15); demo data loads old history and is not pushed.
                if (entry.Entity.IsMessage && DateTimeOffset.UtcNow - entry.Entity.CreatedAtUtc < TimeSpan.FromMinutes(5))
                {
                    pushes.Add(new Notifications.PushQueueItem { EntryId = entry.Entity.Id, EnqueuedAtTicks = DateTimeOffset.UtcNow.UtcTicks });
                }
            }

            if (entry.Entity.IsMessage && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                queued.Add(new Search.SearchQueueItem
                {
                    EntryId = entry.Entity.Id,
                    ConversationId = entry.Entity.ConversationId,
                    EnqueuedAtTicks = DateTimeOffset.UtcNow.UtcTicks,
                });
            }
        }

        // Linking attachments changes the indexed text (file names) of a message added in the same save.
        foreach (var entry in ChangeTracker.Entries<Domain.Attachments.Attachment>())
        {
            if (entry.State == EntityState.Modified && entry.Entity.MessageEntryId is { } messageId && queued.All(q => q.EntryId != messageId))
            {
                queued.Add(new Search.SearchQueueItem
                {
                    EntryId = messageId,
                    ConversationId = entry.Entity.ConversationId,
                    EnqueuedAtTicks = DateTimeOffset.UtcNow.UtcTicks,
                });
            }
        }

        foreach (var entry in ChangeTracker.Entries<Domain.Conversations.MessageReaction>())
        {
            if (entry.State is EntityState.Added or EntityState.Deleted)
            {
                Changed(entry.Entity.ConversationId, entry.Entity.EntryId);
            }
        }

        SearchQueue.AddRange(queued);
        PushQueue.AddRange(pushes);
        ChangeLog.AddRange(changes.Values);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);

        builder.Entity<ChatXUser>().ToTable("users");
        builder.Entity<ChatXRole>().ToTable("roles");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<Guid>>().ToTable("role_claims");
        builder.Entity<DataProtectionKey>().ToTable("data_protection_keys");

        builder.ApplyConfigurationsFromAssembly(typeof(ChatXDbContext).Assembly);
        ApplySnakeCaseNames(builder);
    }

    private static void ApplySnakeCaseNames(ModelBuilder builder)
    {
        foreach (var entity in builder.Model.GetEntityTypes())
        {
            if (entity.GetTableName() is { } table)
            {
                entity.SetTableName(ToSnakeCase(table));
            }

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.GetColumnName()));
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName()!));
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                foreignKey.SetConstraintName(ToSnakeCase(foreignKey.GetConstraintName()!));
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
            }
        }
    }

    internal static string ToSnakeCase(string name)
    {
        var result = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                var previous = i > 0 ? name[i - 1] : '\0';
                var next = i + 1 < name.Length ? name[i + 1] : '\0';
                var boundary = i > 0 && previous != '_' &&
                    (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && char.IsLower(next)));
                if (boundary)
                {
                    result.Append('_');
                }

                result.Append(char.ToLowerInvariant(c));
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }
}
