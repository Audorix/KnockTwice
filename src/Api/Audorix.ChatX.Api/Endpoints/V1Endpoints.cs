using Audorix.ChatX.Application.Localization;
using Audorix.ChatX.Contracts.V1.Meta;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Api.Endpoints;

/// <summary>
/// Endpoints under <c>/api/v1/</c>. Module endpoint groups (auth, users, conversations, …)
/// are added here as their phases are implemented.
/// </summary>
internal static class V1Endpoints
{
    public const string Product = "Audorix.ChatX";
    public const string Stage = "foundation";

    public static RouteGroupBuilder MapChatXV1Endpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/meta", GetMetadata)
            .WithName("GetApiMetadata")
            .WithSummary("Describes the public API surface of this installation.")
            .WithDescription("Unauthenticated. Contains no build, runtime, host or database details.")
            .WithTags("Meta");

        return group;
    }

    private static Ok<ApiMetadataResponse> GetMetadata(IOptions<LocalizationSettings> localization) =>
        TypedResults.Ok(new ApiMetadataResponse(
            Product,
            ApiVersion: "v1",
            Stage,
            localization.Value.DefaultLanguage.Trim().ToLowerInvariant(),
            localization.Value.EffectiveSupportedLanguages));
}
