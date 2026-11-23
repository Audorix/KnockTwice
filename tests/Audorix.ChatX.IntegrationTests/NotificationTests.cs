using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Audorix.ChatX.Infrastructure.Notifications;
using Audorix.ChatX.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using static Audorix.ChatX.IntegrationTests.Support.ApiSession;
using static Audorix.ChatX.IntegrationTests.Support.TestUsers;

namespace Audorix.ChatX.IntegrationTests;

/// <summary>
/// Notifications (Phase 15, owner decisions of 2026-11-23): an email when messages stay unread
/// (batched, at most one per interval; daily or off per person), and Web Push to subscribed browsers
/// while the person has no open connection. Previews follow the person's setting.
/// </summary>
public sealed class NotificationTests
{
    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, string?> Fast => new()
    {
        ["Notifications:DispatchInterval"] = "00:00:00.200",
        ["Notifications:EmailDelay"] = "00:00:00",
        ["Notifications:EmailInterval"] = "01:00:00",
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Unread_messages_are_emailed_once_batched_and_as_the_person_chose(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db, Fast);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (carl, carlId) = await SignedInAsync(api, "carl@example.org", "Carl");
        var (dana, danaId) = await SignedInAsync(api, "dana@example.org", "Dana");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = carl;
        using var _4 = dana;
        foreach (var (other, otherId) in new[] { (ben, benId), (carl, carlId), (dana, danaId) })
        {
            await BecomeContactsAsync(ana, anaId, other, otherId);
        }

        // Carl switches email off; Dana keeps it but hides message text.
        AssertStatus(HttpStatusCode.OK, await carl.SendAsync(HttpMethod.Put, "/api/v1/users/me/preferences", new { emailNotifications = "off" }));
        var danaPrefs = await JsonAsync(await dana.SendAsync(HttpMethod.Put, "/api/v1/users/me/preferences", new { notificationPreviews = false, emailNotifications = "daily" }));
        Assert.Equal("daily", danaPrefs.GetProperty("preferences").GetProperty("emailNotifications").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await dana.SendAsync(HttpMethod.Put, "/api/v1/users/me/preferences", new { emailNotifications = "hourly" })).StatusCode);

        foreach (var otherId in new[] { benId, carlId, danaId })
        {
            var direct = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/direct", new { userId = otherId }))).GetProperty("id").GetGuid();
            AssertStatus(HttpStatusCode.Created, await ana.PostAsync($"/api/v1/conversations/{direct}/messages", new { clientMessageId = "m1", body = "Lunch at noon?" }));
        }

        var benEmail = await WaitForEmailAsync(api, "ben@example.org");
        Assert.Contains("Ana", benEmail.TextBody, StringComparison.Ordinal);
        Assert.Contains("Lunch at noon?", benEmail.TextBody, StringComparison.Ordinal);
        Assert.Contains("https://chat.test/conversations", benEmail.TextBody, StringComparison.Ordinal);
        var danaEmail = await WaitForEmailAsync(api, "dana@example.org");
        Assert.DoesNotContain("Lunch", danaEmail.TextBody, StringComparison.Ordinal);

        // Batched: a second message within the interval is not emailed again; the author and Carl get nothing.
        var withBen = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/direct", new { userId = benId }))).GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.Created, await ana.PostAsync($"/api/v1/conversations/{withBen}/messages", new { clientMessageId = "m2", body = "Or one?" }));
        await Task.Delay(1500, Ct);
        Assert.Single(api.Emails.To("ben@example.org", "unread-messages"));
        Assert.Empty(api.Emails.To("carl@example.org", "unread-messages"));
        Assert.Empty(api.Emails.To("ana@example.org", "unread-messages"));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task New_messages_are_pushed_encrypted_to_subscribed_browsers(string provider)
    {
        var pushService = new FakePushService();
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db, Fast, services =>
            services.AddHttpClient(WebPushSender.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => pushService));
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;
        await BecomeContactsAsync(ana, anaId, ben, benId);

        var config = await JsonAsync(await ben.GetAsync("/api/v1/notifications/push"));
        Assert.True(config.GetProperty("enabled").GetBoolean());
        var vapid = config.GetProperty("publicKey").GetString()!;
        Assert.Equal(65, WebPushCrypto.FromBase64Url(vapid).Length);

        // Ben's browser subscribes.
        using var browser = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var q = browser.ExportParameters(false).Q;
        byte[] browserPublic = [0x04, .. q.X!, .. q.Y!];
        var auth = RandomNumberGenerator.GetBytes(16);
        var subscription = new { endpoint = "https://push.test/send/ben-1", keys = new { p256dh = WebPushCrypto.Base64Url(browserPublic), auth = WebPushCrypto.Base64Url(auth) } };
        AssertStatus(HttpStatusCode.NoContent, await ben.PostAsync("/api/v1/notifications/push/subscriptions", subscription));
        Assert.Equal(HttpStatusCode.BadRequest, (await ben.PostAsync("/api/v1/notifications/push/subscriptions",
            new { endpoint = "http://push.test/x", keys = subscription.keys })).StatusCode);

        var direct = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/direct", new { userId = benId }))).GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.Created, await ana.PostAsync($"/api/v1/conversations/{direct}/messages", new { clientMessageId = "p1", body = "Are you there?" }));

        var request = await pushService.WaitAsync(1);
        Assert.Equal("https://push.test/send/ben-1", request.Url);
        Assert.StartsWith("vapid t=", request.Authorization, StringComparison.Ordinal);
        Assert.Contains($"k={vapid}", request.Authorization, StringComparison.Ordinal);
        Assert.Equal("aes128gcm", request.Encoding);
        using (var payload = JsonDocument.Parse(Decrypt(request.Body, browser, browserPublic, auth)))
        {
            Assert.Equal("Ana", payload.RootElement.GetProperty("title").GetString());
            Assert.Equal("Are you there?", payload.RootElement.GetProperty("body").GetString());
            Assert.Equal($"/conversations?id={direct}", payload.RootElement.GetProperty("url").GetString());
        }

        // Without previews only the sender is named; a gone subscription is dropped.
        AssertStatus(HttpStatusCode.OK, await ben.SendAsync(HttpMethod.Put, "/api/v1/users/me/preferences", new { notificationPreviews = false }));
        pushService.Status = HttpStatusCode.Gone;
        AssertStatus(HttpStatusCode.Created, await ana.PostAsync($"/api/v1/conversations/{direct}/messages", new { clientMessageId = "p2", body = "Secret plans" }));
        var second = await pushService.WaitAsync(2);
        Assert.DoesNotContain("Secret", Decrypt(second.Body, browser, browserPublic, auth), StringComparison.Ordinal);
        AssertStatus(HttpStatusCode.Created, await ana.PostAsync($"/api/v1/conversations/{direct}/messages", new { clientMessageId = "p3", body = "Hello?" }));
        await Task.Delay(1500, Ct);
        Assert.Equal(2, pushService.Requests.Count);

        // Unsubscribing is only possible for one's own browser and is idempotent.
        AssertStatus(HttpStatusCode.NoContent, await ben.PostAsync("/api/v1/notifications/push/subscriptions/remove", new { endpoint = "https://push.test/send/ben-1" }));

        // A browser whose sign-in ended gets nothing more, even if it never unsubscribed.
        pushService.Status = HttpStatusCode.Created;
        AssertStatus(HttpStatusCode.NoContent, await ben.PostAsync("/api/v1/notifications/push/subscriptions", subscription with { }));
        AssertStatus(HttpStatusCode.NoContent, await ben.PostAsync("/api/v1/auth/logout"));
        AssertStatus(HttpStatusCode.Created, await ana.PostAsync($"/api/v1/conversations/{direct}/messages", new { clientMessageId = "p4", body = "Still there?" }));
        await Task.Delay(1500, Ct);
        Assert.Equal(2, pushService.Requests.Count);
    }

    private static async Task<Application.Abstractions.EmailMessage> WaitForEmailAsync(ChatXApiFactory api, string to)
    {
        for (var i = 0; i < 100; i++)
        {
            if (api.Emails.To(to, "unread-messages") is [var email, ..])
            {
                return email;
            }

            await Task.Delay(100, Ct);
        }

        Assert.Fail($"No unread-messages email to {to}.");
        return null!;
    }

    private static string Decrypt(byte[] body, ECDiffieHellman receiver, byte[] receiverPublic, byte[] auth)
    {
        var salt = body[..16];
        var senderPublic = body[21..(21 + body[20])];
        var cipher = body[(21 + body[20])..];
        using var sender = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = senderPublic[1..33], Y = senderPublic[33..65] },
        });
        var shared = receiver.DeriveRawSecretAgreement(sender.PublicKey);
        byte[] info = [.. Encoding.ASCII.GetBytes("WebPush: info\0"), .. receiverPublic, .. senderPublic];
        var ikm = HKDF.Expand(HashAlgorithmName.SHA256, HKDF.Extract(HashAlgorithmName.SHA256, shared, auth), 32, info);
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));
        var plain = new byte[cipher.Length - 16];
        using (var aes = new AesGcm(cek, 16))
        {
            aes.Decrypt(nonce, cipher[..^16], cipher[^16..], plain);
        }

        return Encoding.UTF8.GetString(plain[..^1]);
    }

    private sealed record PushRequest(string Url, string Authorization, string Encoding, byte[] Body);

    /// <summary>A push service that records what it receives and answers with <see cref="Status"/>.</summary>
    private sealed class FakePushService : HttpMessageHandler
    {
        public ConcurrentQueue<PushRequest> Requests { get; } = new();

        public HttpStatusCode Status { get; set; } = HttpStatusCode.Created;

        public async Task<PushRequest> WaitAsync(int count)
        {
            for (var i = 0; i < 100 && Requests.Count < count; i++)
            {
                await Task.Delay(100, Ct);
            }

            Assert.True(Requests.Count >= count, $"Expected {count} push request(s), got {Requests.Count}.");
            return Requests.ToArray()[count - 1];
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(new PushRequest(
                request.RequestUri!.AbsoluteUri,
                request.Headers.TryGetValues("Authorization", out var auth) ? auth.Single() : string.Empty,
                request.Content?.Headers.ContentEncoding.SingleOrDefault() ?? string.Empty,
                await request.Content!.ReadAsByteArrayAsync(cancellationToken)));
            return new HttpResponseMessage(Status);
        }
    }
}
