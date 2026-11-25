using System.Collections.Concurrent;
using System.Net;
using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Realtime;
using Audorix.KnockTwice.Contracts.V1.Realtime;
using Audorix.KnockTwice.IntegrationTests.Support;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using static Audorix.KnockTwice.IntegrationTests.Support.ApiSession;
using static Audorix.KnockTwice.IntegrationTests.Support.TestUsers;

namespace Audorix.KnockTwice.IntegrationTests;

/// <summary>
/// Several API instances (Phase 21, ADR-035): two instances over one database share presence and
/// rate limits. Redis itself is replaced by an in-memory stand-in shared by both instances (no
/// Redis server in the test environment), so this checks the instances' behaviour, not Redis.
/// </summary>
public sealed class ScaleOutTests
{
    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, string?> Fast => new()
    {
        ["Realtime:PresenceOfflineGrace"] = "00:00:00",
        ["Realtime:SweepInterval"] = "00:00:00.050",
        ["RateLimiting:AuthPermitLimit"] = "1000",
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Presence_is_shared_between_instances(string provider)
    {
        var cluster = new SharedPresence();
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var first = await StartAsync(db, Fast, s => s.AddSingleton<IClusterPresence>(cluster.Node("first")));
        await using var second = await StartAsync(db, Fast, s => s.AddSingleton<IClusterPresence>(cluster.Node("second")));
        var (ana, anaId) = await SignedInAsync(first, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(first, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        var firstPresence = first.Services.GetRequiredService<PresenceDirectory>();

        await using var benHub = await HubDevice.ConnectAsync(first, "ben@example.org");
        Assert.False(await firstPresence.IsOnlineAsync(anaId, Ct));

        // Ana connects to the second instance: the first one knows she is online.
        await using var anaOnSecond = await HubDevice.ConnectAsync(second, "ana@example.org");
        for (var i = 0; i < 100 && !await firstPresence.IsOnlineAsync(anaId, Ct); i++)
        {
            await Task.Delay(50, Ct); // the second instance records the connection just after the handshake
        }

        Assert.True(await firstPresence.IsOnlineAsync(anaId, Ct));
        var snapshot = await benHub.Hub.InvokeAsync<IReadOnlyList<UserPresenceResponse>>(RealtimeContract.Methods.GetPresence, Ct);
        Assert.Equal("online", snapshot.Single(p => p.UserId == anaId).Status);

        // She also connects to the first instance: no second "online" for Ben.
        var anaOnFirst = await HubDevice.ConnectAsync(first, "ana@example.org");
        await benHub.AssertNoPresenceAsync();

        // Leaving the first instance: still online on the second, so no "offline" either.
        await anaOnFirst.DisposeAsync();
        await benHub.AssertNoPresenceAsync(1000);
        Assert.True(await firstPresence.IsOnlineAsync(anaId, Ct));

        // Leaving the second instance too: offline everywhere.
        await anaOnSecond.DisposeAsync();
        for (var i = 0; i < 100 && await firstPresence.IsOnlineAsync(anaId, Ct); i++)
        {
            await Task.Delay(50, Ct);
        }

        Assert.False(await firstPresence.IsOnlineAsync(anaId, Ct));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Sign_in_limits_are_shared_between_instances(string provider)
    {
        var counter = new LocalRateCounter(TimeProvider.System); // stands in for the shared Redis counter
        var limited = new Dictionary<string, string?>(Fast) { ["RateLimiting:AuthPermitLimit"] = "4" };
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var first = await StartAsync(db, limited, s => s.AddSingleton<IRateCounter>(counter));
        await using var second = await StartAsync(db, limited, s => s.AddSingleton<IRateCounter>(counter));

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            using var session = new ApiSession((i % 2 == 0 ? first : second).CreateHttpsClient());
            statuses.Add((await session.LoginAsync("nobody@example.org", "wrong password here")).StatusCode);
        }

        // Four attempts in total across both instances, then both refuse.
        Assert.Equal(4, statuses.Count(s => s != HttpStatusCode.TooManyRequests));
        Assert.Equal([HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests], statuses.Skip(4));
    }

    /// <summary>The in-memory stand-in for Redis presence: entries per (user, instance).</summary>
    private sealed class SharedPresence
    {
        private readonly ConcurrentDictionary<(Guid User, string Node), byte> _entries = new();

        public IClusterPresence Node(string name) => new View(this, name);

        private sealed class View(SharedPresence shared, string node) : IClusterPresence
        {
            public Task MarkOnlineAsync(Guid userId, CancellationToken cancellationToken)
            {
                shared._entries[(userId, node)] = 1;
                return Task.CompletedTask;
            }

            public Task MarkOfflineAsync(Guid userId, CancellationToken cancellationToken)
            {
                shared._entries.TryRemove((userId, node), out _);
                return Task.CompletedTask;
            }

            public Task<IReadOnlySet<Guid>> OnlineElsewhereAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlySet<Guid>>(userIds.Where(id => shared._entries.Keys.Any(k => k.User == id && k.Node != node)).ToHashSet());

            public Task RefreshAsync(IReadOnlyCollection<Guid> onlineHere, CancellationToken cancellationToken)
            {
                foreach (var id in onlineHere)
                {
                    shared._entries[(id, node)] = 1;
                }

                return Task.CompletedTask;
            }
        }
    }
}
