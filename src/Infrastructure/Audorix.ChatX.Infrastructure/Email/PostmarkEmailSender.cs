using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Audorix.ChatX.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Infrastructure.Email;

/// <summary>
/// Postmark delivery through its HTTP API (<c>POST /email</c>). No Postmark SDK is required.
/// See https://postmarkapp.com/developer/api/email-api.
/// </summary>
internal sealed class PostmarkEmailSender(HttpClient http, IOptions<EmailOptions> options, ILogger<PostmarkEmailSender> logger) : IEmailSender
{
    public const string HttpClientName = "postmark";

    /// <summary>Postmark field names are PascalCase (no camelCase naming policy).</summary>
    private static readonly System.Text.Json.JsonSerializerOptions PostmarkJson = new();

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;
        var payload = new PostmarkMessage(
            From: $"{settings.FromName} <{settings.FromAddress}>",
            To: message.To,
            Subject: message.Subject,
            TextBody: message.TextBody,
            HtmlBody: message.HtmlBody,
            Tag: message.Tag,
            MessageStream: settings.Postmark.MessageStream,
            TrackOpens: false,
            TrackLinks: "None");

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.Postmark.BaseUrl, "email"))
        {
            Content = JsonContent.Create(payload, options: PostmarkJson),
        };
        request.Headers.Add("X-Postmark-Server-Token", settings.Postmark.ServerToken);
        request.Headers.Accept.ParseAdd("application/json");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError("Postmark delivery of {Tag} email failed: provider unreachable.", message.Tag);
            throw new EmailDeliveryException("Postmark could not be reached.", ex);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Sent {Tag} email through Postmark.", message.Tag);
                return;
            }

            var error = await TryReadErrorAsync(response, cancellationToken).ConfigureAwait(false);
            logger.LogError(
                "Postmark rejected {Tag} email with HTTP {StatusCode}, error code {PostmarkErrorCode}.",
                message.Tag,
                (int)response.StatusCode,
                error?.ErrorCode);
            throw new EmailDeliveryException($"Postmark rejected the message (HTTP {(int)response.StatusCode}).");
        }
    }

    private static async Task<PostmarkError?> TryReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<PostmarkError>(cancellationToken).ConfigureAwait(false);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    internal sealed record PostmarkMessage(
        string From,
        string To,
        string Subject,
        string TextBody,
        string HtmlBody,
        string Tag,
        string MessageStream,
        bool TrackOpens,
        string TrackLinks);

    internal sealed record PostmarkError([property: JsonPropertyName("ErrorCode")] int ErrorCode, [property: JsonPropertyName("Message")] string? Message);
}
