using System.Globalization;
using System.Net;
using Audorix.KnockTwice.Application.Abstractions;
using Microsoft.Extensions.Localization;

namespace Audorix.KnockTwice.Application.Email;

/// <summary>Marker type for <c>Resources/Email/AccountEmails*.resx</c>.</summary>
#pragma warning disable CA1812 // Used as a generic type argument for IStringLocalizer<T>.
internal sealed class AccountEmails;
#pragma warning restore CA1812

/// <summary>
/// Builds localized account emails. Every dynamic value is HTML-encoded in the HTML body;
/// links are absolute URLs to the web app built from <c>App:PublicBaseUrl</c>.
/// </summary>
public sealed class AccountEmailComposer
{
    private readonly IStringLocalizer _text;

    public AccountEmailComposer(IStringLocalizerFactory localizerFactory)
    {
        ArgumentNullException.ThrowIfNull(localizerFactory);
        _text = localizerFactory.Create(typeof(AccountEmails));
    }

    public EmailMessage EmailConfirmation(string to, string displayName, string language, Uri link, TimeSpan validity) =>
        Compose(language, "email-confirmation", to, "ConfirmSubject", displayName,
            [Text("ConfirmBody")], (Text("ConfirmAction"), link), Format("LinkExpiry", Hours(validity)));

    public EmailMessage PasswordReset(string to, string displayName, string language, Uri link, TimeSpan validity) =>
        Compose(language, "password-reset", to, "ResetSubject", displayName,
            [Text("ResetBody")], (Text("ResetAction"), link), Format("LinkExpiry", Hours(validity)));

    public EmailMessage AlreadyRegistered(string to, string displayName, string language, Uri signInLink, Uri resetLink) =>
        Compose(language, "already-registered", to, "ExistingSubject", displayName,
            [Text("ExistingBody"), Format("ExistingReset", resetLink.AbsoluteUri)], (Text("ExistingAction"), signInLink), null);

    private EmailMessage Compose(
        string language,
        string tag,
        string to,
        string subjectKey,
        string displayName,
        Func<string>[] paragraphs,
        (Func<string> Label, Uri Url) action,
        Func<string>? footnote)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            var subject = _text[subjectKey].Value;
            var greeting = string.Format(CultureInfo.CurrentCulture, _text["Greeting"].Value, displayName);
            var body = paragraphs.Select(p => p()).ToList();
            var actionLabel = action.Label();
            var note = footnote?.Invoke();
            var ignore = _text["IgnoreNotice"].Value;
            var signature = _text["Signature"].Value;

            var text = string.Join("\n\n", new[] { greeting }
                .Concat(body)
                .Append($"{actionLabel}: {action.Url.AbsoluteUri}")
                .Concat(note is null ? [] : [note])
                .Append(ignore)
                .Append(signature));

            var html =
                "<!doctype html><html><body style=\"font-family:system-ui,-apple-system,'Segoe UI',Roboto,Arial,sans-serif;color:#1d2725;line-height:1.5\">" +
                $"<p>{Encode(greeting)}</p>" +
                string.Concat(body.Select(p => $"<p>{Encode(p)}</p>")) +
                $"<p><a href=\"{Encode(action.Url.AbsoluteUri)}\" style=\"display:inline-block;padding:10px 18px;background:#0b6e66;color:#ffffff;border-radius:6px;text-decoration:none\">{Encode(actionLabel)}</a></p>" +
                (note is null ? string.Empty : $"<p style=\"color:#56635f\">{Encode(note)}</p>") +
                $"<p style=\"color:#56635f\">{Encode(ignore)}</p><p>{Encode(signature)}</p></body></html>";

            return new EmailMessage(to, subject, text, html, tag);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    private Func<string> Text(string key) => () => _text[key].Value;

    private Func<string> Format(string key, object argument) =>
        () => string.Format(CultureInfo.CurrentCulture, _text[key].Value, argument);

    private static int Hours(TimeSpan validity) => (int)Math.Max(1, Math.Round(validity.TotalHours));

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
