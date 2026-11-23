using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Api.Setup;

/// <summary>
/// Process-wide setup state: the setup code that proves operator access (it is printed to the
/// server log, like a first-run unlock code), one completion at a time, and whether the host
/// should restart into normal mode.
/// </summary>
internal sealed class SetupCoordinator : IDisposable
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private readonly byte[] _token;
    private readonly SemaphoreSlim _completion = new(1, 1);

    public SetupCoordinator(IOptions<SetupSettings> settings)
    {
        var configured = settings.Value.Token;
        if (string.IsNullOrWhiteSpace(configured))
        {
            var generated = RandomNumberGenerator.GetString(Alphabet, 12);
            DisplayToken = $"{generated[..4]}-{generated[4..8]}-{generated[8..]}";
            TokenWasGenerated = true;
        }
        else
        {
            DisplayToken = configured.Trim();
        }

        _token = Encoding.UTF8.GetBytes(Normalize(DisplayToken));
    }

    /// <summary>The code as shown to the operator.</summary>
    public string DisplayToken { get; }

    public bool TokenWasGenerated { get; }

    public bool Completed { get; private set; }

    public bool RestartRequested { get; private set; }

    public bool VerifyToken(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > 128)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Normalize(candidate)), _token);
    }

    /// <summary>Runs <paramref name="work"/> exclusively; a concurrent completion is rejected instead of queued.</summary>
    public async Task<T> CompleteExclusivelyAsync<T>(Func<Task<T>> work, bool restart)
    {
        if (!await _completion.WaitAsync(TimeSpan.Zero).ConfigureAwait(false))
        {
            throw new SetupException(StatusCodes.Status409Conflict, Contracts.Errors.ErrorCodes.SetupInProgress);
        }

        try
        {
            EnsurePending();
            var result = await work().ConfigureAwait(false);
            Completed = true;
            RestartRequested = restart;
            return result;
        }
        finally
        {
            _completion.Release();
        }
    }

    public void EnsurePending()
    {
        if (Completed)
        {
            throw new SetupException(StatusCodes.Status409Conflict, Contracts.Errors.ErrorCodes.SetupAlreadyCompleted);
        }
    }

    public void Dispose() => _completion.Dispose();

    // Codes are case-insensitive and may be typed with or without separators.
    private static string Normalize(string value) =>
        new(value.Where(c => c is not ('-' or ' ')).Select(char.ToUpperInvariant).ToArray());
}

/// <summary>A setup request failed with a stable code; optional per-field codes and operator-facing details.</summary>
internal sealed class SetupException(
    int status,
    string code,
    IReadOnlyDictionary<string, string[]>? errors = null,
    IReadOnlyList<string>? details = null) : Exception($"Setup request failed: {code}.")
{
    public int Status { get; } = status;

    public string Code { get; } = code;

    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;

    /// <summary>Configuration validation messages. They name settings but never contain their values.</summary>
    public IReadOnlyList<string>? Details { get; } = details;
}
