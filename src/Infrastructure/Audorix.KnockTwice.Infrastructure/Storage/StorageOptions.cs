using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Infrastructure.Storage;

/// <summary>Attachment content storage (ADR-007), bound from <c>Storage</c>. <c>Local</c> by default.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    public const string LocalProvider = "Local";
    public const string S3Provider = "S3";
    public const string AzureBlobProvider = "AzureBlob";

    public string Provider { get; set; } = LocalProvider;

    public LocalStorageOptions Local { get; set; } = new();

    public S3StorageOptions S3 { get; set; } = new();

    public AzureBlobStorageOptions AzureBlob { get; set; } = new();

    public static string Normalize(string? provider) =>
        string.Equals(provider?.Trim(), S3Provider, StringComparison.OrdinalIgnoreCase) ? S3Provider
        : string.Equals(provider?.Trim(), AzureBlobProvider, StringComparison.OrdinalIgnoreCase) ? AzureBlobProvider
        : string.IsNullOrWhiteSpace(provider) || string.Equals(provider.Trim(), LocalProvider, StringComparison.OrdinalIgnoreCase) ? LocalProvider
        : provider.Trim();
}

/// <summary>Azure Blob destination. Keep the container private; all reads pass through API authorization.</summary>
public sealed class AzureBlobStorageOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public string Container { get; set; } = string.Empty;

    /// <summary>Optional prefix inside the container.</summary>
    public string Prefix { get; set; } = string.Empty;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);
}

public sealed class LocalStorageOptions
{
    /// <summary>Root directory; relative paths resolve against the host's content root. Keep it outside any web root.</summary>
    public string Path { get; set; } = "App_Data/attachments";
}

/// <summary>Any S3-compatible service (AWS S3, MinIO, Cloudflare R2, Backblaze B2, ...). The bucket must be private.</summary>
public sealed class S3StorageOptions
{
    /// <summary>Service URL, for example <c>https://s3.eu-west-1.amazonaws.com</c> or <c>http://minio:9000</c>.</summary>
    public string Endpoint { get; set; } = string.Empty;

    public string Region { get; set; } = "us-east-1";

    public string Bucket { get; set; } = string.Empty;

    public string AccessKeyId { get; set; } = string.Empty;

    public string SecretAccessKey { get; set; } = string.Empty;

    /// <summary>Path-style URLs (<c>endpoint/bucket/key</c>), needed by most self-hosted services. False uses <c>bucket.endpoint/key</c>.</summary>
    public bool ForcePathStyle { get; set; } = true;

    /// <summary>Optional key prefix inside the bucket, for example <c>chatx/</c>.</summary>
    public string Prefix { get; set; } = string.Empty;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);
}

internal sealed class StorageOptionsValidator : IValidateOptions<StorageOptions>
{
    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        var errors = new List<string>();
        switch (StorageOptions.Normalize(options.Provider))
        {
            case StorageOptions.LocalProvider:
                if (string.IsNullOrWhiteSpace(options.Local.Path))
                {
                    errors.Add("Storage:Local:Path is required for the Local provider.");
                }

                break;
            case StorageOptions.S3Provider:
                var s3 = options.S3;
                if (!Uri.TryCreate(s3.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
                {
                    errors.Add("Storage:S3:Endpoint must be an absolute http(s) URL.");
                }

                if (string.IsNullOrWhiteSpace(s3.Bucket) || s3.Bucket.Length is < 3 or > 63 || s3.Bucket.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or '.')))
                {
                    errors.Add("Storage:S3:Bucket must be a valid bucket name (3-63 lower-case letters, digits, dots or hyphens).");
                }

                if (string.IsNullOrWhiteSpace(s3.Region) || string.IsNullOrWhiteSpace(s3.AccessKeyId) || string.IsNullOrWhiteSpace(s3.SecretAccessKey))
                {
                    errors.Add("Storage:S3:Region, AccessKeyId and SecretAccessKey are required for the S3 provider.");
                }

                if (s3.Timeout <= TimeSpan.Zero)
                {
                    errors.Add("Storage:S3:Timeout must be positive.");
                }

                break;
            case StorageOptions.AzureBlobProvider:
                var azure = options.AzureBlob;
                if (string.IsNullOrWhiteSpace(azure.ConnectionString))
                {
                    errors.Add("Storage:AzureBlob:ConnectionString is required for the Azure Blob provider.");
                }

                if (string.IsNullOrWhiteSpace(azure.Container) || azure.Container.Length is < 3 or > 63
                    || !IsAzureContainerAlphanumeric(azure.Container[0]) || !IsAzureContainerAlphanumeric(azure.Container[^1])
                    || azure.Container.Any(c => !(IsAzureContainerAlphanumeric(c) || c == '-'))
                    || azure.Container.Contains("--", StringComparison.Ordinal))
                {
                    errors.Add("Storage:AzureBlob:Container must be a valid lower-case Azure container name (3-63 letters, digits or single hyphens).");
                }

                if (azure.Prefix.Contains("..", StringComparison.Ordinal)
                    || azure.Prefix.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.')))
                {
                    errors.Add("Storage:AzureBlob:Prefix may contain only letters, digits, slash, dot, underscore and hyphen, and cannot contain '..'.");
                }

                if (azure.Timeout <= TimeSpan.Zero)
                {
                    errors.Add("Storage:AzureBlob:Timeout must be positive.");
                }

                break;
            default:
                errors.Add($"Storage:Provider '{options.Provider}' is not supported. Use Local, S3 or AzureBlob.");
                break;
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static bool IsAzureContainerAlphanumeric(char value) => value is >= 'a' and <= 'z' or >= '0' and <= '9';
}

/// <summary>Malware scanning (Architecture §9), bound from <c>Scanning</c>. Off by default (owner decision).</summary>
public sealed class ScanningOptions
{
    public const string SectionName = "Scanning";
    public const string NoneProvider = "None";
    public const string ClamAvProvider = "ClamAv";

    public string Provider { get; set; } = NoneProvider;

    public ClamAvOptions ClamAv { get; set; } = new();
}

/// <summary>A clamd daemon reachable over TCP (INSTREAM).</summary>
public sealed class ClamAvOptions
{
    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 3310;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}

internal sealed class ScanningOptionsValidator : IValidateOptions<ScanningOptions>
{
    public ValidateOptionsResult Validate(string? name, ScanningOptions options)
    {
        var provider = options.Provider?.Trim() ?? string.Empty;
        if (provider.Length == 0 || string.Equals(provider, ScanningOptions.NoneProvider, StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Success;
        }

        if (!string.Equals(provider, ScanningOptions.ClamAvProvider, StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail($"Scanning:Provider '{options.Provider}' is not supported. Use None or ClamAv.");
        }

        return string.IsNullOrWhiteSpace(options.ClamAv.Host) || options.ClamAv.Port is < 1 or > 65535 || options.ClamAv.Timeout <= TimeSpan.Zero
            ? ValidateOptionsResult.Fail("Scanning:ClamAv needs a Host, a Port (1-65535) and a positive Timeout.")
            : ValidateOptionsResult.Success;
    }
}
