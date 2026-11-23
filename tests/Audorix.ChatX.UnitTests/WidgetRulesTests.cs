using System.Text;
using Audorix.ChatX.Application.Widgets;
using Audorix.ChatX.Domain;
using Audorix.ChatX.Domain.Widgets;

namespace Audorix.ChatX.UnitTests;

/// <summary>Identity tokens (HS256, owner decision) and the exact-origin rule of ADR-024.</summary>
public sealed class WidgetRulesTests
{
    private const string Secret = "a-test-signing-secret-with-enough-entropy";
    private static readonly Guid WidgetId = Guid.Parse("01a205e7-68c1-76f1-bcaf-e17aba6f1c76");
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(1);

    [Fact]
    public void A_token_signed_with_the_widget_secret_is_accepted()
    {
        var token = IdentityTokens.Sign(Secret, WidgetId, " customer-42 ", "Dora", "dora@example.org", Now.AddMinutes(-1), TimeSpan.FromMinutes(10));
        var identity = IdentityTokens.Verify(token, Secret, WidgetId, Now, MaxLifetime);
        Assert.Equal(new VerifiedIdentity("customer-42", "Dora", "dora@example.org"), identity);
    }

    [Fact]
    public void The_name_defaults_to_the_subject()
    {
        var token = IdentityTokens.Sign(Secret, WidgetId, "customer-42", null, null, Now, TimeSpan.FromMinutes(10));
        Assert.Equal("customer-42", IdentityTokens.Verify(token, Secret, WidgetId, Now, MaxLifetime)!.Name);
    }

    public static TheoryData<string> Refused => new()
    {
        IdentityTokens.Sign("another-secret", WidgetId, "c", null, null, Now, TimeSpan.FromMinutes(5)),            // wrong key
        IdentityTokens.Sign(Secret, Guid.NewGuid(), "c", null, null, Now, TimeSpan.FromMinutes(5)),               // other widget
        IdentityTokens.Sign(Secret, WidgetId, "c", null, null, Now.AddMinutes(-10), TimeSpan.FromMinutes(5)),     // expired
        IdentityTokens.Sign(Secret, WidgetId, "c", null, null, Now.AddMinutes(5), TimeSpan.FromMinutes(5)),       // issued in the future
        IdentityTokens.Sign(Secret, WidgetId, "c", null, null, Now, TimeSpan.FromHours(3)),                       // lives too long
        IdentityTokens.Sign(Secret, WidgetId, " ", null, null, Now, TimeSpan.FromMinutes(5)),                     // no subject
        Forge("""{"alg":"none","typ":"JWT"}""", Now),                                                            // unsigned
        Forge("""{"alg":"HS512","typ":"JWT"}""", Now),                                                           // other algorithm
        Tamper(IdentityTokens.Sign(Secret, WidgetId, "c", null, null, Now, TimeSpan.FromMinutes(5))),            // payload changed
        "a.b",
        "not base64.at.all",
        string.Empty,
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void Anything_else_is_refused(string token) => Assert.Null(IdentityTokens.Verify(token, Secret, WidgetId, Now, MaxLifetime));

    [Theory]
    [InlineData("https://Shop.Example.org", "https://shop.example.org")]
    [InlineData("https://shop.example.org:443/", "https://shop.example.org")]
    [InlineData("http://localhost:5173", "http://localhost:5173")]
    [InlineData("https://bücher.example", "https://xn--bcher-kva.example")]
    public void Origins_are_normalized(string input, string expected) => Assert.Equal(expected, Widget.NormalizeOrigin(input));

    [Theory]
    [InlineData("shop.example.org")]
    [InlineData("ftp://shop.example.org")]
    [InlineData("https://shop.example.org/path")]
    [InlineData("https://shop.example.org/?q=1")]
    [InlineData("https://user@shop.example.org")]
    [InlineData("https://*.example.org")]
    public void Anything_but_an_exact_origin_is_rejected(string input) =>
        Assert.Equal("widget_origin_invalid", Assert.Throws<DomainRuleException>(() => Widget.NormalizeOrigin(input)).Code);

    [Fact]
    public void Only_listed_origins_may_embed()
    {
        var widget = Widget.Create(WidgetId, Guid.NewGuid(), "Shop", Guid.NewGuid(), "protected", Now);
        widget.Update("Shop", "Chat", null, "#2563eb", WidgetPosition.Right, "auto", true, ["https://shop.example.org"], Now);
        Assert.True(widget.AllowsOrigin("https://SHOP.example.org"));
        Assert.False(widget.AllowsOrigin("https://shop.example.org.evil.example"));
        Assert.False(widget.AllowsOrigin("http://shop.example.org"));
        Assert.False(widget.AllowsOrigin(null));
        Assert.False(widget.AllowsOrigin("null"));
    }

    [Fact]
    public void Rate_windows_slide()
    {
        var time = new ManualTime(Now);
        var limiter = new VisitorRateLimiter(new Application.Common.LocalRateCounter(time));
        Assert.True(limiter.TryAcquire("k", 2, TimeSpan.FromMinutes(1)));
        Assert.True(limiter.TryAcquire("k", 2, TimeSpan.FromMinutes(1)));
        Assert.False(limiter.TryAcquire("k", 2, TimeSpan.FromMinutes(1)));
        Assert.True(limiter.TryAcquire("other", 2, TimeSpan.FromMinutes(1)));
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(limiter.TryAcquire("k", 2, TimeSpan.FromMinutes(1)));
    }

    private static string Encode(string json) => IdentityTokens.Base64UrlEncode(Encoding.UTF8.GetBytes(json));

    private static string Forge(string header, DateTimeOffset now) =>
        $"{Encode(header)}.{Encode($$"""{"sub":"c","aud":"{{WidgetId}}","iat":{{now.ToUnixTimeSeconds()}},"exp":{{now.AddMinutes(5).ToUnixTimeSeconds()}}}""")}.";

    private static string Tamper(string token)
    {
        var parts = token.Split('.');
        parts[1] = Encode($$"""{"sub":"admin","aud":"{{WidgetId}}","iat":{{Now.ToUnixTimeSeconds()}},"exp":{{Now.AddMinutes(5).ToUnixTimeSeconds()}}}""");
        return string.Join('.', parts);
    }

    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
