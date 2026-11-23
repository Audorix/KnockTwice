using System.Globalization;
using System.Net;
using Audorix.ChatX.Application.Abstractions;
using Microsoft.Extensions.Localization;

namespace Audorix.ChatX.Application.Notifications;

/// <summary>Marker type for <c>Resources/Email/NotificationEmails*.resx</c>.</summary>
#pragma warning disable CA1812 // Used as a generic type argument for the localizer.
internal sealed class NotificationEmails;
#pragma warning restore CA1812

/// <summary>One conversation in an unread-messages email.</summary>
/// <param name="Preview">The latest message's excerpt, or null when the person does not allow previews.</param>
public sealed record UnreadItem(string Conversation, string LatestAuthor, int Count, string? Preview, DateTimeOffset LatestAtUtc);

/// <summary>The localized "messages are waiting" email (Phase 15). Every dynamic value is HTML-encoded.</summary>
public sealed class NotificationEmailComposer
{
    public const string Tag = "unread-messages";
    private const int MaxItems = 10;

    private readonly IStringLocalizer _text;

    public NotificationEmailComposer(IStringLocalizerFactory localizerFactory)
    {
        ArgumentNullException.ThrowIfNull(localizerFactory);
        _text = localizerFactory.Create(typeof(NotificationEmails));
    }

    public EmailMessage UnreadMessages(string to, string displayName, string language, IReadOnlyList<UnreadItem> items, Uri open, Uri settings)
    {
        ArgumentNullException.ThrowIfNull(items);
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            var total = items.Sum(i => i.Count);
            var subject = total == 1 ? T("SubjectOne") : F("SubjectMany", total);
            var greeting = F("Greeting", displayName);
            var shown = items.Take(MaxItems).ToList();
            var lines = shown.Select(i => (
                Line: i.Count == 1 ? F("ItemOne", i.Conversation, i.LatestAuthor) : F("ItemMany", i.Conversation, i.Count, i.LatestAuthor),
                i.Preview)).ToList();
            var more = items.Count > MaxItems ? F("More", items.Count - MaxItems) : null;
            var action = T("Action");
            var footer = F("Footer", settings.AbsoluteUri);
            var signature = T("Signature");

            var text = string.Join("\n\n", new[] { greeting, T("Intro") }
                .Concat(lines.Select(l => l.Preview is null ? $"• {l.Line}" : $"• {l.Line}\n  “{l.Preview}”"))
                .Concat(more is null ? [] : [more])
                .Append($"{action}: {open.AbsoluteUri}")
                .Append(footer)
                .Append(signature));

            var html =
                "<!doctype html><html><body style=\"font-family:system-ui,-apple-system,'Segoe UI',Roboto,Arial,sans-serif;color:#1d2725;line-height:1.5\">" +
                $"<p>{E(greeting)}</p><p>{E(T("Intro"))}</p><ul>" +
                string.Concat(lines.Select(l => $"<li>{E(l.Line)}" + (l.Preview is null ? string.Empty : $"<br><span style=\"color:#56635f\">“{E(l.Preview)}”</span>") + "</li>")) +
                "</ul>" +
                (more is null ? string.Empty : $"<p>{E(more)}</p>") +
                $"<p><a href=\"{E(open.AbsoluteUri)}\" style=\"display:inline-block;padding:10px 18px;background:#0b6e66;color:#ffffff;border-radius:6px;text-decoration:none\">{E(action)}</a></p>" +
                $"<p style=\"color:#56635f\">{E(footer)}</p><p>{E(signature)}</p></body></html>";
            return new EmailMessage(to, subject, text, html, Tag);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    public const string ReminderTag = "reminder";

    /// <summary>A reminder about a message came due (Phase 20). <paramref name="preview"/> is null without previews.</summary>
    public EmailMessage Reminder(string to, string displayName, string language, string conversation, string author, string? preview, Uri open)
    {
        ArgumentNullException.ThrowIfNull(open);
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            var subject = F("ReminderSubject", conversation);
            var greeting = F("Greeting", displayName);
            var intro = F("ReminderIntro", author, conversation);
            var action = T("ReminderAction");
            var signature = T("Signature");
            var text = string.Join("\n\n", new[] { greeting, intro }
                .Concat(preview is null ? [] : [$"“{preview}”"])
                .Append($"{action}: {open.AbsoluteUri}")
                .Append(signature));
            var html =
                "<!doctype html><html><body style=\"font-family:system-ui,-apple-system,'Segoe UI',Roboto,Arial,sans-serif;color:#1d2725;line-height:1.5\">" +
                $"<p>{E(greeting)}</p><p>{E(intro)}</p>" +
                (preview is null ? string.Empty : $"<p style=\"color:#56635f\">“{E(preview)}”</p>") +
                $"<p><a href=\"{E(open.AbsoluteUri)}\" style=\"display:inline-block;padding:10px 18px;background:#0b6e66;color:#ffffff;border-radius:6px;text-decoration:none\">{E(action)}</a></p>" +
                $"<p>{E(signature)}</p></body></html>";
            return new EmailMessage(to, subject, text, html, ReminderTag);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private string T(string key) => _text[key].Value;

    private string F(string key, params object[] arguments) => string.Format(CultureInfo.CurrentCulture, _text[key].Value, arguments);

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
