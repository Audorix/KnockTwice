using Audorix.ChatX.Domain.Conversations;

namespace Audorix.ChatX.Application.Demo;

/// <summary>A company's support team: the administrator who loads the data ("me") and these people are support agents.</summary>
public sealed record DemoSupportDesk(string CompanyKey, IReadOnlyList<string> Agents);

/// <param name="Agent">The agent who wrote it (a person key or "me"); null for the customer.</param>
/// <param name="At">Minutes after the start of the demo period.</param>
public sealed record DemoSupportMessage(string? Agent, string Body, int At);

public sealed record DemoSupportNote(string Agent, string Body, int At);

/// <summary>A customer's support conversation through one of the company's two demo widgets.</summary>
/// <param name="InApp">Came through the "In-app" widget rather than "Website chat".</param>
/// <param name="Subject">Null for a few, so the first message stands in as the title.</param>
/// <param name="Assignee">A person key, "me", or null.</param>
public sealed record DemoSupport(
    string Key, Guid Id, string CompanyKey, bool InApp, string CustomerName, string CustomerLanguage, string? Subject, SupportStatus Status,
    string? Assignee, IReadOnlyList<DemoSupportMessage> Messages, IReadOnlyList<DemoSupportNote> Notes);

/// <summary>
/// Support demo data (owner request of 2026-11-24): about 500 customer conversations with replies,
/// internal notes, statuses and assignments, in the companies the administrator who loads it belongs
/// to. That administrator is a support agent there, so the whole inbox is theirs to work.
/// </summary>
public sealed partial class DemoDataPlan
{
    public const int SupportCount = 500;

    public IReadOnlyList<DemoSupportDesk> SupportDesks { get; private set; } = [];

    public IReadOnlyList<DemoSupport> Support { get; private set; } = [];

    private sealed record SupportTopic(string Subject, string[] Customer, string[] Agent, string[] Notes, bool Spanish = false);

    private (List<DemoSupportDesk>, List<DemoSupport>) BuildSupport(Random random)
    {
        var companies = Companies.Where(c => c.Members.Any(m => m.Key == Me)).ToList();
        var desks = companies.Select(c => new DemoSupportDesk(c.Key,
            Sample(random, c.Members.Select(m => m.Key).Where(k => k != Me).ToList(), 3))).ToList();

        // The first two companies carry most of the load; the rest share what is left.
        var weights = companies.Select((_, i) => i switch { 0 => 120, 1 => 80, _ => 0 }).ToArray();
        var restCount = companies.Count - 2;
        for (var i = 2; i < companies.Count; i++)
        {
            weights[i] = (SupportCount - 200) / restCount + (i - 2 < (SupportCount - 200) % restCount ? 1 : 0);
        }

        var period = PeriodDays * 24 * 60;
        var items = new List<DemoSupport>();
        for (var d = 0; d < desks.Count; d++)
        {
            var desk = desks[d];
            var team = desk.Agents.Prepend(Me).ToList();
            for (var i = 0; i < weights[d]; i++)
            {
                var n = items.Count;
                var spanish = random.Next(5) == 0;
                var pool = spanish ? SpanishTopics : EnglishTopics;
                var topic = pool[random.Next(pool.Length)];
                var first = Pick(random, FirstNames);
                var customer = $"{first} {Pick(random, LastNames)}";

                // About 40% open, 15% pending (waiting for the customer), 45% resolved.
                var roll = random.Next(100);
                var status = roll < 40 ? SupportStatus.Open : roll < 55 ? SupportStatus.Pending : SupportStatus.Closed;

                // Open ones are recent; pending ones a few days old; resolved ones anywhere in the period.
                var end = status switch
                {
                    SupportStatus.Open => period - random.Next(5, 3 * 24 * 60),
                    SupportStatus.Pending => period - random.Next(12 * 60, 7 * 24 * 60),
                    _ => random.Next(2 * 24 * 60, period - 24 * 60),
                };

                string? assignee = status switch
                {
                    SupportStatus.Open when random.Next(100) < 35 => null,
                    _ => random.Next(100) < 25 ? Me : desk.Agents[random.Next(desk.Agents.Count)],
                };

                // The shape of the conversation follows its state: open ones end with the customer,
                // pending ones with the team, resolved ones with the customer's thanks (and sometimes a goodbye).
                var turns = new List<(bool Customer, int Line)>();
                if (assignee is null)
                {
                    turns.Add((true, 0));
                }
                else if (status == SupportStatus.Open)
                {
                    turns.AddRange([(true, 0), (false, 0), (true, 1)]);
                }
                else if (status == SupportStatus.Pending)
                {
                    turns.AddRange([(true, 0), (false, 0), (true, 1), (false, 1)]);
                }
                else
                {
                    turns.AddRange([(true, 0), (false, 0), (true, 1), (false, 1), (true, 2)]);
                    if (random.Next(2) == 0)
                    {
                        turns.Add((false, 2));
                    }
                }

                var gaps = turns.Select(_ => random.Next(2, 25)).ToList();
                var at = end - gaps.Sum();
                var messages = new List<DemoSupportMessage>();
                for (var t = 0; t < turns.Count; t++)
                {
                    at += gaps[t];
                    var (fromCustomer, line) = turns[t];
                    var text = (fromCustomer ? topic.Customer[line] : topic.Agent[line]).Replace("{name}", first, StringComparison.Ordinal);
                    messages.Add(new DemoSupportMessage(fromCustomer ? null : assignee, text, at));
                }

                // Internal notes on about a third of the conversations someone works on.
                var notes = new List<DemoSupportNote>();
                if (assignee is not null && topic.Notes.Length > 0 && random.Next(100) < 35)
                {
                    var count = random.Next(1, Math.Min(2, topic.Notes.Length) + 1);
                    for (var k = 0; k < count; k++)
                    {
                        var writer = random.Next(3) == 0 ? team[random.Next(team.Count)] : assignee;
                        var after = messages[Math.Min(messages.Count - 1, 1 + k)].At;
                        notes.Add(new DemoSupportNote(writer, topic.Notes[k].Replace("{name}", first, StringComparison.Ordinal), after + 1));
                    }
                }

                var key = $"support-{n:D3}";
                items.Add(new DemoSupport(key, DemoId("conversation:" + key), desk.CompanyKey, random.Next(3) == 0, customer, spanish ? "es" : "en",
                    random.Next(10) == 0 ? null : topic.Subject, status, assignee, messages, notes));
            }
        }

        return (desks, items);
    }

    private static readonly SupportTopic[] EnglishTopics =
    [
        new("Unable to upload a document",
            ["Hi! I'm trying to upload a PDF, but it stops at 80% every time.", "It's 12 MB. I'm using Chrome.", "That worked, thank you so much!"],
            ["Hi {name}, I can help. What size is the file, and which browser are you using?", "Thanks! Files over 10 MB need to be compressed first. Could you try a smaller copy?", "Great, I'll mark this as resolved. Have a good day!"],
            ["Checking the upload logs before asking {name} to retry.", "Their plan has a 10 MB file limit; suggested compressing the PDF."]),
        new("Invite link has expired",
            ["Could you send me a new invitation? The link says it expired.", "Same email address as this chat, please.", "Got it, I'm in now!"],
            ["Hi {name}, invitations last seven days. Which email should I send the new one to?", "Done, a fresh invitation is on its way.", "Welcome aboard!"],
            ["Their administrator is on leave, so we're resending from our side.", "Second expired invitation for this company this month."]),
        new("Access to the shared workspace",
            ["I need access to the design workspace for my team.", "My manager approved it yesterday.", "Perfect, I can see it now."],
            ["Hi {name}, workspace access is granted by its administrators. Do you know who manages it?", "Thanks, I've asked their administrator to add you.", "Glad it's sorted!"],
            ["Workspace administrator confirmed the approval by email."]),
        new("Can I update my organization name?",
            ["We've rebranded and need to update the organization name.", "Where do I find that?", "Found it, thanks a lot."],
            ["Hi {name}, owners can rename it from the Organizations page.", "Open Organizations, choose Manage next to yours, then Rename.", "You're welcome!"],
            ["{name} is the owner; no action needed from us."]),
        new("Exporting conversation history",
            ["Is there a way to export our past conversations?", "We need it for an audit next month.", "Understood, thanks for checking."],
            ["Hi {name}, exports are on our roadmap. Which format would you need?", "I've added your request to the feature ticket; an administrator can request a data copy meanwhile.", "Anytime!"],
            ["Linked to the export feature request.", "Audit is next month; flagged as a priority."]),
        new("Notification settings",
            ["I'm not receiving email notifications anymore.", "I checked my spam folder, nothing there.", "They're coming through again, thanks!"],
            ["Hi {name}, let's check your settings. Is \"When you are away\" set to email on your account page?", "Your address bounced once, so emails were paused. I've resumed them.", "Great to hear!"],
            ["A bounce was recorded earlier this month; cleared it."]),
        new("Charged twice this month",
            ["I was charged twice for this month.", "Both charges are on the same card.", "I see the refund, thank you."],
            ["Hi {name}, sorry about that. Could you share the invoice number?", "Thanks, one charge was a duplicate. The refund will show in three to five days.", "Thanks for your patience!"],
            ["Duplicate charge confirmed; refund issued.", "Copied the billing team on the refund."]),
        new("Password reset email not arriving",
            ["I asked for a password reset an hour ago, but nothing arrived.", "Yes, it's the address I signed up with.", "It arrived, I'm back in. Thanks!"],
            ["Hi {name}, sorry for the trouble. Is the address you used the one on your account?", "I've sent another reset link; it can take a few minutes.", "Happy to help!"],
            ["Mail provider showed a short delay this morning."]),
        new("Lost my phone with two-factor codes",
            ["I lost my phone and can't sign in with two-factor authentication.", "I don't have the recovery codes, unfortunately.", "I'm signed in again, thank you!"],
            ["Hi {name}, we can help. Do you still have your recovery codes?", "No problem. I've started an identity check; please reply to the email we just sent.", "Remember to set up two-factor again on your new phone."],
            ["Identity check started; waiting for the signed form.", "Verified through the account owner."]),
        new("Search doesn't find older messages",
            ["Search only finds messages from the last few weeks.", "It happens in every conversation.", "Older messages show up now, thanks!"],
            ["Hi {name}, thanks for reporting it. Does it happen in one conversation or everywhere?", "Your installation was rebuilding its search index. It's finished now; please try again.", "Thanks for letting us know!"],
            ["Index rebuild was still running for this company."]),
        new("Mobile app crashes on start",
            ["The app closes as soon as I open it.", "I'm on the latest version already.", "Reinstalling fixed it, thanks."],
            ["Hi {name}, sorry about that. Which phone and app version are you using?", "Could you try removing and reinstalling the app? That clears a damaged cache.", "Glad it works again!"],
            ["Known cache issue after the last update.", "Asked engineering to look at the crash report."]),
        new("Adding more members to our plan",
            ["We're hiring and need ten more seats.", "Monthly billing, please.", "All set, thank you!"],
            ["Hi {name}, happy to help. Would you like monthly or yearly billing?", "I've added ten seats; the next invoice will show the change.", "Congratulations on growing the team!"],
            ["Sales informed about the seat increase."]),
        new("Changing my account email",
            ["I need to change the email address on my account.", "The new one is my work address.", "Confirmed, thanks!"],
            ["Hi {name}, you can change it on your account page. We'll send a confirmation to the new address.", "Once you click the link in that email, the change takes effect.", "You're welcome!"],
            []),
        new("Adding the chat to our website",
            ["How do we add the chat window to our website?", "We use a static site, no special platform.", "It's live on our site now, great!"],
            ["Hi {name}, an organization administrator creates a website chat under Organizations. Which platform is your site built with?", "Then copy the snippet from the website chat settings into your pages and add your address to the allowed sites.", "Looks great!"],
            ["Sent the embedding guide.", "Their site address was missing from the allowed list."]),
        new("Pages load slowly",
            ["Everything has been slow since this morning.", "It's slow on both Wi-Fi and mobile data.", "Much faster now, thank you."],
            ["Hi {name}, thanks for telling us. Is it slow on every network?", "We found a slow database server and moved the load. Could you check again?", "Thanks for your patience!"],
            ["Correlates with the morning incident.", "Incident closed; confirmed with {name}."]),
        new("Deleting my account",
            ["I'd like to delete my account.", "Yes, I understand my messages stay as \"Deleted user\".", "Done, thanks for the help."],
            ["Hi {name}, you can do it yourself on your account page. Before that, owners must hand over their organizations. Is that the case for you?", "Then you're ready: Account, Delete account, and confirm with your password.", "Take care!"],
            ["Not an owner anywhere; no blockers."]),
    ];

    private static readonly SupportTopic[] SpanishTopics =
    [
        new("No puedo subir un archivo",
            ["Hola, intento subir un PDF pero se queda al 80 %.", "Pesa 15 MB y uso Firefox.", "¡Funcionó, muchas gracias!"],
            ["Hola {name}, te ayudo. ¿Cuánto pesa el archivo y qué navegador usas?", "Gracias. Los archivos de más de 10 MB deben comprimirse. ¿Puedes probar con una copia más pequeña?", "Perfecto, cierro la conversación. ¡Buen día!"],
            ["Revisando los registros de subida antes de pedir otro intento.", "El plan tiene un límite de 10 MB."], true),
        new("Cambiar el correo de la cuenta",
            ["Necesito cambiar el correo de mi cuenta.", "El nuevo es el del trabajo.", "Confirmado, ¡gracias!"],
            ["Hola {name}, puedes cambiarlo en la página de tu cuenta. Enviaremos una confirmación a la nueva dirección.", "Cuando pulses el enlace de ese correo, el cambio queda hecho.", "¡De nada!"],
            [], true),
        new("Factura duplicada",
            ["Me habéis cobrado dos veces este mes.", "Los dos cargos están en la misma tarjeta.", "Ya veo el reembolso, gracias."],
            ["Hola {name}, perdona las molestias. ¿Puedes indicarnos el número de factura?", "Gracias, uno de los cargos estaba duplicado. El reembolso llegará en tres a cinco días.", "¡Gracias por tu paciencia!"],
            ["Cargo duplicado confirmado; reembolso emitido."], true),
        new("No recibo notificaciones",
            ["Ya no me llegan las notificaciones por correo.", "He mirado en spam y no hay nada.", "Ya llegan otra vez, ¡gracias!"],
            ["Hola {name}, revisemos la configuración. ¿Tienes activado el correo en \"Cuando no estás\"?", "Tu dirección rebotó una vez y pausamos los envíos. Ya los he reactivado.", "¡Me alegro!"],
            ["Rebote registrado este mes; eliminado."], true),
        new("Acceso al espacio de trabajo",
            ["Necesito acceso al espacio de trabajo de diseño.", "Mi responsable lo aprobó ayer.", "Perfecto, ya lo veo."],
            ["Hola {name}, el acceso lo conceden sus administradores. ¿Sabes quién lo gestiona?", "Gracias, he pedido a su administrador que te añada.", "¡Genial!"],
            ["El administrador confirmó la aprobación por correo."], true),
    ];
}
