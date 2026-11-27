using System.Globalization;
using Audorix.KnockTwice.Api.Endpoints;
using Audorix.KnockTwice.Api.Errors;
using Audorix.KnockTwice.Api.Health;
using Audorix.KnockTwice.Api.Identity;
using Audorix.KnockTwice.Api.Realtime;
using Audorix.KnockTwice.Application;
using Audorix.KnockTwice.Application.Localization;
using Audorix.KnockTwice.Infrastructure;
using Audorix.KnockTwice.Infrastructure.Persistence;
using Audorix.KnockTwice.Persistence.PostgreSql;
using Audorix.KnockTwice.Persistence.Sqlite;
using Audorix.KnockTwice.Api.Administration;
using Audorix.KnockTwice.Api.Widgets;
using Audorix.KnockTwice.Api.SupportQueue;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Api.Hosting;

/// <summary>Composition root: wires Application, Infrastructure and both database adapters.</summary>
public static class ChatXApiComposition
{
    public static WebApplicationBuilder AddChatXApi(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;

        // Do not advertise the server implementation in responses.
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);

        // Live settings (Phase 9): a database layer above the settings file, below the environment.
        builder.AddChatXRuntimeSettings();

        services.AddChatXCoreServices(builder.Configuration, builder.Environment.ContentRootPath);
        services.AddChatXAdministration();
        services.AddChatXRequestLocalization();
        services.AddChatXProblemDetails();
        services.AddChatXRealtime();
        // Phase 21: Redis backplane, shared presence and shared rate limits when ScaleOut:Redis is set.
        ScaleOut.ScaleOutSetup.AddChatXScaleOut(services, builder.Configuration);
        services.AddChatXWidgets();
        Search.SearchSetup.AddChatXSearchIndexing(services);
        Notifications.NotificationSetup.AddChatXNotificationDispatch(services);
        Planning.PlanningSetup.AddChatXPlanningDispatch(services);
        services.AddChatXOpenApi("REST API of Knock Twice. Application endpoints live under /api/v1/.");

        return builder;
    }

    /// <summary>OpenAPI document generation; exposed only when <c>OpenApi:Enabled</c> (see <see cref="UseChatXOpenApi"/>).</summary>
    internal static IServiceCollection AddChatXOpenApi(this IServiceCollection services, string description)
    {
        services.AddOptions<OpenApiSettings>().BindConfiguration(OpenApiSettings.SectionName);
        services.AddOpenApi(OpenApiSettings.DocumentName, options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "Knock Twice API";
            document.Info.Version = OpenApiSettings.DocumentName;
            document.Info.Description = description;
            return Task.CompletedTask;
        }));
        return services;
    }

    /// <summary>Serves <c>/openapi/v1.json</c> and Swagger UI at <c>/swagger</c> when <c>OpenApi:Enabled</c>.</summary>
    internal static WebApplication UseChatXOpenApi(this WebApplication app)
    {
        if (app.Services.GetRequiredService<IOptions<OpenApiSettings>>().Value.Enabled)
        {
            app.MapOpenApi("/openapi/{documentName}.json");

            // Swagger UI renders the document above; it has no generator of its own.
            app.UseSwaggerUI(ui =>
            {
                ui.RoutePrefix = OpenApiSettings.SwaggerUiPath;
                ui.SwaggerEndpoint($"/openapi/{OpenApiSettings.DocumentName}.json", "Knock Twice API v1");
                ui.DocumentTitle = "Knock Twice API";
            });
        }

        return app;
    }

    /// <summary>
    /// Application, Infrastructure, both database adapters and Identity. Shared by the HTTP host
    /// and the setup wizard, which builds the same graph over candidate settings to validate
    /// them, migrate the database and create the first administrator.
    /// </summary>
    internal static IServiceCollection AddChatXCoreServices(this IServiceCollection services, IConfiguration configuration, string contentRoot)
    {
        services.AddOptions<LocalizationSettings>()
            .BindConfiguration(LocalizationSettings.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<LocalizationSettings>, LocalizationSettingsValidator>();
        services.AddOptions<Application.Users.DiscoverySettings>().BindConfiguration(Application.Users.DiscoverySettings.SectionName);
        services.AddOptions<Application.Organizations.OrganizationSettings>()
            .BindConfiguration(Application.Organizations.OrganizationSettings.SectionName)
            .Validate(s => !string.IsNullOrWhiteSpace(s.DefaultWorkspaceName) && s.DefaultWorkspaceName.Length <= 80,
                "Organizations:DefaultWorkspaceName must be 1-80 characters.")
            .ValidateOnStart();
        services.AddOptions<Application.Conversations.ConversationSettings>()
            .BindConfiguration(Application.Conversations.ConversationSettings.SectionName)
            .Validate(s => s.MaxGroupParticipants is >= 2 and <= 10000, "Conversations:MaxGroupParticipants must be between 2 and 10000.")
            .ValidateOnStart();

        services.AddOptions<Application.Demo.DemoDataSettings>().BindConfiguration(Application.Demo.DemoDataSettings.SectionName)
            .PostConfigure<Microsoft.Extensions.Hosting.IHostEnvironment>((s, env) =>
            {
                if (!string.IsNullOrWhiteSpace(s.AvatarsPath))
                {
                    s.AvatarsPath = Path.GetFullPath(s.AvatarsPath, env.ContentRootPath);
                }
            });
        services.AddSingleton<Administration.DemoDataJobs>();
        services.AddOptions<Application.Messaging.MessagingSettings>()
            .BindConfiguration(Application.Messaging.MessagingSettings.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<Application.Messaging.MessagingSettings>, Identity.DelegatingValidator<Application.Messaging.MessagingSettings>>(
            _ => new Identity.DelegatingValidator<Application.Messaging.MessagingSettings>(s => s.Validate()));

        services.AddOptions<Application.Attachments.AttachmentSettings>()
            .BindConfiguration(Application.Attachments.AttachmentSettings.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<Application.Attachments.AttachmentSettings>, Identity.DelegatingValidator<Application.Attachments.AttachmentSettings>>(
            _ => new Identity.DelegatingValidator<Application.Attachments.AttachmentSettings>(s => s.Validate()));
        services.AddOptions<Application.PrivateChats.PrivateChatSettings>()
            .BindConfiguration(Application.PrivateChats.PrivateChatSettings.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<Application.PrivateChats.PrivateChatSettings>, Identity.DelegatingValidator<Application.PrivateChats.PrivateChatSettings>>(
            _ => new Identity.DelegatingValidator<Application.PrivateChats.PrivateChatSettings>(s => s.Validate()));

        services.AddOptions<Application.Administration.AuditSettings>()
            .BindConfiguration(Application.Administration.AuditSettings.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<Application.Administration.AuditSettings>, Identity.DelegatingValidator<Application.Administration.AuditSettings>>(
            _ => new Identity.DelegatingValidator<Application.Administration.AuditSettings>(s => s.Validate()));
        services.TryAddScoped<Application.Administration.IAuditContext, Administration.NoAuditContext>();

        services.AddOptions<Application.Administration.RetentionSettings>()
            .BindConfiguration(Application.Administration.RetentionSettings.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<Application.Administration.RetentionSettings>, Identity.DelegatingValidator<Application.Administration.RetentionSettings>>(
            _ => new Identity.DelegatingValidator<Application.Administration.RetentionSettings>(s => s.Validate()));

        services.AddOptions<Application.Search.SearchSettings>()
            .BindConfiguration(Application.Search.SearchSettings.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<Application.Search.SearchSettings>, Identity.DelegatingValidator<Application.Search.SearchSettings>>(
            _ => new Identity.DelegatingValidator<Application.Search.SearchSettings>(s => s.Validate()));

        services.AddOptions<Application.Notifications.NotificationSettings>()
            .BindConfiguration(Application.Notifications.NotificationSettings.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<Application.Notifications.NotificationSettings>, Identity.DelegatingValidator<Application.Notifications.NotificationSettings>>(
            _ => new Identity.DelegatingValidator<Application.Notifications.NotificationSettings>(s => s.Validate()));

        services.AddOptions<Application.Planning.PlanningSettings>()
            .BindConfiguration(Application.Planning.PlanningSettings.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<Application.Planning.PlanningSettings>, Identity.DelegatingValidator<Application.Planning.PlanningSettings>>(
            _ => new Identity.DelegatingValidator<Application.Planning.PlanningSettings>(s => s.Validate()));

        services.AddOptions<Application.Widgets.WidgetSettings>()
            .BindConfiguration(Application.Widgets.WidgetSettings.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<Application.Widgets.WidgetSettings>, Identity.DelegatingValidator<Application.Widgets.WidgetSettings>>(
            _ => new Identity.DelegatingValidator<Application.Widgets.WidgetSettings>(s => s.Validate()));

        services.AddChatXApplication();
        services.AddChatXInfrastructure();
        services.AddSupportRoutingQueue();
        services.AddHostedService<SupportQueue.AttachmentStorageMigrationWorker>();

        // Local attachment storage: relative paths are relative to the content root, like the SQLite file.
        services.PostConfigure<Infrastructure.Storage.StorageOptions>(options =>
        {
            if (!string.IsNullOrWhiteSpace(options.Local.Path))
            {
                options.Local.Path = Path.GetFullPath(options.Local.Path, contentRoot);
            }
        });

        // Both adapters ship in the host; Database:Provider selects exactly one at runtime.
        services.AddChatXDatabaseProviders(contentRoot);
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddChatXIdentity(configuration);
        return services;
    }

    internal static IServiceCollection AddChatXDatabaseProviders(this IServiceCollection services, string contentRoot)
    {
        services.AddSingleton<IDatabaseProvider>(new SqliteDatabaseProvider(contentRoot));
        services.AddSingleton<IDatabaseProvider, PostgreSqlDatabaseProvider>();
        return services;
    }

    /// <summary>Request cultures from <see cref="LocalizationSettings"/> (registered separately).</summary>
    internal static IServiceCollection AddChatXRequestLocalization(this IServiceCollection services)
    {
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddOptions<RequestLocalizationOptions>()
            .Configure<IOptions<LocalizationSettings>>((options, settings) =>
            {
                // Every shipped language is technically available; which ones are offered (and the
                // default) is decided per request from the live settings (Phase 9).
                var cultures = LocalizationSettings.AvailableLanguages
                    .Select(CultureInfo.GetCultureInfo)
                    .ToList();
                options.DefaultRequestCulture = new RequestCulture(settings.Value.DefaultLanguage);
                options.SupportedCultures = cultures;
                options.SupportedUICultures = cultures;
                options.FallBackToParentCultures = true;
                options.FallBackToParentUICultures = true;
                options.RequestCultureProviders.Insert(0, new LiveLanguageCultureProvider());
            });
        return services;
    }

    internal static WebApplication UseChatXForwardedHeaders(this WebApplication app) => NetworkSettings.UseForwardedHeadersFrom(app);

    /// <summary>Localization, problem responses and security headers; the start of both pipelines.</summary>
    internal static WebApplication UseChatXRequestBasics(this WebApplication app)
    {
        // Localization first so error responses produced further out use the request language.
        app.UseRequestLocalization();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.Use(static (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            return next(context);
        });
        return app;
    }

    public static WebApplication UseChatXApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseChatXForwardedHeaders();
        app.UseChatXRequestBasics();
        app.UseChatXRealtimeOriginCheck();
        app.UseOrganizationOidcCallbackRegistration();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        app.MapChatXHealthEndpoints();
        app.UseChatXOpenApi();
        app.MapGroup("/api/v1").MapChatXV1Endpoints();
        app.MapChatXRealtime();
        app.MapChatXWidgets();

        return app;
    }
}

/// <summary>
/// Reverse proxies whose <c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c> are believed, so per-IP
/// limits (sign-in, anonymous widget sessions) see the real client. Empty (the default) believes
/// no one. List only addresses that every request must pass through: a client that can reach the
/// API directly from a listed network could otherwise claim any address.
/// </summary>
public sealed class NetworkSettings
{
    public const string SectionName = "Network";

    /// <summary>IP addresses or CIDR networks, e.g. <c>10.0.0.5</c> or <c>172.18.0.0/16</c>.</summary>
    public IList<string> TrustedProxies { get; } = [];

    internal static WebApplication UseForwardedHeadersFrom(WebApplication app)
    {
        var settings = app.Configuration.GetSection(SectionName).Get<NetworkSettings>() ?? new();
        if (settings.TrustedProxies.Count == 0)
        {
            return app;
        }

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            // Walk back through the chain only while the hop is a trusted proxy (TLS proxy → nginx → API
            // is two hops); the first untrusted address is the client.
            ForwardLimit = null,
        };
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var entry in settings.TrustedProxies)
        {
            var value = entry.Trim();
            if (System.Net.IPNetwork.TryParse(value, out var network))
            {
                options.KnownIPNetworks.Add(network);
            }
            else if (System.Net.IPAddress.TryParse(value, out var address))
            {
                options.KnownProxies.Add(address);
            }
            else
            {
                throw new InvalidOperationException($"Network:TrustedProxies contains '{value}', which is neither an IP address nor a CIDR network.");
            }
        }

        app.UseForwardedHeaders(options);
        return app;
    }
}

/// <summary>
/// OpenAPI document and Swagger UI exposure. Enabled in <c>appsettings.Development.json</c>;
/// disabled by default elsewhere so production does not publish its surface unless an operator opts in.
/// </summary>
public sealed class OpenApiSettings
{
    public const string SectionName = "OpenApi";
    public const string DocumentName = "v1";
    public const string SwaggerUiPath = "swagger";

    public bool Enabled { get; set; }
}

internal sealed class LocalizationSettingsValidator : IValidateOptions<LocalizationSettings>
{
    public ValidateOptionsResult Validate(string? name, LocalizationSettings options)
    {
        var errors = options.Validate();
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
