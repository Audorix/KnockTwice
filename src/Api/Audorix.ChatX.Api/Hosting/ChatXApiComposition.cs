using System.Globalization;
using Audorix.ChatX.Api.Endpoints;
using Audorix.ChatX.Api.Errors;
using Audorix.ChatX.Api.Health;
using Audorix.ChatX.Application;
using Audorix.ChatX.Application.Localization;
using Audorix.ChatX.Infrastructure;
using Audorix.ChatX.Infrastructure.Persistence;
using Audorix.ChatX.Persistence.PostgreSql;
using Audorix.ChatX.Persistence.Sqlite;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Api.Hosting;

/// <summary>Composition root: wires Application, Infrastructure and both database adapters.</summary>
public static class ChatXApiComposition
{
    public static WebApplicationBuilder AddChatXApi(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;

        // Do not advertise the server implementation in responses.
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);

        services.AddOptions<LocalizationSettings>()
            .BindConfiguration(LocalizationSettings.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<LocalizationSettings>, LocalizationSettingsValidator>();
        services.AddOptions<OpenApiSettings>().BindConfiguration(OpenApiSettings.SectionName);

        services.AddChatXApplication();
        services.AddChatXInfrastructure();

        // Both adapters ship in the host; Database:Provider selects exactly one at runtime.
        services.AddSingleton<IDatabaseProvider>(new SqliteDatabaseProvider(builder.Environment.ContentRootPath));
        services.AddSingleton<IDatabaseProvider, PostgreSqlDatabaseProvider>();

        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddOptions<RequestLocalizationOptions>()
            .Configure<IOptions<LocalizationSettings>>((options, settings) =>
            {
                var cultures = settings.Value.EffectiveSupportedLanguages
                    .Select(CultureInfo.GetCultureInfo)
                    .ToList();
                options.DefaultRequestCulture = new RequestCulture(settings.Value.DefaultLanguage);
                options.SupportedCultures = cultures;
                options.SupportedUICultures = cultures;
                options.FallBackToParentCultures = true;
                options.FallBackToParentUICultures = true;
            });

        services.AddChatXProblemDetails();
        services.AddOpenApi(OpenApiSettings.DocumentName, options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "Audorix.ChatX API";
            document.Info.Version = OpenApiSettings.DocumentName;
            document.Info.Description = "REST API of Audorix.ChatX. Application endpoints live under /api/v1/.";
            return Task.CompletedTask;
        }));

        return builder;
    }

    public static WebApplication UseChatXApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

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

        app.MapChatXHealthEndpoints();

        if (app.Services.GetRequiredService<IOptions<OpenApiSettings>>().Value.Enabled)
        {
            app.MapOpenApi("/openapi/{documentName}.json");

            // Swagger UI renders the document above; it has no generator of its own.
            app.UseSwaggerUI(ui =>
            {
                ui.RoutePrefix = OpenApiSettings.SwaggerUiPath;
                ui.SwaggerEndpoint($"/openapi/{OpenApiSettings.DocumentName}.json", "Audorix.ChatX API v1");
                ui.DocumentTitle = "Audorix.ChatX API";
            });
        }

        app.MapGroup("/api/v1").MapChatXV1Endpoints();

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
