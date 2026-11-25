using System.Net;
using System.Text.Json;
using Audorix.KnockTwice.Domain.Conversations;
using Audorix.KnockTwice.Infrastructure.Persistence;
using Audorix.KnockTwice.IntegrationTests.Support;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Audorix.KnockTwice.IntegrationTests.Support.ApiSession;
using static Audorix.KnockTwice.IntegrationTests.Support.TestUsers;

namespace Audorix.KnockTwice.IntegrationTests;

/// <summary>
/// Phase 5 acceptance on real SQLite and PostgreSQL: direct conversations, groups, membership
/// enforcement, consistently ordered and paginated history, unread counts, and the owner's
/// gate decisions (history from join, leavers lose access, contacts/organization members only,
/// roles, ADR-022 sequence).
/// </summary>
public sealed class ConversationTests
{
    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Direct_conversations_are_unique_per_pair_and_scope_and_need_a_relationship(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (carl, carlId) = await SignedInAsync(api, "carl@example.org", "Carl");
        var (dana, danaId) = await SignedInAsync(api, "dana@example.org", "Dana");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = carl;
        using var _4 = dana;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        await BecomeContactsAsync(ana, anaId, dana, danaId);

        var first = await ana.PostAsync("/api/v1/conversations/direct", new { userId = benId });
        AssertStatus(HttpStatusCode.Created, first, await first.Content.ReadAsStringAsync(Ct));
        var direct = await JsonAsync(first);
        Assert.Equal("direct", direct.GetProperty("type").GetString());
        Assert.Equal("Ben", direct.GetProperty("title").GetString());
        var directId = direct.GetProperty("id").GetGuid();

        // Opening again, from either side, returns the same conversation.
        var again = await ana.PostAsync("/api/v1/conversations/direct", new { userId = benId });
        AssertStatus(HttpStatusCode.OK, again);
        Assert.Equal(directId, (await JsonAsync(again)).GetProperty("id").GetGuid());
        var fromBen = await JsonAsync(await ben.PostAsync("/api/v1/conversations/direct", new { userId = anaId }));
        Assert.Equal(directId, fromBen.GetProperty("id").GetGuid());
        Assert.Equal("Ana", fromBen.GetProperty("title").GetString());

        // Not contacts, yourself, or a block: refused without saying why.
        Assert.Equal("user_not_found", await ErrorCodeAsync(await ana.PostAsync("/api/v1/conversations/direct", new { userId = carlId })));
        Assert.Equal("validation_failed", await ErrorCodeAsync(await ana.PostAsync("/api/v1/conversations/direct", new { userId = anaId })));
        AssertStatus(HttpStatusCode.NoContent, await dana.PostAsync($"/api/v1/contacts/{anaId}/block"));
        Assert.Equal("user_not_found", await ErrorCodeAsync(await ana.PostAsync("/api/v1/conversations/direct", new { userId = danaId })));

        // In an organization, members need no contact; the organization chat is separate from the personal one (ADR-021).
        var orgId = await CreateOrganizationAsync(ana, (ben, "ben@example.org"), (carl, "carl@example.org"));
        var orgDirect = await JsonAsync(await ana.PostAsync("/api/v1/conversations/direct", new { userId = carlId, organizationId = orgId }));
        Assert.Equal("organization", orgDirect.GetProperty("scope").GetString());
        Assert.Equal("Acme", orgDirect.GetProperty("organizationName").GetString());
        var orgWithBen = await JsonAsync(await ana.PostAsync("/api/v1/conversations/direct", new { userId = benId, organizationId = orgId }));
        Assert.NotEqual(directId, orgWithBen.GetProperty("id").GetGuid());
        Assert.Equal("user_not_found", await ErrorCodeAsync(await ana.PostAsync("/api/v1/conversations/direct", new { userId = danaId, organizationId = orgId })));
        Assert.Equal("organization_not_found", await ErrorCodeAsync(await dana.PostAsync("/api/v1/conversations/direct", new { userId = anaId, organizationId = orgId })));

        // Membership is enforced: outsiders see nothing.
        Assert.Equal("conversation_not_found", await ErrorCodeAsync(await carl.GetAsync($"/api/v1/conversations/{directId}")));
        Assert.Equal("conversation_not_found", await ErrorCodeAsync(await carl.GetAsync($"/api/v1/conversations/{directId}/history")));
        var carlsList = (await JsonAsync(await carl.GetAsync("/api/v1/conversations"))).EnumerateArray().ToList();
        Assert.Single(carlsList);
        Assert.Equal(orgDirect.GetProperty("id").GetGuid(), carlsList[0].GetProperty("id").GetGuid());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Group_history_is_ordered_paginated_visible_from_join_and_unread_counts_are_correct(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (carl, carlId) = await SignedInAsync(api, "carl@example.org", "Carl");
        var (dana, danaId) = await SignedInAsync(api, "dana@example.org", "Dana");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = carl;
        using var _4 = dana;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        await BecomeContactsAsync(ana, anaId, dana, danaId);

        // Only contacts can be added to a personal group.
        var refused = await ana.PostAsync("/api/v1/conversations/groups", new { title = "Team", userIds = new[] { benId, carlId } });
        Assert.Equal("participant_not_allowed", (await JsonAsync(refused)).GetProperty("errors").GetProperty("userIds")[0].GetString());

        var group = await JsonAsync(await ana.PostAsync("/api/v1/conversations/groups", new { title = "Team", userIds = new[] { benId } }));
        var groupId = group.GetProperty("id").GetGuid();
        Assert.Equal("owner", group.GetProperty("myRole").GetString());
        for (var i = 1; i <= 55; i++)
        {
            AssertStatus(HttpStatusCode.OK, await ana.SendAsync(HttpMethod.Put, $"/api/v1/conversations/{groupId}", new { title = $"Team {i}" }));
        }

        // 1 created + 1 member_added + 55 renames; Ben sees from his addition (sequence 2).
        var all = await ReadAllHistoryAsync(ben, groupId, pageSize: 20);
        Assert.Equal(Enumerable.Range(2, 56).Select(i => (long)i), all.Select(e => e.GetProperty("sequence").GetInt64()));
        Assert.Equal("member_added", all[0].GetProperty("kind").GetString());
        Assert.Equal("Team 55", all[^1].GetProperty("text").GetString());

        // Unread for Ben: everything Ana did since his join (56 entries).
        var bensView = (await JsonAsync(await ben.GetAsync("/api/v1/conversations"))).EnumerateArray().Single();
        Assert.Equal(56, bensView.GetProperty("unreadCount").GetInt32());
        Assert.Equal(0, (await JsonAsync(await ana.GetAsync("/api/v1/conversations"))).EnumerateArray().Single().GetProperty("unreadCount").GetInt32());

        var read = await JsonAsync(await ben.PostAsync($"/api/v1/conversations/{groupId}/read", new { sequence = 40 }));
        Assert.Equal((40L, 17), (read.GetProperty("lastReadSequence").GetInt64(), read.GetProperty("unreadCount").GetInt32()));
        var backwards = await JsonAsync(await ben.PostAsync($"/api/v1/conversations/{groupId}/read", new { sequence = 5 }));
        Assert.Equal(40L, backwards.GetProperty("lastReadSequence").GetInt64());
        var beyond = await JsonAsync(await ben.PostAsync($"/api/v1/conversations/{groupId}/read", new { sequence = 9999 }));
        Assert.Equal((57L, 0), (beyond.GetProperty("lastReadSequence").GetInt64(), beyond.GetProperty("unreadCount").GetInt32()));

        // A later member sees history only from the entry that added her.
        await ana.PostAsync($"/api/v1/conversations/{groupId}/participants", new { userIds = new[] { danaId } });
        var danasHistory = await ReadAllHistoryAsync(dana, groupId, pageSize: 20);
        var addedDana = Assert.Single(danasHistory);
        Assert.Equal(("member_added", 58L, danaId), (addedDana.GetProperty("kind").GetString(), addedDana.GetProperty("sequence").GetInt64(),
            addedDana.GetProperty("subject").GetProperty("id").GetGuid()));
        Assert.Equal(1, (await JsonAsync(await dana.GetAsync($"/api/v1/conversations/{groupId}"))).GetProperty("conversation").GetProperty("unreadCount").GetInt32());

        // Cursors are opaque and validated; non-participants are refused.
        Assert.Equal("validation_failed", await ErrorCodeAsync(await ben.GetAsync($"/api/v1/conversations/{groupId}/history?before=bm90LWEtY3Vyc29y")));
        Assert.Equal("validation_failed", await ErrorCodeAsync(await ben.GetAsync($"/api/v1/conversations/{groupId}/history?limit=500")));
        Assert.Equal("conversation_not_found", await ErrorCodeAsync(await carl.GetAsync($"/api/v1/conversations/{groupId}/history")));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Roles_govern_group_management_and_people_who_leave_lose_access(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (dana, danaId) = await SignedInAsync(api, "dana@example.org", "Dana");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = dana;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        await BecomeContactsAsync(ana, anaId, dana, danaId);
        var groupId = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/groups", new { title = "Team", userIds = new[] { benId, danaId } })))
            .GetProperty("id").GetGuid();

        // Members cannot manage; owners grant administrator; administrators rename and remove members only.
        Assert.Equal(HttpStatusCode.Forbidden, (await ben.SendAsync(HttpMethod.Put, $"/api/v1/conversations/{groupId}", new { title = "Mine" })).StatusCode);
        AssertStatus(HttpStatusCode.OK, await ana.SendAsync(HttpMethod.Put, $"/api/v1/conversations/{groupId}/participants/{benId}", new { role = "administrator" }));
        AssertStatus(HttpStatusCode.OK, await ben.SendAsync(HttpMethod.Put, $"/api/v1/conversations/{groupId}", new { title = "Renamed", description = "Plans" }));
        Assert.Equal(HttpStatusCode.Forbidden, (await ben.SendAsync(HttpMethod.Delete, $"/api/v1/conversations/{groupId}/participants/{anaId}")).StatusCode);
        Assert.Equal("last_owner", await ErrorCodeAsync(await ana.SendAsync(HttpMethod.Put, $"/api/v1/conversations/{groupId}/participants/{anaId}", new { role = "member" })));

        // Removal ends access immediately, including history and the list entry.
        AssertStatus(HttpStatusCode.NoContent, await ben.SendAsync(HttpMethod.Delete, $"/api/v1/conversations/{groupId}/participants/{danaId}"));
        Assert.Equal("conversation_not_found", await ErrorCodeAsync(await dana.GetAsync($"/api/v1/conversations/{groupId}")));
        Assert.Equal("conversation_not_found", await ErrorCodeAsync(await dana.GetAsync($"/api/v1/conversations/{groupId}/history")));
        Assert.Empty((await JsonAsync(await dana.GetAsync("/api/v1/conversations"))).EnumerateArray());

        // The last owner leaving hands ownership to the longest-standing participant.
        AssertStatus(HttpStatusCode.NoContent, await ana.PostAsync($"/api/v1/conversations/{groupId}/leave"));
        var detail = await JsonAsync(await ben.GetAsync($"/api/v1/conversations/{groupId}"));
        var participant = Assert.Single(detail.GetProperty("participants").EnumerateArray());
        Assert.Equal((benId, "owner"), (participant.GetProperty("user").GetProperty("id").GetGuid(), participant.GetProperty("role").GetString()));
        Assert.Equal("Plans", detail.GetProperty("description").GetString());
        Assert.Equal("conversation_not_found", await ErrorCodeAsync(await ana.GetAsync($"/api/v1/conversations/{groupId}")));

        var kinds = (await ReadAllHistoryAsync(ben, groupId, 50)).Select(e => e.GetProperty("kind").GetString()).ToList();
        // Ben's history starts at his own addition; Dana was added right after him.
        Assert.Equal(["member_added", "member_added", "role_changed", "renamed", "description_changed", "member_removed", "member_left", "role_changed"], kinds);

        // Direct conversations have no group management.
        var direct = (await JsonAsync(await ben.PostAsync("/api/v1/conversations/direct", new { userId = anaId }))).GetProperty("id").GetGuid();
        Assert.Equal("not_a_group", await ErrorCodeAsync(await ben.PostAsync($"/api/v1/conversations/{direct}/leave")));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Leaving_an_organization_ends_its_conversations_and_rejoining_does_not_restore_them(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, _) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;
        var orgId = await CreateOrganizationAsync(ana, (ben, "ben@example.org"));
        var groupId = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/groups", new { title = "Acme team", organizationId = orgId, userIds = new[] { benId } })))
            .GetProperty("id").GetGuid();

        AssertStatus(HttpStatusCode.NoContent, await ana.SendAsync(HttpMethod.Delete, $"/api/v1/organizations/{orgId}/members/{benId}"));
        Assert.Equal("conversation_not_found", await ErrorCodeAsync(await ben.GetAsync($"/api/v1/conversations/{groupId}")));

        await CreateInvitationAndAcceptAsync(ana, ben, orgId, "ben@example.org");
        Assert.Equal("conversation_not_found", await ErrorCodeAsync(await ben.GetAsync($"/api/v1/conversations/{groupId}")));
        Assert.Equal("member_removed",
            (await ReadAllHistoryAsync(ana, groupId, 50)).Last().GetProperty("kind").GetString());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Concurrent_changes_get_unique_contiguous_sequence_numbers(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        var groupId = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/groups", new { title = "Race", userIds = new[] { benId } })))
            .GetProperty("id").GetGuid();
        await ana.SendAsync(HttpMethod.Put, $"/api/v1/conversations/{groupId}/participants/{benId}", new { role = "administrator" });

        var writers = Enumerable.Range(0, 12).Select(i => (i % 2 == 0 ? ana : ben).SendAsync(HttpMethod.Put, $"/api/v1/conversations/{groupId}", new { title = $"Title {i}" }));
        var results = await Task.WhenAll(writers);
        Assert.All(results, r => Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, r.StatusCode.ToString()));

        var sequences = (await ReadAllHistoryAsync(ana, groupId, 100)).Select(e => e.GetProperty("sequence").GetInt64()).ToList();
        Assert.Equal(Enumerable.Range(1, sequences.Count).Select(i => (long)i), sequences);
        var detail = await JsonAsync(await ana.GetAsync($"/api/v1/conversations/{groupId}"));
        Assert.Equal(sequences[^1], detail.GetProperty("conversation").GetProperty("lastSequence").GetInt64());
        Assert.Equal(3 + results.Count(r => r.IsSuccessStatusCode), sequences.Count);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Database_enforces_direct_uniqueness_and_unique_history_positions(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        ana.Dispose();
        ben.Dispose();
        var now = DateTimeOffset.UtcNow;

        await using (var scope = api.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ChatXDbContext>();
            context.Conversations.Add(Conversation.CreateDirect(Guid.NewGuid(), ConversationScope.Personal, null, anaId, benId, now));
            context.Conversations.Add(Conversation.CreateDirect(Guid.NewGuid(), ConversationScope.Personal, null, benId, anaId, now));
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));
        }

        await using (var scope = api.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ChatXDbContext>();
            var group = Conversation.CreateGroup(Guid.NewGuid(), ConversationScope.Personal, null, "G", anaId, now);
            context.Conversations.Add(group);
            context.ConversationEvents.Add(ConversationEvent.Record(Guid.NewGuid(), group, ConversationEventKind.Created, anaId, null, null, now));
            await context.SaveChangesAsync(Ct);

            // A second writer reusing position 1 (as a MAX + 1 scheme could) is rejected.
            var stale = Conversation.CreateGroup(group.Id, ConversationScope.Personal, null, "G", anaId, now);
            context.ChangeTracker.Clear();
            context.ConversationEvents.Add(ConversationEvent.Record(Guid.NewGuid(), stale, ConversationEventKind.Renamed, anaId, null, "x", now));
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));
        }
    }

    [Fact]
    public async Task Removed_participants_stop_receiving_realtime_events_on_open_connections()
    {
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db, new() { ["Realtime:TypingTimeout"] = "00:00:05" });
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        var groupId = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/groups", new { title = "Live", userIds = new[] { benId } })))
            .GetProperty("id").GetGuid();

        await using var anaHub = await HubDevice.ConnectAsync(api, "ana@example.org");
        await using var benHub = await HubDevice.ConnectAsync(api, "ben@example.org");
        var updates = new System.Collections.Concurrent.ConcurrentQueue<Contracts.V1.Realtime.ConversationUpdatedEvent>();
        benHub.Hub.On<Contracts.V1.Realtime.ConversationUpdatedEvent>("ConversationUpdated", e => updates.Enqueue(e));
        await anaHub.Hub.InvokeAsync("JoinConversation", groupId, Ct);
        await benHub.Hub.InvokeAsync("JoinConversation", groupId, Ct);
        await anaHub.Hub.InvokeAsync("SendTypingStatus", groupId, true, Ct);
        Assert.True((await benHub.NextTypingAsync()).IsTyping);
        await anaHub.Hub.InvokeAsync("SendTypingStatus", groupId, false, Ct);
        Assert.False((await benHub.NextTypingAsync()).IsTyping);

        AssertStatus(HttpStatusCode.NoContent, await ana.SendAsync(HttpMethod.Delete, $"/api/v1/conversations/{groupId}/participants/{benId}"));

        await anaHub.Hub.InvokeAsync("SendTypingStatus", groupId, true, Ct);
        await benHub.AssertNoTypingAsync();
        Assert.Contains(updates, u => u.ConversationId == groupId && u.Change == "removed");
        var refused = await Assert.ThrowsAsync<HubException>(() => benHub.Hub.InvokeAsync("JoinConversation", groupId, Ct));
        Assert.EndsWith("conversation_not_found", refused.Message, StringComparison.Ordinal);
    }

    private static async Task CreateInvitationAndAcceptAsync(ApiSession owner, ApiSession member, Guid orgId, string email)
    {
        var invite = await JsonAsync(await owner.PostAsync($"/api/v1/organizations/{orgId}/invitations", new { email, role = "member" }));
        AssertStatus(HttpStatusCode.OK, await member.PostAsync($"/api/v1/organizations/invitations/{invite.GetProperty("id").GetGuid()}/accept"));
    }

    /// <summary>Walks every history page from newest to oldest and returns all entries in ascending order, checking page consistency.</summary>
    private static async Task<List<JsonElement>> ReadAllHistoryAsync(ApiSession session, Guid conversationId, int pageSize)
    {
        var pages = new List<List<JsonElement>>();
        string? cursor = null;
        do
        {
            var url = $"/api/v1/conversations/{conversationId}/history?limit={pageSize}" + (cursor is null ? string.Empty : $"&before={cursor}");
            var response = await session.GetAsync(url);
            AssertStatus(HttpStatusCode.OK, response, await response.Content.ReadAsStringAsync(Ct));
            var page = await JsonAsync(response);
            var items = page.GetProperty("items").EnumerateArray().ToList();
            Assert.True(items.Count <= pageSize);
            var sequences = items.Select(i => i.GetProperty("sequence").GetInt64()).ToList();
            Assert.Equal(sequences.Order(), sequences);
            pages.Add(items);
            cursor = page.GetProperty("hasMore").GetBoolean() ? page.GetProperty("nextCursor").GetString() : null;
        }
        while (cursor is not null);

        pages.Reverse();
        var all = pages.SelectMany(p => p).ToList();
        Assert.Equal(all.Count, all.Select(e => e.GetProperty("sequence").GetInt64()).Distinct().Count());
        return all;
    }
}
