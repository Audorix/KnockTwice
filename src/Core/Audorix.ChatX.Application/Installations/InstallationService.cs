using Audorix.ChatX.Application.Localization;
using Audorix.ChatX.Domain;
using Audorix.ChatX.Domain.Installations;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Application.Installations;

/// <summary>
/// Use cases for installation metadata. Initialization is idempotent: running it again
/// (for example by re-running the migration command) never creates a second record.
/// </summary>
public sealed class InstallationService(
    IInstallationStore store,
    TimeProvider timeProvider,
    IOptions<LocalizationSettings> localization)
{
    public async Task<InstallationInitializationResult> EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        var existing = await store.FindAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return new InstallationInitializationResult(existing, Created: false);
        }

        var candidate = Installation.Initialize(
            Guid.CreateVersion7(timeProvider.GetUtcNow()),
            localization.Value.DefaultLanguage,
            timeProvider.GetUtcNow());

        if (await store.TryAddAsync(candidate, cancellationToken).ConfigureAwait(false))
        {
            return new InstallationInitializationResult(candidate, Created: true);
        }

        // A concurrent initializer won the unique constraint; return its record.
        var winner = await store.FindAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Installation record conflicted but could not be read back.");
        return new InstallationInitializationResult(winner, Created: false);
    }

    public Task<Installation?> FindAsync(CancellationToken cancellationToken) => store.FindAsync(cancellationToken);

    public async Task<Installation> ChangeDefaultLanguageAsync(string language, CancellationToken cancellationToken)
    {
        var normalized = language?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!localization.Value.EffectiveSupportedLanguages.Contains(normalized))
        {
            throw new DomainRuleException("unsupported_language", "The language is not supported by this installation.");
        }

        var installation = await store.FindAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new DomainRuleException("installation_not_initialized", "The installation has not been initialized.");

        installation.ChangeDefaultLanguage(normalized, timeProvider.GetUtcNow());
        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return installation;
    }
}

public sealed record InstallationInitializationResult(Installation Installation, bool Created);
