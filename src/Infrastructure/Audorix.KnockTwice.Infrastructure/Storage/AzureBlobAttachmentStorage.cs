using System.Text.RegularExpressions;
using Audorix.KnockTwice.Application.Attachments;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Infrastructure.Storage;

/// <summary>
/// Private Azure Blob Storage adapter. It stores only object bytes; callers continue to authorize
/// every download through Knock Twice. Connection strings stay in server configuration.
/// </summary>
public sealed partial class AzureBlobAttachmentStorage : IAttachmentStorage
{
    private readonly BlobContainerClient _container;
    private readonly string _prefix;

    public AzureBlobAttachmentStorage(IOptions<StorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var azure = options.Value.AzureBlob;
        var clientOptions = new BlobClientOptions();
        clientOptions.Retry.NetworkTimeout = azure.Timeout;
        _container = new BlobContainerClient(azure.ConnectionString, azure.Container, clientOptions);
        _prefix = azure.Prefix.Trim('/');
    }

    public string Name => StorageOptions.AzureBlobProvider;

    public async Task PutAsync(string key, Stream content, long length, string contentType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        try
        {
            await Blob(key).UploadAsync(content, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex)
        {
            throw new StorageUnavailableException("Azure Blob Storage refused the upload.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new StorageUnavailableException("Azure Blob Storage is unreachable.", ex);
        }
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        var blob = Blob(key);
        try
        {
            if (!(await blob.ExistsAsync(cancellationToken).ConfigureAwait(false)).Value)
            {
                return null;
            }

            return await blob.OpenReadAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
        catch (RequestFailedException ex)
        {
            throw new StorageUnavailableException("Azure Blob Storage refused the download.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new StorageUnavailableException("Azure Blob Storage is unreachable.", ex);
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await Blob(key).DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex)
        {
            throw new StorageUnavailableException("Azure Blob Storage refused the deletion.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new StorageUnavailableException("Azure Blob Storage is unreachable.", ex);
        }
    }

    private BlobClient Blob(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.StartsWith('/') || key.Contains("..", StringComparison.Ordinal) || !KeyPattern().IsMatch(key))
        {
            throw new ArgumentException("The storage key is not valid.", nameof(key));
        }

        var name = _prefix.Length == 0 ? key : $"{_prefix}/{key}";
        return _container.GetBlobClient(name);
    }

    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9._/-]{0,199}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
