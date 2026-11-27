using Audorix.KnockTwice.Application.Attachments;
using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Infrastructure.Storage;

/// <summary>Holds each configured storage destination so old attachments stay readable after a default changes.</summary>
internal sealed class StorageProviderRegistry : IAttachmentStorageProviderRegistry
{
    private readonly Dictionary<string, Lazy<IAttachmentStorage>> _providers;
    private readonly StorageOptions _options;
    private readonly IHttpClientFactory _clients;
    private readonly TimeProvider _time;

    public StorageProviderRegistry(IOptions<StorageOptions> options, IHttpClientFactory clients, TimeProvider time)
    {
        _options = options.Value;
        _clients = clients;
        _time = time;
        DefaultProvider = StorageOptions.Normalize(_options.Provider);
        _providers = new Dictionary<string, Lazy<IAttachmentStorage>>(StringComparer.OrdinalIgnoreCase)
        {
            [StorageOptions.LocalProvider] = new(() => new LocalAttachmentStorage(Path.GetFullPath(_options.Local.Path))),
            [StorageOptions.S3Provider] = new(() => new S3AttachmentStorage(_clients.CreateClient(S3AttachmentStorage.HttpClientName), Options.Create(_options), _time)),
            [StorageOptions.AzureBlobProvider] = new(() => new AzureBlobAttachmentStorage(Options.Create(_options))),
        };
    }

    public string DefaultProvider { get; }

    public IAttachmentStorage Resolve(string? provider)
    {
        var selected = string.IsNullOrWhiteSpace(provider) ? DefaultProvider : StorageOptions.Normalize(provider);
        return _providers.TryGetValue(selected, out var storage)
            ? storage.Value
            : throw new InvalidOperationException($"Storage provider '{selected}' is not configured.");
    }
}
