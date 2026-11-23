using Audorix.ChatX.Api.Identity;
using Audorix.ChatX.Application.Contacts;
using Audorix.ChatX.Application.Users;
using Audorix.ChatX.Contracts.V1.Contacts;
using Microsoft.AspNetCore.Http.HttpResults;
using static Audorix.ChatX.Api.Endpoints.OrganizationEndpoints;

namespace Audorix.ChatX.Api.Endpoints;

/// <summary><c>/api/v1/contacts</c> and user discovery (<c>/api/v1/users/search</c>, <c>/api/v1/users/{id}</c>).</summary>
internal static class ContactEndpoints
{
    public static RouteGroupBuilder MapContactEndpoints(this RouteGroupBuilder v1)
    {
        var users = v1.MapGroup("/users").RequireAuthorization().WithTags("Users");
        users.MapGet("/search", async (string? q, HttpContext http, UserDirectoryService directory, CancellationToken ct) =>
            TypedResults.Ok((await directory.SearchAsync(http.User.RequireUserId(), q, ct)).Select(ToUser).ToList()))
            .WithSummary("Finds users by exact email address, or by name among people sharing an organization. Never returns emails.");
        users.MapGet("/{userId:guid}", async (Guid userId, HttpContext http, UserDirectoryService directory, CancellationToken ct) =>
            TypedResults.Ok(ToUser(await directory.GetAsync(http.User.RequireUserId(), userId, ct))))
            .WithSummary("A user's public summary, if the caller has a relationship with them.");

        var contacts = v1.MapGroup("/contacts").RequireAuthorization().WithTags("Contacts");
        contacts.MapGet(string.Empty, async (HttpContext http, ContactService service, CancellationToken ct) =>
            TypedResults.Ok((await service.ListContactsAsync(http.User.RequireUserId(), ct)).Select(ToResponse).ToList()));
        contacts.MapDelete("/{userId:guid}", async (Guid userId, HttpContext http, ContactService service, CancellationToken ct) =>
        {
            await service.RemoveContactAsync(http.User.RequireUserId(), userId, ct);
            return TypedResults.NoContent();
        }).WithSummary("Removes a contact (either side may remove).");

        contacts.MapGet("/requests", async (HttpContext http, ContactService service, CancellationToken ct) =>
            TypedResults.Ok((await service.ListRequestsAsync(http.User.RequireUserId(), ct)).Select(ToResponse).ToList()))
            .WithSummary("Pending incoming and outgoing contact requests.");
        contacts.MapPost("/requests", async (SendContactRequest request, HttpContext http, ContactService service, CancellationToken ct) =>
        {
            var result = await service.SendRequestAsync(http.User.RequireUserId(), request.UserId, ct);
            return TypedResults.Ok(new SendContactRequestResponse(result.BecameContacts ? "accepted" : "pending",
                result.Request is null ? null : ToResponse(result.Request)));
        }).WithSummary("Sends a contact request. If the other user already asked, the two become contacts.");
        contacts.MapPost("/requests/{requestId:guid}/accept", async (Guid requestId, HttpContext http, ContactService service, CancellationToken ct) =>
        {
            await service.AcceptAsync(http.User.RequireUserId(), requestId, ct);
            return TypedResults.NoContent();
        });
        contacts.MapPost("/requests/{requestId:guid}/reject", async (Guid requestId, HttpContext http, ContactService service, CancellationToken ct) =>
        {
            await service.DeclineAsync(http.User.RequireUserId(), requestId, ct);
            return TypedResults.NoContent();
        });
        contacts.MapDelete("/requests/{requestId:guid}", async (Guid requestId, HttpContext http, ContactService service, CancellationToken ct) =>
        {
            await service.CancelAsync(http.User.RequireUserId(), requestId, ct);
            return TypedResults.NoContent();
        }).WithSummary("Cancels the caller's own outgoing request.");

        contacts.MapGet("/blocked", async (HttpContext http, ContactService service, CancellationToken ct) =>
            TypedResults.Ok((await service.ListBlockedAsync(http.User.RequireUserId(), ct)).Select(ToResponse).ToList()));
        contacts.MapPost("/{userId:guid}/block", async (
            Guid userId, HttpContext http, ContactService service, Application.PrivateChats.PrivateChatService privateChats,
            Application.Administration.AuditService audit, CancellationToken ct) =>
        {
            await service.BlockAsync(http.User.RequireUserId(), userId, ct);
            await privateChats.EndBetweenAsync(http.User.RequireUserId(), userId, ct);
            await audit.RecordAsync(Application.Administration.AuditActions.UserBlocked, http.User.RequireUserId(), "user", userId.ToString(), cancellationToken: ct);
            return TypedResults.NoContent();
        }).WithSummary("Blocks a user: removes the contact, closes pending requests and prevents new ones both ways.");
        contacts.MapDelete("/{userId:guid}/block", async (
            Guid userId, HttpContext http, ContactService service, Application.Administration.AuditService audit, CancellationToken ct) =>
        {
            await service.UnblockAsync(http.User.RequireUserId(), userId, ct);
            await audit.RecordAsync(Application.Administration.AuditActions.UserUnblocked, http.User.RequireUserId(), "user", userId.ToString(), cancellationToken: ct);
            return TypedResults.NoContent();
        });

        return v1;
    }

    private static ContactResponse ToResponse(ContactView view) => new(ToUser(view.User), view.SinceUtc);

    private static ContactRequestResponse ToResponse(ContactRequestView view) =>
        new(view.Id, view.Direction == RequestDirection.Incoming ? "incoming" : "outgoing", ToUser(view.User), view.CreatedAtUtc);
}
