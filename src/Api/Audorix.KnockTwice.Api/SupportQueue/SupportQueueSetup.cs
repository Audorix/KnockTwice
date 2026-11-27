using Audorix.KnockTwice.Api.Hosting;
using Audorix.KnockTwice.Application.Conversations;
using Audorix.KnockTwice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Audorix.KnockTwice.Api.SupportQueue;

internal sealed class SupportQueueSettings
{
    public const string SectionName = "SupportQueue";
    public string Provider { get; set; } = "Database";
    public RedisQueueSettings Redis { get; set; } = new();
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxAttempts { get; set; } = 5;
}

internal sealed class RedisQueueSettings
{
    public string ConnectionString { get; set; } = string.Empty;
    public string StreamPrefix { get; set; } = "knocktwice-support";
}

internal sealed class SupportQueueSettingsValidator : IValidateOptions<SupportQueueSettings>
{
    public ValidateOptionsResult Validate(string? name, SupportQueueSettings settings)
    {
        var errors = new List<string>();
        if (!string.Equals(settings.Provider, "Database", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(settings.Provider, "Redis", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("SupportQueue:Provider must be Database or Redis.");
        }

        if (string.Equals(settings.Provider, "Redis", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(settings.Redis.ConnectionString))
            {
                errors.Add("SupportQueue:Redis:ConnectionString is required when Redis is selected.");
            }
            else
            {
                try { _ = ConfigurationOptions.Parse(settings.Redis.ConnectionString); }
                catch (ArgumentException) { errors.Add("SupportQueue:Redis:ConnectionString is invalid."); }
            }

            if (string.IsNullOrWhiteSpace(settings.Redis.StreamPrefix) || settings.Redis.StreamPrefix.Length > 48
                || settings.Redis.StreamPrefix.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            {
                errors.Add("SupportQueue:Redis:StreamPrefix must be 1-48 letters, digits, '-' or '_'.");
            }
        }

        if (settings.PollInterval < TimeSpan.FromMilliseconds(100) || settings.PollInterval > TimeSpan.FromMinutes(1))
        {
            errors.Add("SupportQueue:PollInterval must be between 100ms and 1m.");
        }

        if (settings.LeaseDuration < TimeSpan.FromSeconds(5) || settings.LeaseDuration > TimeSpan.FromMinutes(10))
        {
            errors.Add("SupportQueue:LeaseDuration must be between 5s and 10m.");
        }

        if (settings.MaxAttempts is < 1 or > 20)
        {
            errors.Add("SupportQueue:MaxAttempts must be between 1 and 20.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

internal static class SupportQueueSetup
{
    public static IServiceCollection AddSupportRoutingQueue(this IServiceCollection services)
    {
        services.AddOptions<SupportQueueSettings>()
            .BindConfiguration(SupportQueueSettings.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SupportQueueSettings>, SupportQueueSettingsValidator>();
        services.AddHostedService<SupportRoutingQueueWorker>();
        return services;
    }
}

/// <summary>
/// Dispatches transactional support events. Database mode leases directly from the relational
/// outbox. Redis mode relays those same rows to a durable Stream, then uses a consumer group.
/// </summary>
internal sealed class SupportRoutingQueueWorker(
    IServiceScopeFactory scopes,
    IOptions<SupportQueueSettings> settings,
    TimeProvider time,
    ILogger<SupportRoutingQueueWorker> logger) : QuietBackgroundService
{
    private const string ConsumerGroup = "support-routing-v1";
    private const int BatchSize = 25;

    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        var options = settings.Value;
        if (string.Equals(options.Provider, "Database", StringComparison.OrdinalIgnoreCase))
        {
            await RunDatabaseAsync(options, stoppingToken).ConfigureAwait(false);
            return;
        }

        await RunRedisAsync(options, stoppingToken).ConfigureAwait(false);
    }

    private async Task RunDatabaseAsync(SupportQueueSettings options, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.PollInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                for (var i = 0; i < BatchSize && await ProcessDatabaseItemAsync(options, stoppingToken).ConfigureAwait(false); i++) { }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Database support routing dispatch failed ({ExceptionType}).", ex.GetType().Name);
            }
        }
    }

    private async Task RunRedisAsync(SupportQueueSettings options, CancellationToken stoppingToken)
    {
        var redisOptions = ConfigurationOptions.Parse(options.Redis.ConnectionString);
        redisOptions.AbortOnConnectFail = false;
        redisOptions.ConnectTimeout = 3000;
        redisOptions.AsyncTimeout = 5000;
        var stream = (RedisKey)$"{options.Redis.StreamPrefix}:routing";
        var deadLetter = (RedisKey)$"{options.Redis.StreamPrefix}:routing:dead";
        var consumer = $"{Environment.MachineName}-{Guid.NewGuid():N}";
        RedisValue claimStartId = "0-0";
        ConnectionMultiplexer? connection = null;
        using var timer = new PeriodicTimer(options.PollInterval, time);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    if (connection is null || !connection.IsConnected)
                    {
                        connection?.Dispose();
                        connection = await ConnectionMultiplexer.ConnectAsync(redisOptions).ConfigureAwait(false);
                    }

                    var database = connection.GetDatabase();
                    await EnsureConsumerGroupAsync(database, stream).ConfigureAwait(false);
                    for (var i = 0; i < BatchSize && await RelayOneAsync(database, stream, options, stoppingToken).ConfigureAwait(false); i++) { }
                    claimStartId = await ConsumeBatchAsync(database, stream, deadLetter, consumer, options, claimStartId, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Redis support routing dispatch failed ({ExceptionType}); the database outbox will retry unpublished work.", ex.GetType().Name);
                    connection?.Dispose();
                    connection = null;
                }
            }
        }
        finally
        {
            connection?.Dispose();
        }
    }

    private async Task<bool> ProcessDatabaseItemAsync(SupportQueueSettings options, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatXDbContext>();
        var item = await TryLeaseOutboxItemAsync(db, null, options, cancellationToken).ConfigureAwait(false);
        if (item is null) return false;

        try
        {
            await scope.ServiceProvider.GetRequiredService<ISupportRoutingNotifier>()
                .NotifySupportRoutingAsync(item.ConversationId, cancellationToken).ConfigureAwait(false);
            await CompleteAsync(db, item.Id, item.LeaseToken!.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await FailAsync(db, item, options, ex, cancellationToken).ConfigureAwait(false);
            logger.LogWarning("Support routing work {WorkId} failed and will be retried.", item.Id);
        }

        return true;
    }

    private async Task<bool> RelayOneAsync(IDatabase database, RedisKey stream, SupportQueueSettings options, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatXDbContext>();
        var item = await TryLeaseOutboxItemAsync(db, null, options, cancellationToken).ConfigureAwait(false);
        if (item is null) return false;

        try
        {
            await database.StreamAddAsync(stream,
            [
                new NameValueEntry("workId", item.Id.ToString("N")),
                new NameValueEntry("organizationId", item.OrganizationId.ToString("N")),
                new NameValueEntry("conversationId", item.ConversationId.ToString("N")),
                new NameValueEntry("eventType", item.EventType),
                new NameValueEntry("enqueuedAtUtc", item.EnqueuedAtUtc.ToString("O")),
            ]).WaitAsync(cancellationToken).ConfigureAwait(false);

            var now = time.GetUtcNow();
            await db.SupportRoutingOutbox.Where(x => x.Id == item.Id && x.LeaseToken == item.LeaseToken)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.PublishedAtUtc, now)
                    .SetProperty(x => x.LeaseExpiresAtTicks, (long?)null)
                    .SetProperty(x => x.LeaseToken, (Guid?)null)
                    .SetProperty(x => x.LeaseExpiresAtUtc, (DateTimeOffset?)null)
                    .SetProperty(x => x.LastError, (string?)null), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await FailAsync(db, item, options, ex, cancellationToken).ConfigureAwait(false);
            throw;
        }

        return true;
    }

    private async Task<RedisValue> ConsumeBatchAsync(IDatabase database, RedisKey stream, RedisKey deadLetter, string consumer,
        SupportQueueSettings options, RedisValue claimStartId, CancellationToken cancellationToken)
    {
        // Reclaim messages left pending by a crashed consumer before reading new entries.
        var reclaimed = await database.StreamAutoClaimAsync(stream, ConsumerGroup, consumer, (long)options.LeaseDuration.TotalMilliseconds,
            claimStartId, BatchSize).WaitAsync(cancellationToken).ConfigureAwait(false);
        foreach (var entry in reclaimed.ClaimedEntries)
        {
            await ProcessRedisEntryAsync(database, stream, deadLetter, entry, options, cancellationToken).ConfigureAwait(false);
        }

        var entries = await database.StreamReadGroupAsync(stream, ConsumerGroup, consumer, ">", BatchSize)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        foreach (var entry in entries)
        {
            await ProcessRedisEntryAsync(database, stream, deadLetter, entry, options, cancellationToken).ConfigureAwait(false);
        }

        return reclaimed.NextStartId;
    }

    private async Task ProcessRedisEntryAsync(IDatabase database, RedisKey stream, RedisKey deadLetter,
        StreamEntry entry, SupportQueueSettings options, CancellationToken cancellationToken)
    {
        var rawId = entry.Values.FirstOrDefault(v => v.Name == "workId").Value;
        if (!Guid.TryParseExact(rawId.ToString(), "N", out var workId))
        {
            await database.StreamAcknowledgeAsync(stream, ConsumerGroup, entry.Id).WaitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatXDbContext>();
        var item = await FindOutboxItemAsync(db, workId, cancellationToken).ConfigureAwait(false);
        if (item is null || item.CompletedAtUtc is not null || item.DeadLetteredAtUtc is not null)
        {
            await database.StreamAcknowledgeAsync(stream, ConsumerGroup, entry.Id).WaitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var leased = await TryLeaseOutboxItemAsync(db, workId, options, cancellationToken).ConfigureAwait(false);
        if (leased is null) return;
        try
        {
            await scope.ServiceProvider.GetRequiredService<ISupportRoutingNotifier>()
                .NotifySupportRoutingAsync(leased.ConversationId, cancellationToken).ConfigureAwait(false);
            await CompleteAsync(db, leased.Id, leased.LeaseToken!.Value, cancellationToken).ConfigureAwait(false);
            await database.StreamAcknowledgeAsync(stream, ConsumerGroup, entry.Id).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var dead = await FailAsync(db, leased, options, ex, cancellationToken).ConfigureAwait(false);
            if (dead)
            {
                await database.StreamAddAsync(deadLetter,
                [
                    new NameValueEntry("workId", leased.Id.ToString("N")),
                    new NameValueEntry("error", (leased.LastError ?? ex.GetType().Name)[..Math.Min(1000, (leased.LastError ?? ex.GetType().Name).Length)]),
                    new NameValueEntry("failedAtUtc", time.GetUtcNow().ToString("O")),
                ]).WaitAsync(cancellationToken).ConfigureAwait(false);
                await database.StreamAcknowledgeAsync(stream, ConsumerGroup, entry.Id).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<SupportRoutingOutboxItem?> TryLeaseOutboxItemAsync(ChatXDbContext db, Guid? workId,
        SupportQueueSettings options, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var nowTicks = now.UtcTicks;
        var candidate = db.SupportRoutingOutbox.AsNoTracking()
            .Where(x => x.CompletedAtUtc == null && x.DeadLetteredAtUtc == null && x.AvailableAtTicks <= nowTicks
                && (x.LeaseExpiresAtTicks == null || x.LeaseExpiresAtTicks <= nowTicks));
        if (workId is { } id) candidate = candidate.Where(x => x.Id == id && x.PublishedAtUtc != null);
        else candidate = candidate.Where(x => x.PublishedAtUtc == null);

        var candidateId = workId ?? await candidate.OrderBy(x => x.EnqueuedAtTicks).Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (candidateId is null) return null;

        var token = Guid.NewGuid();
        var updated = await db.SupportRoutingOutbox.Where(x => x.Id == candidateId.Value && x.CompletedAtUtc == null
                && x.DeadLetteredAtUtc == null && x.AvailableAtTicks <= nowTicks && (x.LeaseExpiresAtTicks == null || x.LeaseExpiresAtTicks <= nowTicks)
                && (workId.HasValue ? x.PublishedAtUtc != null : x.PublishedAtUtc == null))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.LeaseToken, (Guid?)token)
                .SetProperty(x => x.LeaseExpiresAtUtc, now + options.LeaseDuration)
                .SetProperty(x => x.LeaseExpiresAtTicks, (long?)(now + options.LeaseDuration).UtcTicks)
                .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1), cancellationToken).ConfigureAwait(false);
        return updated == 1 ? await FindOutboxItemAsync(db, candidateId.Value, cancellationToken).ConfigureAwait(false) : null;
    }

    private static Task<SupportRoutingOutboxItem?> FindOutboxItemAsync(ChatXDbContext db, Guid id, CancellationToken cancellationToken) =>
        db.SupportRoutingOutbox.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    private async Task CompleteAsync(ChatXDbContext db, Guid id, Guid token, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await db.SupportRoutingOutbox.Where(x => x.Id == id && x.LeaseToken == token)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.CompletedAtUtc, now)
                .SetProperty(x => x.LeaseToken, (Guid?)null)
                .SetProperty(x => x.LeaseExpiresAtUtc, (DateTimeOffset?)null)
                .SetProperty(x => x.LeaseExpiresAtTicks, (long?)null)
                .SetProperty(x => x.LastError, (string?)null), cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> FailAsync(ChatXDbContext db, SupportRoutingOutboxItem item, SupportQueueSettings options,
        Exception exception, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var error = exception.GetType().Name + ": " + exception.Message;
        if (error.Length > 1000) error = error[..1000];
        var dead = item.AttemptCount >= options.MaxAttempts;
        await db.SupportRoutingOutbox.Where(x => x.Id == item.Id && x.LeaseToken == item.LeaseToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.DeadLetteredAtUtc, dead ? now : null)
                .SetProperty(x => x.AvailableAtUtc, now + TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, item.AttemptCount))))
                .SetProperty(x => x.AvailableAtTicks, (now + TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, item.AttemptCount)))).UtcTicks)
                .SetProperty(x => x.LeaseToken, (Guid?)null)
                .SetProperty(x => x.LeaseExpiresAtUtc, (DateTimeOffset?)null)
                .SetProperty(x => x.LeaseExpiresAtTicks, (long?)null)
                .SetProperty(x => x.LastError, error), cancellationToken).ConfigureAwait(false);
        return dead;
    }

    private static async Task EnsureConsumerGroupAsync(IDatabase database, RedisKey stream)
    {
        try { await database.StreamCreateConsumerGroupAsync(stream, ConsumerGroup, "0-0", createStream: true).ConfigureAwait(false); }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase)) { }
    }
}
