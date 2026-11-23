using Audorix.ChatX.Application.Abstractions;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Audorix.ChatX.Infrastructure.Email;

/// <summary>
/// Provider-neutral SMTP delivery (MailKit). The default configuration targets a local
/// development server (Mailpit) on localhost:1025 without TLS or authentication.
/// </summary>
internal sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;
        using var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress!));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Headers.Add("X-ChatX-Tag", message.Tag);
        mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();

        using var client = new SmtpClient { Timeout = (int)settings.Smtp.Timeout.TotalMilliseconds };
        try
        {
            await client.ConnectAsync(settings.Smtp.Host!, settings.Smtp.Port, ParseSecurity(settings.Smtp.Security), cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrEmpty(settings.Smtp.Username))
            {
                await client.AuthenticateAsync(settings.Smtp.Username, settings.Smtp.Password!, cancellationToken).ConfigureAwait(false);
            }

            await client.SendAsync(new SendRequest(mime), cancellationToken).ConfigureAwait(false);
            await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Sent {Tag} email through SMTP.", message.Tag);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("SMTP delivery of {Tag} email failed ({ExceptionType}).", message.Tag, ex.GetType().Name);
            throw new EmailDeliveryException("The SMTP server did not accept the message.", ex);
        }
    }

    private static SecureSocketOptions ParseSecurity(string value) =>
        Enum.TryParse<SecureSocketOptions>(value, ignoreCase: true, out var parsed) ? parsed : SecureSocketOptions.None;
}
