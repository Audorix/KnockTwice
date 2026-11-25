using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Audorix.KnockTwice.Application.Abstractions;

namespace Audorix.KnockTwice.IntegrationTests.Support;

/// <summary>Captures outgoing email instead of delivering it.</summary>
public sealed partial class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyList<EmailMessage> Sent => _sent.ToList();

    public bool Fail { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new EmailDeliveryException("Simulated delivery failure.");
        }

        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> To(string address, string tag) =>
        Sent.Where(m => m.To == address && m.Tag == tag).ToList();

    /// <summary>Extracts user id and token from the latest link to <paramref name="route"/> sent to the address.</summary>
    public (Guid UserId, string Token) LatestLink(string address, string tag, string route)
    {
        var messages = To(address, tag);
        var message = messages[^1];
        var match = LinkPattern().Matches(message.TextBody).Single(m => m.Groups["route"].Value == route);
        return (Guid.Parse(match.Groups["user"].Value), Uri.UnescapeDataString(match.Groups["token"].Value));
    }

    [GeneratedRegex(@"https://chat\.test/(?<route>[a-z-]+)\?user=(?<user>[0-9a-f-]{36})&token=(?<token>[^\s&]+)")]
    private static partial Regex LinkPattern();
}

/// <summary>Captcha verifier whose outcome is set by the test; records the actions it was asked to verify.</summary>
public sealed class FakeCaptchaVerifier : ICaptchaVerifier
{
    private readonly ConcurrentQueue<string> _actions = new();

    public CaptchaOutcome Outcome { get; set; } = CaptchaOutcome.Passed;

    public IReadOnlyList<string> Actions => _actions.ToList();

    public CaptchaClientConfiguration ClientConfiguration { get; } = new("turnstile", "test-site-key");

    public Task<CaptchaOutcome> VerifyAsync(string? token, string action, string? remoteIp, CancellationToken cancellationToken)
    {
        _actions.Enqueue(action);
        return Task.FromResult(Outcome);
    }
}
