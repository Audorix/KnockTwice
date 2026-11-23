using Audorix.ChatX.Api.Identity;
using Audorix.ChatX.Application.Localization;
using Audorix.ChatX.Contracts.V1.Meta;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Api.Endpoints;

/// <summary>
/// Endpoints under <c>/api/v1/</c>. Every POST/PUT/PATCH/DELETE in this group requires the
/// antiforgery token from <c>GET /api/v1/auth/csrf</c> in the <c>X-CSRF-TOKEN</c> header.
/// </summary>
internal static class V1Endpoints
{
    public const string Product = "Audorix.ChatX";
    public const string Stage = "preview";

    public static RouteGroupBuilder MapChatXV1Endpoints(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter<AntiforgeryEndpointFilter>();

        group.MapGet("/meta", GetMetadata)
            .WithName("GetApiMetadata")
            .WithSummary("Describes the public API surface of this installation.")
            .WithDescription("Unauthenticated. Contains no build, runtime, host or database details.")
            .WithTags("Meta");

        // The setup wizard's status probe also answers in normal mode, so the web app can always ask.
        group.MapGet("/setup/status", static () => TypedResults.Ok(new Contracts.V1.Setup.SetupStatusResponse(Required: false, Completed: true)))
            .WithName("GetSetupStatus")
            .WithSummary("Whether the installation still needs the setup wizard.")
            .WithTags("Meta");

        group.MapAuthEndpoints();
        group.MapUserEndpoints();
        group.MapContactEndpoints();
        group.MapOrganizationEndpoints();
        group.MapConversationEndpoints();
        group.MapAttachmentEndpoints();
        group.MapPrivateChatEndpoints();
        group.MapAdminEndpoints();
        group.MapBusinessEndpoints();
        Widgets.WidgetEndpoints.MapWidgetAdminEndpoints(group);
        Search.SearchSetup.MapSearchEndpoints(group);
        Notifications.NotificationSetup.MapNotificationEndpoints(group);
        Planning.PlanningSetup.MapPlanningEndpoints(group);
        group.MapChangeEndpoints();
        return group;
    }

    private static Ok<ApiMetadataResponse> GetMetadata(IOptionsSnapshot<LocalizationSettings> localization) =>
        TypedResults.Ok(new ApiMetadataResponse(
            Product,
            ApiVersion: "v1",
            Stage,
            localization.Value.DefaultLanguage.Trim().ToLowerInvariant(),
            localization.Value.EffectiveSupportedLanguages));
}
