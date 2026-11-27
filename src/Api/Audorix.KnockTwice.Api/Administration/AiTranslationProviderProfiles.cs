using System.Globalization;
using System.Text.Json;
using Audorix.KnockTwice.Api.Setup;
using Audorix.KnockTwice.Application.Administration;
using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Translation;
using Audorix.KnockTwice.Infrastructure.AI;
using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Api.Administration;

internal sealed record AiTranslationProviderProfileRequest(
    string? Name,
    string? Provider,
    string? Endpoint,
    string? Model,
    string? ApiKey,
    bool ClearApiKey = false,
    int MaxOutputTokens = 4000,
    int TimeoutSeconds = 45);

internal sealed record AiTranslationProviderProfileView(
    string Id,
    string Name,
    string Provider,
    string Endpoint,
    string Model,
    bool HasApiKey,
    int MaxOutputTokens,
    int TimeoutSeconds,
    bool Active,
    bool Legacy = false);

/// <summary>Stores named translation provider profiles in the private installation settings file.</summary>
internal sealed class AiTranslationProviderProfiles(
    IConfiguration configuration,
    ConfigurationSources sources,
    AuditService audit,
    IHttpClientFactory clients)
{
    private const string Section = "AI:Translation";
    private const string ActiveKey = Section + ":ActiveProviderId";
    private const string ProviderKey = Section + ":Provider";
    private const string ProfilePrefix = Section + ":Providers:";
    private static readonly SemaphoreSlim SaveLock = new(1, 1);

    public IReadOnlyList<AiTranslationProviderProfileView> List()
    {
        var active = configuration[ActiveKey];
        var result = configuration.GetSection(Section + ":Providers").GetChildren()
            .Select(section => ToView(section.Key, section, string.Equals(active, section.Key, StringComparison.Ordinal)))
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Keep installations configured before profiles existed usable and make their active
        // connection visible until the administrator creates or selects a named profile.
        if (!string.IsNullOrWhiteSpace(configuration[ProviderKey]) && configuration[ProviderKey] != "None")
        {
            var timeout = TimeSpan.TryParse(configuration[Section + ":Timeout"], CultureInfo.InvariantCulture, out var configuredTimeout)
                ? configuredTimeout : TimeSpan.FromSeconds(45);
            result.Add(new AiTranslationProviderProfileView(
                "legacy", "Current translation provider", configuration[ProviderKey]!, configuration[Section + ":Endpoint"] ?? "",
                configuration[Section + ":Model"] ?? "", !string.IsNullOrWhiteSpace(configuration[Section + ":ApiKey"]),
                ParseInt(configuration[Section + ":MaxOutputTokens"], 4000), (int)timeout.TotalSeconds, string.IsNullOrWhiteSpace(active), true));
        }

        return result;
    }

    public async Task<AiTranslationProviderProfileView> SaveAsync(Guid actorId, string? id, AiTranslationProviderProfileRequest request, CancellationToken ct)
    {
        var profileId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : RequireProfileId(id);
        var existing = configuration.GetSection(ProfilePrefix + profileId);
        var apiKey = request.ClearApiKey ? null : string.IsNullOrWhiteSpace(request.ApiKey) ? existing["ApiKey"] : request.ApiKey.Trim();
        var options = new AiTranslationOptions
        {
            Provider = request.Provider?.Trim() ?? "",
            Endpoint = request.Endpoint?.Trim() ?? "",
            Model = request.Model?.Trim() ?? "",
            ApiKey = apiKey ?? "",
            MaxOutputTokens = request.MaxOutputTokens,
            Timeout = TimeSpan.FromSeconds(request.TimeoutSeconds),
        };
        Validate(request.Name, options);
        if (options.Provider == "None") throw new RequestValidationException("provider", "provider_required");
        await SaveLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (List().Any(profile => !profile.Legacy && profile.Id != profileId && string.Equals(profile.Name, request.Name!.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new RequestValidationException("name", "provider_name_in_use");
            var changes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                [$"{ProfilePrefix}{profileId}:Name"] = request.Name!.Trim(),
                [$"{ProfilePrefix}{profileId}:Provider"] = options.Provider,
                [$"{ProfilePrefix}{profileId}:Endpoint"] = options.Endpoint,
                [$"{ProfilePrefix}{profileId}:Model"] = options.Model,
                [$"{ProfilePrefix}{profileId}:MaxOutputTokens"] = options.MaxOutputTokens.ToString(CultureInfo.InvariantCulture),
                [$"{ProfilePrefix}{profileId}:Timeout"] = options.Timeout.ToString("c", CultureInfo.InvariantCulture),
            };
            if (request.ClearApiKey) changes[$"{ProfilePrefix}{profileId}:ApiKey"] = null;
            else if (!string.IsNullOrWhiteSpace(request.ApiKey) || string.IsNullOrWhiteSpace(id)) changes[$"{ProfilePrefix}{profileId}:ApiKey"] = apiKey;
            if (string.IsNullOrWhiteSpace(configuration[ActiveKey]) &&
                (string.IsNullOrWhiteSpace(configuration[ProviderKey]) || configuration[ProviderKey] == "None"))
                changes[ActiveKey] = profileId;
            EnsureWritable(changes.Keys);

            await ChatXSettingsFile.MergeAsync(sources.SettingsFilePath, changes, ct).ConfigureAwait(false);
            ReloadConfiguration();
            await audit.RecordAsync(AuditActions.ProviderChanged, actorId, "ai_translation_provider", profileId,
                new Dictionary<string, object?> { ["provider"] = options.Provider, ["settings"] = changes.Keys.Order(StringComparer.Ordinal).ToList() }, cancellationToken: ct).ConfigureAwait(false);
        }
        finally { SaveLock.Release(); }

        var saved = configuration.GetSection(ProfilePrefix + profileId);
        return ToView(profileId, saved, string.Equals(configuration[ActiveKey] ?? profileId, profileId, StringComparison.Ordinal));
    }

    public async Task ActivateAsync(Guid actorId, string id, CancellationToken ct)
    {
        if (id == "legacy")
        {
            if (string.IsNullOrWhiteSpace(configuration[ProviderKey]) || configuration[ProviderKey] == "None")
                throw new ResourceNotFoundException("provider_not_found");
            EnsureWritable([ActiveKey]);
            await SaveLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await ChatXSettingsFile.MergeAsync(sources.SettingsFilePath, new Dictionary<string, string?> { [ActiveKey] = null }, ct).ConfigureAwait(false);
                ReloadConfiguration();
            }
            finally { SaveLock.Release(); }
            await audit.RecordAsync(AuditActions.ProviderChanged, actorId, "ai_translation_provider", id,
                new Dictionary<string, object?> { ["active"] = true }, cancellationToken: ct).ConfigureAwait(false);
            return;
        }
        var profileId = RequireProfileId(id);
        if (!configuration.GetSection(ProfilePrefix + profileId).Exists()) throw new ResourceNotFoundException("provider_not_found");
        EnsureWritable([ActiveKey]);
        await SaveLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ChatXSettingsFile.MergeAsync(sources.SettingsFilePath, new Dictionary<string, string?> { [ActiveKey] = profileId }, ct).ConfigureAwait(false);
            ReloadConfiguration();
        }
        finally { SaveLock.Release(); }
        await audit.RecordAsync(AuditActions.ProviderChanged, actorId, "ai_translation_provider", profileId,
            new Dictionary<string, object?> { ["active"] = true }, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid actorId, string id, CancellationToken ct)
    {
        var profileId = RequireProfileId(id);
        if (!configuration.GetSection(ProfilePrefix + profileId).Exists()) throw new ResourceNotFoundException("provider_not_found");
        if (string.Equals(configuration[ActiveKey], profileId, StringComparison.Ordinal))
            throw new RequestValidationException("provider", "active_provider_cannot_be_deleted");
        var changes = configuration.GetSection(ProfilePrefix + profileId).GetChildren()
            .Select(child => new KeyValuePair<string, string?>($"{ProfilePrefix}{profileId}:{child.Key}", null))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        EnsureWritable(changes.Keys);
        await SaveLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (string.Equals(configuration[ActiveKey], profileId, StringComparison.Ordinal))
                throw new RequestValidationException("provider", "active_provider_cannot_be_deleted");
            await ChatXSettingsFile.MergeAsync(sources.SettingsFilePath, changes, ct).ConfigureAwait(false);
            ReloadConfiguration();
        }
        finally { SaveLock.Release(); }
        await audit.RecordAsync(AuditActions.ProviderChanged, actorId, "ai_translation_provider", profileId,
            new Dictionary<string, object?> { ["deleted"] = true }, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<string> TestAsync(Guid actorId, string id, CancellationToken ct)
    {
        var legacy = id == "legacy";
        var profileId = legacy ? id : RequireProfileId(id);
        var section = legacy ? configuration.GetSection(Section) : configuration.GetSection(ProfilePrefix + profileId);
        if (!legacy && !section.Exists()) throw new ResourceNotFoundException("provider_not_found");
        var options = Bind(section);
        Validate(legacy ? "Current translation provider" : section["Name"], options);
        try
        {
            // Force the test adapter to use the selected profile even when it is not active.
            var overrides = legacy
                ? new Dictionary<string, string?> { [ActiveKey] = null }
                : new Dictionary<string, string?> { [ActiveKey] = profileId };
            var testConfig = new ConfigurationBuilder().AddConfiguration(configuration).AddInMemoryCollection(overrides).Build();
            var provider = new OpenAiCompatibleMessageTranslationProvider(clients.CreateClient(), testConfig, Options.Create(options));
            var response = await provider.TranslateAsync("Hello", "es", ct).ConfigureAwait(false);
            var outcome = string.IsNullOrWhiteSpace(response) ? "failed" : "ok";
            await RecordTestAsync(actorId, profileId, outcome, outcome == "ok", ct).ConfigureAwait(false);
            return outcome;
        }
        catch (AiTranslationUnavailableException ex)
        {
            await RecordTestAsync(actorId, profileId, ex.Code, false, ct).ConfigureAwait(false);
            return ex.Code;
        }
    }

    private static AiTranslationOptions Bind(IConfigurationSection section)
    {
        var options = new AiTranslationOptions();
        section.Bind(options);
        return options;
    }

    private static AiTranslationProviderProfileView ToView(string id, IConfigurationSection section, bool active) => new(
        id,
        section["Name"] ?? "Translation provider",
        section["Provider"] ?? "None",
        section["Endpoint"] ?? "",
        section["Model"] ?? "",
        !string.IsNullOrWhiteSpace(section["ApiKey"]),
        ParseInt(section["MaxOutputTokens"], 4000),
        (int)Bind(section).Timeout.TotalSeconds,
        active);

    private static int ParseInt(string? value, int fallback) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private Task RecordTestAsync(Guid actorId, string profileId, string outcome, bool succeeded, CancellationToken ct) =>
        audit.RecordAsync(AuditActions.ProviderTested, actorId, "ai_translation_provider", profileId,
            new Dictionary<string, object?> { ["outcome"] = outcome }, succeeded: succeeded, cancellationToken: ct);

    private void ReloadConfiguration()
    {
        if (configuration is IConfigurationRoot root) root.Reload();
    }

    private void EnsureWritable(IEnumerable<string> keys)
    {
        var locked = keys.FirstOrDefault(key => sources.Describe(key).Locked);
        if (locked is not null) throw new RequestValidationException(locked, "setting_locked");
    }

    private static string RequireProfileId(string id) => Guid.TryParseExact(id, "N", out _) ? id : throw new ResourceNotFoundException("provider_not_found");

    private static void Validate(string? name, AiTranslationOptions options)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80)
            throw new RequestValidationException("name", "provider_name_required");
        var result = new AiTranslationOptionsValidator().Validate(null, options);
        if (result.Failed) throw new SetupException(StatusCodes.Status400BadRequest, "settings_invalid", null, result.Failures.ToList());
    }
}
