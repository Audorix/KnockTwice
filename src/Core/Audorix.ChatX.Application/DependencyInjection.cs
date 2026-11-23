using Audorix.ChatX.Application.Email;
using Audorix.ChatX.Application.Identity;
using Audorix.ChatX.Application.Installations;
using Audorix.ChatX.Application.Sessions;
using Audorix.ChatX.Application.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Audorix.ChatX.Application;

public static class DependencyInjection
{
    /// <summary>Registers application use cases. Ports are implemented and registered by Infrastructure.</summary>
    public static IServiceCollection AddChatXApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<InstallationService>();
        services.AddScoped<AccountService>();
        services.AddScoped<SessionService>();
        services.AddScoped<UserProfileService>();
        services.AddSingleton<AccountEmailComposer>();
        services.AddScoped<Organizations.OrganizationService>();
        services.AddScoped<Organizations.WorkspaceService>();
        services.AddScoped<Organizations.TeamService>();
        services.AddScoped<Organizations.GuestService>();
        services.AddScoped<Contacts.ContactService>();
        services.AddScoped<UserDirectoryService>();
        services.AddSingleton<Realtime.RealtimeState>();
        // Phase 21: single-server defaults; the API swaps in the Redis implementations when scale-out is configured.
        services.TryAddSingleton<Realtime.IClusterPresence, Realtime.LocalClusterPresence>();
        services.AddSingleton<Realtime.PresenceDirectory>();
        services.TryAddSingleton<Common.IRateCounter, Common.LocalRateCounter>();
        services.AddScoped<Realtime.RealtimeService>();
        services.AddScoped<Conversations.ConversationService>();
        services.AddScoped<Conversations.MentionService>();
        services.AddScoped<Planning.PlanningService>();
        services.AddScoped<Realtime.ChangeFeedService>();
        services.AddScoped<Demo.DemoDataService>();
        services.AddScoped<Messaging.MessagingService>();
        services.AddScoped<Attachments.AttachmentService>();
        services.AddSingleton<PrivateChats.PrivateChatRegistry>();
        services.AddScoped<PrivateChats.PrivateChatService>();
        services.AddScoped<Administration.AuditService>();
        services.AddScoped<Administration.UserAdministrationService>();
        services.AddScoped<Administration.OrganizationAdministrationService>();
        services.AddScoped<AccountDeletionService>();
        services.AddScoped<Organizations.OrganizationDeletionService>();
        services.AddScoped<Search.SearchService>();
        services.AddScoped<Search.SearchIndexer>();
        services.AddSingleton<Notifications.NotificationEmailComposer>();
        services.AddScoped<Notifications.NotificationService>();
        services.AddScoped<Administration.DataRetentionService>();
        services.AddScoped<Widgets.WidgetService>();
        services.AddScoped<Widgets.VisitorService>();
        services.AddSingleton<Widgets.VisitorRateLimiter>();
        return services;
    }
}
