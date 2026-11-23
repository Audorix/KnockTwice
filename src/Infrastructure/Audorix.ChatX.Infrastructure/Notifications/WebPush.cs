using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Audorix.ChatX.Application.Identity;
using Audorix.ChatX.Application.Notifications;
using Audorix.ChatX.Domain.Notifications;
using Audorix.ChatX.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Infrastructure.Notifications;

/// <summary>
/// Web Push message encryption, RFC 8291 ("aes128gcm" content coding, RFC 8188), and VAPID
/// authentication, RFC 8292. Uses only the platform's ECDH, ECDSA, HKDF and AES-GCM.
/// </summary>
public static class WebPushCrypto
{
    private const int RecordSize = 4096;

    /// <summary>A new VAPID key pair: the uncompressed public point (65 bytes) and the private scalar (32 bytes).</summary>
    public static (byte[] PublicKey, byte[] PrivateKey) CreateVapidKeys()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(includePrivateParameters: true);
        return (Uncompressed(parameters.Q), parameters.D!);
    }

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> for a subscription: header (salt, record size, sender
    /// public key) followed by one AES-128-GCM record padded with the final-record delimiter.
    /// </summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] userAgentPublicKey, byte[] authSecret, byte[]? salt = null, ECDiffieHellman? senderKey = null)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        salt ??= RandomNumberGenerator.GetBytes(16);
        using var ephemeral = senderKey is null ? ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256) : null;
        var sender = senderKey ?? ephemeral!;
        var senderPublic = Uncompressed(sender.ExportParameters(false).Q);
        using var receiver = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = Point(userAgentPublicKey) });
        var shared = sender.DeriveRawSecretAgreement(receiver.PublicKey);

        // RFC 8291 §3.4: combine the ECDH secret with the authentication secret.
        var keyInfo = Concat(Encoding.ASCII.GetBytes("WebPush: info\0"), userAgentPublicKey, senderPublic);
        var ikm = HKDF.Expand(HashAlgorithmName.SHA256, HKDF.Extract(HashAlgorithmName.SHA256, shared, authSecret), 32, keyInfo);
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

        var padded = new byte[plaintext.Length + 1];
        plaintext.CopyTo(padded, 0);
        padded[^1] = 0x02; // last (and only) record
        if (padded.Length + 16 > RecordSize)
        {
            throw new ArgumentException("The payload is too large for one push record.", nameof(plaintext));
        }

        var cipher = new byte[padded.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(cek, 16))
        {
            aes.Encrypt(nonce, padded, cipher, tag);
        }

        var header = new byte[16 + 4 + 1 + senderPublic.Length];
        salt.CopyTo(header, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16, 4), RecordSize);
        header[20] = (byte)senderPublic.Length;
        senderPublic.CopyTo(header, 21);
        return Concat(header, cipher, tag);
    }

    /// <summary>A VAPID JWT (ES256) for the push service's origin, valid for 12 hours.</summary>
    public static string VapidToken(string audience, string subject, byte[] publicKey, byte[] privateKey, DateTimeOffset now)
    {
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { typ = "JWT", alg = "ES256" }));
        var claims = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { aud = audience, exp = now.AddHours(12).ToUnixTimeSeconds(), sub = subject }));
        using var key = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = Point(publicKey), D = privateKey });
        var signature = key.SignData(Encoding.ASCII.GetBytes($"{header}.{claims}"), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{header}.{claims}.{Base64Url(signature)}";
    }

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] FromBase64Url(string text)
    {
        var padded = text.Trim().Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        return Convert.FromBase64String(padded);
    }

    private static byte[] Uncompressed(ECPoint point) => Concat([0x04], point.X!, point.Y!);

    private static ECPoint Point(byte[] uncompressed)
    {
        if (uncompressed.Length != 65 || uncompressed[0] != 0x04)
        {
            throw new CryptographicException("Expected an uncompressed P-256 point.");
        }

        return new ECPoint { X = uncompressed[1..33], Y = uncompressed[33..65] };
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }
}

/// <summary>
/// Sends Web Push messages (Phase 15). The VAPID key pair is created on first use and kept in the
/// database (private key protected by Data Protection), so every API instance uses the same one
/// and browser subscriptions stay valid across restarts.
/// </summary>
internal sealed class WebPushSender(
    ChatXDbContext db,
    IDataProtectionProvider protection,
    IHttpClientFactory clients,
    IOptions<NotificationSettings> settings,
    IOptions<PublicUrlSettings> urls,
    TimeProvider time) : IWebPushSender
{
    public const string HttpClientName = "webpush";
    private const string Purpose = "Audorix.ChatX.WebPush.Vapid.v1";

    public async Task<string> GetPublicKeyAsync(CancellationToken cancellationToken) =>
        (await GetKeysAsync(cancellationToken).ConfigureAwait(false)).PublicKey;

    public async Task<PushOutcome> SendAsync(PushSubscription subscription, string payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        var keys = await GetKeysAsync(cancellationToken).ConfigureAwait(false);
        var endpoint = new Uri(subscription.Endpoint);
        byte[] body;
        try
        {
            body = WebPushCrypto.Encrypt(Encoding.UTF8.GetBytes(payload), WebPushCrypto.FromBase64Url(subscription.P256dh), WebPushCrypto.FromBase64Url(subscription.Auth));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return PushOutcome.Gone; // the subscription's keys are unusable
        }

        var subject = settings.Value.PushContact is { Length: > 0 } contact ? contact : urls.Value.BaseUri.GetLeftPart(UriPartial.Authority);
        var token = WebPushCrypto.VapidToken(endpoint.GetLeftPart(UriPartial.Authority), subject,
            WebPushCrypto.FromBase64Url(keys.PublicKey), keys.PrivateKey, time.GetUtcNow());
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentEncoding.Add("aes128gcm");
        request.Headers.TryAddWithoutValidation("Authorization", $"vapid t={token}, k={keys.PublicKey}");
        request.Headers.Add("TTL", "86400");
        request.Headers.Add("Urgency", "normal");
        try
        {
            using var response = await clients.CreateClient(HttpClientName).SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.StatusCode switch
            {
                _ when response.IsSuccessStatusCode => PushOutcome.Delivered,
                HttpStatusCode.NotFound or HttpStatusCode.Gone => PushOutcome.Gone,
                _ => PushOutcome.Failed,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return PushOutcome.Failed;
        }
    }

    private async Task<(string PublicKey, byte[] PrivateKey)> GetKeysAsync(CancellationToken cancellationToken)
    {
        var protector = protection.CreateProtector(Purpose);
        var stored = await db.WebPushKeys.AsNoTracking().SingleOrDefaultAsync(k => k.Id == 1, cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            var (publicKey, privateKey) = WebPushCrypto.CreateVapidKeys();
            db.WebPushKeys.Add(new WebPushKeys
            {
                Id = 1,
                PublicKey = WebPushCrypto.Base64Url(publicKey),
                ProtectedPrivateKey = protector.Protect(WebPushCrypto.Base64Url(privateKey)),
                CreatedAtUtc = time.GetUtcNow(),
            });
            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                // Another instance created them first.
            }

            db.ChangeTracker.Clear();
            stored = await db.WebPushKeys.AsNoTracking().SingleAsync(k => k.Id == 1, cancellationToken).ConfigureAwait(false);
        }

        return (stored.PublicKey, WebPushCrypto.FromBase64Url(protector.Unprotect(stored.ProtectedPrivateKey)));
    }
}
