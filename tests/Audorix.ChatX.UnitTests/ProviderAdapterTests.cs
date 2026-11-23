using System.Net;
using System.Text;
using System.Text.Json;
using Audorix.ChatX.Application.Abstractions;
using Audorix.ChatX.Infrastructure.Captcha;
using Audorix.ChatX.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.UnitTests;

/// <summary>Email and captcha adapters against a recording HTTP handler (no network).</summary>
public sealed class ProviderAdapterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Postmark_sender_posts_pascal_case_payload_with_server_token_header()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ErrorCode\":0}") });
        var options = Options.Create(new EmailOptions
        {
            Provider = EmailProviders.Postmark,
            FromAddress = "no-reply@example.org",
            FromName = "ChatX",
            Postmark = new PostmarkEmailOptions { ServerToken = "server-token", MessageStream = "outbound" },
        });
        var sender = new PostmarkEmailSender(new HttpClient(handler), options, NullLogger<PostmarkEmailSender>.Instance);

        await sender.SendAsync(new EmailMessage("ada@example.org", "Subject", "text", "<p>html</p>", "email-confirmation"), Ct);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.postmarkapp.com/email", request.Uri.AbsoluteUri);
        Assert.Equal("server-token", request.Headers["X-Postmark-Server-Token"]);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("ChatX <no-reply@example.org>", body.RootElement.GetProperty("From").GetString());
        Assert.Equal("ada@example.org", body.RootElement.GetProperty("To").GetString());
        Assert.Equal("outbound", body.RootElement.GetProperty("MessageStream").GetString());
        Assert.Equal("email-confirmation", body.RootElement.GetProperty("Tag").GetString());
        Assert.False(body.RootElement.GetProperty("TrackOpens").GetBoolean());
    }

    [Fact]
    public async Task Postmark_rejection_becomes_delivery_exception_without_body_or_token_in_message()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage((HttpStatusCode)422)
        {
            Content = new StringContent("{\"ErrorCode\":300,\"Message\":\"Invalid email request\"}", Encoding.UTF8, "application/json"),
        });
        var options = Options.Create(new EmailOptions { Postmark = new PostmarkEmailOptions { ServerToken = "secret-token" } });
        var sender = new PostmarkEmailSender(new HttpClient(handler), options, NullLogger<PostmarkEmailSender>.Instance);

        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() =>
            sender.SendAsync(new EmailMessage("a@example.org", "s", "secret body", "h", "t"), Ct));

        Assert.DoesNotContain("secret", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Turnstile_passes_valid_token_and_sends_secret_response_and_ip()
    {
        var handler = new RecordingHandler(_ => Json("{\"success\":true,\"hostname\":\"chat.example.org\",\"action\":\"register\"}"));
        var verifier = Turnstile(handler);

        var outcome = await verifier.VerifyAsync("token-123", "register", "203.0.113.7", Ct);

        Assert.Equal(CaptchaOutcome.Passed, outcome);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://challenges.cloudflare.com/turnstile/v0/siteverify", request.Uri.AbsoluteUri);
        Assert.Contains("secret=turnstile-secret", request.Body, StringComparison.Ordinal);
        Assert.Contains("response=token-123", request.Body, StringComparison.Ordinal);
        Assert.Contains("remoteip=203.0.113.7", request.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"success\":false,\"error-codes\":[\"invalid-input-response\"]}")]
    [InlineData("{\"success\":true,\"action\":\"forgot_password\"}")]
    public async Task Turnstile_fails_rejected_or_wrong_action_tokens(string response)
    {
        var verifier = Turnstile(new RecordingHandler(_ => Json(response)));
        Assert.Equal(CaptchaOutcome.Failed, await verifier.VerifyAsync("token", "register", null, Ct));
    }

    [Fact]
    public async Task Turnstile_fails_missing_token_without_calling_provider()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("must not be called"));
        Assert.Equal(CaptchaOutcome.Failed, await Turnstile(handler).VerifyAsync(" ", "register", null, Ct));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Turnstile_fails_closed_when_provider_is_unreachable_or_erroring()
    {
        var unreachable = Turnstile(new RecordingHandler(_ => throw new HttpRequestException("down")));
        var erroring = Turnstile(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)));

        Assert.Equal(CaptchaOutcome.Unavailable, await unreachable.VerifyAsync("t", "register", null, Ct));
        Assert.Equal(CaptchaOutcome.Unavailable, await erroring.VerifyAsync("t", "register", null, Ct));
    }

    [Fact]
    public void Captcha_and_email_validation_require_secrets_for_selected_providers()
    {
        var captcha = new CaptchaOptionsValidator().Validate(null, new CaptchaOptions { Provider = "Turnstile" });
        var unknownCaptcha = new CaptchaOptionsValidator().Validate(null, new CaptchaOptions { Provider = "Recaptcha" });
        var postmark = new EmailOptionsValidator().Validate(null, new EmailOptions { Provider = "Postmark" });
        var smtpDefaults = new EmailOptionsValidator().Validate(null, new EmailOptions());
        var none = new CaptchaOptionsValidator().Validate(null, new CaptchaOptions { Provider = "none" });

        Assert.Contains("Captcha:Turnstile:SecretKey", captcha.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Captcha:Provider is not supported", unknownCaptcha.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Email:Postmark:ServerToken", postmark.FailureMessage, StringComparison.Ordinal);
        Assert.True(smtpDefaults.Succeeded, smtpDefaults.FailureMessage);
        Assert.True(none.Succeeded);
    }

    private static TurnstileCaptchaVerifier Turnstile(RecordingHandler handler) => new(
        new HttpClient(handler),
        Options.Create(new CaptchaOptions { Turnstile = new TurnstileOptions { SiteKey = "site", SecretKey = "turnstile-secret" } }),
        NullLogger<TurnstileCaptchaVerifier>.Instance);

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(Uri Uri, Dictionary<string, string> Headers, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var response = respond(request);
            Requests.Add((request.RequestUri!, request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)), body));
            return response;
        }
    }
}
