namespace Audorix.ChatX.Contracts.V1.Setup;

/// <summary>
/// Public setup state. <see cref="Required"/> is true while the installation runs in setup mode
/// (no completed setup); the web app then shows the setup wizard instead of the application.
/// </summary>
public sealed record SetupStatusResponse(bool Required, bool Completed);

/// <summary>Current values and constraints the wizard starts from. Never contains secrets.</summary>
public sealed record SetupConfigurationResponse(
    string Environment,
    SetupDatabaseDefaults Database,
    SetupEmailDefaults Email,
    SetupOptionsDefaults Options,
    int PasswordMinimumLength);

/// <param name="Locked">The database is configured by environment variables or command-line arguments and cannot be changed here.</param>
/// <param name="Provider">The locked provider, when <see cref="Locked"/> is true.</param>
public sealed record SetupDatabaseDefaults(
    bool Locked,
    string? Provider,
    IReadOnlyList<string> SupportedProviders,
    string SqlitePath,
    string PostgreSqlHost,
    int PostgreSqlPort,
    string PostgreSqlDatabase);

public sealed record SetupEmailDefaults(
    string Provider,
    string FromAddress,
    string FromName,
    string SmtpHost,
    int SmtpPort,
    string SmtpSecurity,
    IReadOnlyList<string> SmtpSecurityModes);

/// <param name="CaptchaKeysConfigured">Turnstile keys are already configured, so the wizard may leave them empty.</param>
public sealed record SetupOptionsDefaults(
    bool DevelopmentMode,
    string? PublicBaseUrl,
    string DefaultLanguage,
    IReadOnlyList<string> SupportedLanguages,
    string CaptchaProvider,
    bool CaptchaKeysConfigured);

public sealed record SetupDatabaseInput(string? Provider, SetupSqliteInput? Sqlite, SetupPostgreSqlInput? PostgreSql);

public sealed record SetupSqliteInput(string? Path);

/// <param name="SslMode"><c>Disable</c>, <c>Prefer</c> (default) or <c>Require</c>.</param>
public sealed record SetupPostgreSqlInput(string? Host, int? Port, string? Database, string? Username, string? Password, string? SslMode);

public sealed record SetupEmailInput(
    string? Provider,
    string? FromAddress,
    string? FromName,
    SetupSmtpInput? Smtp,
    SetupPostmarkInput? Postmark);

public sealed record SetupSmtpInput(string? Host, int? Port, string? Security, string? Username, string? Password);

public sealed record SetupPostmarkInput(string? ServerToken, string? MessageStream);

public sealed record SetupAdministratorInput(string? Email, string? DisplayName, string? Password);

/// <param name="DevelopmentMode">New accounts are confirmed immediately and no confirmation email is sent. For development installations only.</param>
public sealed record SetupOptionsInput(
    bool DevelopmentMode,
    string? PublicBaseUrl,
    string? DefaultLanguage,
    string? CaptchaProvider,
    string? CaptchaSiteKey,
    string? CaptchaSecretKey);

/// <param name="Result"><c>connected</c> (the database exists) or <c>will_be_created</c> (the server and credentials work; the database is created during setup).</param>
public sealed record SetupDatabaseTestResponse(bool DatabaseExists, string Result);

public sealed record SetupEmailTestRequest(SetupEmailInput? Email, string? To);

public sealed record SetupCompleteRequest(
    SetupDatabaseInput? Database,
    SetupEmailInput? Email,
    SetupAdministratorInput? Administrator,
    SetupOptionsInput? Options);

/// <param name="Restarting">The API restarts into normal mode; poll <c>GET /api/v1/setup/status</c> until <c>required</c> is false.</param>
public sealed record SetupCompleteResponse(string AdministratorEmail, bool Restarting);
