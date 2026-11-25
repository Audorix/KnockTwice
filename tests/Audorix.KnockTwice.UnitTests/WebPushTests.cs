using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Audorix.KnockTwice.Domain;
using Audorix.KnockTwice.Domain.Notifications;
using Audorix.KnockTwice.Infrastructure.Notifications;

namespace Audorix.KnockTwice.UnitTests;

/// <summary>Web Push (Phase 15): RFC 8291 message encryption and RFC 8292 VAPID tokens.</summary>
public sealed class WebPushTests
{
    [Fact]
    public void Encryption_matches_the_RFC_8291_example()
    {
        // RFC 8291, Appendix A.
        var senderPrivate = WebPushCrypto.FromBase64Url("yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw");
        var senderPublic = WebPushCrypto.FromBase64Url("BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8");
        var receiverPublic = WebPushCrypto.FromBase64Url("BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4");
        var auth = WebPushCrypto.FromBase64Url("BTBZMqHH6r4Tts7J_aSIgg");
        var salt = WebPushCrypto.FromBase64Url("DGv6ra1nlYgDCS1FRnbzlw");
        using var sender = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = senderPrivate,
            Q = new ECPoint { X = senderPublic[1..33], Y = senderPublic[33..65] },
        });

        var body = WebPushCrypto.Encrypt(Encoding.UTF8.GetBytes("When I grow up, I want to be a watermelon"), receiverPublic, auth, salt, sender);

        Assert.Equal(
            "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN",
            WebPushCrypto.Base64Url(body));
    }

    [Fact]
    public void A_browser_can_decrypt_what_is_sent_to_it()
    {
        using var browser = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var p = browser.ExportParameters(false).Q;
        byte[] browserPublic = [0x04, .. p.X!, .. p.Y!];
        var auth = RandomNumberGenerator.GetBytes(16);
        var message = """{"title":"Ana","body":"Hola 👋","url":"/conversations?id=1","tag":"1"}""";

        var body = WebPushCrypto.Encrypt(Encoding.UTF8.GetBytes(message), browserPublic, auth);

        Assert.Equal(message, Decrypt(body, browser, browserPublic, auth));
        Assert.NotEqual(body, WebPushCrypto.Encrypt(Encoding.UTF8.GetBytes(message), browserPublic, auth)); // fresh salt and key each time
    }

    [Fact]
    public void Vapid_tokens_are_ES256_signed_for_the_push_service_origin()
    {
        var (publicKey, privateKey) = WebPushCrypto.CreateVapidKeys();
        var now = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
        var token = WebPushCrypto.VapidToken("https://push.example.net", "https://chat.example.org", publicKey, privateKey, now);

        var parts = token.Split('.');
        using var claims = JsonDocument.Parse(WebPushCrypto.FromBase64Url(parts[1]));
        Assert.Equal("https://push.example.net", claims.RootElement.GetProperty("aud").GetString());
        Assert.Equal(now.AddHours(12).ToUnixTimeSeconds(), claims.RootElement.GetProperty("exp").GetInt64());
        using var verifier = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = publicKey[1..33], Y = publicKey[33..65] } });
        Assert.True(verifier.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), WebPushCrypto.FromBase64Url(parts[2]),
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public void Subscriptions_need_an_https_endpoint_and_well_formed_keys()
    {
        var now = DateTimeOffset.UtcNow;
        var key = WebPushCrypto.Base64Url(new byte[65]);
        var auth = WebPushCrypto.Base64Url(new byte[16]);
        Assert.Equal("push_endpoint_invalid", Assert.Throws<DomainRuleException>(() =>
            PushSubscription.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "http://push.example.net/x", key, auth, now)).Code);
        Assert.Equal("push_keys_invalid", Assert.Throws<DomainRuleException>(() =>
            PushSubscription.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "https://push.example.net/x", "short", auth, now)).Code);
        var subscription = PushSubscription.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "https://push.example.net/x", key, auth, now);
        for (var i = 1; i < PushSubscription.MaxFailures; i++)
        {
            Assert.False(subscription.Failed());
        }

        Assert.True(subscription.Failed());
    }

    /// <summary>The receiving side of RFC 8291, as a browser does it.</summary>
    private static string Decrypt(byte[] body, ECDiffieHellman receiver, byte[] receiverPublic, byte[] auth)
    {
        var salt = body[..16];
        var idLength = body[20];
        var senderPublic = body[21..(21 + idLength)];
        var cipher = body[(21 + idLength)..];
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

        Assert.Equal(0x02, plain[^1]);
        return Encoding.UTF8.GetString(plain[..^1]);
    }
}
