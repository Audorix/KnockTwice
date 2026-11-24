using System.Globalization;
using System.Net.Mail;
using Audorix.ChatX.Api.Hosting;
using Audorix.ChatX.Api.Identity;
using Audorix.ChatX.Application.Abstractions;
using Audorix.ChatX.Application.Common;
using Audorix.ChatX.Application.Identity;
using Audorix.ChatX.Application.Localization;
using Audorix.ChatX.Contracts.Errors;
using Audorix.ChatX.Contracts.V1.Setup;
using Audorix.ChatX.Infrastructure.Captcha;
using Audorix.ChatX.Infrastructure.Email;
using Audorix.ChatX.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Api.Setup;

/// <summary>
/// Turns wizard input into installation settings and applies them. Candidate settings are
/// layered over the host configuration and run through the same service graph and option
/// validators as the real host, so the wizard cannot write a file the API would refuse to
/// start with. The settings file is written only after the database is migrated and the
/// administrator exists.
/// </summary>
internal sealed class SetupProvisioner(
    IConfiguration hostConfiguration,
    IWebHostEnvironment environment,
    ILoggerFactory loggerFactory,
    IEnumerable<IDatabaseProvider> providers,
    IOptions<SetupSettings> setupSettings)
{
    public const string DefaultSqlitePath = "App_Data/chatx.db";
    private static readonly string[] SmtpSecurityModes = ["None", "StartTls", "StartTlsWhenAvailable", "SslOnConnect", "Auto"];
    private static readonly string[] PostgreSqlSslModes = ["Disable", "Prefer", "Require"];
    private readonly ILogger _logger = loggerFactory.CreateLogger("Audorix.ChatX.Setup");

    public string SettingsPath => ChatXSettingsFile.ResolvePath(hostConfiguration, environment.ContentRootPath);

    /// <summary>The setup code file, next to the settings file; removed when setup completes.</summary>
    public string SetupCodePath => Path.Combine(Path.GetDirectoryName(SettingsPath)!, "setup-code.txt");

    /// <summary>Saves the current setup code for operators who cannot see the console (owner-only on Unix).</summary>
    public async Task<string> WriteSetupCodeFileAsync(string code)
    {
        var path = SetupCodePath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, code + Environment.NewLine).ConfigureAwait(false);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The console and log still show the code.
            _logger.LogWarning("The setup code could not be saved to {CodeFile}: {Message}", path, ex.Message);
        }

        return path;
    }

    public bool DatabaseLocked =>
        ChatXSettingsFile.IsProvidedAboveSettingsFile(hostConfiguration, "Database:Provider")
        || ChatXSettingsFile.IsProvidedAboveSettingsFile(hostConfiguration, "Database:ConnectionString");

    public SetupConfigurationResponse GetConfiguration()
    {
        var email = hostConfiguration.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new EmailOptions();
        var captcha = hostConfiguration.GetSection(CaptchaOptions.SectionName).Get<CaptchaOptions>() ?? new CaptchaOptions();
        var localization = hostConfiguration.GetSection(LocalizationSettings.SectionName).Get<LocalizationSettings>() ?? new LocalizationSettings();
        var account = hostConfiguration.GetSection(AccountSettings.SectionName).Get<AccountSettings>() ?? new AccountSettings();
        var publicUrl = hostConfiguration["App:PublicBaseUrl"];

        return new SetupConfigurationResponse(
            environment.EnvironmentName,
            new SetupDatabaseDefaults(
                DatabaseLocked,
                DatabaseLocked ? DatabaseProviderKinds.Names.FirstOrDefault(n => string.Equals(n, hostConfiguration["Database:Provider"], StringComparison.OrdinalIgnoreCase)) : null,
                DatabaseProviderKinds.Names,
                DefaultSqlitePath,
                "localhost",
                5432,
                "chatx"),
            new SetupEmailDefaults(
                EmailProviders.Normalize(email.Provider) ?? EmailProviders.Smtp,
                email.FromAddress ?? string.Empty,
                email.FromName,
                email.Smtp.Host ?? "localhost",
                email.Smtp.Port,
                email.Smtp.Security,
                SmtpSecurityModes),
            new SetupOptionsDefaults(
                environment.IsDevelopment(),
                string.IsNullOrWhiteSpace(publicUrl) ? null : publicUrl,
                localization.DefaultLanguage.Trim().ToLowerInvariant(),
                localization.EffectiveSupportedLanguages,
                CaptchaProviders.Normalize(captcha.Provider) ?? CaptchaProviders.Turnstile,
                !string.IsNullOrWhiteSpace(captcha.Turnstile.SiteKey) && !string.IsNullOrWhiteSpace(captcha.Turnstile.SecretKey)),
            account.PasswordMinimumLength);
    }

    public async Task<SetupDatabaseTestResponse> TestDatabaseAsync(SetupDatabaseInput? input, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var database = MapDatabase(input, errors);
        ThrowIfInvalid(errors);
        await using var candidate = BuildCandidate(database);
        var result = await ProbeOrThrowAsync(candidate, cancellationToken).ConfigureAwait(false);
        return new SetupDatabaseTestResponse(
            result == DatabaseProbeResult.Connected,
            result == DatabaseProbeResult.Connected ? "connected" : "will_be_created");
    }

    public async Task SendTestEmailAsync(SetupEmailTestRequest request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var settings = MapEmail(request.Email, errors);
        var to = request.To?.Trim();
        if (string.IsNullOrEmpty(to) || !MailAddress.TryCreate(to, out _))
        {
            errors["email.testRecipient"] = ["email_invalid"];
        }

        ThrowIfInvalid(errors);
        await using var candidate = BuildCandidate(settings);
        ValidateOptions(candidate, typeof(EmailOptions));
        await using var scope = candidate.CreateAsyncScope();
        var spanish = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es";
        var subject = spanish ? "Correo de prueba de Knock Twice" : "Knock Twice test email";
        var body = spanish
            ? "La configuración de correo del asistente de instalación funciona."
            : "The email settings from the setup wizard work.";
        await scope.ServiceProvider.GetRequiredService<IEmailSender>()
            .SendAsync(new EmailMessage(to!, subject, body, $"<p>{System.Net.WebUtility.HtmlEncode(body)}</p>", "setup-test"), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SetupCompleteResponse> CompleteAsync(SetupCompleteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new Dictionary<string, string[]>();
        var settings = new List<KeyValuePair<string, object?>>
        {
            new("Setup:Completed", true),
            new("Setup:CompletedAtUtc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
        };
        settings.AddRange(MapDatabase(request.Database, errors));
        settings.AddRange(MapEmail(request.Email, errors));
        settings.AddRange(MapOptions(request.Options, errors));
        var administrator = request.Administrator ?? new SetupAdministratorInput(null, null, null);
        ThrowIfInvalid(errors);

        await using var candidate = BuildCandidate(settings);
        ValidateOptions(candidate, null);

        var account = candidate.GetRequiredService<IOptions<AccountSettings>>().Value;
        var adminErrors = AccountService.ValidateAdministratorInput(administrator.Email, administrator.DisplayName, administrator.Password, account.PasswordMinimumLength);
        if (adminErrors.Count > 0)
        {
            throw new RequestValidationException(Prefix("administrator", adminErrors));
        }

        await ProbeOrThrowAsync(candidate, cancellationToken).ConfigureAwait(false);

        await using (var scope = candidate.CreateAsyncScope())
        {
            try
            {
                await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Provider messages can include host names but not passwords; keep them in the server log only.
                _logger.LogError("Setup could not migrate the database ({ExceptionType}): {Message}", ex.GetType().Name, ex.Message);
                throw new SetupException(StatusCodes.Status400BadRequest, ErrorCodes.SetupDatabaseUnavailable,
                    new Dictionary<string, string[]> { ["database"] = ["migration_failed"] });
            }

            try
            {
                var language = request.Options?.DefaultLanguage;
                await scope.ServiceProvider.GetRequiredService<AccountService>()
                    .CreateInstallationAdministratorAsync(administrator.Email, administrator.DisplayName, administrator.Password, language, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (RequestValidationException ex)
            {
                throw new RequestValidationException(Prefix("administrator", ex.Errors));
            }
            catch (ConflictException ex) when (ex.Code == ErrorCodes.SetupAdministratorExists)
            {
                throw new SetupException(StatusCodes.Status409Conflict, ErrorCodes.SetupAdministratorExists);
            }
        }

        await ChatXSettingsFile.WriteAsync(SettingsPath, settings, cancellationToken).ConfigureAwait(false);
        File.Delete(SetupCodePath); // no-op when missing; the code is useless after setup
        _logger.LogInformation("Setup completed; settings written to {SettingsPath}.", SettingsPath);
        return new SetupCompleteResponse(administrator.Email!.Trim(), setupSettings.Value.RestartAfterCompletion);
    }

    // ---- Candidate service graph --------------------------------------------------------

    private ServiceProvider BuildCandidate(IEnumerable<KeyValuePair<string, object?>> overrides)
    {
        var values = overrides.ToDictionary(
            o => o.Key,
            o => o.Value switch
            {
                null => null,
                bool b => b ? "true" : "false",
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => o.Value.ToString(),
            },
            StringComparer.OrdinalIgnoreCase);
        var configuration = new ConfigurationBuilder()
            .AddConfiguration(hostConfiguration)
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(environment);
        services.AddSingleton<IWebHostEnvironment>(environment);
        services.AddSingleton(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddChatXCoreServices(configuration, environment.ContentRootPath);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>Resolves the options the host validates at startup; failures name settings, never values.</summary>
    private static void ValidateOptions(IServiceProvider candidate, Type? only)
    {
        var errors = new Dictionary<string, string[]>();
        var details = new List<string>();
        void Check<T>(string field)
            where T : class
        {
            if (only is not null && only != typeof(T))
            {
                return;
            }

            try
            {
                _ = candidate.GetRequiredService<IOptions<T>>().Value;
            }
            catch (OptionsValidationException ex)
            {
                errors[field] = ["setting_invalid"];
                details.AddRange(ex.Failures);
            }
        }

        Check<DatabaseOptions>("database");
        Check<EmailOptions>("email");
        Check<PublicUrlSettings>("options.publicBaseUrl");
        Check<CaptchaOptions>("options.captcha");
        Check<LocalizationSettings>("options.defaultLanguage");
        Check<AccountSettings>("options");
        Check<ExternalAuthenticationSettings>("options");

        if (errors.Count > 0)
        {
            throw new SetupException(StatusCodes.Status400BadRequest, ErrorCodes.SetupSettingsInvalid, errors, details);
        }
    }

    private static async Task<DatabaseProbeResult> ProbeOrThrowAsync(ServiceProvider candidate, CancellationToken cancellationToken)
    {
        ValidateOptions(candidate, typeof(DatabaseOptions));
        var options = candidate.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var provider = candidate.GetServices<IDatabaseProvider>().Single(p => p.Kind == options.ProviderKind);
        var result = await provider.ProbeAsync(options.ConnectionString!, cancellationToken).ConfigureAwait(false);
        var reason = result switch
        {
            DatabaseProbeResult.Connected or DatabaseProbeResult.DatabaseMissing => null,
            DatabaseProbeResult.AuthenticationFailed => "authentication_failed",
            DatabaseProbeResult.Unreachable => "unreachable",
            _ => "connection_failed",
        };
        if (reason is not null)
        {
            throw new SetupException(StatusCodes.Status400BadRequest, ErrorCodes.SetupDatabaseUnavailable,
                new Dictionary<string, string[]> { ["database"] = [reason] });
        }

        return result;
    }

    // ---- Input mapping ------------------------------------------------------------------

    private List<KeyValuePair<string, object?>> MapDatabase(SetupDatabaseInput? input, Dictionary<string, string[]> errors)
    {
        if (DatabaseLocked)
        {
            // Environment variables or the command line decide; the wizard writes nothing.
            return [];
        }

        if (input is null || !DatabaseProviderKinds.TryParse(input.Provider, out var kind))
        {
            errors["database.provider"] = ["unsupported_value"];
            return [];
        }

        DatabaseConnectionParameters parameters;
        if (kind == DatabaseProviderKind.Sqlite)
        {
            var path = input.Sqlite?.Path?.Trim();
            if (string.IsNullOrEmpty(path))
            {
                errors["database.path"] = ["required"];
            }
            else if (path == ":memory:" || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || path.Contains(';', StringComparison.Ordinal))
            {
                errors["database.path"] = ["invalid"];
            }

            parameters = new DatabaseConnectionParameters(Path: path);
        }
        else
        {
            var pg = input.PostgreSql ?? new SetupPostgreSqlInput(null, null, null, null, null, null);
            Require(errors, "database.host", pg.Host);
            Require(errors, "database.database", pg.Database);
            Require(errors, "database.username", pg.Username);
            if (pg.Port is < 1 or > 65535)
            {
                errors["database.port"] = ["port_invalid"];
            }

            if (pg.SslMode is not null && !PostgreSqlSslModes.Contains(pg.SslMode, StringComparer.OrdinalIgnoreCase))
            {
                errors["database.sslMode"] = ["unsupported_value"];
            }

            parameters = new DatabaseConnectionParameters(
                Host: pg.Host, Port: pg.Port ?? 5432, Database: pg.Database, Username: pg.Username, Password: pg.Password, SslMode: pg.SslMode ?? "Prefer");
        }

        if (errors.Keys.Any(k => k.StartsWith("database", StringComparison.Ordinal)))
        {
            return [];
        }

        var provider = providers.Single(p => p.Kind == kind);
        return
        [
            new("Database:Provider", DatabaseProviderKinds.Names[kind == DatabaseProviderKind.Sqlite ? 0 : 1]),
            new("Database:ConnectionString", provider.BuildConnectionString(parameters)),
        ];
    }

    private static List<KeyValuePair<string, object?>> MapEmail(SetupEmailInput? input, Dictionary<string, string[]> errors)
    {
        var provider = EmailProviders.Normalize(input?.Provider);
        if (input is null || provider is null)
        {
            errors["email.provider"] = ["unsupported_value"];
            return [];
        }

        var from = input.FromAddress?.Trim();
        if (string.IsNullOrEmpty(from))
        {
            errors["email.fromAddress"] = ["required"];
        }
        else if (!MailAddress.TryCreate(from, out _))
        {
            errors["email.fromAddress"] = ["email_invalid"];
        }

        var settings = new List<KeyValuePair<string, object?>>
        {
            new("Email:Provider", provider),
            new("Email:FromAddress", from),
            new("Email:FromName", string.IsNullOrWhiteSpace(input.FromName) ? "Knock Twice" : input.FromName.Trim()),
        };

        if (provider == EmailProviders.Smtp)
        {
            var smtp = input.Smtp ?? new SetupSmtpInput(null, null, null, null, null);
            Require(errors, "email.smtpHost", smtp.Host);
            if (smtp.Port is null or < 1 or > 65535)
            {
                errors["email.smtpPort"] = ["port_invalid"];
            }

            var security = SmtpSecurityModes.FirstOrDefault(m => string.Equals(m, smtp.Security, StringComparison.OrdinalIgnoreCase));
            if (security is null)
            {
                errors["email.smtpSecurity"] = ["unsupported_value"];
            }

            var username = smtp.Username?.Trim();
            if (string.IsNullOrEmpty(username) != string.IsNullOrEmpty(smtp.Password))
            {
                errors["email.smtpCredentials"] = ["credentials_incomplete"];
            }

            settings.Add(new("Email:Smtp:Host", smtp.Host?.Trim()));
            settings.Add(new("Email:Smtp:Port", smtp.Port ?? 0));
            settings.Add(new("Email:Smtp:Security", security));
            settings.Add(new("Email:Smtp:Username", string.IsNullOrEmpty(username) ? string.Empty : username));
            settings.Add(new("Email:Smtp:Password", string.IsNullOrEmpty(username) ? string.Empty : smtp.Password));
        }
        else
        {
            var postmark = input.Postmark ?? new SetupPostmarkInput(null, null);
            Require(errors, "email.postmarkToken", postmark.ServerToken);
            settings.Add(new("Email:Postmark:ServerToken", postmark.ServerToken?.Trim()));
            settings.Add(new("Email:Postmark:MessageStream", string.IsNullOrWhiteSpace(postmark.MessageStream) ? "outbound" : postmark.MessageStream.Trim()));
        }

        return settings;
    }

    private static List<KeyValuePair<string, object?>> MapOptions(SetupOptionsInput? input, Dictionary<string, string[]> errors)
    {
        input ??= new SetupOptionsInput(false, null, null, null, null, null);
        var settings = new List<KeyValuePair<string, object?>>
        {
            new("Setup:DevelopmentMode", input.DevelopmentMode),
            new("Identity:RequireConfirmedEmail", !input.DevelopmentMode),
            new("Identity:AutoConfirmEmail", input.DevelopmentMode),
            new("DemoData:Enabled", input.DevelopmentMode),
        };

        var url = input.PublicBaseUrl?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(url))
        {
            errors["options.publicBaseUrl"] = ["required"];
        }
        else if (new PublicUrlSettings { PublicBaseUrl = url }.Validate().Count > 0)
        {
            errors["options.publicBaseUrl"] = ["url_invalid"];
        }

        settings.Add(new("App:PublicBaseUrl", url));

        var language = input.DefaultLanguage?.Trim().ToLowerInvariant();
        if (language is null || !LocalizationSettings.AvailableLanguages.Contains(language))
        {
            errors["options.defaultLanguage"] = ["unsupported_value"];
        }

        settings.Add(new("Localization:DefaultLanguage", language));

        var captcha = CaptchaProviders.Normalize(input.CaptchaProvider);
        if (captcha is null)
        {
            errors["options.captchaProvider"] = ["unsupported_value"];
            return settings;
        }

        settings.Add(new("Captcha:Provider", captcha));
        if (captcha == CaptchaProviders.Turnstile)
        {
            var siteKey = input.CaptchaSiteKey?.Trim();
            var secretKey = input.CaptchaSecretKey?.Trim();
            if (string.IsNullOrEmpty(siteKey) != string.IsNullOrEmpty(secretKey))
            {
                errors["options.captchaKeys"] = ["credentials_incomplete"];
            }
            else if (!string.IsNullOrEmpty(siteKey))
            {
                settings.Add(new("Captcha:Turnstile:SiteKey", siteKey));
                settings.Add(new("Captcha:Turnstile:SecretKey", secretKey));
            }
        }

        return settings;
    }

    private static void Require(Dictionary<string, string[]> errors, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = ["required"];
        }
    }

    private static void ThrowIfInvalid(Dictionary<string, string[]> errors)
    {
        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }
    }

    private static Dictionary<string, string[]> Prefix(string prefix, IReadOnlyDictionary<string, string[]> errors) =>
        errors.ToDictionary(e => $"{prefix}.{e.Key}", e => e.Value);
}
