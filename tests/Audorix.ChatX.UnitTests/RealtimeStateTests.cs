using Audorix.ChatX.Application.Realtime;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.UnitTests;

public sealed class RealtimeStateTests
{
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Ben = Guid.NewGuid();
    private static readonly Guid Chat = Guid.NewGuid();

    private readonly ManualClock _clock = new();
    private readonly RealtimeState _state;

    public RealtimeStateTests() => _state = new RealtimeState(_clock, Options.Create(new RealtimeSettings
    {
        PresenceOfflineGrace = TimeSpan.FromSeconds(5),
        TypingTimeout = TimeSpan.FromSeconds(6),
    }));

    [Fact]
    public void A_user_is_online_while_any_device_is_connected_and_goes_offline_only_after_the_grace_period()
    {
        Assert.True(_state.Connect("phone", Ana, Guid.NewGuid()));
        Assert.False(_state.Connect("laptop", Ana, Guid.NewGuid()));

        _state.Disconnect("phone");
        _state.Disconnect("laptop");
        Assert.True(_state.IsOnline(Ana));

        _clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Empty(_state.Sweep().WentOffline);

        // Reconnecting within the grace period is not a new "online" transition.
        Assert.False(_state.Connect("phone-2", Ana, Guid.NewGuid()));
        _clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Empty(_state.Sweep().WentOffline);

        _state.Disconnect("phone-2");
        _clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal([Ana], _state.Sweep().WentOffline);
        Assert.False(_state.IsOnline(Ana));
    }

    [Fact]
    public void Typing_starts_once_renewals_are_silent_and_it_expires()
    {
        Assert.Equal((true, _clock.GetUtcNow() + TimeSpan.FromSeconds(6)), _state.SetTyping(Chat, Ben, true));
        _clock.Advance(TimeSpan.FromSeconds(4));
        Assert.False(_state.SetTyping(Chat, Ben, true).Changed);

        _clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Empty(_state.Sweep().TypingExpired);
        _clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal([(Chat, Ben)], _state.Sweep().TypingExpired);
        Assert.False(_state.SetTyping(Chat, Ben, false).Changed);
    }

    [Fact]
    public void Typing_ends_when_the_users_last_joined_connection_leaves_or_disconnects()
    {
        _state.Connect("phone", Ben, Guid.NewGuid());
        _state.Connect("laptop", Ben, Guid.NewGuid());
        Assert.True(_state.Join("phone", Chat));
        Assert.False(_state.Join("phone", Chat));
        _state.Join("laptop", Chat);
        _state.SetTyping(Chat, Ben, true);

        Assert.False(_state.Leave("phone", Chat));
        Assert.Equal([Chat], _state.Disconnect("laptop").TypingStoppedIn);
    }

    [Fact]
    public void Revoking_a_user_removes_every_device_from_the_conversation()
    {
        _state.Connect("phone", Ben, Guid.NewGuid());
        _state.Connect("laptop", Ben, Guid.NewGuid());
        _state.Join("phone", Chat);
        _state.Join("laptop", Chat);
        _state.SetTyping(Chat, Ben, true);

        var (connections, typingStopped) = _state.RemoveUserFromConversation(Ben, Chat);

        Assert.Equal(["laptop", "phone"], connections.Order(StringComparer.Ordinal));
        Assert.True(typingStopped);
        Assert.False(_state.HasJoined("phone", Chat));
    }

    [Fact]
    public void Settings_reject_out_of_range_timers()
    {
        Assert.Empty(new RealtimeSettings().Validate());
        Assert.Equal(4, new RealtimeSettings
        {
            PresenceOfflineGrace = TimeSpan.FromHours(1),
            TypingTimeout = TimeSpan.Zero,
            SweepInterval = TimeSpan.FromMinutes(1),
            SessionCheckInterval = TimeSpan.Zero,
        }.Validate().Count);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
