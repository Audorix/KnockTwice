using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Audorix.KnockTwice.IntegrationTests.Support;
using static Audorix.KnockTwice.IntegrationTests.Support.ApiSession;
using static Audorix.KnockTwice.IntegrationTests.Support.TestUsers;

namespace Audorix.KnockTwice.IntegrationTests;

/// <summary>
/// Business communication (Phase 11, owner decisions of 2026-11-23): public and private channels in
/// workspaces with history from the join; teams as member groups; channel-scoped guests; support
/// agents with an inbox of website conversations; organization-specific configuration.
/// </summary>
public sealed class BusinessTests
{
    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Channels_follow_workspace_membership_and_visibility(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (carl, _) = await SignedInAsync(api, "carl@example.org", "Carl");
        var (dan, danId) = await SignedInAsync(api, "dan@example.org", "Dan");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = carl;
        using var _4 = dan;
        var org = await CreateOrganizationAsync(ana, (ben, "ben@example.org"), (carl, "carl@example.org"), (dan, "dan@example.org"));
        var ws = await DefaultWorkspaceAsync(ana, org);
        await AddToWorkspaceAsync(ana, org, ws, benId, danId);
        var channels = $"/api/v1/organizations/{org}/workspaces/{ws}/channels";

        // A workspace member creates a public channel and posts before Dan joins.
        var sales = await CreateChannelAsync(ben, channels, new { name = "sales", visibility = "public" });
        var salesId = sales.GetProperty("channel").GetProperty("id").GetGuid();
        Assert.Equal(("manager", true), (sales.GetProperty("channel").GetProperty("myRole").GetString(), sales.GetProperty("channel").GetProperty("canManage").GetBoolean()));
        AssertStatus(HttpStatusCode.Created, await ben.PostAsync($"/api/v1/conversations/{salesId}/messages", new { clientMessageId = "b1", body = "Before Dan" }));
        Assert.Equal("channel_name_taken", await ErrorCodeAsync(await ben.PostAsync(channels, new { name = "Sales" })));

        // Carl is in the organization but not the workspace: no channels at all.
        Assert.Equal("workspace_not_found", await ErrorCodeAsync(await carl.GetAsync(channels)));
        Assert.Equal("channel_not_found", await ErrorCodeAsync(await carl.GetAsync($"/api/v1/channels/{salesId}")));
        Assert.Equal("channel_not_found", await ErrorCodeAsync(await carl.PostAsync($"/api/v1/channels/{salesId}/join")));

        // A private channel is invisible to workspace members outside it, but organization owners see it.
        var leads = await CreateChannelAsync(ana, channels, new { name = "leads", visibility = "private", userIds = new[] { benId } });
        var leadsId = leads.GetProperty("channel").GetProperty("id").GetGuid();
        Assert.Equal(["leads", "sales"], (await JsonAsync(await ana.GetAsync(channels))).EnumerateArray().Select(c => c.GetProperty("name").GetString()));
        Assert.Equal(["sales"], (await JsonAsync(await dan.GetAsync(channels))).EnumerateArray().Select(c => c.GetProperty("name").GetString()));
        Assert.Equal("channel_not_found", await ErrorCodeAsync(await dan.GetAsync($"/api/v1/channels/{leadsId}")));
        Assert.Equal("channel_not_found", await ErrorCodeAsync(await dan.PostAsync($"/api/v1/channels/{leadsId}/join")));

        // Dan joins the public channel; history starts at his join.
        var joined = await JsonAsync(await dan.PostAsync($"/api/v1/channels/{salesId}/join"));
        Assert.Equal(("member", 2), (joined.GetProperty("channel").GetProperty("myRole").GetString(), joined.GetProperty("channel").GetProperty("memberCount").GetInt32()));
        var history = await JsonAsync(await dan.GetAsync($"/api/v1/conversations/{salesId}/history"));
        Assert.DoesNotContain(history.GetProperty("items").EnumerateArray(), i => i.GetProperty("body").ValueKind == JsonValueKind.String && i.GetProperty("body").GetString() == "Before Dan");
        var summary = (await JsonAsync(await dan.GetAsync("/api/v1/conversations"))).EnumerateArray().Single();
        Assert.Equal(("channel", "public", ws), (summary.GetProperty("type").GetString(), summary.GetProperty("visibility").GetString(), summary.GetProperty("workspaceId").GetGuid()));

        // Members cannot manage; the channel manager moderates messages.
        Assert.Equal(HttpStatusCode.Forbidden, (await dan.SendAsync(HttpMethod.Put, $"/api/v1/channels/{salesId}", new { name = "mine" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await dan.PostAsync($"/api/v1/channels/{salesId}/members", new { userIds = new[] { benId } })).StatusCode);
        var danMessage = await JsonAsync(await dan.PostAsync($"/api/v1/conversations/{salesId}/messages", new { clientMessageId = "d1", body = "Spam" }));
        AssertStatus(HttpStatusCode.NoContent, await ben.SendAsync(HttpMethod.Delete, $"/api/v1/conversations/{salesId}/messages/{danMessage.GetProperty("id").GetGuid()}"));

        // Workspace administrators manage every channel, even private ones they are not in.
        var renamed = await JsonAsync(await ana.SendAsync(HttpMethod.Put, $"/api/v1/channels/{salesId}", new { name = "sales-emea", visibility = "private" }));
        Assert.Equal(("sales-emea", "private"), (renamed.GetProperty("channel").GetProperty("name").GetString(), renamed.GetProperty("channel").GetProperty("visibility").GetString()));

        // Leaving the workspace ends its channels.
        AssertStatus(HttpStatusCode.NoContent, await ana.SendAsync(HttpMethod.Delete, $"/api/v1/organizations/{org}/workspaces/{ws}/members/{danId}"));
        Assert.Equal("conversation_not_found", await ErrorCodeAsync(await dan.GetAsync($"/api/v1/conversations/{salesId}/history")));
        Assert.Empty((await JsonAsync(await dan.GetAsync("/api/v1/conversations"))).EnumerateArray());

        // Organization configuration applies to its own organization only.
        var settings = await JsonAsync(await ana.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{org}/settings", new { channelCreation = "administrators" }));
        Assert.Equal("administrators", settings.GetProperty("channelCreation").GetString());
        Assert.Equal("channel_creation_restricted", await ErrorCodeAsync(await ben.PostAsync(channels, new { name = "ideas" })));
        Assert.Equal(HttpStatusCode.Forbidden, (await ben.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{org}/settings", new { channelCreation = "members" })).StatusCode);
        var benOrg = (await JsonAsync(await ben.PostAsync("/api/v1/organizations", new { name = "Ben Co" }))).GetProperty("id").GetGuid();
        var benWs = await DefaultWorkspaceAsync(ben, benOrg);
        Assert.Equal("members", (await JsonAsync(await ben.GetAsync($"/api/v1/organizations/{benOrg}"))).GetProperty("channelCreation").GetString());
        await CreateChannelAsync(ben, $"/api/v1/organizations/{benOrg}/workspaces/{benWs}/channels", new { name = "ideas" });
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Workspace_channel_permissions_override_legacy_policy_and_channel_manager_roles(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (owner, _) = await SignedInAsync(api, "owner@example.org", "Owner");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (dana, danaId) = await SignedInAsync(api, "dana@example.org", "Dana");
        using var _1 = owner;
        using var _2 = ben;
        using var _3 = dana;
        var orgId = await CreateOrganizationAsync(owner, (ben, "ben@example.org"), (dana, "dana@example.org"));
        var workspaceId = await DefaultWorkspaceAsync(owner, orgId);
        await AddToWorkspaceAsync(owner, orgId, workspaceId, benId, danaId);
        var channels = $"/api/v1/organizations/{orgId}/workspaces/{workspaceId}/channels";
        var access = $"/api/v1/organizations/{orgId}/workspaces/{workspaceId}/access-control";
        AssertStatus(HttpStatusCode.OK, await owner.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{orgId}/settings", new { channelCreation = "administrators" }));
        Assert.Equal("channel_creation_restricted", await ErrorCodeAsync(await ben.PostAsync(channels, new { name = "blocked-before-config" })));

        async Task<(Guid Role, Guid Group)> CreateWorkspaceRole(string roleName, string effect, string permission)
        {
            var roleResponse = await owner.PostAsync($"{access}/roles", new { name = roleName, permissions = new[] { new { key = permission, effect } } });
            AssertStatus(HttpStatusCode.Created, roleResponse);
            var roleId = (await JsonAsync(roleResponse)).GetProperty("id").GetGuid();
            var groupResponse = await owner.PostAsync($"{access}/groups", new { name = $"{roleName} group", description = "" });
            AssertStatus(HttpStatusCode.Created, groupResponse);
            var groupId = (await JsonAsync(groupResponse)).GetProperty("id").GetGuid();
            AssertStatus(HttpStatusCode.OK, await owner.PostAsync($"{access}/roles/{roleId}/groups/{groupId}"));
            return (roleId, groupId);
        }

        var (_, createGroup) = await CreateWorkspaceRole("Channel creators", "allow", "channels.create");
        AssertStatus(HttpStatusCode.OK, await owner.PostAsync($"{access}/groups/{createGroup}/members/{benId}"));
        var created = await JsonAsync(await ben.PostAsync(channels, new { name = "permitted-by-group" }));
        var channelId = created.GetProperty("channel").GetProperty("id").GetGuid();

        var (_, manageGroup) = await CreateWorkspaceRole("Channel managers", "allow", "channels.manage");
        AssertStatus(HttpStatusCode.OK, await owner.PostAsync($"{access}/groups/{manageGroup}/members/{danaId}"));
        AssertStatus(HttpStatusCode.OK, await owner.PostAsync($"{access}/groups/{manageGroup}/members/{benId}"));
        var managed = await JsonAsync(await dana.SendAsync(HttpMethod.Put, $"/api/v1/channels/{channelId}", new { name = "managed-by-group" }));
        Assert.Equal("managed-by-group", managed.GetProperty("channel").GetProperty("name").GetString());

        var (denyRole, denyGroup) = await CreateWorkspaceRole("Channel manager restriction", "deny", "channels.manage");
        AssertStatus(HttpStatusCode.OK, await owner.PostAsync($"{access}/groups/{denyGroup}/members/{benId}"));
        AssertStatus(HttpStatusCode.OK, await owner.PostAsync($"{access}/roles/{denyRole}/groups/{denyGroup}"));
        Assert.Equal(HttpStatusCode.Forbidden, (await ben.SendAsync(HttpMethod.Put, $"/api/v1/channels/{channelId}", new { name = "denied" })).StatusCode);

        // The workspace access boundary prevents policies from granting privileges in another workspace.
        var otherWorkspaceId = (await JsonAsync(await owner.PostAsync($"/api/v1/organizations/{orgId}/workspaces", new { name = "Other" }))).GetProperty("id").GetGuid();
        await AddToWorkspaceAsync(owner, orgId, otherWorkspaceId, benId);
        Assert.Equal("channel_creation_restricted", await ErrorCodeAsync(await ben.PostAsync($"/api/v1/organizations/{orgId}/workspaces/{otherWorkspaceId}/channels", new { name = "out-of-scope" })));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Managers_archive_a_channel_read_only_and_restore_it(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, _) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (dan, danId) = await SignedInAsync(api, "dan@example.org", "Dan");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = dan;
        var org = await CreateOrganizationAsync(ana, (ben, "ben@example.org"), (dan, "dan@example.org"));
        var ws = await DefaultWorkspaceAsync(ana, org);
        await AddToWorkspaceAsync(ana, org, ws, benId, danId);
        var channels = $"/api/v1/organizations/{org}/workspaces/{ws}/channels";
        var id = (await CreateChannelAsync(ben, channels, new { name = "launch", visibility = "public" })).GetProperty("channel").GetProperty("id").GetGuid();
        var message = await JsonAsync(await ben.PostAsync($"/api/v1/conversations/{id}/messages", new { clientMessageId = "b1", body = "Done!" }));
        var messageId = message.GetProperty("id").GetGuid();

        // Only managers archive.
        Assert.Equal(HttpStatusCode.NotFound, (await dan.SendAsync(HttpMethod.Put, $"/api/v1/channels/{Guid.NewGuid()}/archived", new { archived = true })).StatusCode);
        AssertStatus(HttpStatusCode.OK, await dan.PostAsync($"/api/v1/channels/{id}/join"));
        Assert.Equal(HttpStatusCode.Forbidden, (await dan.SendAsync(HttpMethod.Put, $"/api/v1/channels/{id}/archived", new { archived = true })).StatusCode);
        var archived = await JsonAsync(await ben.SendAsync(HttpMethod.Put, $"/api/v1/channels/{id}/archived", new { archived = true }));
        Assert.Equal(JsonValueKind.String, archived.GetProperty("channel").GetProperty("archivedAtUtc").ValueKind);
        AssertStatus(HttpStatusCode.OK, await ben.SendAsync(HttpMethod.Put, $"/api/v1/channels/{id}/archived", new { archived = true })); // no change

        // Read-only: history stays, no posting, editing, reacting, joining or adding.
        var history = (await JsonAsync(await dan.GetAsync($"/api/v1/conversations/{id}/history"))).GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(history, i => i.GetProperty("kind").GetString() == "archived" && i.GetProperty("text").GetString() == "archived");
        Assert.Equal("channel_archived", await ErrorCodeAsync(await dan.PostAsync($"/api/v1/conversations/{id}/messages", new { clientMessageId = "d1", body = "Hi" })));
        Assert.Equal("channel_archived", await ErrorCodeAsync(await ben.SendAsync(HttpMethod.Put, $"/api/v1/conversations/{id}/messages/{messageId}", new { body = "Edited" })));
        Assert.Equal("channel_archived", await ErrorCodeAsync(await dan.SendAsync(HttpMethod.Put, $"/api/v1/conversations/{id}/messages/{messageId}/reactions", new { emoji = "👍", reacted = true })));
        AssertStatus(HttpStatusCode.NoContent, await dan.PostAsync($"/api/v1/channels/{id}/leave"));
        Assert.Equal("channel_archived", await ErrorCodeAsync(await dan.PostAsync($"/api/v1/channels/{id}/join")));
        Assert.Equal("channel_archived", await ErrorCodeAsync(await ben.PostAsync($"/api/v1/channels/{id}/members", new { userIds = new[] { danId } })));
        var listed = (await JsonAsync(await ana.GetAsync(channels))).EnumerateArray().Single();
        Assert.Equal(JsonValueKind.String, listed.GetProperty("archivedAtUtc").ValueKind);
        var summary = (await JsonAsync(await ben.GetAsync("/api/v1/conversations"))).EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == id);
        Assert.Equal(JsonValueKind.String, summary.GetProperty("archivedAtUtc").ValueKind);

        // Restored: everything works again.
        var restored = await JsonAsync(await ana.SendAsync(HttpMethod.Put, $"/api/v1/channels/{id}/archived", new { archived = false }));
        Assert.Equal(JsonValueKind.Null, restored.GetProperty("channel").GetProperty("archivedAtUtc").ValueKind);
        AssertStatus(HttpStatusCode.OK, await dan.PostAsync($"/api/v1/channels/{id}/join"));
        AssertStatus(HttpStatusCode.Created, await dan.PostAsync($"/api/v1/conversations/{id}/messages", new { clientMessageId = "d2", body = "Back" }));
    }

    [Fact]
    public async Task Teams_bring_their_members_into_linked_channels_but_grant_no_administration()
    {
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db);
        var (ana, _) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (dan, danId) = await SignedInAsync(api, "dan@example.org", "Dan");
        var (eve, eveId) = await SignedInAsync(api, "eve@example.org", "Eve");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = dan;
        using var _4 = eve;
        var org = await CreateOrganizationAsync(ana, (ben, "ben@example.org"), (dan, "dan@example.org"), (eve, "eve@example.org"));
        var ws = await DefaultWorkspaceAsync(ana, org);
        await AddToWorkspaceAsync(ana, org, ws, benId, danId);
        var teams = $"/api/v1/organizations/{org}/workspaces/{ws}/teams";

        Assert.Equal(HttpStatusCode.Forbidden, (await ben.PostAsync(teams, new { name = "Support" })).StatusCode);
        var team = await JsonAsync(await ana.PostAsync(teams, new { name = "Support", description = "First line" }));
        var teamId = team.GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.OK, await ana.PostAsync($"{teams}/{teamId}/members", new { userId = benId, role = "manager" }));

        // The team manager manages the team's members; only workspace members can join.
        AssertStatus(HttpStatusCode.OK, await ben.PostAsync($"{teams}/{teamId}/members", new { userId = danId }));
        Assert.Contains("not_workspace_member", await (await ben.PostAsync($"{teams}/{teamId}/members", new { userId = eveId })).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // A channel created with the team gets its members; later team members follow.
        var help = await CreateChannelAsync(ana, $"/api/v1/organizations/{org}/workspaces/{ws}/channels",
            new { name = "help", visibility = "private", teamIds = new[] { teamId } });
        var helpId = help.GetProperty("channel").GetProperty("id").GetGuid();
        Assert.Equal(["Support"], help.GetProperty("channel").GetProperty("teams").EnumerateArray().Select(t => t.GetProperty("name").GetString()));
        Assert.Equal(3, help.GetProperty("channel").GetProperty("memberCount").GetInt32());

        await AddToWorkspaceAsync(ana, org, ws, eveId);
        AssertStatus(HttpStatusCode.OK, await ben.PostAsync($"{teams}/{teamId}/members", new { userId = eveId }));
        var detail = await JsonAsync(await eve.GetAsync($"/api/v1/channels/{helpId}"));
        Assert.Equal("member", detail.GetProperty("channel").GetProperty("myRole").GetString());

        // Managing a team does not make Ben a channel manager.
        Assert.Equal(HttpStatusCode.Forbidden, (await ben.SendAsync(HttpMethod.Put, $"/api/v1/channels/{helpId}", new { name = "ours" })).StatusCode);

        // Leaving the team keeps the channel; unlinking stops new members from joining.
        AssertStatus(HttpStatusCode.NoContent, await dan.SendAsync(HttpMethod.Delete, $"{teams}/{teamId}/members/{danId}"));
        AssertStatus(HttpStatusCode.OK, await dan.GetAsync($"/api/v1/conversations/{helpId}/history"));
        var unlinked = await JsonAsync(await ana.SendAsync(HttpMethod.Delete, $"/api/v1/channels/{helpId}/teams/{teamId}"));
        Assert.Empty(unlinked.GetProperty("channel").GetProperty("teams").EnumerateArray());
        AssertStatus(HttpStatusCode.OK, await ben.PostAsync($"{teams}/{teamId}/members", new { userId = danId }));
        Assert.Equal(4, (await JsonAsync(await ana.GetAsync($"/api/v1/channels/{helpId}"))).GetProperty("channel").GetProperty("memberCount").GetInt32());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Guests_see_only_the_channels_they_were_invited_to(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (gina, _) = await SignedInAsync(api, "gina@partner.example", "Gina");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = gina;
        var org = await CreateOrganizationAsync(ana, (ben, "ben@example.org"));
        var ws = await DefaultWorkspaceAsync(ana, org);
        await AddToWorkspaceAsync(ana, org, ws, benId);
        var channels = $"/api/v1/organizations/{org}/workspaces/{ws}/channels";
        var partners = (await CreateChannelAsync(ana, channels, new { name = "partners", visibility = "private", userIds = new[] { benId } }))
            .GetProperty("channel").GetProperty("id").GetGuid();
        var general = (await CreateChannelAsync(ana, channels, new { name = "general", visibility = "public" })).GetProperty("channel").GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.Created, await ana.PostAsync($"/api/v1/conversations/{partners}/messages", new { clientMessageId = "a0", body = "Internal note" }));

        // Only those who manage members invite; the invitee accepts.
        Assert.Equal(HttpStatusCode.Forbidden, (await ben.PostAsync($"/api/v1/organizations/{org}/guests/invitations", new { channelId = partners, email = "gina@partner.example" })).StatusCode);
        AssertStatus(HttpStatusCode.Created, await ana.PostAsync($"/api/v1/organizations/{org}/guests/invitations", new { channelId = partners, email = "gina@partner.example" }));
        var mine = (await JsonAsync(await gina.GetAsync("/api/v1/guest-invitations"))).EnumerateArray().Single();
        Assert.Equal(("Acme", "partners"), (mine.GetProperty("organizationName").GetString(), mine.GetProperty("channelName").GetString()));
        var invitationExpiresAt = mine.GetProperty("expiresAtUtc").GetDateTimeOffset();
        Assert.InRange(invitationExpiresAt, DateTimeOffset.UtcNow.AddDays(13), DateTimeOffset.UtcNow.AddDays(15));
        var accepted = await JsonAsync(await gina.PostAsync($"/api/v1/guest-invitations/{mine.GetProperty("id").GetGuid()}/accept"));
        Assert.Equal(partners, accepted.GetProperty("conversationId").GetGuid());

        // The guest takes part in that channel (history from the join) ...
        var list = (await JsonAsync(await gina.GetAsync("/api/v1/conversations"))).EnumerateArray().Single();
        Assert.Equal(("channel", "guest", "Acme"), (list.GetProperty("type").GetString(), list.GetProperty("myRole").GetString(), list.GetProperty("organizationName").GetString()));
        Assert.DoesNotContain("Internal note", await (await gina.GetAsync($"/api/v1/conversations/{partners}/history")).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        AssertStatus(HttpStatusCode.Created, await gina.PostAsync($"/api/v1/conversations/{partners}/messages", new { clientMessageId = "g1", body = "Hello from Partner" }));
        var members = await JsonAsync(await ana.GetAsync($"/api/v1/channels/{partners}"));
        Assert.Contains(members.GetProperty("members").EnumerateArray(), m => m.GetProperty("user").GetProperty("displayName").GetString() == "Gina" && m.GetProperty("role").GetString() == "guest");

        // ... and nothing else of the organization.
        Assert.Equal("organization_not_found", await ErrorCodeAsync(await gina.GetAsync($"/api/v1/organizations/{org}/members")));
        Assert.Equal("workspace_not_found", await ErrorCodeAsync(await gina.GetAsync(channels)));
        Assert.Equal("channel_not_found", await ErrorCodeAsync(await gina.GetAsync($"/api/v1/channels/{general}")));
        Assert.Equal("organization_not_found", await ErrorCodeAsync(await gina.PostAsync("/api/v1/conversations/direct", new { userId = anaId, organizationId = org })));
        Assert.Equal(HttpStatusCode.Forbidden, (await gina.PostAsync($"/api/v1/channels/{partners}/members", new { userIds = new[] { benId } })).StatusCode);
        Assert.DoesNotContain("Gina", await (await ana.GetAsync($"/api/v1/organizations/{org}/members")).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // Switching guests off suspends them; switching back restores access.
        AssertStatus(HttpStatusCode.OK, await ana.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{org}/settings", new { guestsEnabled = false }));
        Assert.Equal("conversation_not_found", await ErrorCodeAsync(await gina.GetAsync($"/api/v1/conversations/{partners}/history")));
        Assert.Empty((await JsonAsync(await gina.GetAsync("/api/v1/conversations"))).EnumerateArray());
        Assert.Equal("guests_disabled", await ErrorCodeAsync(await ana.PostAsync($"/api/v1/organizations/{org}/guests/invitations", new { channelId = partners, email = "x@partner.example" })));
        AssertStatus(HttpStatusCode.OK, await ana.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{org}/settings", new { guestsEnabled = true }));
        AssertStatus(HttpStatusCode.OK, await gina.GetAsync($"/api/v1/conversations/{partners}/history"));

        // Members join as members, not guests.
        AssertStatus(HttpStatusCode.Created, await ana.PostAsync($"/api/v1/organizations/{org}/guests/invitations", new { channelId = general, email = "ben@example.org" }));
        var benInvite = (await JsonAsync(await ben.GetAsync("/api/v1/guest-invitations"))).EnumerateArray().Single().GetProperty("id").GetGuid();
        Assert.Equal("already_member", await ErrorCodeAsync(await ben.PostAsync($"/api/v1/guest-invitations/{benInvite}/accept")));

        // Removing the guest ends their channels.
        var guests = (await JsonAsync(await ana.GetAsync($"/api/v1/organizations/{org}/guests"))).EnumerateArray().Single();
        Assert.InRange(guests.GetProperty("accessExpiresAtUtc").GetDateTimeOffset(), DateTimeOffset.UtcNow.AddDays(89), DateTimeOffset.UtcNow.AddDays(91));
        Assert.Equal(["partners"], guests.GetProperty("channels").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
        AssertStatus(HttpStatusCode.NoContent, await ana.SendAsync(HttpMethod.Delete, $"/api/v1/organizations/{org}/guests/{guests.GetProperty("user").GetProperty("id").GetGuid()}"));
        Assert.Equal("conversation_not_found", await ErrorCodeAsync(await gina.GetAsync($"/api/v1/conversations/{partners}/history")));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Support_agents_work_the_website_inbox(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, _) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (carl, carlId) = await SignedInAsync(api, "carl@example.org", "Carl");
        var (dan, danId) = await SignedInAsync(api, "dan@example.org", "Dan");
        var (zed, _) = await SignedInAsync(api, "zed@example.org", "Zed");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = carl;
        using var _4 = dan;
        using var _5 = zed;
        var org = await CreateOrganizationAsync(ana, (ben, "ben@example.org"), (carl, "carl@example.org"), (dan, "dan@example.org"));

        // A widget answered by Ben; a visitor writes.
        var widget = (await JsonAsync(await ana.PostAsync($"/api/v1/organizations/{org}/widgets", new { name = "Shop" }))).GetProperty("widget").GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.OK, await ana.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{org}/widgets/{widget}",
            new { allowedOrigins = new[] { "https://shop.example.org" }, responderIds = new[] { benId }, enabled = true }));
        using var visitor = api.CreateHttpsClient();
        var session = await JsonAsync(await visitor.PostAsJsonAsync($"/api/v1/widget/{widget}/sessions", new { parentOrigin = "https://shop.example.org" }, Ct));
        visitor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("token").GetString());
        AssertStatus(HttpStatusCode.Created, await visitor.PostAsJsonAsync("/api/v1/widget/visitor/messages", new { clientMessageId = "v1", body = "Where is my order?" }, Ct));

        var inbox = $"/api/v1/organizations/{org}/support";
        AssertStatus(HttpStatusCode.OK, await ana.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{org}/members/{carlId}/support-agent", new { supportAgent = true }));
        var item = (await JsonAsync(await carl.GetAsync(inbox))).EnumerateArray().Single();
        var conversationId = item.GetProperty("conversationId").GetGuid();
        Assert.Equal(("open", false, "Shop"), (item.GetProperty("status").GetString(), item.GetProperty("participating").GetBoolean(), item.GetProperty("widgetName").GetString()));
        Assert.True((await JsonAsync(await carl.GetAsync($"/api/v1/organizations/{org}"))).GetProperty("supportAgent").GetBoolean());

        // Responders see their own conversations; other members see none; outsiders nothing.
        Assert.Single((await JsonAsync(await ben.GetAsync(inbox))).EnumerateArray());
        Assert.Empty((await JsonAsync(await dan.GetAsync(inbox))).EnumerateArray());
        Assert.Equal("organization_not_found", await ErrorCodeAsync(await zed.GetAsync(inbox)));

        // Claiming joins with the whole history.
        var claimed = await JsonAsync(await carl.PostAsync($"/api/v1/support/conversations/{conversationId}/claim"));
        Assert.Equal(("Carl", true), (claimed.GetProperty("assignedTo").GetProperty("displayName").GetString(), claimed.GetProperty("participating").GetBoolean()));
        Assert.Contains("Where is my order?", await (await carl.GetAsync($"/api/v1/conversations/{conversationId}/history")).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Single((await JsonAsync(await carl.GetAsync($"{inbox}?mine=true"))).EnumerateArray());

        // Responders cannot assign others; nobody can assign to someone who is not support staff.
        Assert.Equal(HttpStatusCode.Forbidden, (await ben.SendAsync(HttpMethod.Put, $"/api/v1/support/conversations/{conversationId}/assignee", new { assigneeId = carlId })).StatusCode);
        Assert.Contains("assignee_not_agent",
            await (await carl.SendAsync(HttpMethod.Put, $"/api/v1/support/conversations/{conversationId}/assignee", new { assigneeId = danId })).Content.ReadAsStringAsync(Ct),
            StringComparison.Ordinal);

        // Closing hides it from the open inbox; the visitor writing again reopens it.
        var closed = await JsonAsync(await ben.SendAsync(HttpMethod.Put, $"/api/v1/support/conversations/{conversationId}/status", new { status = "closed" }));
        Assert.Equal("closed", closed.GetProperty("status").GetString());
        Assert.Empty((await JsonAsync(await carl.GetAsync($"{inbox}?status=open"))).EnumerateArray());
        Assert.Equal("closed", (await JsonAsync(await ben.GetAsync("/api/v1/conversations"))).EnumerateArray().Single().GetProperty("supportStatus").GetString());
        AssertStatus(HttpStatusCode.Created, await visitor.PostAsJsonAsync("/api/v1/widget/visitor/messages", new { clientMessageId = "v2", body = "Hello again" }, Ct));
        Assert.Single((await JsonAsync(await carl.GetAsync($"{inbox}?status=open"))).EnumerateArray());

        // Status and assignment are recorded in the history the team sees, not in the visitor's.
        var kinds = (await JsonAsync(await carl.GetAsync($"/api/v1/conversations/{conversationId}/history"))).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("kind").GetString()).ToList();
        Assert.Contains("assigned", kinds);
        Assert.Equal(2, kinds.Count(k => k == "status_changed"));
    }

    [Fact]
    public async Task Organization_support_permissions_come_from_group_roles_and_deny_overrides_allow()
    {
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (carl, carlId) = await SignedInAsync(api, "carl@example.org", "Carl");
        using var _1 = ana;
        using var _2 = carl;
        var organizationId = await CreateOrganizationAsync(ana, (carl, "carl@example.org"));
        var widget = (await JsonAsync(await ana.PostAsync($"/api/v1/organizations/{organizationId}/widgets", new { name = "Support" })))
            .GetProperty("widget").GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.OK, await ana.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{organizationId}/widgets/{widget}",
            new { allowedOrigins = new[] { "https://shop.example.org" }, responderIds = new[] { anaId }, enabled = true }));
        using var visitor = api.CreateHttpsClient();
        var session = await JsonAsync(await visitor.PostAsJsonAsync($"/api/v1/widget/{widget}/sessions", new { parentOrigin = "https://shop.example.org" }, Ct));
        visitor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("token").GetString());
        AssertStatus(HttpStatusCode.Created, await visitor.PostAsJsonAsync("/api/v1/widget/visitor/messages", new { clientMessageId = "v1", body = "Need help" }, Ct));
        var inbox = $"/api/v1/organizations/{organizationId}/support";
        var conversationId = (await JsonAsync(await ana.GetAsync(inbox))).EnumerateArray().Single().GetProperty("conversationId").GetGuid();

        // Existing behavior remains until this organization configures a support decision.
        Assert.Empty((await JsonAsync(await carl.GetAsync(inbox))).EnumerateArray());
        Assert.Equal("conversation_not_found", await ErrorCodeAsync(await carl.PostAsync($"/api/v1/support/conversations/{conversationId}/claim")));

        var access = $"/api/v1/organizations/{organizationId}/access-control";
        var readerRoleResponse = await ana.PostAsync(access + "/roles", new
        {
            name = "Support operators",
            permissions = new[]
            {
                new { key = "support.access", effect = "allow" },
                new { key = "support.cases.read", effect = "allow" },
            },
        });
        AssertStatus(HttpStatusCode.Created, readerRoleResponse, await readerRoleResponse.Content.ReadAsStringAsync(Ct));
        var roleId = (await JsonAsync(readerRoleResponse)).GetProperty("id").GetGuid();
        var groupResponse = await ana.PostAsync(access + "/groups", new { name = "Support team" });
        AssertStatus(HttpStatusCode.Created, groupResponse);
        var groupId = (await JsonAsync(groupResponse)).GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.OK, await ana.PostAsync($"{access}/roles/{roleId}/groups/{groupId}"));
        AssertStatus(HttpStatusCode.OK, await ana.PostAsync($"{access}/groups/{groupId}/members/{carlId}"));
        Assert.Single((await JsonAsync(await carl.GetAsync(inbox))).EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await carl.PostAsync($"/api/v1/support/conversations/{conversationId}/claim")).StatusCode);

        var managerResponse = await ana.PostAsync(access + "/roles", new
        {
            name = "Support case managers",
            permissions = new[] { new { key = "support.cases.manage", effect = "allow" } },
        });
        AssertStatus(HttpStatusCode.Created, managerResponse, await managerResponse.Content.ReadAsStringAsync(Ct));
        var managerRoleId = (await JsonAsync(managerResponse)).GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.OK, await ana.PostAsync($"{access}/roles/{managerRoleId}/groups/{groupId}"));
        AssertStatus(HttpStatusCode.OK, await carl.PostAsync($"/api/v1/support/conversations/{conversationId}/claim"));

        var denyResponse = await ana.PostAsync(access + "/roles", new
        {
            name = "Support restricted",
            permissions = new[] { new { key = "support.access", effect = "deny" } },
        });
        AssertStatus(HttpStatusCode.Created, denyResponse, await denyResponse.Content.ReadAsStringAsync(Ct));
        var denyRoleId = (await JsonAsync(denyResponse)).GetProperty("id").GetGuid();
        var denyGroupResponse = await ana.PostAsync(access + "/groups", new { name = "Support restrictions" });
        AssertStatus(HttpStatusCode.Created, denyGroupResponse);
        var denyGroupId = (await JsonAsync(denyGroupResponse)).GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.OK, await ana.PostAsync($"{access}/roles/{denyRoleId}/groups/{denyGroupId}"));
        AssertStatus(HttpStatusCode.OK, await ana.PostAsync($"{access}/groups/{denyGroupId}/members/{carlId}"));
        Assert.Equal(HttpStatusCode.Forbidden, (await carl.GetAsync(inbox)).StatusCode);
        Assert.Equal("conversation_not_found", await ErrorCodeAsync(await carl.GetAsync($"/api/v1/support/conversations/{conversationId}/history")));
        Assert.Equal("conversation_not_found", await ErrorCodeAsync(await carl.GetAsync($"/api/v1/conversations/{conversationId}/history")));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Support_inbox_pages_subjects_notes_and_pending(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, _) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (carl, carlId) = await SignedInAsync(api, "carl@example.org", "Carl");
        var (dan, _) = await SignedInAsync(api, "dan@example.org", "Dan");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = carl;
        using var _4 = dan;
        var org = await CreateOrganizationAsync(ana, (ben, "ben@example.org"), (carl, "carl@example.org"), (dan, "dan@example.org"));
        AssertStatus(HttpStatusCode.OK, await ana.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{org}/members/{carlId}/support-agent", new { supportAgent = true }));

        // Two customers write through a widget Ben answers.
        var widget = (await JsonAsync(await ana.PostAsync($"/api/v1/organizations/{org}/widgets", new { name = "Website chat" }))).GetProperty("widget").GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.OK, await ana.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{org}/widgets/{widget}",
            new { allowedOrigins = new[] { "https://shop.example.org" }, responderIds = new[] { benId }, enabled = true }));
        async Task<HttpClient> CustomerAsync(string body)
        {
            var client = api.CreateHttpsClient();
            var session = await JsonAsync(await client.PostAsJsonAsync($"/api/v1/widget/{widget}/sessions", new { parentOrigin = "https://shop.example.org" }, Ct));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("token").GetString());
            AssertStatus(HttpStatusCode.Created, await client.PostAsJsonAsync("/api/v1/widget/visitor/messages", new { clientMessageId = "c1", body }, Ct));
            return client;
        }

        using var first = await CustomerAsync("Where is my order?\nIt was due on Monday.");
        using var second = await CustomerAsync("Can I change my address?");
        var inbox = $"/api/v1/organizations/{org}/support/inbox";

        // A page with totals per status, the first line as the title and the newest message as the preview.
        var page = await JsonAsync(await carl.GetAsync(inbox));
        Assert.Equal((2, 2, 0, 0, 2), (page.GetProperty("total").GetInt32(), page.GetProperty("counts").GetProperty("open").GetInt32(),
            page.GetProperty("counts").GetProperty("pending").GetInt32(), page.GetProperty("counts").GetProperty("closed").GetInt32(),
            page.GetProperty("counts").GetProperty("all").GetInt32()));
        Assert.Equal(2, page.GetProperty("counts").GetProperty("unassignedOpen").GetInt32());
        var oldest = page.GetProperty("items")[0];
        Assert.Equal("Where is my order?", oldest.GetProperty("title").GetString());
        Assert.True(oldest.GetProperty("lastMessage").GetProperty("fromCustomer").GetBoolean());
        var orderId = oldest.GetProperty("conversation").GetProperty("conversationId").GetGuid();
        Assert.Equal("Can I change my address?", (await JsonAsync(await carl.GetAsync($"{inbox}?sort=newest"))).GetProperty("items")[0].GetProperty("title").GetString());
        var one = await JsonAsync(await carl.GetAsync($"{inbox}?take=1&skip=1"));
        Assert.Equal((1, 2), (one.GetProperty("items").GetArrayLength(), one.GetProperty("total").GetInt32()));
        Assert.Equal(HttpStatusCode.BadRequest, (await carl.GetAsync($"{inbox}?sort=sideways")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await carl.GetAsync($"{inbox}?take=0")).StatusCode);
        Assert.Equal(2, (await JsonAsync(await carl.GetAsync($"{inbox}?assignment=unassigned"))).GetProperty("total").GetInt32());
        Assert.Equal(0, (await JsonAsync(await carl.GetAsync($"{inbox}?assignment=assigned"))).GetProperty("total").GetInt32());

        // The subject replaces the title and is searchable.
        var titled = await JsonAsync(await carl.SendAsync(HttpMethod.Put, $"/api/v1/support/conversations/{orderId}/subject", new { subject = "Late order 1234" }));
        Assert.Equal("Late order 1234", titled.GetProperty("subject").GetString());
        var found = await JsonAsync(await carl.GetAsync($"{inbox}?q=order%201234"));
        Assert.Equal(("Late order 1234", 1), (found.GetProperty("items")[0].GetProperty("title").GetString(), found.GetProperty("total").GetInt32()));

        // Support staff read the history before taking part; other members cannot.
        Assert.Contains("Where is my order?", await (await carl.GetAsync($"/api/v1/support/conversations/{orderId}/history")).Content.ReadAsStringAsync(Ct),
            StringComparison.Ordinal);
        Assert.Equal("conversation_not_found", await ErrorCodeAsync(await dan.GetAsync($"/api/v1/support/conversations/{orderId}/history")));

        // Internal notes: for the team only, never in the history or the customer's view.
        var note = await carl.PostAsync($"/api/v1/support/conversations/{orderId}/notes", new { body = "Checking with the warehouse." });
        AssertStatus(HttpStatusCode.Created, note);
        var notes = (await JsonAsync(await ben.GetAsync($"/api/v1/support/conversations/{orderId}/notes"))).EnumerateArray().ToList();
        Assert.Equal(("Checking with the warehouse.", "Carl"), (notes.Single().GetProperty("body").GetString(),
            notes.Single().GetProperty("author").GetProperty("displayName").GetString()));
        Assert.Equal("conversation_not_found", await ErrorCodeAsync(await dan.GetAsync($"/api/v1/support/conversations/{orderId}/notes")));
        Assert.Equal("note_body_required", await ErrorCodeAsync(await carl.PostAsync($"/api/v1/support/conversations/{orderId}/notes", new { body = "  " })));
        Assert.DoesNotContain("warehouse", await (await ben.GetAsync($"/api/v1/conversations/{orderId}/history")).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.DoesNotContain("warehouse", await (await first.GetAsync("/api/v1/widget/visitor/conversation", Ct)).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // Joining to reply keeps the assignment as it is.
        var joined = await JsonAsync(await carl.PostAsync($"/api/v1/support/conversations/{orderId}/join"));
        Assert.Equal((true, System.Text.Json.JsonValueKind.Null), (joined.GetProperty("participating").GetBoolean(), joined.GetProperty("assignedTo").ValueKind));
        AssertStatus(HttpStatusCode.Created, await carl.PostAsync($"/api/v1/conversations/{orderId}/messages", new { clientMessageId = "r1", body = "Let me check." }));
        Assert.False((await JsonAsync(await carl.GetAsync(inbox))).GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("conversation").GetProperty("conversationId").GetGuid() == orderId).GetProperty("lastMessage").GetProperty("fromCustomer").GetBoolean());

        // Pending waits for the customer, whose next message opens it again.
        var pending = await JsonAsync(await carl.SendAsync(HttpMethod.Put, $"/api/v1/support/conversations/{orderId}/status", new { status = "pending" }));
        Assert.Equal("pending", pending.GetProperty("status").GetString());
        var pendingPage = await JsonAsync(await carl.GetAsync($"{inbox}?status=pending"));
        Assert.Equal((1, 1, 1), (pendingPage.GetProperty("total").GetInt32(), pendingPage.GetProperty("counts").GetProperty("open").GetInt32(),
            pendingPage.GetProperty("counts").GetProperty("pending").GetInt32()));
        AssertStatus(HttpStatusCode.Created, await first.PostAsJsonAsync("/api/v1/widget/visitor/messages", new { clientMessageId = "c2", body = "Any news?" }, Ct));
        Assert.Equal(2, (await JsonAsync(await carl.GetAsync(inbox))).GetProperty("counts").GetProperty("open").GetInt32());
    }

    private static async Task<Guid> DefaultWorkspaceAsync(ApiSession owner, Guid organizationId) =>
        (await JsonAsync(await owner.GetAsync($"/api/v1/organizations/{organizationId}/workspaces"))).EnumerateArray().First().GetProperty("id").GetGuid();

    private static async Task AddToWorkspaceAsync(ApiSession admin, Guid organizationId, Guid workspaceId, params Guid[] userIds)
    {
        foreach (var userId in userIds)
        {
            AssertStatus(HttpStatusCode.OK, await admin.PostAsync($"/api/v1/organizations/{organizationId}/workspaces/{workspaceId}/members", new { userId }));
        }
    }

    private static async Task<JsonElement> CreateChannelAsync(ApiSession session, string path, object body)
    {
        var response = await session.PostAsync(path, body);
        AssertStatus(HttpStatusCode.Created, response, await response.Content.ReadAsStringAsync(Ct));
        return await JsonAsync(response);
    }
}
