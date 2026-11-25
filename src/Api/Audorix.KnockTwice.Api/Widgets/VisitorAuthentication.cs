using System.Security.Claims;
using System.Text.Encodings.Web;
using Audorix.KnockTwice.Application.Widgets;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Api.Widgets;

/// <summary>
/// Website visitors authenticate with the opaque session token from
/// <c>POST /api/v1/widget/{widgetId}/sessions</c>, sent as <c>Authorization: Bearer</c> (or, for the
/// <c>/hubs/widget</c> WebSocket only, as the <c>access_token</c> query value browsers require).
/// Tokens are never cookies, so the widget API needs no antiforgery token and cannot be driven
/// cross-site. Visitor principals never satisfy the signed-in user policies (different scheme).
/// </summary>
internal sealed class VisitorAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Visitor";
    public const string PolicyName = "visitor";
    public const string HubPath = "/hubs/widget";

    private const string VisitorIdClaim = "chatx:visitor";
    private const string SessionIdClaim = "chatx:vsid";
    private const string WidgetIdClaim = "chatx:widget";
    private const string IdentifiedClaim = "chatx:identified";
    private const string TokenItem = "chatx.visitor-token";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ReadToken(Request);
        if (token is null)
        {
            return AuthenticateResult.NoResult();
        }

        var visitors = Context.RequestServices.GetRequiredService<VisitorService>();
        var visitor = await visitors.AuthenticateAsync(token, Context.RequestAborted).ConfigureAwait(false);
        if (visitor is null)
        {
            return AuthenticateResult.Fail("The visitor session is not valid.");
        }

        Context.Items[TokenItem] = token;
        var identity = new ClaimsIdentity(
            [
                // NameIdentifier is the stand-in account, so user-addressed realtime events reach the visitor.
                new Claim(ClaimTypes.NameIdentifier, visitor.UserId.ToString()),
                new Claim(ClaimTypes.Name, visitor.DisplayName),
                new Claim(VisitorIdClaim, visitor.VisitorId.ToString()),
                new Claim(SessionIdClaim, visitor.SessionId.ToString()),
                new Claim(WidgetIdClaim, visitor.WidgetId.ToString()),
                new Claim(IdentifiedClaim, visitor.Identified ? "true" : "false"),
            ],
            SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public static VisitorPrincipal RequireVisitor(ClaimsPrincipal user) =>
        TryGetVisitor(user) ?? throw new UnauthorizedVisitorException("unauthorized");

    public static VisitorPrincipal? TryGetVisitor(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.Identity?.AuthenticationType != SchemeName
            || !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !Guid.TryParse(user.FindFirstValue(VisitorIdClaim), out var visitorId)
            || !Guid.TryParse(user.FindFirstValue(SessionIdClaim), out var sessionId)
            || !Guid.TryParse(user.FindFirstValue(WidgetIdClaim), out var widgetId))
        {
            return null;
        }

        return new VisitorPrincipal(sessionId, visitorId, widgetId, userId, user.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
            user.FindFirstValue(IdentifiedClaim) == "true");
    }

    public static string? CurrentToken(HttpContext context) => context.Items[TokenItem] as string;

    private static string? ReadToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var value = header["Bearer ".Length..].Trim();
            return value.Length > 0 ? value : null;
        }

        // Browsers cannot set headers on WebSocket handshakes; the query form is accepted on the hub only.
        if (request.Path.StartsWithSegments(HubPath, StringComparison.OrdinalIgnoreCase)
            && request.Query["access_token"].ToString() is { Length: > 0 } query)
        {
            return query;
        }

        return null;
    }
}
