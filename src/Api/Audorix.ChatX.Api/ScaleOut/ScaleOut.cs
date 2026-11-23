using System.Threading.RateLimiting;
using Audorix.ChatX.Application.Common;
using Audorix.ChatX.Application.Realtime;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Audorix.ChatX.Api.ScaleOut;

/// <summary>
/// Running several API instances (Phase 21, ADR-035), bound from <c>ScaleOut</c>. Without
/// <see cref="Redis"/> the API is a single server, as before.
/// </summary>
public sealed class ScaleOutSettings
{
    public const string SectionName = "ScaleOut";

    /// <summary>How often an instance renews its presence entries; entries live <see cref="PresenceTtl"/>.</summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);

    public static readonly TimeSpan PresenceTtl = TimeSpan.FromSeconds(60);

    /// <summary>A StackExchange.Redis connection string (for example <c>redis:6379,password=…,ssl=true</c>). Empty: single server.</summary>
    public string Redis { get; set; } = string.Empty;

    /// <summary>Prefix of every Redis key and channel, so several installations can share one Redis.</summary>
    public string Prefix { get; set; } = "chatx";

    public bool Enabled => !string.IsNullOrWhiteSpace(Redis);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Prefix) || Prefix.Length > 32 || Prefix.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
        {
            errors.Add("ScaleOut:Prefix must be 1-32 letters, digits, '-' or '_'.");
        }

        if (Enabled)
        {
            try
            {
                ConfigurationOptions.Parse(Redis);
            }
            catch (ArgumentException)
            {
                errors.Add("ScaleOut:Redis is not a valid Redis connection string.");
            }
        }

        return errors;
    }
}

internal static class ScaleOutSetup
{
    /// <summary>
    /// Adds the Redis SignalR backplane, shared presence and shared rate limits when
    /// <c>ScaleOut:Redis</c> is set; otherwise leaves the single-server defaults in place.
    /// </summary>
    public static IServiceCollection AddChatXScaleOut(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ScaleOutSettings>().BindConfiguration(ScaleOutSettings.SectionName).ValidateOnStart();
        services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<ScaleOutSettings>, Identity.DelegatingValidator<ScaleOutSettings>>(
            _ => new Identity.DelegatingValidator<ScaleOutSettings>(s => s.Validate()));

        var settings = new ScaleOutSettings();
        configuration.GetSection(ScaleOutSettings.SectionName).Bind(settings);
        if (!settings.Enabled)
        {
            return services;
        }

        var options = ConfigurationOptions.Parse(settings.Redis);
        options.AbortOnConnectFail = false; // start even if Redis is briefly unavailable; the readiness check reports it
        options.ClientName ??= $"chatx-{Environment.MachineName}";
        var connection = new Lazy<IConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(options));
        services.AddSingleton(_ => connection.Value);
        var node = $"{Environment.MachineName}-{Guid.NewGuid():N}";
        services.AddSingleton<IClusterPresence>(sp => new RedisClusterPresence(sp.GetRequiredService<IConnectionMultiplexer>(), settings.Prefix, node, sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IRateCounter>(sp => new RedisRateCounter(sp.GetRequiredService<IConnectionMultiplexer>(), settings.Prefix, sp.GetRequiredService<TimeProvider>()));
        services.AddSignalR().AddStackExchangeRedis(o =>
        {
            o.Configuration = options;
            o.Configuration.ChannelPrefix = RedisChannel.Literal($"{settings.Prefix}:signalr");
            o.ConnectionFactory = _ => Task.FromResult(connection.Value);
        });
        services.AddHealthChecks().AddCheck<RedisHealthCheck>("redis", tags: [Infrastructure.DependencyInjection.ReadinessTag]);
        services.AddHostedService<ScaleOutNotice>();
        return services;
    }
}

/// <summary>
/// Logs at startup what scale-out does not cover: private chats (ADR-025) live in one process's
/// memory, so both people must reach the same instance.
/// </summary>
internal sealed class ScaleOutNotice(
    Microsoft.Extensions.Options.IOptionsMonitor<Application.PrivateChats.PrivateChatSettings> privateChats, ILogger<ScaleOutNotice> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Scale-out is on: realtime, presence and sign-in limits are shared through Redis.");
        if (privateChats.CurrentValue.Enabled)
        {
            logger.LogWarning(
                "Private chats are kept in one instance's memory and do not work across instances. " +
                "Run a single instance, route everyone to one instance, or switch PrivateChats:Enabled off.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Shared presence in Redis: one sorted set per user (<c>{prefix}:presence:{userId}</c>) whose members
/// are the instances that have the user online, scored by when the entry expires. A crashed
/// instance's entries simply expire.
/// </summary>
internal sealed class RedisClusterPresence(IConnectionMultiplexer redis, string prefix, string node, TimeProvider time) : IClusterPresence
{
    private RedisKey Key(Guid userId) => $"{prefix}:presence:{userId:N}";

    public Task MarkOnlineAsync(Guid userId, CancellationToken cancellationToken) => RefreshAsync([userId], cancellationToken);

    public Task MarkOfflineAsync(Guid userId, CancellationToken cancellationToken) =>
        redis.GetDatabase().SortedSetRemoveAsync(Key(userId), node);

    public async Task<IReadOnlySet<Guid>> OnlineElsewhereAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        var result = new HashSet<Guid>();
        if (userIds.Count == 0)
        {
            return result;
        }

        var now = time.GetUtcNow().ToUnixTimeMilliseconds();
        var batch = redis.GetDatabase().CreateBatch();
        var queries = userIds.Distinct().Select(id => (Id: id, Task: batch.SortedSetRangeByScoreAsync(Key(id), now, double.PositiveInfinity))).ToList();
        batch.Execute();
        foreach (var (id, task) in queries)
        {
            if ((await task.ConfigureAwait(false)).Any(member => member != node))
            {
                result.Add(id);
            }
        }

        return result;
    }

    public async Task RefreshAsync(IReadOnlyCollection<Guid> onlineHere, CancellationToken cancellationToken)
    {
        if (onlineHere.Count == 0)
        {
            return;
        }

        var expires = time.GetUtcNow().Add(ScaleOutSettings.PresenceTtl).ToUnixTimeMilliseconds();
        var batch = redis.GetDatabase().CreateBatch();
        var writes = new List<Task>();
        foreach (var id in onlineHere)
        {
            writes.Add(batch.SortedSetAddAsync(Key(id), node, expires));
            writes.Add(batch.KeyExpireAsync(Key(id), ScaleOutSettings.PresenceTtl * 2));
        }

        batch.Execute();
        await Task.WhenAll(writes).ConfigureAwait(false);
    }
}

/// <summary>Fixed-window counters in Redis (<c>{prefix}:rate:{key}:{window}</c>), shared by every instance.</summary>
internal sealed class RedisRateCounter(IConnectionMultiplexer redis, string prefix, TimeProvider time) : IRateCounter
{
    public bool TryAcquire(string key, int limit, TimeSpan window)
    {
        var bucket = time.GetUtcNow().UtcTicks / Math.Max(1, window.Ticks);
        RedisKey redisKey = $"{prefix}:rate:{key}:{bucket}";
        var database = redis.GetDatabase();
        try
        {
            var count = database.StringIncrement(redisKey);
            if (count == 1)
            {
                database.KeyExpire(redisKey, window + TimeSpan.FromSeconds(5), CommandFlags.FireAndForget);
            }

            return count <= limit;
        }
        catch (RedisException)
        {
            // Redis unavailable: fail open rather than lock everyone out; the outage shows in readiness.
            return true;
        }
    }
}

/// <summary>A <see cref="RateLimiter"/> over an <see cref="IRateCounter"/>, for ASP.NET Core rate-limiting policies.</summary>
internal sealed class CounterRateLimiter(IRateCounter counter, string key, int limit, TimeSpan window) : RateLimiter
{
    private static readonly RateLimitLease Granted = new Lease(true);
    private static readonly RateLimitLease Refused = new Lease(false);

    public override TimeSpan? IdleDuration => null;

    public override RateLimiterStatistics? GetStatistics() => null;

    protected override RateLimitLease AttemptAcquireCore(int permitCount) => counter.TryAcquire(key, limit, window) ? Granted : Refused;

    protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken) =>
        ValueTask.FromResult(AttemptAcquireCore(permitCount));

    private sealed class Lease(bool acquired) : RateLimitLease
    {
        public override bool IsAcquired => acquired;

        public override IEnumerable<string> MetadataNames => [];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }
    }
}

internal sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await redis.GetDatabase().PingAsync().ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (RedisException)
        {
            return HealthCheckResult.Unhealthy("Redis is unavailable.");
        }
    }
}
