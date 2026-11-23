using System.Collections.Concurrent;
using System.Security.Cryptography;
using Audorix.ChatX.Infrastructure.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace Audorix.ChatX.IntegrationTests.Support;

/// <summary>
/// An in-process S3-compatible endpoint (PUT/GET/DELETE object, path style). It recomputes the
/// Signature V4 of every request from what it actually received (host, path, headers, body hash)
/// and refuses mismatches with 403, like a real service would.
/// </summary>
public sealed class FakeS3 : IAsyncDisposable
{
    public const string AccessKeyId = "AKIDCHATXTEST";
    public const string SecretAccessKey = "chatx-test-secret-key";
    public const string Region = "eu-test-1";
    public const string Bucket = "chatx-attachments";

    private readonly WebApplication _app;

    private FakeS3(WebApplication app) => _app = app;

    public ConcurrentDictionary<string, (byte[] Data, string? ContentType)> Objects { get; } = new(StringComparer.Ordinal);

    public ConcurrentQueue<string> Requests { get; } = new();

    public static async Task<FakeS3> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        var fake = new FakeS3(app);
        app.Run(fake.HandleAsync);
        await app.StartAsync();
        return fake;
    }

    public HttpMessageHandler CreateHandler() => _app.GetTestServer().CreateHandler();

    public static IReadOnlyDictionary<string, string?> Settings(string prefix = "") => new Dictionary<string, string?>
    {
        ["Storage:Provider"] = "S3",
        ["Storage:S3:Endpoint"] = "https://s3.test",
        ["Storage:S3:Region"] = Region,
        ["Storage:S3:Bucket"] = Bucket,
        ["Storage:S3:AccessKeyId"] = AccessKeyId,
        ["Storage:S3:SecretAccessKey"] = SecretAccessKey,
        ["Storage:S3:Prefix"] = prefix,
    };

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private async Task HandleAsync(HttpContext context)
    {
        var request = context.Request;
        Requests.Enqueue($"{request.Method} {request.Path}");
        using var body = new MemoryStream();
        await request.Body.CopyToAsync(body);
        var bytes = body.ToArray();

        var payloadHash = request.Headers["x-amz-content-sha256"].ToString();
        if (payloadHash != "UNSIGNED-PAYLOAD" && payloadHash != Convert.ToHexStringLower(SHA256.HashData(bytes)))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest; // XAmzContentSHA256Mismatch
            return;
        }

        var uri = new Uri($"https://{request.Host}{request.Path}{request.QueryString}");
        var amzDate = request.Headers["x-amz-date"].ToString();
        var now = DateTimeOffset.ParseExact(amzDate, "yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal);
        var expected = S3Signer.Authorization(request.Method, uri,
            new Dictionary<string, string> { ["host"] = request.Host.Value!, ["x-amz-content-sha256"] = payloadHash, ["x-amz-date"] = amzDate },
            payloadHash, AccessKeyId, SecretAccessKey, Region, now);
        if (request.Headers.Authorization.ToString() != expected || request.Headers.ContainsKey("x-amz-acl"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden; // SignatureDoesNotMatch
            return;
        }

        var key = request.Path.Value!.TrimStart('/');
        if (!key.StartsWith(Bucket + "/", StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound; // NoSuchBucket
            return;
        }

        key = key[(Bucket.Length + 1)..];
        switch (request.Method)
        {
            case "PUT":
                Objects[key] = (bytes, request.ContentType);
                context.Response.StatusCode = StatusCodes.Status200OK;
                break;
            case "GET" when Objects.TryGetValue(key, out var stored):
                context.Response.ContentType = stored.ContentType ?? "application/octet-stream";
                await context.Response.Body.WriteAsync(stored.Data);
                break;
            case "GET":
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                break;
            case "DELETE":
                Objects.TryRemove(key, out _);
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                break;
            default:
                context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                break;
        }
    }
}

/// <summary>A malware scanner whose verdict the test controls.</summary>
public sealed class FakeScanner : Application.Attachments.IMalwareScanner
{
    public Application.Attachments.ScanVerdict Verdict { get; set; } = Application.Attachments.ScanVerdict.Clean;

    public int Scans { get; private set; }

    public bool Enabled => true;

    public Task<Application.Attachments.ScanVerdict> ScanAsync(Stream content, CancellationToken cancellationToken)
    {
        Scans++;
        return Task.FromResult(Verdict);
    }
}

/// <summary>A clock tests can move forward.</summary>
public sealed class AdjustableTime : TimeProvider
{
    public TimeSpan Offset { get; set; }

    public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + Offset;
}
