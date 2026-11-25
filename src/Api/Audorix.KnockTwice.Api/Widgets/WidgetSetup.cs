using Audorix.KnockTwice.Application.Widgets;
using Microsoft.AspNetCore.Authentication;

namespace Audorix.KnockTwice.Api.Widgets;

/// <summary>Registration of the embedded chat: visitor authentication, its policy, the widget hub and its endpoints.</summary>
internal static class WidgetSetup
{
    public static IServiceCollection AddChatXWidgets(this IServiceCollection services)
    {
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, VisitorAuthenticationHandler>(VisitorAuthenticationHandler.SchemeName, null);
        services.AddAuthorizationBuilder().AddPolicy(VisitorAuthenticationHandler.PolicyName, policy => policy
            .AddAuthenticationSchemes(VisitorAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser());
        services.AddSingleton<WidgetConnections>();
        services.AddHostedService<WidgetConnectionSweeper>();
        return services;
    }

    public static WebApplication MapChatXWidgets(this WebApplication app)
    {
        app.MapGroup("/api/v1/widget").MapWidgetEndpoints();
        app.MapHub<WidgetHub>(VisitorAuthenticationHandler.HubPath).RequireAuthorization(VisitorAuthenticationHandler.PolicyName);
        return app;
    }
}
