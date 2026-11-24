using System.Net;
using Audorix.ChatX.Domain.Contacts;
using Audorix.ChatX.Infrastructure.Persistence;
using Audorix.ChatX.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Audorix.ChatX.IntegrationTests.OrganizationTests;
using static Audorix.ChatX.IntegrationTests.Support.ApiSession;
using static Audorix.ChatX.IntegrationTests.Support.TestUsers;

namespace Audorix.ChatX.IntegrationTests;

/// <summary>Phase 3 discovery, contacts and blocking.</summary>
public sealed class ContactTests
{
    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Contacts page redesign (2026-11): each contact lists the organizations you share.</summary>
    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Contacts_list_the_organizations_both_people_belong_to(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (carl, carlId) = await SignedInAsync(api, "carl@example.org", "Carl");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = carl;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        await BecomeContactsAsync(ana, anaId, carl, carlId);
        var acme = await CreateOrganizationAsync(ana, (ben, "ben@example.org"));
        // Carl has his own organization without Ana: not shared, so not shown.
        await CreateOrganizationAsync(carl);

        var contacts = (await JsonAsync(await ana.GetAsync("/api/v1/contacts"))).EnumerateArray()
            .ToDictionary(c => c.GetProperty("user").GetProperty("id").GetGuid());
        var benOrganizations = contacts[benId].GetProperty("sharedOrganizations").EnumerateArray().ToList();
        Assert.Equal((acme, "Acme"), (Assert.Single(benOrganizations).GetProperty("id").GetGuid(), benOrganizations[0].GetProperty("name").GetString()));
        Assert.Empty(contacts[carlId].GetProperty("sharedOrganizations").EnumerateArray());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Contacts_are_requested_accepted_and_independent_of_organizations(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana Alvarez");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben Brown");
        using var _1 = ana;
        using var _2 = ben;

        // Exact email finds the user; the result never contains an email. Name search needs a shared organization.
        var byEmail = Assert.Single((await JsonAsync(await ana.GetAsync("/api/v1/users/search?q=BEN%40example.org"))).EnumerateArray());
        Assert.Equal(benId, byEmail.GetProperty("id").GetGuid());
        Assert.False(byEmail.TryGetProperty("email", out _));
        Assert.Empty((await JsonAsync(await ana.GetAsync("/api/v1/users/search?q=Ben"))).EnumerateArray());
        Assert.Equal("validation_failed", await ErrorCodeAsync(await ana.GetAsync("/api/v1/users/search?q=B")));
        Assert.Equal("user_not_found", await ErrorCodeAsync(await ana.GetAsync($"/api/v1/users/{benId}")));

        var sent = await JsonAsync(await ana.PostAsync("/api/v1/contacts/requests", new { userId = benId }));
        Assert.Equal("pending", sent.GetProperty("status").GetString());
        var again = await JsonAsync(await ana.PostAsync("/api/v1/contacts/requests", new { userId = benId }));
        Assert.Equal(sent.GetProperty("request").GetProperty("id").GetGuid(), again.GetProperty("request").GetProperty("id").GetGuid());
        Assert.Equal("cannot_target_self", await ErrorCodeAsync(await ana.PostAsync("/api/v1/contacts/requests", new { userId = anaId })));

        // A pending request makes the other user visible by id.
        Assert.Equal("Ben Brown", (await JsonAsync(await ana.GetAsync($"/api/v1/users/{benId}"))).GetProperty("displayName").GetString());

        var incoming = Assert.Single((await JsonAsync(await ben.GetAsync("/api/v1/contacts/requests"))).EnumerateArray());
        Assert.Equal("incoming", incoming.GetProperty("direction").GetString());
        Assert.Equal("contact_request_not_found", await ErrorCodeAsync(await ana.PostAsync($"/api/v1/contacts/requests/{incoming.GetProperty("id").GetGuid()}/accept")));
        AssertStatus(HttpStatusCode.NoContent, await ben.PostAsync($"/api/v1/contacts/requests/{incoming.GetProperty("id").GetGuid()}/accept"));

        Assert.Equal(benId, Assert.Single((await JsonAsync(await ana.GetAsync("/api/v1/contacts"))).EnumerateArray()).GetProperty("user").GetProperty("id").GetGuid());
        Assert.Equal(anaId, Assert.Single((await JsonAsync(await ben.GetAsync("/api/v1/contacts"))).EnumerateArray()).GetProperty("user").GetProperty("id").GetGuid());
        Assert.Equal("already_contacts", await ErrorCodeAsync(await ben.PostAsync("/api/v1/contacts/requests", new { userId = anaId })));

        // Joining and leaving a shared organization does not affect the contact relationship.
        var orgId = (await JsonAsync(await ana.PostAsync("/api/v1/organizations", new { name = "Shared" }))).GetProperty("id").GetGuid();
        var invitationId = (await JsonAsync(await ana.PostAsync($"/api/v1/organizations/{orgId}/invitations", new { email = "ben@example.org" }))).GetProperty("id").GetGuid();
        await ben.PostAsync($"/api/v1/organizations/invitations/{invitationId}/accept");
        Assert.Single((await JsonAsync(await ana.GetAsync("/api/v1/users/search?q=brow"))).EnumerateArray());
        await ben.SendAsync(HttpMethod.Delete, $"/api/v1/organizations/{orgId}/members/{benId}");
        Assert.Empty((await JsonAsync(await ana.GetAsync("/api/v1/users/search?q=brow"))).EnumerateArray());
        Assert.Single((await JsonAsync(await ana.GetAsync("/api/v1/contacts"))).EnumerateArray());

        AssertStatus(HttpStatusCode.NoContent, await ben.SendAsync(HttpMethod.Delete, $"/api/v1/contacts/{anaId}"));
        Assert.Empty((await JsonAsync(await ana.GetAsync("/api/v1/contacts"))).EnumerateArray());
    }

    [Fact]
    public async Task Crossing_requests_become_a_contact()
    {
        await using var db = new SqliteTestDatabase();
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;

        await ana.PostAsync("/api/v1/contacts/requests", new { userId = benId });
        var reply = await JsonAsync(await ben.PostAsync("/api/v1/contacts/requests", new { userId = anaId }));

        Assert.Equal("accepted", reply.GetProperty("status").GetString());
        Assert.Single((await JsonAsync(await ana.GetAsync("/api/v1/contacts"))).EnumerateArray());
        Assert.Empty((await JsonAsync(await ana.GetAsync("/api/v1/contacts/requests"))).EnumerateArray());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Blocked_users_cannot_initiate_contact_or_find_each_other(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;
        var request = await JsonAsync(await ana.PostAsync("/api/v1/contacts/requests", new { userId = benId }));
        await ben.PostAsync($"/api/v1/contacts/requests/{request.GetProperty("request").GetProperty("id").GetGuid()}/accept");

        AssertStatus(HttpStatusCode.NoContent, await ben.PostAsync($"/api/v1/contacts/{anaId}/block"));
        AssertStatus(HttpStatusCode.NoContent, await ben.PostAsync($"/api/v1/contacts/{anaId}/block")); // idempotent

        // The block removed the contact on both sides.
        Assert.Empty((await JsonAsync(await ana.GetAsync("/api/v1/contacts"))).EnumerateArray());
        Assert.Empty((await JsonAsync(await ben.GetAsync("/api/v1/contacts"))).EnumerateArray());

        // The blocked user cannot tell they were blocked: the blocker looks like they do not exist.
        Assert.Equal((HttpStatusCode.NotFound, "user_not_found"), await StatusAndCode(ana.PostAsync("/api/v1/contacts/requests", new { userId = benId })));
        Assert.Equal("user_not_found", await ErrorCodeAsync(await ana.GetAsync($"/api/v1/users/{benId}")));
        Assert.Empty((await JsonAsync(await ana.GetAsync("/api/v1/users/search?q=ben%40example.org"))).EnumerateArray());

        // The blocker is told to unblock first.
        Assert.Equal((HttpStatusCode.Conflict, "user_blocked"), await StatusAndCode(ben.PostAsync("/api/v1/contacts/requests", new { userId = anaId })));
        Assert.Equal(anaId, Assert.Single((await JsonAsync(await ben.GetAsync("/api/v1/contacts/blocked"))).EnumerateArray()).GetProperty("user").GetProperty("id").GetGuid());

        AssertStatus(HttpStatusCode.NoContent, await ben.SendAsync(HttpMethod.Delete, $"/api/v1/contacts/{anaId}/block"));
        Assert.Equal("pending", (await JsonAsync(await ana.PostAsync("/api/v1/contacts/requests", new { userId = benId }))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Blocking_closes_pending_requests()
    {
        await using var db = new SqliteTestDatabase();
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;
        await ana.PostAsync("/api/v1/contacts/requests", new { userId = benId });

        await ben.PostAsync($"/api/v1/contacts/{anaId}/block");

        Assert.Empty((await JsonAsync(await ana.GetAsync("/api/v1/contacts/requests"))).EnumerateArray());
        Assert.Empty((await JsonAsync(await ben.GetAsync("/api/v1/contacts/requests"))).EnumerateArray());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Database_allows_only_one_pending_request_per_pair(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        ana.Dispose();
        ben.Dispose();

        await using var scope = api.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ChatXDbContext>();
        context.ContactRequests.Add(ContactRequest.Create(Guid.NewGuid(), anaId, benId, DateTimeOffset.UtcNow));
        context.ContactRequests.Add(ContactRequest.Create(Guid.NewGuid(), benId, anaId, DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));
    }
}
