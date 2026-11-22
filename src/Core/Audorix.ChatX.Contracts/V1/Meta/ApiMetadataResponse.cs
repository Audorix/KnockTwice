namespace Audorix.ChatX.Contracts.V1.Meta;

/// <summary>
/// Public, unauthenticated description of the API surface. It deliberately carries no
/// build version, runtime, host or database details.
/// </summary>
/// <param name="Product">Product name.</param>
/// <param name="ApiVersion">REST API version segment (for example <c>v1</c>).</param>
/// <param name="Stage">Delivery stage of this build (for example <c>foundation</c>).</param>
/// <param name="DefaultLanguage">Installation default UI language.</param>
/// <param name="SupportedLanguages">UI languages this installation serves.</param>
public sealed record ApiMetadataResponse(
    string Product,
    string ApiVersion,
    string Stage,
    string DefaultLanguage,
    IReadOnlyList<string> SupportedLanguages);
