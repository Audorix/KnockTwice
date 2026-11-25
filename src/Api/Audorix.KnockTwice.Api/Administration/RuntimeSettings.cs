using System.Globalization;
using System.Text.Json;
using Audorix.KnockTwice.Application.Administration;
using Audorix.KnockTwice.Application.Attachments;
using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Identity;
using Audorix.KnockTwice.Application.Localization;
using Audorix.KnockTwice.Application.Messaging;
using Audorix.KnockTwice.Application.PrivateChats;
using Microsoft.Extensions.Configuration.Json;

namespace Audorix.KnockTwice.Api.Administration;

/// <summary>
/// Live installation settings (Phase 9, owner decision): stored in the database and layered into
/// configuration directly above the settings file and below environment variables and the command
/// line, so operators keep the last word. Services read them through <c>IOptionsSnapshot</c>, so a
/// saved change applies to the next request. Every instance reloads them periodically.
/// </summary>
internal sealed class RuntimeSettingsConfigurationSource : IConfigurationSource
{
    public RuntimeSettingsConfigurationProvider Provider { get; } = new();

    public IConfigurationProvider Build(IConfigurationBuilder builder) => Provider;
}

internal sealed class RuntimeSettingsConfigurationProvider : ConfigurationProvider
{
    /// <summary>Replaces every value (rows from the database, list values expanded to indexed keys).</summary>
    public void Replace(IEnumerable<KeyValuePair<string, string?>> rows)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in rows)
        {
            if (value is not null && value.StartsWith('[') && TryReadList(value, out var items))
            {
                for (var i = 0; i < items.Count; i++)
                {
                    data[$"{key}:{i}"] = items[i];
                }

                if (items.Count == 0)
                {
                    data[key] = string.Empty;
                }
            }
            else
            {
                data[key] = value;
            }
        }

        Data = data;
        OnReload();
    }

    private static bool TryReadList(string value, out List<string> items)
    {
        try
        {
            items = JsonSerializer.Deserialize<List<string>>(value) ?? [];
            return true;
        }
        catch (JsonException)
        {
            items = [];
            return false;
        }
    }
}

internal static class RuntimeSettingsConfiguration
{
    /// <summary>Inserts the database layer directly after the last file source (the installation settings file).</summary>
    public static RuntimeSettingsConfigurationProvider AddChatXRuntimeSettings(this WebApplicationBuilder builder)
    {
        var source = new RuntimeSettingsConfigurationSource();
        var sources = ((IConfigurationBuilder)builder.Configuration).Sources;
        var lastFile = -1;
        for (var i = 0; i < sources.Count; i++)
        {
            if (sources[i] is FileConfigurationSource)
            {
                lastFile = i;
            }
        }

        sources.Insert(lastFile + 1, source);
        builder.Services.AddSingleton(source.Provider);
        return source.Provider;
    }
}

/// <summary>Loads the live settings before the host serves requests and refreshes them every 30 seconds.</summary>
internal sealed class RuntimeSettingsLoader(IServiceScopeFactory scopes, RuntimeSettingsConfigurationProvider provider, ILogger<RuntimeSettingsLoader> logger)
    : Hosting.QuietBackgroundService
{
    public static readonly TimeSpan ReloadInterval = TimeSpan.FromSeconds(30);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await ReloadAsync(cancellationToken).ConfigureAwait(false);
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ReloadInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await ReloadAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var rows = await scope.ServiceProvider.GetRequiredService<IInstallationSettingsStore>().ListAsync(cancellationToken).ConfigureAwait(false);
            provider.Replace(rows.Select(r => new KeyValuePair<string, string?>(r.Key, r.Value)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Database unavailable or not migrated yet: keep the current values and try again later.
            logger.LogDebug("Live settings could not be loaded ({ExceptionType}).", ex.GetType().Name);
        }
    }
}

internal enum SettingKind
{
    Boolean,
    Integer,
    Bytes,
    Duration,
    Choice,
    Languages,
    Extensions,
}

internal sealed record SettingDefinition(string Key, string Group, SettingKind Kind, long Min = 0, long Max = long.MaxValue, IReadOnlyList<string>? Choices = null);

/// <summary>The settings administrators may change live. Anything else stays in files and the environment.</summary>
internal static class RuntimeSettingsCatalog
{
    public static IReadOnlyList<SettingDefinition> All { get; } =
    [
        new("Identity:RegistrationMode", "access", SettingKind.Choice, Choices: [RegistrationModes.Open, RegistrationModes.Invitation, RegistrationModes.Closed]),
        new("Organizations:AllowCreation", "access", SettingKind.Boolean),
        new("Discovery:AllowEmailLookup", "access", SettingKind.Boolean),
        new("Discovery:AllowOrganizationDirectory", "access", SettingKind.Boolean),
        new("PrivateChats:Enabled", "features", SettingKind.Boolean),
        new("Attachments:Enabled", "features", SettingKind.Boolean),
        new("Widgets:Enabled", "features", SettingKind.Boolean),
        new("Search:Enabled", "features", SettingKind.Boolean),
        new("Notifications:EmailEnabled", "features", SettingKind.Boolean),
        new("Notifications:PushEnabled", "features", SettingKind.Boolean),
        new("Notifications:EmailDelay", "limits", SettingKind.Duration, 0, (long)TimeSpan.FromDays(1).TotalSeconds),
        new("Notifications:EmailInterval", "limits", SettingKind.Duration, 0, (long)TimeSpan.FromDays(7).TotalSeconds),
        new("Attachments:MaxFileSize", "limits", SettingKind.Bytes, 1024, 5L * 1024 * 1024 * 1024),
        new("Attachments:MaxPerMessage", "limits", SettingKind.Integer, 1, 50),
        new("Attachments:UserQuota", "limits", SettingKind.Bytes, 0, long.MaxValue),
        new("Attachments:AllowedExtensions", "limits", SettingKind.Extensions),
        new("Messaging:MaxLength", "limits", SettingKind.Integer, 1, 20000),
        new("Messaging:EditWindow", "limits", SettingKind.Duration, 0, (long)TimeSpan.FromDays(7).TotalSeconds),
        new("Conversations:MaxGroupParticipants", "limits", SettingKind.Integer, 2, 10000),
        new("PrivateChats:MaxFileSize", "limits", SettingKind.Bytes, 1024, 1024L * 1024 * 1024),
        new("PrivateChats:MaxBytesPerChat", "limits", SettingKind.Bytes, 1024, 1024L * 1024 * 1024),
        new("Localization:DefaultLanguage", "localization", SettingKind.Choice, Choices: LocalizationSettings.AvailableLanguages),
        new("Localization:SupportedLanguages", "localization", SettingKind.Languages),
        new("Audit:RetentionDays", "audit", SettingKind.Integer, 7, 3650),
        new("Retention:VisitorInactiveDays", "retention", SettingKind.Integer, 0, 3650),
        new("Retention:UnconfirmedAccountDays", "retention", SettingKind.Integer, 0, 3650),
        new("Retention:MessageDays", "retention", SettingKind.Integer, 0, 36500),
        new("Retention:ClosedSessionDays", "retention", SettingKind.Integer, 0, 3650),
    ];

    public static SettingDefinition? Find(string key) => All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The configuration text for a JSON value from the admin UI, or a field error code.</summary>
    public static string Normalize(SettingDefinition definition, JsonElement value)
    {
        switch (definition.Kind)
        {
            case SettingKind.Boolean:
                return value.ValueKind switch
                {
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => throw Invalid(definition, "boolean_required"),
                };
            case SettingKind.Integer or SettingKind.Bytes:
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
                {
                    throw Invalid(definition, "number_required");
                }

                return number < definition.Min || number > definition.Max
                    ? throw Invalid(definition, "out_of_range")
                    : number.ToString(CultureInfo.InvariantCulture);
            case SettingKind.Duration:
                // Seconds from the UI, stored as a TimeSpan.
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var seconds))
                {
                    throw Invalid(definition, "number_required");
                }

                return seconds < definition.Min || seconds > definition.Max
                    ? throw Invalid(definition, "out_of_range")
                    : TimeSpan.FromSeconds(seconds).ToString("c", CultureInfo.InvariantCulture);
            case SettingKind.Choice:
                var choice = value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;
                return definition.Choices!.FirstOrDefault(c => string.Equals(c, choice, StringComparison.OrdinalIgnoreCase))
                    ?? throw Invalid(definition, "choice_invalid");
            case SettingKind.Languages or SettingKind.Extensions:
                if (value.ValueKind != JsonValueKind.Array)
                {
                    throw Invalid(definition, "list_required");
                }

                var allowed = definition.Kind == SettingKind.Languages ? LocalizationSettings.AvailableLanguages : FileTypes.Known.Keys.ToList();
                var items = value.EnumerateArray().Select(e => e.GetString()?.Trim().TrimStart('.').ToLowerInvariant() ?? string.Empty).Distinct().ToList();
                if (items.Count == 0 || items.Any(i => !allowed.Contains(i)))
                {
                    throw Invalid(definition, "choice_invalid");
                }

                return JsonSerializer.Serialize(items);
            default:
                throw Invalid(definition, "value_invalid");
        }
    }

    /// <summary>The effective value as JSON for the admin UI.</summary>
    public static object? Read(SettingDefinition definition, IConfiguration configuration)
    {
        var section = configuration.GetSection(definition.Key);
        return definition.Kind switch
        {
            SettingKind.Boolean => section.Get<bool?>(),
            SettingKind.Integer or SettingKind.Bytes => section.Get<long?>(),
            SettingKind.Duration => section.Get<TimeSpan?>() is { } span ? (long)span.TotalSeconds : null,
            SettingKind.Choice => section.Value,
            _ => section.GetChildren().Select(c => c.Value).Where(v => !string.IsNullOrEmpty(v)).ToList(),
        };
    }

    private static RequestValidationException Invalid(SettingDefinition definition, string code) => new(definition.Key, code);
}

/// <summary>Defaults of the options classes, so the admin UI can show "default" values that no source sets.</summary>
internal static class RuntimeSettingDefaults
{
    public static object? For(SettingDefinition definition) => definition.Key switch
    {
        "Identity:RegistrationMode" => RegistrationModes.Open,
        "Organizations:AllowCreation" or "Discovery:AllowEmailLookup" or "Discovery:AllowOrganizationDirectory" or "PrivateChats:Enabled" or "Attachments:Enabled" or "Widgets:Enabled" or "Search:Enabled" or "Notifications:EmailEnabled" or "Notifications:PushEnabled" => true,
        "Notifications:EmailDelay" => (long)new Application.Notifications.NotificationSettings().EmailDelay.TotalSeconds,
        "Notifications:EmailInterval" => (long)new Application.Notifications.NotificationSettings().EmailInterval.TotalSeconds,
        "Attachments:MaxFileSize" => new AttachmentSettings().MaxFileSize,
        "Attachments:MaxPerMessage" => (long)new AttachmentSettings().MaxPerMessage,
        "Attachments:UserQuota" => new AttachmentSettings().UserQuota,
        "Attachments:AllowedExtensions" => AttachmentSettings.DefaultExtensions.ToList(),
        "Messaging:MaxLength" => (long)new MessagingSettings().MaxLength,
        "Messaging:EditWindow" => (long)new MessagingSettings().EditWindow.TotalSeconds,
        "Conversations:MaxGroupParticipants" => 256L,
        "PrivateChats:MaxFileSize" => new PrivateChatSettings().MaxFileSize,
        "PrivateChats:MaxBytesPerChat" => new PrivateChatSettings().MaxBytesPerChat,
        "Localization:DefaultLanguage" => "en",
        "Localization:SupportedLanguages" => LocalizationSettings.AvailableLanguages.ToList(),
        "Audit:RetentionDays" => (long)new AuditSettings().RetentionDays,
        "Retention:VisitorInactiveDays" => (long)new RetentionSettings().VisitorInactiveDays,
        "Retention:UnconfirmedAccountDays" => (long)new RetentionSettings().UnconfirmedAccountDays,
        "Retention:MessageDays" => (long)new RetentionSettings().MessageDays,
        "Retention:ClosedSessionDays" => (long)new RetentionSettings().ClosedSessionDays,
        _ => null,
    };
}
