using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Audorix.ChatX.Application.Attachments;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Infrastructure.Storage;

/// <summary>
/// S3-compatible object storage over plain HTTPS with AWS Signature Version 4 (no SDK, so it
/// works the same with AWS S3, MinIO, R2 and similar services). Objects are written private (no
/// ACL is set; the bucket must not allow public reads); downloads always go through the API's
/// authorization, never through public or pre-signed URLs.
/// </summary>
public sealed class S3AttachmentStorage(HttpClient http, IOptions<StorageOptions> options, TimeProvider time) : IAttachmentStorage
{
    public const string HttpClientName = "ChatX.S3";
    private const string UnsignedPayload = "UNSIGNED-PAYLOAD";

    public string Name => StorageOptions.S3Provider;

    public async Task PutAsync(string key, Stream content, long length, string contentType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var payloadHash = UnsignedPayload;
        if (content.CanSeek)
        {
            var start = content.Position;
            payloadHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(content, cancellationToken).ConfigureAwait(false));
            content.Position = start;
        }

        using var body = new StreamContent(content);
        body.Headers.ContentLength = length;
        body.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        using var response = await SendAsync(HttpMethod.Put, key, body, payloadHash, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new StorageUnavailableException($"S3 refused the upload ({(int)response.StatusCode}).");
        }
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get, key, null, S3Signer.EmptyPayloadHash, cancellationToken, HttpCompletionOption.ResponseHeadersRead)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new StorageUnavailableException($"S3 refused the download ({status}).");
        }

        return new ResponseStream(response, await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Delete, key, null, S3Signer.EmptyPayloadHash, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            throw new StorageUnavailableException($"S3 refused the deletion ({(int)response.StatusCode}).");
        }
    }

    /// <summary>The object URL: <c>endpoint/bucket/key</c> (path style) or <c>bucket.endpoint/key</c>.</summary>
    internal Uri ObjectUri(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Contains("..", StringComparison.Ordinal) || key.StartsWith('/'))
        {
            throw new ArgumentException("The storage key is not valid.", nameof(key));
        }

        var s3 = options.Value.S3;
        var endpoint = new Uri(s3.Endpoint.TrimEnd('/') + "/");
        var objectKey = (s3.Prefix ?? string.Empty).Trim('/') is { Length: > 0 } prefix ? $"{prefix}/{key}" : key;
        var path = string.Join('/', objectKey.Split('/').Select(Uri.EscapeDataString));
        if (s3.ForcePathStyle)
        {
            return new Uri(endpoint, $"{Uri.EscapeDataString(s3.Bucket)}/{path}");
        }

        var host = new UriBuilder(endpoint) { Host = $"{s3.Bucket}.{endpoint.Host}" }.Uri;
        return new Uri(host, path);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string key, HttpContent? content, string payloadHash, CancellationToken cancellationToken,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        var s3 = options.Value.S3;
        var uri = ObjectUri(key);
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        var now = time.GetUtcNow();
        var amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["host"] = S3Signer.HostHeader(uri),
            ["x-amz-content-sha256"] = payloadHash,
            ["x-amz-date"] = amzDate,
        };
        request.Headers.TryAddWithoutValidation("Authorization",
            S3Signer.Authorization(method.Method, uri, headers, payloadHash, s3.AccessKeyId, s3.SecretAccessKey, s3.Region, now));

        try
        {
            return await http.SendAsync(request, completion, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new StorageUnavailableException("S3 storage is unreachable.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new StorageUnavailableException("S3 storage did not answer in time.", ex);
        }
    }

    /// <summary>Disposes the HTTP response together with its body stream.</summary>
    private sealed class ResponseStream(HttpResponseMessage response, Stream inner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => response.Content.Headers.ContentLength ?? throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

/// <summary>AWS Signature Version 4 for S3 requests (header-based, no query parameters).</summary>
internal static class S3Signer
{
    public const string EmptyPayloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    public static string HostHeader(Uri uri) => uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port.ToString(CultureInfo.InvariantCulture)}";

    /// <param name="headers">Lower-case header names (including <c>host</c>) and trimmed values, all of which are signed.</param>
    public static string Authorization(
        string method, Uri uri, IReadOnlyDictionary<string, string> headers, string payloadHash, string accessKeyId, string secretAccessKey, string region, DateTimeOffset now)
    {
        var date = now.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var amzDate = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var ordered = headers.OrderBy(h => h.Key, StringComparer.Ordinal).ToList();
        var signedHeaders = string.Join(';', ordered.Select(h => h.Key));
        var canonicalHeaders = string.Concat(ordered.Select(h => $"{h.Key}:{h.Value.Trim()}\n"));

        // The path is already URI-encoded per segment; S3 does not normalize it further.
        var canonicalRequest = string.Join('\n', method, uri.AbsolutePath, CanonicalQuery(uri), canonicalHeaders, signedHeaders, payloadHash);
        var scope = $"{date}/{region}/s3/aws4_request";
        var stringToSign = string.Join('\n', "AWS4-HMAC-SHA256", amzDate, scope, Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))));

        var key = HMACSHA256.HashData(Encoding.UTF8.GetBytes("AWS4" + secretAccessKey), Encoding.UTF8.GetBytes(date));
        key = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(region));
        key = HMACSHA256.HashData(key, "s3"u8);
        key = HMACSHA256.HashData(key, "aws4_request"u8);
        var signature = Hex(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(stringToSign)));
        return $"AWS4-HMAC-SHA256 Credential={accessKeyId}/{scope},SignedHeaders={signedHeaders},Signature={signature}";
    }

    private static string CanonicalQuery(Uri uri)
    {
        var query = uri.Query.TrimStart('?');
        if (query.Length == 0)
        {
            return string.Empty;
        }

        return string.Join('&', query.Split('&')
            .Select(part => part.Split('=', 2))
            .Select(kv => (Key: Uri.EscapeDataString(Uri.UnescapeDataString(kv[0])), Value: kv.Length > 1 ? Uri.EscapeDataString(Uri.UnescapeDataString(kv[1])) : string.Empty))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ThenBy(kv => kv.Value, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value}"));
    }

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);
}
