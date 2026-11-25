namespace Audorix.KnockTwice.Api.Hosting;

/// <summary>
/// A background job that ends quietly when the host stops: cancellation caused by shutdown (from
/// the timer wait or from work in progress) is caught here instead of leaving the job as an
/// unhandled exception. The host treated both the same, but a debugger stopped on the latter every
/// time the API was stopped. Any other failure, and cancellation that is not a shutdown, still
/// surfaces as before.
/// </summary>
internal abstract class QuietBackgroundService : BackgroundService
{
    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping: nothing to report.
        }
    }

    /// <summary>The job's loop; return or let shutdown cancellation end it.</summary>
    protected abstract Task RunAsync(CancellationToken stoppingToken);
}
