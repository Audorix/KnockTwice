using Audorix.ChatX.Application.Abstractions;
using Audorix.ChatX.Application.Identity;
using Audorix.ChatX.Application.Installations;
using Audorix.ChatX.Application.Sessions;
using Audorix.ChatX.Application.Users;
using Audorix.ChatX.Infrastructure.Captcha;
using Audorix.ChatX.Infrastructure.Email;
using Audorix.ChatX.Infrastructure.Identity;
using Audorix.ChatX.Infrastructure.Persistence;
using Audorix.ChatX.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Infrastructure;

public static class DependencyInjection
{
    public const string ReadinessTag = "ready";
    public const string DataProtectionApplicationName = "Audorix.ChatX";

    /// <summary>
    /// Registers persistence and adapters. At least one <see cref="IDatabaseProvider"/> must be
    /// registered by the host; the configured <c>Database:Provider</c> selects among them.
    /// </summary>
    public static IServiceCollection AddChatXInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();

        services.AddDbContext<ChatXDbContext>((sp, builder) =>
        {
            var options = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value; // validated on first access
            var provider = sp.GetServices<IDatabaseProvider>().Single(p => p.Kind == options.ProviderKind);
            provider.Configure(builder, options.ConnectionString!);
        });

        services.AddScoped<IInstallationStore, EfInstallationStore>();
        services.AddScoped<IUserProfileStore, EfUserProfileStore>();
        services.AddScoped<ISessionStore, EfSessionStore>();
        services.AddScoped<IUserAccountManager, IdentityUserAccountManager>();
        services.AddScoped<Application.Organizations.IOrganizationStore, EfOrganizationStore>();
        services.AddScoped<Application.Contacts.IContactStore, EfContactStore>();
        services.AddScoped<IUserDirectory, EfUserDirectory>();
        services.AddScoped<Application.Realtime.IPresenceAudience, EfPresenceAudience>();
        services.AddScoped<Application.Conversations.IConversationStore, EfConversationStore>();
        services.AddScoped<Application.Planning.IPlanningStore, EfPlanningStore>();
        services.AddScoped<Application.Realtime.IChangeFeedStore, EfChangeFeedStore>();
        services.AddScoped<Application.Realtime.IConversationAccess, EfConversationAccess>();
        services.AddScoped<DatabaseMigrator>();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<Application.Attachments.IAttachmentStore, EfAttachmentStore>();
        services.AddScoped<Application.Administration.IAuditStore, EfAuditStore>();
        services.AddScoped<Application.Administration.IInstallationSettingsStore, EfInstallationSettingsStore>();
        services.AddScoped<Application.Administration.IAdminDirectory, EfAdminDirectory>();
        services.AddScoped<Application.Identity.IRegistrationInvitations, EfRegistrationInvitations>();
        services.AddScoped<Application.Widgets.IWidgetStore, EfWidgetStore>();
        services.AddScoped<IAccountErasure, EfAccountErasure>();
        services.AddScoped<Application.Demo.IDemoDataStore, EfDemoDataStore>();
        services.AddScoped<Application.Notifications.INotificationStore, Notifications.EfNotificationStore>();
        services.AddScoped<Application.Notifications.IWebPushSender, Notifications.WebPushSender>();
        services.AddHttpClient(Notifications.WebPushSender.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddScoped<Application.Organizations.IOrganizationRemoval, EfOrganizationRemoval>();
        services.AddScoped<Application.Administration.IMessageRetentionStore, EfMessageRetentionStore>();
        services.AddScoped<Application.Widgets.IVisitorAccounts, IdentityVisitorAccounts>();
        services.AddSingleton<Application.Widgets.IWidgetSecretProtector, DataProtectionWidgetSecretProtector>();

        services.AddChatXEmail();
        services.AddChatXCaptcha();
        services.AddChatXStorage();
        services.AddChatXSearch();

        // Keys are stored in the application database so cookies and tokens survive restarts
        // and stay valid across instances that share the database (Architecture §17.7).
        services.AddDataProtection()
            .SetApplicationName(DataProtectionApplicationName)
            .PersistKeysToDbContext<ChatXDbContext>();

        services.AddHealthChecks()
            .AddCheck<DatabaseReadinessHealthCheck>(
                DatabaseReadinessHealthCheck.Name,
                failureStatus: HealthStatus.Unhealthy,
                tags: [ReadinessTag],
                timeout: TimeSpan.FromSeconds(5));

        return services;
    }

    /// <summary>Uses the ChatX database for ASP.NET Core Identity users, roles, logins and tokens.</summary>
    public static IdentityBuilder AddChatXIdentityStores(this IdentityBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddEntityFrameworkStores<ChatXDbContext>();
    }

    private static void AddChatXEmail(this IServiceCollection services)
    {
        services.AddOptions<EmailOptions>().BindConfiguration(EmailOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<EmailOptions>, EmailOptionsValidator>();
        services.AddHttpClient<PostmarkEmailSender>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddTransient<SmtpEmailSender>();
        services.AddScoped<IEmailSender>(sp =>
            EmailProviders.Normalize(sp.GetRequiredService<IOptions<EmailOptions>>().Value.Provider) == EmailProviders.Postmark
                ? sp.GetRequiredService<PostmarkEmailSender>()
                : sp.GetRequiredService<SmtpEmailSender>());
    }

    /// <summary>
    /// Attachment content storage (Local or S3, ADR-007) and the optional malware scanner. Relative
    /// local paths are resolved by the host against its content root (see the API composition).
    /// </summary>
    private static void AddChatXStorage(this IServiceCollection services)
    {
        services.AddOptions<StorageOptions>().BindConfiguration(StorageOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>();
        services.AddHttpClient(S3AttachmentStorage.HttpClientName)
            .ConfigureHttpClient((sp, client) => client.Timeout = sp.GetRequiredService<IOptions<StorageOptions>>().Value.S3.Timeout);
        services.AddSingleton<Application.Attachments.IAttachmentStorage>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<StorageOptions>>();
            return StorageOptions.Normalize(options.Value.Provider) == StorageOptions.S3Provider
                ? new S3AttachmentStorage(sp.GetRequiredService<IHttpClientFactory>().CreateClient(S3AttachmentStorage.HttpClientName), options,
                    sp.GetRequiredService<TimeProvider>())
                : new LocalAttachmentStorage(Path.GetFullPath(options.Value.Local.Path));
        });

        services.AddOptions<ScanningOptions>().BindConfiguration(ScanningOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<ScanningOptions>, ScanningOptionsValidator>();
        services.TryAddSingleton<Application.Attachments.IMalwareScanner>(sp =>
            string.Equals(sp.GetRequiredService<IOptions<ScanningOptions>>().Value.Provider?.Trim(), ScanningOptions.ClamAvProvider, StringComparison.OrdinalIgnoreCase)
                ? new ClamAvScanner(sp.GetRequiredService<IOptions<ScanningOptions>>())
                : new Application.Attachments.DisabledMalwareScanner());
    }

    /// <summary>
    /// Message search (Phase 14): the queue, and the provider chosen by <c>Search:Provider</c> —
    /// the database (default) or OpenSearch. <c>SearchSettings</c> is bound by the host.
    /// </summary>
    private static void AddChatXSearch(this IServiceCollection services)
    {
        services.AddScoped<Application.Search.ISearchBacklog, Search.EfSearchBacklog>();
        services.AddOptions<Search.OpenSearchOptions>().BindConfiguration(Search.OpenSearchOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<Search.OpenSearchOptions>, Search.OpenSearchOptionsValidator>();
        services.AddHttpClient(Search.OpenSearchProvider.HttpClientName)
            .ConfigureHttpClient((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<Search.OpenSearchOptions>>().Value;
                if (Uri.TryCreate(options.Url.TrimEnd('/') + "/", UriKind.Absolute, out var url))
                {
                    client.BaseAddress = url;
                }

                client.Timeout = options.Timeout;
            });
        services.AddScoped<Search.DatabaseSearchProvider>();
        services.AddScoped<Application.Search.ISearchProvider>(sp =>
            Application.Search.SearchSettings.Normalize(sp.GetRequiredService<IOptions<Application.Search.SearchSettings>>().Value.Provider)
                == Application.Search.SearchSettings.OpenSearchProvider
                ? new Search.OpenSearchProvider(
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient(Search.OpenSearchProvider.HttpClientName),
                    sp.GetRequiredService<IOptions<Search.OpenSearchOptions>>())
                : sp.GetRequiredService<Search.DatabaseSearchProvider>());
    }

    private static void AddChatXCaptcha(this IServiceCollection services)
    {
        services.AddOptions<CaptchaOptions>().BindConfiguration(CaptchaOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<CaptchaOptions>, CaptchaOptionsValidator>();
        services.AddHttpClient<TurnstileCaptchaVerifier>();
        services.AddSingleton<DisabledCaptchaVerifier>();
        services.AddScoped<ICaptchaVerifier>(sp =>
            CaptchaProviders.Normalize(sp.GetRequiredService<IOptions<CaptchaOptions>>().Value.Provider) == CaptchaProviders.None
                ? sp.GetRequiredService<DisabledCaptchaVerifier>()
                : sp.GetRequiredService<TurnstileCaptchaVerifier>());
    }
}
