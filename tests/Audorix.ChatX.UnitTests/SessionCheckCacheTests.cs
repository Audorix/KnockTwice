using Audorix.ChatX.Application.Sessions;

namespace Audorix.ChatX.UnitTests;

/// <summary>Phase 23: recently validated sessions are trusted briefly, never past expiry, revocation or the activity write.</summary>
public sealed class SessionCheckCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Touch = TimeSpan.FromMinutes(5);
    private static readonly Guid Session = Guid.Parse("01a20a49-1a03-700a-9de0-adb3fcb32f33");
    private static readonly Guid User = Guid.Parse("01a20a49-1a03-700a-9de0-adb3fcb32f34");

    [Fact]
    public void A_remembered_session_is_trusted_only_for_its_owner_and_for_a_short_time()
    {
        var clock = new ManualTime(Now);
        var cache = new SessionCheckCache(clock);
        Assert.False(cache.IsKnownActive(Session, User, Touch));

        cache.Remember(Session, User, Now.AddHours(12), Now);
        Assert.True(cache.IsKnownActive(Session, User, Touch));
        Assert.False(cache.IsKnownActive(Session, Guid.NewGuid(), Touch));

        clock.Advance(SessionCheckCache.Lifetime);
        Assert.False(cache.IsKnownActive(Session, User, Touch));
    }

    [Fact]
    public void Revoked_and_expiring_sessions_and_due_activity_writes_go_back_to_the_database()
    {
        var clock = new ManualTime(Now);
        var cache = new SessionCheckCache(clock);

        cache.Remember(Session, User, Now.AddHours(12), Now);
        cache.Forget(Session);
        Assert.False(cache.IsKnownActive(Session, User, Touch));

        cache.Remember(Session, User, Now.AddSeconds(1), Now);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(cache.IsKnownActive(Session, User, Touch)); // expired

        var lastSeen = clock.GetUtcNow() - Touch + TimeSpan.FromSeconds(1);
        cache.Remember(Session, User, Now.AddHours(12), lastSeen);
        Assert.True(cache.IsKnownActive(Session, User, Touch));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(cache.IsKnownActive(Session, User, Touch)); // the activity write is due
    }

    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
