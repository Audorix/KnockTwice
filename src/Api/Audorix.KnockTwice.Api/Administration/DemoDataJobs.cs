using Audorix.KnockTwice.Application.Administration;
using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Demo;

namespace Audorix.KnockTwice.Api.Administration;

/// <summary>
/// One demo data load or clearing in the background (owner requests 2026-11-25): both take longer
/// than browsers and proxies wait for one request, so they run here and report progress per part.
/// </summary>
internal sealed class DemoDataJob : IDemoDataProgress
{
    public const string Load = "load";
    public const string Clear = "clear";

    private readonly string[] _keys;
    private readonly int[] _totals;
    private readonly int[] _done;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DemoDataJob(Guid id, string kind, DateTimeOffset startedAtUtc, IReadOnlyList<DemoDataStep> steps)
    {
        Id = id;
        Kind = kind;
        StartedAtUtc = startedAtUtc;
        _keys = steps.Select(s => s.Key).ToArray();
        _totals = steps.Select(s => s.Total).ToArray();
        _done = new int[_keys.Length];
    }

    public Guid Id { get; }

    /// <summary><see cref="Load"/> or <see cref="Clear"/>.</summary>
    public string Kind { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset? FinishedAtUtc { get; private set; }

    /// <summary>What a completed load added and the demo accounts.</summary>
    public DemoDataResult? Result { get; private set; }

    /// <summary>What a completed clearing removed.</summary>
    public DemoDataCounts? Removed { get; private set; }

    /// <summary>Completes when the job ends; awaiting it rethrows the failure.</summary>
    public Task Completion => _completion.Task;

    public bool IsRunning => !_completion.Task.IsCompleted;

    public string State => _completion.Task.Status switch
    {
        TaskStatus.RanToCompletion => "completed",
        TaskStatus.Faulted or TaskStatus.Canceled => "failed",
        _ => "running",
    };

    public string? ErrorCode => _completion.Task.Exception?.InnerException switch
    {
        null => _completion.Task.IsCanceled ? "demo_data_cancelled" : null,
        ApplicationRuleException rule => rule.Code,
        _ => "demo_data_failed",
    };

    public void Report(string part, int done)
    {
        var index = Array.IndexOf(_keys, part);
        if (index >= 0)
        {
            Volatile.Write(ref _done[index], Math.Min(done, _totals[index]));
        }
    }

    /// <summary>Each part's progress; a completed job shows every part at 100%.</summary>
    public IReadOnlyList<(string Key, int Done, int Total)> Steps()
    {
        var completed = _completion.Task.Status == TaskStatus.RanToCompletion;
        return _keys.Select((key, i) => (key, completed ? _totals[i] : Volatile.Read(ref _done[i]), _totals[i])).ToList();
    }

    public void Complete(DemoDataResult? result, DemoDataCounts? removed, DateTimeOffset nowUtc)
    {
        Result = result;
        Removed = removed;
        FinishedAtUtc = nowUtc;
        _completion.TrySetResult();
    }

    public void Fail(Exception exception, DateTimeOffset nowUtc)
    {
        FinishedAtUtc = nowUtc;
        if (exception is OperationCanceledException)
        {
            _completion.TrySetCanceled();
        }
        else
        {
            _completion.TrySetException(exception);
        }
    }
}

/// <summary>
/// Starts demo data loads and clearings and keeps the latest one (this node only; demo data is a
/// development feature). One runs at a time: starting the same kind again returns the running one,
/// starting the other kind is refused (<c>demo_data_loading</c> or <c>demo_data_clearing</c>).
/// </summary>
internal sealed partial class DemoDataJobs(
    IServiceScopeFactory scopes, IHostApplicationLifetime lifetime, TimeProvider time, ILogger<DemoDataJobs> logger)
{
    private readonly Lock _gate = new();
    private DemoDataJob? _current;

    /// <summary>The running job, or the last one since the API started.</summary>
    public DemoDataJob? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>Starts a load (refused with <c>demo_data_disabled</c> where demo data is off) or joins the running one.</summary>
    public DemoDataJob StartLoad(Guid callerId, DemoDataService demo)
    {
        ArgumentNullException.ThrowIfNull(demo);
        demo.EnsureEnabled();
        return Start(DemoDataJob.Load, demo.Steps(), callerId, async (services, job) =>
        {
            var result = await services.GetRequiredService<DemoDataService>().PopulateAsync(callerId, job, lifetime.ApplicationStopping).ConfigureAwait(false);
            await services.GetRequiredService<AuditService>().RecordAsync(AuditActions.DemoDataPopulated, callerId, cancellationToken: lifetime.ApplicationStopping)
                .ConfigureAwait(false);
            job.Complete(result, null, time.GetUtcNow());
        });
    }

    /// <summary>Starts clearing (allowed even after demo data was switched off) or joins the running one.</summary>
    public async Task<DemoDataJob> StartClearAsync(Guid callerId, DemoDataService demo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(demo);
        if (Current is { IsRunning: true } running)
        {
            return Join(running, DemoDataJob.Clear);
        }

        var steps = await demo.ClearStepsAsync(cancellationToken).ConfigureAwait(false);
        return Start(DemoDataJob.Clear, steps, callerId, async (services, job) =>
        {
            var removed = await services.GetRequiredService<DemoDataService>().ClearAsync(job, lifetime.ApplicationStopping).ConfigureAwait(false);
            await services.GetRequiredService<AuditService>().RecordAsync(AuditActions.DemoDataCleared, callerId,
                    details: new Dictionary<string, object?> { ["users"] = removed.Users, ["conversations"] = removed.Conversations, ["messages"] = removed.Messages },
                    cancellationToken: lifetime.ApplicationStopping)
                .ConfigureAwait(false);
            job.Complete(null, removed, time.GetUtcNow());
        });
    }

    /// <summary>Waits for <paramref name="job"/>; its failure is rethrown as it happened.</summary>
    public static Task WaitAsync(DemoDataJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        return job.Completion.WaitAsync(cancellationToken);
    }

    private DemoDataJob Start(string kind, IReadOnlyList<DemoDataStep> steps, Guid callerId, Func<IServiceProvider, DemoDataJob, Task> work)
    {
        lock (_gate)
        {
            if (_current is { IsRunning: true } running)
            {
                return Join(running, kind);
            }

            var now = time.GetUtcNow();
            var job = new DemoDataJob(Guid.CreateVersion7(now), kind, now, steps);
            _current = job;
            _ = Task.Run(() => RunAsync(job, work));
            return job;
        }
    }

    private static DemoDataJob Join(DemoDataJob running, string kind) =>
        running.Kind == kind
            ? running
            : throw new ConflictException(running.Kind == DemoDataJob.Load ? "demo_data_loading" : "demo_data_clearing");

    private async Task RunAsync(DemoDataJob job, Func<IServiceProvider, DemoDataJob, Task> work)
    {
        try
        {
            var scope = scopes.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                await work(scope.ServiceProvider, job).ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // A background job reports any failure through its state instead of crashing the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            if (ex is not OperationCanceledException)
            {
                LogFailed(logger, job.Kind, ex);
            }

            job.Fail(ex, time.GetUtcNow());
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Demo data job ({Kind}) failed.")]
    private static partial void LogFailed(ILogger logger, string kind, Exception exception);
}
