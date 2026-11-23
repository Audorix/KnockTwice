namespace Audorix.ChatX.Application.Abstractions;

/// <summary>
/// Outbound email port. Implementations (SMTP, Postmark) live in Infrastructure and are
/// selected by <c>Email:Provider</c>. Message bodies must never be written to logs.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <param name="To">Recipient address.</param>
/// <param name="Subject">Subject line (plain text).</param>
/// <param name="TextBody">Plain-text body.</param>
/// <param name="HtmlBody">HTML body; all dynamic values must already be HTML-encoded.</param>
/// <param name="Tag">Non-sensitive category for provider analytics and logs (for example <c>email-confirmation</c>).</param>
public sealed record EmailMessage(string To, string Subject, string TextBody, string HtmlBody, string Tag);

/// <summary>Raised when the configured email provider rejects or cannot accept a message.</summary>
public sealed class EmailDeliveryException : Exception
{
    public EmailDeliveryException()
    {
    }

    public EmailDeliveryException(string message)
        : base(message)
    {
    }

    public EmailDeliveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
