using System.Net;
using Audorix.ChatX.Domain.Organizations;
using Audorix.ChatX.Infrastructure.Persistence;
using Audorix.ChatX.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Audorix.ChatX.IntegrationTests.Support.ApiSession;
using static Audorix.ChatX.IntegrationTests.Support.TestUsers;

namespace Audorix.ChatX.IntegrationTests;

/// <summary>Phase 3 organizations and workspaces: membership by consent, roles, and tenant isolation.</summary>
public sealed class OrganizationTests
{
    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Members_join_by_accepting_invitations_and_roles_are_enforced(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (owner, ownerId) = await SignedInAsync(api, "owner@example.org", "Olivia Owner");
        var (member, memberId) = await SignedInAsync(api, "member@example.org", "Mateo Member");
        var (outsider, _) = await SignedInAsync(api, "outsider@example.org", "Oscar Outsider");
        using var _1 = owner;
        using var _2 = member;
        using var _3 = outsider;

        var created = await JsonAsync(await owner.PostAsync("/api/v1/organizations", new { name = "  Acme   Labs " }));
        var orgId = created.GetProperty("id").GetGuid();
        Assert.Equal("Acme Labs", created.GetProperty("name").GetString());
        Assert.Equal("owner", created.GetProperty("myRole").GetString());
        var workspaces = await JsonAsync(await owner.GetAsync($"/api/v1/organizations/{orgId}/workspaces"));
        Assert.Equal("General", Assert.Single(workspaces.EnumerateArray()).GetProperty("name").GetString());

        // Invitations are addressed to emails; inviting an unknown address looks exactly the same.
        var invite = await owner.PostAsync($"/api/v1/organizations/{orgId}/invitations", new { email = "member@example.org", role = "member" });
        var inviteUnknown = await owner.PostAsync($"/api/v1/organizations/{orgId}/invitations", new { email = "nobody@example.org" });
        AssertStatus(HttpStatusCode.Created, invite);
        AssertStatus(HttpStatusCode.Created, inviteUnknown);
        Assert.Equal("invitation_pending", await ErrorCodeAsync(await owner.PostAsync($"/api/v1/organizations/{orgId}/invitations", new { email = "MEMBER@example.org" })));
        var invitationId = (await JsonAsync(invite)).GetProperty("id").GetGuid();

        // Only the addressee can see and accept the invitation.
        Assert.Empty((await JsonAsync(await outsider.GetAsync("/api/v1/organizations/invitations"))).EnumerateArray());
        Assert.Equal("invitation_not_found", await ErrorCodeAsync(await outsider.PostAsync($"/api/v1/organizations/invitations/{invitationId}/accept")));
        var mine = Assert.Single((await JsonAsync(await member.GetAsync("/api/v1/organizations/invitations"))).EnumerateArray());
        Assert.Equal("Acme Labs", mine.GetProperty("organizationName").GetString());
        var joined = await JsonAsync(await member.PostAsync($"/api/v1/organizations/invitations/{invitationId}/accept"));
        Assert.Equal("member", joined.GetProperty("myRole").GetString());

        // Members can read but not manage.
        Assert.Equal(2, (await JsonAsync(await member.GetAsync($"/api/v1/organizations/{orgId}/members"))).GetProperty("members").GetArrayLength());
        Assert.Equal(JsonValueKindNull, (await JsonAsync(await member.GetAsync($"/api/v1/organizations/{orgId}/members"))).GetProperty("invitations").ValueKind);
        Assert.Equal((HttpStatusCode.Forbidden, "forbidden"), await StatusAndCode(member.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{orgId}", new { name = "Hijacked" })));
        Assert.Equal((HttpStatusCode.Forbidden, "forbidden"), await StatusAndCode(member.PostAsync($"/api/v1/organizations/{orgId}/invitations", new { email = "x@example.org" })));
        Assert.Equal((HttpStatusCode.Forbidden, "forbidden"), await StatusAndCode(member.PostAsync($"/api/v1/organizations/{orgId}/workspaces", new { name = "Mine" })));

        // The last owner can neither leave nor be demoted; promoting someone else makes it possible.
        Assert.Equal((HttpStatusCode.Conflict, "last_owner"), await StatusAndCode(owner.SendAsync(HttpMethod.Delete, $"/api/v1/organizations/{orgId}/members/{ownerId}")));
        Assert.Equal((HttpStatusCode.Conflict, "last_owner"), await StatusAndCode(owner.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{orgId}/members/{ownerId}", new { role = "member" })));
        AssertStatus(HttpStatusCode.OK, await owner.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{orgId}/members/{memberId}", new { role = "owner" }));
        AssertStatus(HttpStatusCode.NoContent, await owner.SendAsync(HttpMethod.Delete, $"/api/v1/organizations/{orgId}/members/{ownerId}"));
        AssertStatus(HttpStatusCode.NotFound, await owner.GetAsync($"/api/v1/organizations/{orgId}"));
        Assert.Equal("owner", (await JsonAsync(await member.GetAsync($"/api/v1/organizations/{orgId}"))).GetProperty("myRole").GetString());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Organization_data_is_isolated_from_non_members(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (alice, _) = await SignedInAsync(api, "alice@example.org", "Alice");
        var (carol, carolId) = await SignedInAsync(api, "carol@example.org", "Carol");
        using var _1 = alice;
        using var _2 = carol;

        var orgX = (await JsonAsync(await alice.PostAsync("/api/v1/organizations", new { name = "X" }))).GetProperty("id").GetGuid();
        var orgY = (await JsonAsync(await carol.PostAsync("/api/v1/organizations", new { name = "Y" }))).GetProperty("id").GetGuid();
        var workspaceX = (await JsonAsync(await alice.PostAsync($"/api/v1/organizations/{orgX}/workspaces", new { name = "Secret" }))).GetProperty("id").GetGuid();

        // Every read or write against another organization looks like it does not exist.
        string[] reads = [$"/api/v1/organizations/{orgX}", $"/api/v1/organizations/{orgX}/members", $"/api/v1/organizations/{orgX}/workspaces",
            $"/api/v1/organizations/{orgX}/workspaces/{workspaceX}/members"];
        foreach (var path in reads)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await carol.GetAsync(path)).StatusCode);
        }

        Assert.Equal("organization_not_found", await ErrorCodeAsync(await carol.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{orgX}", new { name = "Pwned" })));
        Assert.Equal("organization_not_found", await ErrorCodeAsync(await carol.PostAsync($"/api/v1/organizations/{orgX}/invitations", new { email = "carol2@example.org" })));
        Assert.Equal("organization_not_found", await ErrorCodeAsync(await carol.PostAsync($"/api/v1/organizations/{orgX}/workspaces/{workspaceX}/members", new { userId = carolId })));

        // A valid workspace id from organization X used under organization Y is rejected too.
        Assert.Equal("workspace_not_found", await ErrorCodeAsync(await carol.GetAsync($"/api/v1/organizations/{orgY}/workspaces/{workspaceX}/members")));
        Assert.Equal("workspace_not_found", await ErrorCodeAsync(await carol.PostAsync($"/api/v1/organizations/{orgY}/workspaces/{workspaceX}/members", new { userId = carolId })));

        // Alice cannot pull a non-member into her workspace.
        Assert.Equal("not_organization_member", await ErrorCodeAsync(await alice.PostAsync($"/api/v1/organizations/{orgX}/workspaces/{workspaceX}/members", new { userId = carolId })));

        // Nothing changed.
        Assert.Equal("X", (await JsonAsync(await alice.GetAsync($"/api/v1/organizations/{orgX}"))).GetProperty("name").GetString());
        Assert.Single((await JsonAsync(await alice.GetAsync($"/api/v1/organizations/{orgX}/workspaces/{workspaceX}/members"))).EnumerateArray());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Database_rejects_workspace_membership_across_organizations(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (alice, aliceId) = await SignedInAsync(api, "alice@example.org", "Alice");
        using var _ = alice;
        var orgX = (await JsonAsync(await alice.PostAsync("/api/v1/organizations", new { name = "X" }))).GetProperty("id").GetGuid();
        var orgY = (await JsonAsync(await alice.PostAsync("/api/v1/organizations", new { name = "Y" }))).GetProperty("id").GetGuid();

        await using var scope = api.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ChatXDbContext>();
        var workspaceInX = await context.Workspaces.FirstAsync(w => w.OrganizationId == orgX, Ct);
        var membershipInY = await context.OrganizationMembers.FirstAsync(m => m.OrganizationId == orgY && m.UserId == aliceId, Ct);

        // Bypass the domain factory to prove the composite foreign key itself enforces the rule.
        var forged = (WorkspaceMember)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(WorkspaceMember));
        context.Entry(forged).CurrentValues.SetValues(new Dictionary<string, object>
        {
            ["Id"] = Guid.NewGuid(),
            ["OrganizationId"] = orgY,
            ["WorkspaceId"] = workspaceInX.Id,
            ["UserId"] = membershipInY.UserId,
            ["Role"] = WorkspaceRole.Member,
            ["Status"] = MembershipStatus.Active,
            ["JoinedAtUtc"] = DateTimeOffset.UtcNow,
        });
        context.Add(forged);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Workspaces_are_visible_only_to_their_members_and_organization_administrators()
    {
        await using var db = new SqliteTestDatabase();
        await using var api = await StartAsync(db);
        var (admin, _) = await SignedInAsync(api, "admin@example.org", "Ada Admin");
        var (member, memberId) = await SignedInAsync(api, "member@example.org", "Max Member");
        using var _1 = admin;
        using var _2 = member;
        var orgId = (await JsonAsync(await admin.PostAsync("/api/v1/organizations", new { name = "Org" }))).GetProperty("id").GetGuid();
        var invitationId = (await JsonAsync(await admin.PostAsync($"/api/v1/organizations/{orgId}/invitations", new { email = "member@example.org" }))).GetProperty("id").GetGuid();
        await member.PostAsync($"/api/v1/organizations/invitations/{invitationId}/accept");
        var workspaceId = (await JsonAsync(await admin.PostAsync($"/api/v1/organizations/{orgId}/workspaces", new { name = "Design" }))).GetProperty("id").GetGuid();

        Assert.Empty((await JsonAsync(await member.GetAsync($"/api/v1/organizations/{orgId}/workspaces"))).EnumerateArray());
        Assert.Equal("workspace_not_found", await ErrorCodeAsync(await member.GetAsync($"/api/v1/organizations/{orgId}/workspaces/{workspaceId}/members")));

        AssertStatus(HttpStatusCode.OK, await admin.PostAsync($"/api/v1/organizations/{orgId}/workspaces/{workspaceId}/members", new { userId = memberId }));
        Assert.Equal("already_member", await ErrorCodeAsync(await admin.PostAsync($"/api/v1/organizations/{orgId}/workspaces/{workspaceId}/members", new { userId = memberId })));
        var visible = Assert.Single((await JsonAsync(await member.GetAsync($"/api/v1/organizations/{orgId}/workspaces"))).EnumerateArray());
        Assert.Equal("member", visible.GetProperty("myRole").GetString());

        // Leaving the organization also ends workspace access.
        AssertStatus(HttpStatusCode.NoContent, await member.SendAsync(HttpMethod.Delete, $"/api/v1/organizations/{orgId}/members/{memberId}"));
        var remaining = await JsonAsync(await admin.GetAsync($"/api/v1/organizations/{orgId}/workspaces/{workspaceId}/members"));
        Assert.Single(remaining.EnumerateArray());
    }

    [Fact]
    public async Task Organization_creation_can_be_disabled()
    {
        await using var db = new SqliteTestDatabase();
        await using var api = await StartAsync(db, new() { ["Organizations:AllowCreation"] = "false" });
        var (user, _) = await SignedInAsync(api, "u@example.org", "U");
        using var _1 = user;

        Assert.Equal((HttpStatusCode.Forbidden, "organization_creation_disabled"), await StatusAndCode(user.PostAsync("/api/v1/organizations", new { name = "Nope" })));
    }

    private const System.Text.Json.JsonValueKind JsonValueKindNull = System.Text.Json.JsonValueKind.Null;

    internal static async Task<(HttpStatusCode, string?)> StatusAndCode(Task<HttpResponseMessage> request)
    {
        var response = await request;
        return (response.StatusCode, await ErrorCodeAsync(response));
    }
}
