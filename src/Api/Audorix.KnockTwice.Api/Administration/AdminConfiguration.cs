using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Audorix.KnockTwice.Api.Setup;
using Audorix.KnockTwice.Application.Abstractions;
using Audorix.KnockTwice.Application.Administration;
using Audorix.KnockTwice.Application.Attachments;
using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Identity;
using Audorix.KnockTwice.Application.Localization;
using Audorix.KnockTwice.Application.Messaging;
using Audorix.KnockTwice.Application.PrivateChats;
using Audorix.KnockTwice.Infrastructure.Captcha;
using Audorix.KnockTwice.Infrastructure.AI;
using Audorix.KnockTwice.Infrastructure.Email;
using Audorix.KnockTwice.Infrastructure.Storage;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Api.Administration;

/// <summary>Where a configuration value comes from, and whether the admin UI may change it.</summary>
internal sealed record SettingSource(string Source, bool Locked);

/// <summary>Finds the configuration source of keys relative to the live-settings layer.</summary>
internal sealed class ConfigurationSources(IConfiguration configuration, RuntimeSettingsConfigurationProvider runtime, IWebHostEnvironment environment)
{
    public string SettingsFilePath => ChatXSettingsFile.ResolvePath(configuration, environment.ContentRootPath);

    /// <summary>The winning source; locked when a source above the live-settings layer (environment, command line) sets the key.</summary>
    public SettingSource Describe(string key)
    {
        var providers = ((IConfigurationRoot)configuration).Providers.ToList();
        var runtimeIndex = providers.IndexOf(runtime);
        var settingsPath = SettingsFilePath;
        for (var i = providers.Count - 1; i >= 0; i--)
        {
            if (!Supplies(providers[i], key))
            {
                continue;
            }

            var provider = providers[i];
            var name = provider switch
            {
                RuntimeSettingsConfigurationProvider => "database",
                JsonConfigurationProvider json when json.Source.Path is { } path && SamePath(json.Source.FileProvider, path, settingsPath) => "file",
                JsonConfigurationProvider => "appsettings",
                EnvironmentVariablesConfigurationProvider => "environment",
                CommandLineConfigurationProvider => "command_line",
                MemoryConfigurationProvider => "host",
                _ => "other",
            };
            return new SettingSource(name, i > runtimeIndex);
        }

        return new SettingSource("default", false);
    }

    /// <summary>The value the key would have without the live-settings layer (for "reset to default").</summary>
    public string? ValueBelowRuntime(string key)
    {
        var providers = ((IConfigurationRoot)configuration).Providers.ToList();
        for (var i = providers.IndexOf(runtime) - 1; i >= 0; i--)
        {
            if (providers[i].TryGet(key, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool Supplies(IConfigurationProvider provider, string key) =>
        (provider.TryGet(key, out var value) && value is not null) || provider.GetChildKeys([], key).Any();

    private static bool SamePath(Microsoft.Extensions.FileProviders.IFileProvider? files, string path, string settingsPath)
    {
        var full = files is Microsoft.Extensions.FileProviders.PhysicalFileProvider physical ? Path.Combine(physical.Root, path) : path;
        return string.Equals(Path.GetFullPath(full), settingsPath, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record RuntimeSettingView(string Key, string Group, string Kind, object? Value, string Source, bool Locked, long? Min, long? Max, IReadOnlyList<string>? Choices);

/// <summary>Reads and changes the live settings (database layer).</summary>
internal sealed class RuntimeSettingsManager(
    IConfiguration configuration,
    ConfigurationSources sources,
    IInstallationSettingsStore store,
    RuntimeSettingsLoaderAccessor loader,
    AuditService audit,
    TimeProvider time)
{
    public IReadOnlyList<RuntimeSettingView> Describe() =>
        RuntimeSettingsCatalog.All.Select(d =>
        {
            var source = sources.Describe(d.Key);
            var value = source.Source == "default" ? RuntimeSettingDefaults.For(d) : RuntimeSettingsCatalog.Read(d, configuration);
            return new RuntimeSettingView(d.Key, d.Group, KindName(d.Kind), value, source.Source, source.Locked,
                d.Kind is SettingKind.Integer or SettingKind.Bytes or SettingKind.Duration ? d.Min : null,
                d.Kind is SettingKind.Integer or SettingKind.Bytes or SettingKind.Duration && d.Max != long.MaxValue ? d.Max : null,
                d.Choices ?? (d.Kind == SettingKind.Languages ? LocalizationSettings.AvailableLanguages
                    : d.Kind == SettingKind.Extensions ? FileTypes.Known.Keys.Order(StringComparer.Ordinal).ToList() : null));
        }).ToList();

    /// <summary>Validates every change against the option rules of the whole installation, then saves and applies it.</summary>
    /// <param name="changes">Key → new value; JSON null resets the key to its file/default value.</param>
    public async Task<IReadOnlyList<RuntimeSettingView>> SaveAsync(Guid actorId, IReadOnlyDictionary<string, JsonElement>? changes, CancellationToken cancellationToken)
    {
        if (changes is null || changes.Count == 0)
        {
            throw new RequestValidationException("values", "required");
        }

        var errors = new Dictionary<string, string[]>();
        var stored = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var overlay = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in changes)
        {
            var definition = RuntimeSettingsCatalog.Find(key);
            if (definition is null)
            {
                errors[key] = ["setting_unknown"];
                continue;
            }

            if (sources.Describe(definition.Key).Locked)
            {
                errors[definition.Key] = ["setting_locked"];
                continue;
            }

            try
            {
                var text = value.ValueKind == JsonValueKind.Null ? null : RuntimeSettingsCatalog.Normalize(definition, value);
                stored[definition.Key] = text;
                Overlay(overlay, definition, text);
            }
            catch (RequestValidationException ex)
            {
                foreach (var (field, codes) in ex.Errors)
                {
                    errors[field] = codes;
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        ValidateCombined(overlay);
        await store.SaveAsync(stored, actorId, time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        await loader.ReloadAsync(cancellationToken).ConfigureAwait(false);
        await audit.RecordAsync(AuditActions.SettingsChanged, actorId, "settings", null,
            new Dictionary<string, object?> { ["settings"] = stored.Keys.Order(StringComparer.Ordinal).ToList() }, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Describe();
    }

    private void Overlay(Dictionary<string, string?> overlay, SettingDefinition definition, string? text)
    {
        if (definition.Kind is SettingKind.Languages or SettingKind.Extensions)
        {
            var items = text is null ? sources.ValueBelowRuntimeList(definition.Key) : JsonSerializer.Deserialize<List<string>>(text) ?? [];
            var existing = configuration.GetSection(definition.Key).GetChildren().Count();
            for (var i = 0; i < Math.Max(existing, items.Count); i++)
            {
                overlay[$"{definition.Key}:{i}"] = i < items.Count ? items[i] : null;
            }

            return;
        }

        overlay[definition.Key] = text ?? sources.ValueBelowRuntime(definition.Key);
    }

    /// <summary>Binds the candidate configuration to the options the change affects and runs their own rules.</summary>
    private void ValidateCombined(Dictionary<string, string?> overlay)
    {
        var candidate = new ConfigurationBuilder().AddConfiguration(configuration).AddInMemoryCollection(overlay).Build();
        var failures = new List<string>();
        failures.AddRange(Bind<AttachmentSettings>(candidate, AttachmentSettings.SectionName).Validate());
        failures.AddRange(Bind<MessagingSettings>(candidate, MessagingSettings.SectionName).Validate());
        failures.AddRange(Bind<PrivateChatSettings>(candidate, PrivateChatSettings.SectionName).Validate());
        failures.AddRange(Bind<AccountSettings>(candidate, AccountSettings.SectionName).Validate());
        failures.AddRange(Bind<AuditSettings>(candidate, AuditSettings.SectionName).Validate());
        failures.AddRange(Bind<RetentionSettings>(candidate, RetentionSettings.SectionName).Validate());
        failures.AddRange(Bind<Application.Notifications.NotificationSettings>(candidate, Application.Notifications.NotificationSettings.SectionName).Validate());
        var localization = new LocalizationSettings();
        candidate.GetSection(LocalizationSettings.SectionName).Bind(localization);
        failures.AddRange(localization.Validate());
        if (failures.Count > 0)
        {
            throw new SetupException(StatusCodes.Status400BadRequest, "settings_invalid", null, failures);
        }
    }

    private static T Bind<T>(IConfiguration configuration, string section)
        where T : new()
    {
        var value = new T();
        configuration.GetSection(section).Bind(value);
        return value;
    }

    private static string KindName(SettingKind kind) => kind switch
    {
        SettingKind.Boolean => "boolean",
        SettingKind.Integer => "integer",
        SettingKind.Bytes => "bytes",
        SettingKind.Duration => "duration",
        SettingKind.Choice => "choice",
        SettingKind.Languages => "languages",
        _ => "extensions",
    };
}

/// <summary>Lets request code trigger an immediate reload of the live settings (the loader is a hosted service).</summary>
internal sealed class RuntimeSettingsLoaderAccessor(IServiceProvider services)
{
    public Task ReloadAsync(CancellationToken cancellationToken) =>
        services.GetServices<IHostedService>().OfType<RuntimeSettingsLoader>().Single().ReloadAsync(cancellationToken);
}

internal static class ConfigurationSourcesExtensions
{
    public static List<string> ValueBelowRuntimeList(this ConfigurationSources sources, string key)
    {
        var items = new List<string>();
        for (var i = 0; ; i++)
        {
            var value = sources.ValueBelowRuntime($"{key}:{i}");
            if (value is null)
            {
                return items;
            }

            items.Add(value);
        }
    }
}

// ---- Provider configuration (settings file, restart required) ----------------------------------

internal sealed record ProviderFieldDefinition(string Key, string Kind, IReadOnlyList<string>? Choices = null)
{
    public bool IsSecret => Kind == "secret";
}

internal sealed record ProviderGroupDefinition(string Name, IReadOnlyList<ProviderFieldDefinition> Fields);

internal sealed record ProviderFieldView(string Key, string Kind, object? Value, bool HasValue, string Source, bool Locked, IReadOnlyList<string>? Choices);

internal sealed record ProviderGroupView(string Name, bool RestartRequired, IReadOnlyList<ProviderFieldView> Fields);

/// <summary>Remembers provider groups saved since this process started (they apply after a restart).</summary>
internal sealed class PendingRestart
{
    private readonly ConcurrentDictionary<string, bool> _groups = new(StringComparer.Ordinal);

    public void Mark(string group) => _groups[group] = true;

    public bool IsPending(string group) => _groups.ContainsKey(group);
}

/// <summary>
/// Provider configuration (Phase 9, owner decision): email, captcha, attachment storage and malware
/// scanning are edited in the installation settings file after validation with the same rules as
/// startup; they apply after a restart. Secrets are write-only: the UI only learns whether one is
/// set. Keys set by environment variables or the command line cannot be changed here.
/// </summary>
internal sealed class ProviderSettingsManager(
    IConfiguration configuration,
    ConfigurationSources sources,
    PendingRestart pending,
    IWebHostEnvironment environment,
    ILoggerFactory loggerFactory,
    AuditService audit,
    IAttachmentStore attachments)
{
    public static IReadOnlyList<ProviderGroupDefinition> Groups { get; } =
    [
        new("email",
        [
            new("Email:Provider", "choice", EmailProviders.Names),
            new("Email:FromAddress", "text"),
            new("Email:FromName", "text"),
            new("Email:Smtp:Host", "text"),
            new("Email:Smtp:Port", "integer"),
            new("Email:Smtp:Security", "choice", ["None", "StartTls", "StartTlsWhenAvailable", "SslOnConnect", "Auto"]),
            new("Email:Smtp:Username", "text"),
            new("Email:Smtp:Password", "secret"),
            new("Email:Postmark:ServerToken", "secret"),
            new("Email:Postmark:MessageStream", "text"),
        ]),
        new("captcha",
        [
            new("Captcha:Provider", "choice", ["None", CaptchaProviders.Turnstile]),
            new("Captcha:Turnstile:SiteKey", "text"),
            new("Captcha:Turnstile:SecretKey", "secret"),
        ]),
        new("storage",
        [
            new("Storage:Provider", "choice", [StorageOptions.LocalProvider, StorageOptions.S3Provider, StorageOptions.AzureBlobProvider]),
            new("Storage:Local:Path", "text"),
            new("Storage:S3:Endpoint", "text"),
            new("Storage:S3:Region", "text"),
            new("Storage:S3:Bucket", "text"),
            new("Storage:S3:AccessKeyId", "text"),
            new("Storage:S3:SecretAccessKey", "secret"),
            new("Storage:S3:ForcePathStyle", "boolean"),
            new("Storage:S3:Prefix", "text"),
            new("Storage:AzureBlob:ConnectionString", "secret"),
            new("Storage:AzureBlob:Container", "text"),
            new("Storage:AzureBlob:Prefix", "text"),
        ]),
        new("scanning",
        [
            new("Scanning:Provider", "choice", [ScanningOptions.NoneProvider, ScanningOptions.ClamAvProvider]),
            new("Scanning:ClamAv:Host", "text"),
            new("Scanning:ClamAv:Port", "integer"),
        ]),
        new("search",
        [
            new("Search:Provider", "choice", [Application.Search.SearchSettings.DatabaseProvider, Application.Search.SearchSettings.OpenSearchProvider]),
            new("Search:OpenSearch:Url", "text"),
            new("Search:OpenSearch:IndexName", "text"),
            new("Search:OpenSearch:Username", "text"),
            new("Search:OpenSearch:Password", "secret"),
        ]),
        new("queue",
        [
            new("SupportQueue:Provider", "choice", ["Database", "Redis"]),
            new("SupportQueue:Redis:ConnectionString", "secret"),
            new("SupportQueue:Redis:StreamPrefix", "text"),
        ]),
        new("ai",
        [
            new("AI:Translation:Provider", "choice", ["None", "OpenAICompatible", "Ollama"]),
            new("AI:Translation:Endpoint", "text"),
            new("AI:Translation:Model", "text"),
            new("AI:Translation:ApiKey", "secret"),
            new("AI:Translation:MaxOutputTokens", "integer"),
        ]),
    ];

    public IReadOnlyList<ProviderGroupView> Describe() => Groups.Select(Describe).ToList();

    public ProviderGroupView Describe(ProviderGroupDefinition group) => new(
        group.Name,
        pending.IsPending(group.Name),
        group.Fields.Select(f =>
        {
            var source = sources.Describe(f.Key);
            var raw = configuration[f.Key];
            var hasValue = !string.IsNullOrEmpty(raw);
            object? value = f.IsSecret ? null : f.Kind switch
            {
                "boolean" => hasValue ? bool.TryParse(raw, out var b) && b : null,
                "integer" => hasValue && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null,
                _ => raw,
            };
            return new ProviderFieldView(f.Key, f.Kind, value, hasValue, source.Source, source.Locked || source.Source == "database", f.Choices);
        }).ToList());

    public static ProviderGroupDefinition RequireGroup(string name) =>
        Groups.FirstOrDefault(g => g.Name == name?.ToLowerInvariant()) ?? throw new ResourceNotFoundException("provider_not_found");

    /// <summary>Validates the group with the startup rules and writes it to the settings file.</summary>
    public async Task<ProviderGroupView> SaveAsync(Guid actorId, string groupName, IReadOnlyDictionary<string, JsonElement>? values, CancellationToken cancellationToken)
    {
        var group = RequireGroup(groupName);
        var changes = Parse(group, values);
        await using (var candidate = BuildCandidate(changes))
        {
            Validate(candidate, group);
        }

        if (group.Name == "storage" && changes.TryGetValue("Storage:Provider", out var nextProvider) && nextProvider is not null)
        {
            var currentProvider = StorageOptions.Normalize(configuration["Storage:Provider"]);
            var selectedProvider = StorageOptions.Normalize(nextProvider);
            if (!string.Equals(currentProvider, selectedProvider, StringComparison.Ordinal))
            {
                // Old rows have no provider key. Pin them to the still-active backend before
                // the saved configuration makes the next provider the default after restart.
                await attachments.AssignProviderToLegacyAttachmentsAsync(currentProvider, cancellationToken).ConfigureAwait(false);
            }
        }

        await ChatXSettingsFile.MergeAsync(sources.SettingsFilePath, changes, cancellationToken).ConfigureAwait(false);
        pending.Mark(group.Name);
        await audit.RecordAsync(AuditActions.ProviderChanged, actorId, "provider", group.Name,
            new Dictionary<string, object?> { ["settings"] = changes.Keys.Order(StringComparer.Ordinal).ToList() }, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Describe(group);
    }

    /// <summary>Tries the candidate settings without saving them: sends an email, writes and reads storage, scans a sample.</summary>
    public async Task<string> TestAsync(Guid actorId, string actorEmail, string groupName, IReadOnlyDictionary<string, JsonElement>? values, CancellationToken cancellationToken)
    {
        var group = RequireGroup(groupName);
        var changes = Parse(group, values);
        await using var candidate = BuildCandidate(changes);
        Validate(candidate, group);
        await using var scope = candidate.CreateAsyncScope();
        string outcome;
        try
        {
            outcome = group.Name switch
            {
                "email" => await TestEmailAsync(scope.ServiceProvider, actorEmail, cancellationToken).ConfigureAwait(false),
                "storage" => await TestStorageAsync(scope.ServiceProvider, cancellationToken).ConfigureAwait(false),
                "scanning" => await TestScannerAsync(scope.ServiceProvider, cancellationToken).ConfigureAwait(false),
                "search" => await TestSearchAsync(scope.ServiceProvider, cancellationToken).ConfigureAwait(false),
                "queue" => await TestQueueAsync(scope.ServiceProvider, cancellationToken).ConfigureAwait(false),
                "ai" => await TestAiTranslationAsync(scope.ServiceProvider, cancellationToken).ConfigureAwait(false),
                _ => "valid",
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not RequestValidationException)
        {
            loggerFactory.CreateLogger<ProviderSettingsManager>().LogWarning("Provider test for {Group} failed ({ExceptionType}).", group.Name, ex.GetType().Name);
            outcome = "failed";
        }

        await audit.RecordAsync(AuditActions.ProviderTested, actorId, "provider", group.Name,
            new Dictionary<string, object?> { ["outcome"] = outcome }, succeeded: outcome is not "failed" and not "unavailable", cancellationToken: cancellationToken).ConfigureAwait(false);
        return outcome;
    }

    /// <summary>Text values as configuration strings; secrets left out stay unchanged, an empty secret clears it; null removes a key from the file.</summary>
    private Dictionary<string, string?> Parse(ProviderGroupDefinition group, IReadOnlyDictionary<string, JsonElement>? values)
    {
        var changes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var errors = new Dictionary<string, string[]>();
        foreach (var (key, value) in values ?? new Dictionary<string, JsonElement>())
        {
            var field = group.Fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
            if (field is null)
            {
                errors[key] = ["setting_unknown"];
                continue;
            }

            if (sources.Describe(field.Key) is { } source && (source.Locked || source.Source == "database"))
            {
                errors[field.Key] = ["setting_locked"];
                continue;
            }

            string? text;
            switch (value.ValueKind)
            {
                case JsonValueKind.Null:
                    text = null;
                    break;
                case JsonValueKind.True or JsonValueKind.False when field.Kind == "boolean":
                    text = value.GetBoolean() ? "true" : "false";
                    break;
                case JsonValueKind.Number when field.Kind == "integer" && value.TryGetInt32(out var number):
                    text = number.ToString(CultureInfo.InvariantCulture);
                    break;
                case JsonValueKind.String when field.Kind is "text" or "secret" or "choice":
                    text = value.GetString()!.Trim();
                    if (field.Choices is { } choices)
                    {
                        text = choices.FirstOrDefault(c => string.Equals(c, text, StringComparison.OrdinalIgnoreCase));
                        if (text is null)
                        {
                            errors[field.Key] = ["choice_invalid"];
                            continue;
                        }
                    }

                    break;
                default:
                    errors[field.Key] = ["value_invalid"];
                    continue;
            }

            changes[field.Key] = text;
        }

        return errors.Count > 0 ? throw new RequestValidationException(errors) : changes;
    }

    private ServiceProvider BuildCandidate(IReadOnlyDictionary<string, string?> changes)
    {
        var candidate = new ConfigurationBuilder().AddConfiguration(configuration).AddInMemoryCollection(changes).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(candidate);
        services.AddSingleton<IHostEnvironment>(environment);
        services.AddSingleton(environment);
        services.AddSingleton(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        Hosting.ChatXApiComposition.AddChatXCoreServices(services, candidate, environment.ContentRootPath);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static void Validate(IServiceProvider candidate, ProviderGroupDefinition group)
    {
        try
        {
            _ = group.Name switch
            {
                "email" => candidate.GetRequiredService<IOptions<EmailOptions>>().Value,
                "captcha" => candidate.GetRequiredService<IOptions<CaptchaOptions>>().Value,
                "storage" => candidate.GetRequiredService<IOptions<StorageOptions>>().Value,
                "search" => new object[]
                {
                    candidate.GetRequiredService<IOptions<Application.Search.SearchSettings>>().Value,
                    candidate.GetRequiredService<IOptions<Infrastructure.Search.OpenSearchOptions>>().Value,
                },
                "queue" => ValidateQueue(candidate.GetRequiredService<IConfiguration>()),
                "ai" => candidate.GetRequiredService<IOptions<AiTranslationOptions>>().Value,
                _ => (object)candidate.GetRequiredService<IOptions<ScanningOptions>>().Value,
            };
        }
        catch (OptionsValidationException ex)
        {
            throw new SetupException(StatusCodes.Status400BadRequest, "settings_invalid", null, ex.Failures.ToList());
        }
    }

    private static async Task<string> TestEmailAsync(IServiceProvider services, string to, CancellationToken cancellationToken)
    {
        var spanish = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es";
        var subject = spanish ? "Correo de prueba de Knock Twice" : "Knock Twice test email";
        var body = spanish ? "La configuración de correo de la administración funciona." : "The email settings from the administration page work.";
        await services.GetRequiredService<IEmailSender>()
            .SendAsync(new EmailMessage(to, subject, body, $"<p>{System.Net.WebUtility.HtmlEncode(body)}</p>", "admin-test"), cancellationToken).ConfigureAwait(false);
        return "sent";
    }

    private static async Task<string> TestStorageAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var storage = services.GetRequiredService<IAttachmentStorage>();
        var key = $"probe/{Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16))}";
        byte[] probe = [0x63, 0x68, 0x61, 0x74, 0x78];
        await storage.PutAsync(key, new MemoryStream(probe), probe.Length, "application/octet-stream", cancellationToken).ConfigureAwait(false);
        try
        {
            var read = await storage.OpenReadAsync(key, cancellationToken).ConfigureAwait(false);
            if (read is null)
            {
                return "failed";
            }

            await using (read.ConfigureAwait(false))
            {
                using var copy = new MemoryStream();
                await read.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
                return copy.ToArray().AsSpan().SequenceEqual(probe) ? "ok" : "failed";
            }
        }
        finally
        {
            await storage.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Checks that the search engine answers; changes nothing (the index is created by the API after the restart).</summary>
    private static async Task<string> TestSearchAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        try
        {
            await services.GetRequiredService<Application.Search.ISearchProvider>().CheckAsync(cancellationToken).ConfigureAwait(false);
            return "ok";
        }
        catch (Application.Search.SearchUnavailableException)
        {
            return "unavailable";
        }
    }

    private static object ValidateQueue(IConfiguration configuration)
    {
        var provider = configuration["SupportQueue:Provider"] ?? "Database";
        if (string.Equals(provider, "Database", StringComparison.OrdinalIgnoreCase))
        {
            return new object();
        }

        if (!string.Equals(provider, "Redis", StringComparison.OrdinalIgnoreCase))
        {
            throw new OptionsValidationException("SupportQueue", typeof(string), ["SupportQueue:Provider must be Database or Redis."]);
        }

        var connectionString = configuration["SupportQueue:Redis:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new OptionsValidationException("SupportQueue", typeof(string), ["SupportQueue:Redis:ConnectionString is required when SupportQueue:Provider is Redis."]);
        }

        try
        {
            _ = StackExchange.Redis.ConfigurationOptions.Parse(connectionString);
        }
        catch (ArgumentException)
        {
            throw new OptionsValidationException("SupportQueue", typeof(string), ["SupportQueue:Redis:ConnectionString is invalid."]);
        }

        var prefix = configuration["SupportQueue:Redis:StreamPrefix"];
        if (string.IsNullOrWhiteSpace(prefix) || prefix.Length > 48 || prefix.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
        {
            throw new OptionsValidationException("SupportQueue", typeof(string), ["SupportQueue:Redis:StreamPrefix must be 1-48 letters, digits, '-' or '_'."]);
        }

        return new object();
    }

    private static async Task<string> TestQueueAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        if (!string.Equals(configuration["SupportQueue:Provider"], "Redis", StringComparison.OrdinalIgnoreCase))
        {
            return "valid";
        }

        var options = StackExchange.Redis.ConfigurationOptions.Parse(configuration["SupportQueue:Redis:ConnectionString"]!);
        options.AbortOnConnectFail = true;
        options.ConnectTimeout = 3000;
        options.AsyncTimeout = 3000;
        var connection = await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(options).WaitAsync(TimeSpan.FromSeconds(4), cancellationToken).ConfigureAwait(false);
        using (connection)
        {
            await connection.GetDatabase().PingAsync().WaitAsync(TimeSpan.FromSeconds(4), cancellationToken).ConfigureAwait(false);
        }

        return "ok";
    }

    private static async Task<string> TestAiTranslationAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var settings = services.GetRequiredService<IOptions<AiTranslationOptions>>().Value;
        if (settings.Provider == "None") return "disabled";
        var result = await services.GetRequiredService<Application.Translation.IMessageTranslationProvider>()
            .TranslateAsync("Hello", "es", cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(result) ? "failed" : "ok";
    }

    private static async Task<string> TestScannerAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var scanner = services.GetRequiredService<IMalwareScanner>();
        if (!scanner.Enabled)
        {
            return "disabled";
        }

        return await scanner.ScanAsync(new MemoryStream("ChatX scanner probe"u8.ToArray()), cancellationToken).ConfigureAwait(false) switch
        {
            ScanVerdict.Clean => "ok",
            ScanVerdict.Infected => "failed",
            _ => "unavailable",
        };
    }
}
