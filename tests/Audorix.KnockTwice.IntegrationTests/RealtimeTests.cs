using System.Net;
using Audorix.KnockTwice.Application.Realtime;
using Audorix.KnockTwice.IntegrationTests.Support;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Audorix.KnockTwice.IntegrationTests.Support.ApiSession;
using static Audorix.KnockTwice.IntegrationTests.Support.TestUsers;

namespace Audorix.KnockTwice.IntegrationTests;

/// <summary>
/// Phase 4 acceptance through real SignalR connections (long polling over the test server):
/// authenticated connections, presence across devices and reconnects, conversation
/// destinations guarded by the protected access fixture, typing expiry, revocation and sign-out.
/// </summary>
public sealed class RealtimeTests
{
    private static readonly Dictionary<string, string?> FastTimers = new()
    {
        ["Realtime:PresenceOfflineGrace"] = "00:00:00.300",
        ["Realtime:TypingTimeout"] = "00:00:00.500",
        ["Realtime:SweepInterval"] = "00:00:00.050",
        ["Realtime:SessionCheckInterval"] = "00:00:01",
    };

    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Hub_requires_a_signed_in_session_and_accepts_only_the_web_app_origin()
    {
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db, FastTimers);
        var (ana, _) = await SignedInAsync(api, "ana@example.org", "Ana");
        using var _1 = ana;

        using var anonymous = api.CreateHttpsClient();
        var refused = await anonymous.PostAsync(new Uri("/hubs/chat/negotiate?negotiateVersion=1", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await NegotiateAsync(ana, "https://evil.example")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await NegotiateAsync(ana, "https://chat.test")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await NegotiateAsync(ana, origin: null)).StatusCode);

        await using var device = await HubDevice.ConnectAsync(api, "ana@example.org");
        Assert.Equal(HubConnectionState.Connected, device.Hub.State);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Presence_reaches_only_related_people_once_per_user_across_devices(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db, FastTimers);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (carl, _) = await SignedInAsync(api, "carl@example.org", "Carl");
        var (dana, danaId) = await SignedInAsync(api, "dana@example.org", "Dana");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = carl;
        using var _4 = dana;

        // Ana and Ben are contacts; Dana shares an organization group with Ana. Carl is in the same
        // organization but shares no chat with anyone: since Phase 22 (owner decision) membership alone
        // does not show presence.
        await ana.PostAsync("/api/v1/contacts/requests", new { userId = benId });
        await ben.PostAsync("/api/v1/contacts/requests", new { userId = anaId });
        var organizationId = await CreateOrganizationAsync(ana, (dana, "dana@example.org"), (carl, "carl@example.org"));
        AssertStatus(HttpStatusCode.Created, await ana.PostAsync("/api/v1/conversations/groups", new { title = "Team", userIds = new[] { danaId }, organizationId }));

        await using var anaHub = await HubDevice.ConnectAsync(api, "ana@example.org");
        await using var carlHub = await HubDevice.ConnectAsync(api, "carl@example.org");

        var benPhone = await HubDevice.ConnectAsync(api, "ben@example.org");
        var online = await anaHub.NextPresenceAsync();
        Assert.Equal((1, benId, "online"), (online.Version, online.UserId, online.Status));

        // A second device of the same user changes nothing for observers.
        var benLaptop = await HubDevice.ConnectAsync(api, "ben@example.org");
        await anaHub.AssertNoPresenceAsync();

        await using (var danaHub = await HubDevice.ConnectAsync(api, "dana@example.org"))
        {
            Assert.Equal(danaId, (await anaHub.NextPresenceAsync()).UserId);
            var seenByAna = await anaHub.Hub.InvokeAsync<List<Contracts.V1.Realtime.UserPresenceResponse>>("GetPresence", Ct);
            Assert.Equal(
                new[] { (benId, "online"), (danaId, "online") }.OrderBy(p => p.Item1),
                seenByAna.Select(p => (p.UserId, p.Status)).OrderBy(p => p.Item1));
        }

        Assert.Equal((danaId, "offline"), await PresenceOf(anaHub));
        Assert.Empty(await carlHub.Hub.InvokeAsync<List<Contracts.V1.Realtime.UserPresenceResponse>>("GetPresence", Ct));
        await carlHub.AssertNoPresenceAsync(100);

        // Closing one of two devices keeps Ben online; closing the last one turns him offline.
        await benPhone.DisposeAsync();
        await anaHub.AssertNoPresenceAsync();
        await benLaptop.DisposeAsync();
        Assert.Equal((benId, "offline"), await PresenceOf(anaHub));
        await carlHub.AssertNoPresenceAsync(100);
    }

    [Fact]
    public async Task Reconnecting_within_the_grace_period_does_not_repeat_presence_changes()
    {
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db, new(FastTimers) { ["Realtime:PresenceOfflineGrace"] = "00:00:02" });
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;
        await ana.PostAsync("/api/v1/contacts/requests", new { userId = benId });
        await ben.PostAsync("/api/v1/contacts/requests", new { userId = anaId });

        await using var anaHub = await HubDevice.ConnectAsync(api, "ana@example.org");
        var first = await HubDevice.ConnectAsync(api, "ben@example.org");
        Assert.Equal((benId, "online"), await PresenceOf(anaHub));

        // A dropped connection followed by a quick reconnect (page reload, network blip).
        await first.DisposeAsync();
        await using var second = await HubDevice.ConnectAsync(api, "ben@example.org");
        await anaHub.AssertNoPresenceAsync(2500);
    }

    [Fact]
    public async Task Conversations_require_access_and_typing_indicators_expire_automatically()
    {
        var conversation = Guid.NewGuid();
        var access = new FixtureConversationAccess();
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db, FastTimers, WithAccess(access));
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;
        await using var anaHub = await HubDevice.ConnectAsync(api, "ana@example.org");
        await using var benHub = await HubDevice.ConnectAsync(api, "ben@example.org");

        // Without access nothing can be joined, and typing needs a joined destination.
        var denied = await Assert.ThrowsAsync<HubException>(() => anaHub.Hub.InvokeAsync("JoinConversation", conversation, Ct));
        Assert.EndsWith("conversation_not_found", denied.Message, StringComparison.Ordinal);
        var notJoined = await Assert.ThrowsAsync<HubException>(() => benHub.Hub.InvokeAsync("SendTypingStatus", conversation, true, Ct));
        Assert.EndsWith("conversation_not_found", notJoined.Message, StringComparison.Ordinal);

        access.Grant(conversation, anaId, benId);
        await anaHub.Hub.InvokeAsync("JoinConversation", conversation, Ct);
        await anaHub.Hub.InvokeAsync("JoinConversation", conversation, Ct); // rejoin (e.g. after reconnect) is idempotent
        await benHub.Hub.InvokeAsync("JoinConversation", conversation, Ct);

        await benHub.Hub.InvokeAsync("SendTypingStatus", conversation, true, Ct);
        var typing = await anaHub.NextTypingAsync();
        Assert.Equal((conversation, benId, true), (typing.ConversationId, typing.UserId, typing.IsTyping));
        Assert.NotNull(typing.ExpiresAtUtc);

        // Renewing the indicator is silent; without renewal it expires on its own.
        await benHub.Hub.InvokeAsync("SendTypingStatus", conversation, true, Ct);
        var stopped = await anaHub.NextTypingAsync();
        Assert.Equal((benId, false), (stopped.UserId, stopped.IsTyping));
        await anaHub.AssertNoTypingAsync();

        // Explicit stop and leaving.
        await benHub.Hub.InvokeAsync("SendTypingStatus", conversation, true, Ct);
        Assert.True((await anaHub.NextTypingAsync()).IsTyping);
        await benHub.Hub.InvokeAsync("LeaveConversation", conversation, Ct);
        Assert.False((await anaHub.NextTypingAsync()).IsTyping);
    }

    [Fact]
    public async Task Revoked_access_stops_delivery_to_an_open_connection()
    {
        var conversation = Guid.NewGuid();
        var access = new FixtureConversationAccess();
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db, FastTimers, WithAccess(access));
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;
        access.Grant(conversation, anaId, benId);
        await using var anaHub = await HubDevice.ConnectAsync(api, "ana@example.org");
        await using var benHub = await HubDevice.ConnectAsync(api, "ben@example.org");
        await anaHub.Hub.InvokeAsync("JoinConversation", conversation, Ct);
        await benHub.Hub.InvokeAsync("JoinConversation", conversation, Ct);

        // Membership ends (Phase 5 will call this when a member is removed).
        access.Revoke(conversation, benId);
        await using (var scope = api.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<RealtimeService>().RevokeConversationAccessAsync(benId, conversation, Ct);
        }

        await anaHub.Hub.InvokeAsync("SendTypingStatus", conversation, true, Ct);
        await benHub.AssertNoTypingAsync();
        var refused = await Assert.ThrowsAsync<HubException>(() => benHub.Hub.InvokeAsync("SendTypingStatus", conversation, true, Ct));
        Assert.EndsWith("conversation_not_found", refused.Message, StringComparison.Ordinal);
        var rejoin = await Assert.ThrowsAsync<HubException>(() => benHub.Hub.InvokeAsync("JoinConversation", conversation, Ct));
        Assert.EndsWith("conversation_not_found", rejoin.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Access_is_rechecked_on_use_even_without_an_explicit_revocation()
    {
        var conversation = Guid.NewGuid();
        var access = new FixtureConversationAccess();
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db, FastTimers, WithAccess(access));
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;
        access.Grant(conversation, anaId, benId);
        await using var anaHub = await HubDevice.ConnectAsync(api, "ana@example.org");
        await using var benHub = await HubDevice.ConnectAsync(api, "ben@example.org");
        await anaHub.Hub.InvokeAsync("JoinConversation", conversation, Ct);
        await benHub.Hub.InvokeAsync("JoinConversation", conversation, Ct);

        access.Revoke(conversation, benId);
        await Assert.ThrowsAsync<HubException>(() => benHub.Hub.InvokeAsync("SendTypingStatus", conversation, true, Ct));

        // The failed check also removed Ben's connection from the destination.
        await anaHub.Hub.InvokeAsync("SendTypingStatus", conversation, true, Ct);
        await benHub.AssertNoTypingAsync();
    }

    [Fact]
    public async Task Signing_out_closes_that_sessions_connections_only()
    {
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db, FastTimers);
        var (ben, _) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ben;
        await using var phone = await HubDevice.ConnectAsync(api, "ben@example.org");
        await using var laptop = await HubDevice.ConnectAsync(api, "ben@example.org");

        AssertStatus(HttpStatusCode.NoContent, await phone.Session.PostAsync("/api/v1/auth/logout"));

        await phone.Closed.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal(HubConnectionState.Connected, laptop.Hub.State);
        Assert.Empty(await laptop.Hub.InvokeAsync<List<Contracts.V1.Realtime.UserPresenceResponse>>("GetPresence", Ct));
    }

    [Fact]
    public async Task Revoking_all_other_sessions_closes_their_connections()
    {
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db, FastTimers);
        var (ben, _) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ben;
        await using var phone = await HubDevice.ConnectAsync(api, "ben@example.org");

        var revoked = await ben.PostAsync("/api/v1/auth/sessions/revoke-others");
        AssertStatus(HttpStatusCode.OK, revoked);

        await phone.Closed.WaitAsync(TimeSpan.FromSeconds(10), Ct);
    }

    private static Action<IServiceCollection> WithAccess(FixtureConversationAccess access) => services =>
    {
        services.RemoveAll<IConversationAccess>();
        services.AddSingleton<IConversationAccess>(access);
    };

    private static async Task<(Guid, string)> PresenceOf(HubDevice device)
    {
        var e = await device.NextPresenceAsync();
        return (e.UserId, e.Status);
    }

    private static async Task<HttpResponseMessage> NegotiateAsync(ApiSession session, string? origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/hubs/chat/negotiate?negotiateVersion=1");
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return await session.Client.SendAsync(request, Ct);
    }

}
