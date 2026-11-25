using Audorix.KnockTwice.Application.Administration;
using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Demo;

namespace Audorix.KnockTwice.Api.Administration;

/// <summary>
/// One demo data load in the background (owner request 2026-11-25): loading takes minutes, longer
/// than browsers and proxies wait for one request, so it runs here and reports progress per part.
/// </summary>
internal sealed class DemoDataJob : IDemoDataProgress
{
    private readonly string[] _keys;
    private readonly int[] _totals;
    private readonly int[] _done;
    private readonly TaskCompletionSource<DemoDataResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DemoDataJob(Guid id, DateTimeOffset startedAtUtc, IReadOnlyList<DemoDataStep> steps)
    {
        Id = id;
        StartedAtUtc = startedAtUtc;
        _keys = steps.Select(s => s.Key).ToArray();
        _totals = steps.Select(s => s.Total).ToArray();
        _done = new int[_keys.Length];
    }

    public Guid Id { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset? FinishedAtUtc { get; private set; }

    /// <summary>The result once completed; awaiting it rethrows the failure.</summary>
    public Task<DemoDataResult> Completion => _completion.Task;

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

    /// <summary>Each part's progress; a completed load shows every part at 100%.</summary>
    public IReadOnlyList<(string Key, int Done, int Total)> Steps()
    {
        var completed = _completion.Task.Status == TaskStatus.RanToCompletion;
        return _keys.Select((key, i) => (key, completed ? _totals[i] : Volatile.Read(ref _done[i]), _totals[i])).ToList();
    }

    public void Complete(DemoDataResult result, DateTimeOffset nowUtc)
    {
        FinishedAtUtc = nowUtc;
        _completion.TrySetResult(result);
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
/// Starts demo data loads and keeps the latest one (this node only; demo data is a development
/// feature). Only one load runs at a time: starting while one runs returns the running one.
/// </summary>
internal sealed partial class DemoDataJobs(
    IServiceScopeFactory scopes, IHostApplicationLifetime lifetime, TimeProvider time, ILogger<DemoDataJobs> logger)
{
    private readonly Lock _gate = new();
    private DemoDataJob? _current;

    /// <summary>The running load, or the last one since the API started.</summary>
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

    public bool IsRunning => Current is { IsRunning: true };

    /// <summary>Starts a load (refused with <c>demo_data_disabled</c> where demo data is off) or joins the running one.</summary>
    public DemoDataJob Start(Guid callerId, DemoDataService demo)
    {
        ArgumentNullException.ThrowIfNull(demo);
        demo.EnsureEnabled();
        lock (_gate)
        {
            if (_current is { IsRunning: true } running)
            {
                return running;
            }

            var now = time.GetUtcNow();
            var job = new DemoDataJob(Guid.CreateVersion7(now), now, demo.Steps());
            _current = job;
            _ = Task.Run(() => RunAsync(job, callerId));
            return job;
        }
    }

    /// <summary>Waits for <paramref name="job"/> and returns its result; its failure is rethrown as it happened.</summary>
    public static Task<DemoDataResult> WaitAsync(DemoDataJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        return job.Completion.WaitAsync(cancellationToken);
    }

    private async Task RunAsync(DemoDataJob job, Guid callerId)
    {
        try
        {
            var scope = scopes.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var demo = scope.ServiceProvider.GetRequiredService<DemoDataService>();
                var audit = scope.ServiceProvider.GetRequiredService<AuditService>();
                var result = await demo.PopulateAsync(callerId, job, lifetime.ApplicationStopping).ConfigureAwait(false);
                await audit.RecordAsync(AuditActions.DemoDataPopulated, callerId, cancellationToken: lifetime.ApplicationStopping).ConfigureAwait(false);
                job.Complete(result, time.GetUtcNow());
            }
        }
#pragma warning disable CA1031 // A background load reports any failure through its state instead of crashing the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            if (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }

            job.Fail(ex, time.GetUtcNow());
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Loading demo data failed.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
