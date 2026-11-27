using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Configuration.Json;

namespace Audorix.KnockTwice.Api.Setup;

/// <summary>
/// Setup state bound from the <c>Setup</c> section. <see cref="Completed"/> is written by the
/// setup wizard into the settings file; until it is true the API runs in setup mode.
/// </summary>
public sealed class SetupSettings
{
    public const string SectionName = "Setup";
    public const string DefaultSettingsPath = "App_Data/chatx.settings.json";
    public const int MinimumTokenLength = 12;

    public bool Completed { get; set; }

    /// <summary>Recorded by the wizard for information; the effective switches live in <c>Identity</c>.</summary>
    public bool DevelopmentMode { get; set; }

    /// <summary>Settings file written by the wizard, absolute or relative to the content root.</summary>
    public string? SettingsPath { get; set; }

    /// <summary>Optional fixed setup code (at least 12 characters). A random code is generated and logged otherwise.</summary>
    public string? Token { get; set; }

    /// <summary>Restart into normal mode after a successful setup. Tests turn this off.</summary>
    public bool RestartAfterCompletion { get; set; } = true;
}

/// <summary>
/// The installation settings file. It sits above <c>appsettings*.json</c> and user secrets but
/// below environment variables and command-line arguments, so operators can still override any
/// value the wizard wrote. It holds secrets (database and SMTP passwords): it is created with
/// owner-only permissions on Unix and must never be committed (<c>App_Data/</c> is ignored).
/// </summary>
internal static class ChatXSettingsFile
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string ResolvePath(IConfiguration configuration, string contentRoot)
    {
        var configured = configuration[$"{SetupSettings.SectionName}:{nameof(SetupSettings.SettingsPath)}"];
        var path = string.IsNullOrWhiteSpace(configured) ? SetupSettings.DefaultSettingsPath : configured.Trim();
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(contentRoot, path));
    }

    public static bool IsSetupRequired(IConfiguration configuration) =>
        !configuration.GetValue<bool>($"{SetupSettings.SectionName}:{nameof(SetupSettings.Completed)}");

    /// <summary>Adds the settings file (when present) directly after the last JSON source.</summary>
    public static void AddChatXSettingsFile(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var path = ResolvePath(builder.Configuration, builder.Environment.ContentRootPath);
        if (!File.Exists(path))
        {
            return;
        }

        var source = new JsonConfigurationSource { Path = path, Optional = true, ReloadOnChange = false };
        source.ResolveFileProvider();
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
    }

    /// <summary>
    /// Loads an optional connection-string secret file after the setup settings file and before
    /// environment variables and command-line arguments. This lets an explicit environment key
    /// override a mounted secret file while keeping the secret itself out of container metadata.
    /// </summary>
    public static void AddDatabaseConnectionSecretFile(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        const string pathKey = "Database:ConnectionStringFile";
        var configuredPath = builder.Configuration[pathKey];
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return;
        }

        string path;
        string connectionString;
        try
        {
            path = Path.GetFullPath(configuredPath.Trim(), builder.Environment.ContentRootPath);
            connectionString = File.ReadAllText(path).Trim();
        }
        catch (IOException)
        {
            throw new InvalidOperationException($"{pathKey} is configured but its file cannot be read.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"{pathKey} is configured but its file cannot be read.");
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException($"{pathKey} is configured but its file cannot be read.");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"{pathKey} is configured but its file is empty.");
        }

        var source = new MemoryConfigurationSource
        {
            InitialData = new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = connectionString,
            },
        };
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
    }

    /// <summary>
    /// True when a source with higher precedence than the settings file (environment variables,
    /// command line, test hosts) supplies <paramref name="key"/>; the wizard cannot change it.
    /// </summary>
    public static bool IsProvidedAboveSettingsFile(IConfiguration configuration, string key)
    {
        if (configuration is not IConfigurationRoot root)
        {
            return false;
        }

        var providers = root.Providers.ToList();
        var lastFile = providers.FindLastIndex(p => p is FileConfigurationProvider);
        return providers.Skip(lastFile + 1).Any(p => p.TryGet(key, out var value) && !string.IsNullOrWhiteSpace(value));
    }

    /// <summary>
    /// Sets (or, with a null value, removes) keys in the existing settings file, keeping everything
    /// else, and writes it atomically. Used by the administration's provider settings.
    /// </summary>
    public static async Task MergeAsync(string path, IReadOnlyDictionary<string, string?> changes, CancellationToken cancellationToken)
    {
        var root = File.Exists(path)
            ? JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)) as JsonObject ?? []
            : new JsonObject { ["//"] = "Written by Knock Twice. Contains secrets: keep this file private and out of version control." };
        foreach (var (key, value) in changes)
        {
            var parts = key.Split(':');
            var node = root;
            foreach (var part in parts[..^1])
            {
                if (node[part] is not JsonObject child)
                {
                    child = [];
                    node[part] = child;
                }

                node = child;
            }

            if (value is null)
            {
                node.Remove(parts[^1]);
            }
            else
            {
                node[parts[^1]] = value;
            }
        }

        await WriteRootAsync(path, root, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes the settings atomically (temporary file, then rename).</summary>
    public static async Task WriteAsync(string path, IReadOnlyList<KeyValuePair<string, object?>> settings, CancellationToken cancellationToken)
    {
        var root = new JsonObject
        {
            ["//"] = "Written by the Knock Twice setup wizard. Contains secrets: keep this file private and out of version control.",
        };
        foreach (var (key, value) in settings)
        {
            var parts = key.Split(':');
            var node = root;
            foreach (var part in parts[..^1])
            {
                if (node[part] is not JsonObject child)
                {
                    child = new JsonObject();
                    node[part] = child;
                }

                node = child;
            }

            node[parts[^1]] = value switch
            {
                null => null,
                bool b => JsonValue.Create(b),
                int n => JsonValue.Create(n),
                _ => JsonValue.Create(value.ToString()),
            };
        }

        await WriteRootAsync(path, root, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteRootAsync(string path, JsonObject root, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, root.ToJsonString(WriteOptions), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(temporary, path, overwrite: true);
    }
}
