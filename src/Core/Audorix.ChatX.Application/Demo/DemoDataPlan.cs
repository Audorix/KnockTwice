using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Audorix.ChatX.Application.Demo;

public sealed record DemoPerson(string Key, Guid Id, string Name, string Email, string Language);

/// <param name="Members">Person keys with their role; "me" is the administrator who loads the data.</param>
public sealed record DemoCompany(
    string Key, Guid Id, string Name, string Owner, IReadOnlyList<(string Key, DemoRole Role)> Members, string TeamWorkspace, IReadOnlyList<string> TeamMembers);

public enum DemoRole
{
    Member = 1,
    Administrator = 2,
    Owner = 3,
}

/// <param name="CompanyKey">Null for a personal group.</param>
public sealed record DemoGroup(string Key, Guid Id, string Title, string? Description, string? CompanyKey, string Owner, IReadOnlyList<string> Members);

public sealed record DemoDirect(string Key, string A, string B);

/// <param name="TeamWorkspace">In the company's team workspace; otherwise in its General workspace.</param>
/// <param name="Manager">Creates and manages the channel.</param>
public sealed record DemoChannel(string Key, Guid Id, string Name, string CompanyKey, bool TeamWorkspace, bool Private, string Manager, IReadOnlyList<string> Members);

/// <param name="ConversationKey">A group key, or a direct conversation key.</param>
/// <param name="ClientId">Fixed per message, so a message is created once (the same rule as real clients).</param>
/// <param name="At">Minutes after the start of the demo period.</param>
public sealed record DemoMessage(string ConversationKey, string Author, string ClientId, string Body, int At);

/// <summary>
/// Everything the demo data loader creates (owner request of 2026-11-23: 300 people, 50 companies,
/// 150 groups and about 10,000 messages in groups and direct conversations). Generated from a fixed
/// seed, so every run describes exactly the same records and the loader only adds what is missing.
/// The first people, companies and groups are the ones earlier versions created, so an existing demo
/// installation is extended rather than duplicated. "me" stands for the administrator who loads it.
/// </summary>
public sealed class DemoDataPlan
{
    public const string Me = "me";
    public const string EmailDomain = "demo.chatx.test";
    public const int PeopleCount = 300;
    public const int CompanyCount = 50;
    public const int GroupCount = 150;
    public const int MessageCount = 10_000;
    public const int ChannelCount = 500;

    /// <summary>Messages written in the demo channels, in addition to <see cref="MessageCount"/>.</summary>
    public const int ChannelMessageCount = 2_500;

    /// <summary>Messages are spread over this many days before the load.</summary>
    public const int PeriodDays = 30;

    public IReadOnlyList<DemoPerson> People { get; }

    public IReadOnlyList<DemoCompany> Companies { get; }

    /// <summary>Contact pairs (person keys, "me" included).</summary>
    public IReadOnlyList<(string A, string B)> Contacts { get; }

    public IReadOnlyList<DemoGroup> Groups { get; }

    public IReadOnlyList<DemoDirect> Directs { get; }

    /// <summary>500 channels spread over the companies' workspaces (owner request of 2026-11-23).</summary>
    public IReadOnlyList<DemoChannel> Channels { get; }

    /// <summary>Messages of groups and direct conversations, followed by the channel messages.</summary>
    public IReadOnlyList<DemoMessage> Messages { get; }

    public static Guid DemoId(string key) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes("audorix-chatx-demo:" + key)).AsSpan(0, 16));

    public static string Email(string key) => $"{key}@{EmailDomain}";

    public static DemoDataPlan Create() => new();

    private DemoDataPlan()
    {
        var random = new Random(20261123);
        People = BuildPeople(random);
        var keys = People.Select(p => p.Key).ToList();
        Companies = BuildCompanies(random, keys);
        Contacts = BuildContacts(random, keys);
        var contactsOf = Contacts.SelectMany(c => new[] { (c.A, c.B), (c.B, c.A) })
            .GroupBy(c => c.Item1).ToDictionary(g => g.Key, g => g.Select(c => c.Item2).ToList());
        Groups = BuildGroups(random, keys, contactsOf);
        Directs = BuildDirects(random);
        var messages = BuildMessages(random);

        // Channels came later: their own seed keeps everything above exactly as earlier versions made it.
        var channelRandom = new Random(20261124);
        Channels = BuildChannels(channelRandom);
        Messages = messages.Concat(BuildChannelMessages(channelRandom)).ToList();
    }

    // ---- Channels ----------------------------------------------------------------------------

    private static readonly string[] ChannelNames =
    [
        "announcements", "general-chat", "random", "help-desk", "standup", "planning", "releases", "incidents", "design", "marketing",
        "sales-leads", "customers", "partners", "hiring", "onboarding", "events", "travel", "kudos", "ideas", "feedback", "finance",
        "legal", "security", "infra", "data", "mobile", "web", "qa", "support-tier-2", "social", "book-club", "lunch", "fitness",
        "office-madrid", "office-berlin", "office-tokyo", "remote", "it-help", "procurement", "wins",
    ];

    private List<DemoChannel> BuildChannels(Random random)
    {
        var channels = new List<DemoChannel>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var n = 0; channels.Count < ChannelCount; n++)
        {
            var company = Companies[n % Companies.Count];
            var team = random.Next(3) == 0;
            var roster = (team ? company.TeamMembers : company.Members.Select(m => m.Key).Where(k => k != Me)).ToList();
            if (roster.Count < 2)
            {
                continue;
            }

            var name = Pick(random, ChannelNames);
            if (!names.Add($"{company.Key}|{team}|{name}"))
            {
                name = $"{name}-{n % 97}";
                if (!names.Add($"{company.Key}|{team}|{name}"))
                {
                    continue;
                }
            }

            var manager = roster[random.Next(roster.Count)];
            var members = Sample(random, roster.Where(k => k != manager).ToList(), random.Next(1, roster.Count));

            // The administrator who loads the data is in many channels of the companies they belong to (General workspaces).
            if (!team && company.Members.Any(m => m.Key == Me) && random.Next(10) < 5)
            {
                members.Add(Me);
            }

            var key = $"channel-{n:D3}";
            channels.Add(new DemoChannel(key, DemoId("conversation:" + key), name, company.Key, team, random.Next(5) == 0, manager, members));
        }

        return channels;
    }

    private List<DemoMessage> BuildChannelMessages(Random random)
    {
        var language = People.ToDictionary(p => p.Key, p => p.Language);
        var period = PeriodDays * 24 * 60;
        var messages = new List<DemoMessage>();
        for (var i = 0; messages.Count < ChannelMessageCount; i++)
        {
            var channel = Channels[random.Next(Channels.Count)];
            var authors = new[] { channel.Manager }.Concat(channel.Members).ToList();
            var author = authors[random.Next(authors.Count)];
            var spanish = author != Me && language.GetValueOrDefault(author) == "es";
            var body = spanish
                ? $"{Pick(random, SpanishOpeners)} {string.Format(CultureInfo.InvariantCulture, Pick(random, SpanishBodies), Pick(random, SpanishThings))}"
                : $"{Pick(random, EnglishOpeners)} {string.Format(CultureInfo.InvariantCulture, Pick(random, EnglishBodies), Pick(random, EnglishThings))}";
            messages.Add(new DemoMessage(channel.Key, author, $"demo-{channel.Key}-{i}", body, random.Next(60, period - 1)));
        }

        return messages;
    }

    // ---- People ------------------------------------------------------------------------------

    private static readonly (string Key, string Name)[] LegacyPeople =
    [
        ("alice", "Alice Johnson"), ("bruno", "Bruno Silva"), ("chen", "Chen Wei"), ("dana", "Dana Kowalski"),
        ("elif", "Elif Yılmaz"), ("farah", "Farah Haddad"), ("gabriel", "Gabriel Moreau"), ("hana", "Hana Sato"),
        ("ivan", "Ivan Petrov"), ("julia", "Julia Martínez"),
    ];

    private static readonly string[] FirstNames =
    [
        "Aaron", "Abril", "Adriana", "Ahmed", "Aiko", "Alejandro", "Amara", "Ana", "Andrés", "Anja", "Arjun", "Astrid", "Beatriz", "Benjamin",
        "Camila", "Carlos", "Carmen", "Chloé", "Daniel", "David", "Diego", "Elena", "Emma", "Ethan", "Fatima", "Felipe", "Fernanda", "Finn",
        "Gabriela", "Grace", "Hugo", "Ines", "Isabel", "Jakub", "Javier", "Jin", "Jonas", "Jorge", "Kai", "Karin", "Laura", "Leo", "Lucía",
        "Luis", "Maja", "Marco", "María", "Mateo", "Mia", "Miguel", "Nadia", "Noah", "Olga", "Omar", "Pablo", "Paula", "Priya", "Rafael",
        "Rosa", "Samir", "Sara", "Sofía", "Tomás", "Valentina", "Víctor", "Yuki", "Zoe",
    ];

    private static readonly string[] LastNames =
    [
        "Abbott", "Alonso", "Andersen", "Becker", "Bianchi", "Brown", "Castillo", "Costa", "Díaz", "Dubois", "Fernández", "Fischer", "García",
        "Gómez", "González", "Hansen", "Herrera", "Ito", "Jensen", "Kim", "Kowalczyk", "López", "Martin", "Medina", "Molina", "Morales",
        "Müller", "Navarro", "Nguyen", "Novak", "Ortega", "Patel", "Pereira", "Ramírez", "Rossi", "Ruiz", "Santos", "Schmidt", "Silva",
        "Suárez", "Tanaka", "Torres", "Vargas", "Weber", "Wilson", "Yamamoto", "Zhang",
    ];

    private static List<DemoPerson> BuildPeople(Random random)
    {
        var people = LegacyPeople.Select(p => Person(p.Key, p.Name, "en")).ToList();
        var used = people.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        while (people.Count < PeopleCount)
        {
            var name = $"{Pick(random, FirstNames)} {Pick(random, LastNames)}";
            var key = Slug(name);
            if (!used.Add(key))
            {
                continue;
            }

            people.Add(Person(key, name, random.Next(5) == 0 ? "es" : "en"));
        }

        return people;
    }

    private static DemoPerson Person(string key, string name, string language) => new(key, DemoId("user:" + key), name, Email(key), language);

    // ---- Companies ---------------------------------------------------------------------------

    private static readonly string[] CompanyFirst =
    [
        "Blue", "Bright", "Cedar", "Copper", "Crimson", "Delta", "Evergreen", "Falcon", "Granite", "Harbor", "Horizon", "Iron", "Juniper",
        "Lumen", "Maple", "Meridian", "Nova", "Oak", "Orbit", "Pioneer", "Quantum", "Redwood", "Silver", "Solar", "Summit", "Terra", "Vertex",
        "Willow",
    ];

    private static readonly string[] CompanySecond =
    [
        "Analytics", "Bakery", "Builders", "Consulting", "Designs", "Dynamics", "Energy", "Foods", "Health", "Labs", "Logistics", "Media",
        "Motors", "Networks", "Outfitters", "Partners", "Robotics", "Software", "Studios", "Systems", "Textiles", "Travel", "Ventures",
    ];

    private static readonly string[] TeamWorkspaces = ["Sales", "Engineering", "Support", "Marketing", "Operations", "Finance", "Design", "Research"];

    private static List<DemoCompany> BuildCompanies(Random random, List<string> people)
    {
        var companies = new List<DemoCompany>
        {
            new("northwind", DemoId("org:northwind"), "Northwind Traders", "alice",
                [("alice", DemoRole.Owner), ("bruno", DemoRole.Administrator), ("chen", DemoRole.Member), ("dana", DemoRole.Member), ("elif", DemoRole.Member), (Me, DemoRole.Member)],
                "Sales", ["alice", "dana", "elif"]),
            new("contoso", DemoId("org:contoso"), "Contoso Labs", "gabriel",
                [("gabriel", DemoRole.Owner), ("hana", DemoRole.Administrator), ("ivan", DemoRole.Member), ("julia", DemoRole.Member), ("farah", DemoRole.Member),
                 ("chen", DemoRole.Member), (Me, DemoRole.Member)],
                "Engineering", ["gabriel", "ivan", "julia", "chen"]),
        };
        var names = companies.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        while (companies.Count < CompanyCount)
        {
            var name = $"{Pick(random, CompanyFirst)} {Pick(random, CompanySecond)}";
            if (!names.Add(name))
            {
                continue;
            }

            var roster = Sample(random, people, random.Next(8, 21));
            var members = new List<(string, DemoRole)> { (roster[0], DemoRole.Owner) };
            members.AddRange(roster.Skip(1).Take(random.Next(1, 3)).Select(k => (k, DemoRole.Administrator)));
            members.AddRange(roster.Skip(1 + members.Count - 1).Where(k => members.All(m => m.Item1 != k)).Select(k => (k, DemoRole.Member)));

            // The administrator who loads the data belongs to ten companies in all.
            if (companies.Count < 10)
            {
                members.Add((Me, DemoRole.Member));
            }

            var key = Slug(name);
            var team = Pick(random, TeamWorkspaces);
            var teamMembers = new[] { roster[0] }.Concat(roster.Skip(1).Where(_ => random.Next(2) == 0)).ToList();
            companies.Add(new DemoCompany(key, DemoId("org:" + key), name, roster[0], members, team, teamMembers));
        }

        return companies;
    }

    // ---- Contacts ----------------------------------------------------------------------------

    private static readonly (string A, string B)[] LegacyContacts =
    [
        ("alice", "bruno"), ("alice", "chen"), ("bruno", "dana"), ("elif", "farah"), ("gabriel", "hana"), ("ivan", "julia"), ("chen", "gabriel"),
        (Me, "alice"), (Me, "bruno"), (Me, "chen"), (Me, "gabriel"),
    ];

    private static List<(string A, string B)> BuildContacts(Random random, List<string> people)
    {
        var pairs = new List<(string, string)>(LegacyContacts);
        var seen = pairs.Select(p => Pair(p.Item1, p.Item2)).ToHashSet(StringComparer.Ordinal);
        foreach (var person in people)
        {
            for (var i = 0; i < 3; i++)
            {
                var other = people[random.Next(people.Count)];
                if (other != person && seen.Add(Pair(person, other)))
                {
                    pairs.Add((person, other));
                }
            }
        }

        foreach (var other in Sample(random, people, 60))
        {
            if (seen.Add(Pair(Me, other)))
            {
                pairs.Add((Me, other));
            }
        }

        return pairs;
    }

    private static string Pair(string a, string b) => string.CompareOrdinal(a, b) < 0 ? $"{a}|{b}" : $"{b}|{a}";

    // ---- Groups ------------------------------------------------------------------------------

    private static readonly string[] GroupTopics =
    [
        "Weekly sync", "Product launch", "Customer feedback", "Budget review", "Hiring", "Office move", "Quarterly goals", "Bug bash",
        "Design review", "Onboarding", "Security", "Partnerships", "Events", "Book club", "Running club", "Lunch plans", "Travel desk",
        "Data platform", "Mobile app", "Pricing", "Roadmap", "Support escalations", "Release train", "Coffee chat", "Board games",
    ];

    private List<DemoGroup> BuildGroups(Random random, List<string> people, Dictionary<string, List<string>> contactsOf)
    {
        var groups = new List<DemoGroup>
        {
            new("northwind-team", DemoId("conversation:northwind-team"), "Northwind team chat", "Everyday coordination for the whole company.", "northwind",
                "alice", ["bruno", "chen", "dana", "elif", Me]),
            new("contoso-release", DemoId("conversation:contoso-release"), "Release planning", "Scope, dates and risks for the next release.", "contoso",
                "gabriel", ["hana", "ivan", "julia", Me]),
            new("weekend-hikers", DemoId("conversation:weekend-hikers"), "Weekend hikers", null, null, "alice", ["bruno", "chen", Me]),
        };

        for (var n = groups.Count; n < GroupCount; n++)
        {
            var key = $"group-{n:D3}";
            var topic = Pick(random, GroupTopics);
            if (n % 5 < 3)
            {
                // An organization group among the members of one company.
                var company = Companies[random.Next(Companies.Count)];
                var roster = company.Members.Select(m => m.Key).Where(k => k != Me).ToList();
                var owner = roster[random.Next(roster.Count)];
                var members = Sample(random, roster.Where(k => k != owner).ToList(), Math.Min(roster.Count - 1, random.Next(3, 15))).ToList();
                if (company.Members.Any(m => m.Key == Me) && random.Next(10) < 4)
                {
                    members.Add(Me);
                }

                groups.Add(new DemoGroup(key, DemoId("conversation:" + key), $"{topic} · {company.Name}", null, company.Key, owner, members));
            }
            else
            {
                // A personal group among the owner's contacts.
                var owner = people[random.Next(people.Count)];
                var friends = contactsOf.GetValueOrDefault(owner) ?? [];
                if (friends.Count(f => f != Me) < 2)
                {
                    n--;
                    continue;
                }

                var members = Sample(random, friends.Where(f => f != Me).ToList(), Math.Min(friends.Count(f => f != Me), random.Next(2, 10))).ToList();
                if (friends.Contains(Me) && random.Next(10) < 6)
                {
                    members.Add(Me);
                }

                groups.Add(new DemoGroup(key, DemoId("conversation:" + key), topic, null, null, owner, members));
            }
        }

        return groups;
    }

    // ---- Direct conversations ----------------------------------------------------------------

    private List<DemoDirect> BuildDirects(Random random)
    {
        var directs = new List<DemoDirect> { Direct(Me, "alice"), Direct("alice", "bruno") };
        var seen = directs.Select(d => d.Key).ToHashSet(StringComparer.Ordinal);
        var candidates = Contacts.OrderBy(_ => random.Next()).ToList();
        foreach (var (a, b) in candidates.Where(c => c.A == Me || c.B == Me).Take(25).Concat(candidates.Where(c => c.A != Me && c.B != Me).Take(300)))
        {
            var direct = Direct(a, b);
            if (seen.Add(direct.Key))
            {
                directs.Add(direct);
            }
        }

        return directs;
    }

    private static DemoDirect Direct(string a, string b) => new("direct:" + Pair(a, b), a, b);

    // ---- Messages ----------------------------------------------------------------------------

    private static readonly (string Group, string Author, string ClientId, string Body)[] LegacyMessages =
    [
        ("northwind-team", "alice", "demo-nw-1", "Good morning everyone! Quarterly numbers are in the shared folder."),
        ("northwind-team", "bruno", "demo-nw-2", "Thanks Alice. I'll go through the sales section today."),
        ("northwind-team", "chen", "demo-nw-3", "The new supplier contract is ready for review as well."),
        ("contoso-release", "gabriel", "demo-cl-1", "Release candidate builds are green. Let's lock the scope by Thursday."),
        ("contoso-release", "hana", "demo-cl-2", "Two bugs left in triage; both look small."),
        ("weekend-hikers", "alice", "demo-wh-1", "Saturday: lake trail, 9:00 at the north parking lot?"),
        ("weekend-hikers", "chen", "demo-wh-2", "Count me in. I'll bring snacks."),
    ];

    private static readonly string[] EnglishOpeners =
    [
        "Quick update:", "Heads up:", "FYI,", "Good news:", "Question:", "Reminder:", "Just checked:", "Small thing:", "Morning!", "Thanks all,",
    ];

    private static readonly string[] EnglishBodies =
    [
        "the {0} draft is in the shared folder.", "can we move the {0} meeting to Thursday?", "I pushed the changes for the {0}.",
        "the customer liked the {0} demo.", "who owns the {0} follow-up?", "the {0} numbers look better than last week.",
        "I'll take the {0} action items.", "let's review the {0} after lunch.", "the {0} deadline is Friday.", "the {0} invoice was paid.",
        "I added notes about the {0} to the doc.", "is the {0} still blocked?", "the {0} budget needs one more approval.",
        "sharing the {0} slides in a minute.", "the {0} tests pass now.",
    ];

    private static readonly string[] EnglishThings =
    [
        "roadmap", "proposal", "release", "budget", "contract", "onboarding plan", "pricing page", "survey", "report", "migration", "workshop",
        "newsletter", "dashboard", "supplier deal", "travel plan", "hiring plan", "launch", "invoice", "sprint review", "offsite",
    ];

    private static readonly string[] SpanishOpeners = ["Actualización:", "Ojo:", "Para que lo sepáis,", "Buenas noticias:", "Pregunta:", "Recordatorio:", "¡Buenos días!", "Gracias a todos,"];

    private static readonly string[] SpanishBodies =
    [
        "el borrador del {0} está en la carpeta compartida.", "¿podemos mover la reunión del {0} al jueves?", "subí los cambios del {0}.",
        "al cliente le gustó la demo del {0}.", "¿quién se encarga del seguimiento del {0}?", "las cifras del {0} mejoran esta semana.",
        "el plazo del {0} es el viernes.", "añadí notas sobre el {0} al documento.", "¿sigue bloqueado el {0}?", "el presupuesto del {0} necesita una aprobación más.",
    ];

    private static readonly string[] SpanishThings = ["plan", "presupuesto", "contrato", "informe", "lanzamiento", "taller", "viaje", "proyecto", "café de equipo", "calendario"];

    private List<DemoMessage> BuildMessages(Random random)
    {
        var language = People.ToDictionary(p => p.Key, p => p.Language);
        var messages = LegacyMessages.Select(m => new DemoMessage(m.Group, m.Author, m.ClientId, m.Body, PeriodDays * 24 * 60 - 120)).ToList();

        // Who can write where: group owners and members, both people of a direct conversation.
        var writers = Groups.Select(g => (g.Key, Authors: (IReadOnlyList<string>)new[] { g.Owner }.Concat(g.Members).Distinct().ToList(), Weight: 6))
            .Concat(Directs.Select(d => (d.Key, Authors: (IReadOnlyList<string>)new[] { d.A, d.B }, Weight: 1)))
            .ToList();
        var totalWeight = writers.Sum(w => w.Weight);
        var remaining = MessageCount - messages.Count;
        var period = PeriodDays * 24 * 60;
        foreach (var (key, authors, weight) in writers)
        {
            var count = Math.Max(2, (int)Math.Round((double)remaining * weight / totalWeight * (0.5 + random.NextDouble())));
            var at = random.Next(60, period / 3);
            for (var i = 0; i < count && messages.Count < MessageCount; i++)
            {
                at = Math.Min(period - 1, at + random.Next(5, Math.Max(10, period / count)));
                var author = authors[random.Next(authors.Count)];
                var spanish = author != Me && language.GetValueOrDefault(author) == "es";
                var body = spanish
                    ? $"{Pick(random, SpanishOpeners)} {string.Format(CultureInfo.InvariantCulture, Pick(random, SpanishBodies), Pick(random, SpanishThings))}"
                    : $"{Pick(random, EnglishOpeners)} {string.Format(CultureInfo.InvariantCulture, Pick(random, EnglishBodies), Pick(random, EnglishThings))}";
                messages.Add(new DemoMessage(key, author, $"demo-{key}-{i}", body, at));
            }
        }

        // Fill up to the exact total with more messages in the busiest groups.
        for (var i = 0; messages.Count < MessageCount; i++)
        {
            var group = Groups[i % Groups.Count];
            var authors = new[] { group.Owner }.Concat(group.Members).ToList();
            messages.Add(new DemoMessage(group.Key, authors[i % authors.Count], $"demo-{group.Key}-x{i}",
                $"{Pick(random, EnglishOpeners)} {string.Format(CultureInfo.InvariantCulture, Pick(random, EnglishBodies), Pick(random, EnglishThings))}",
                period - 60 + (i % 59)));
        }

        return messages;
    }

    // ---- Helpers -------------------------------------------------------------------------------

#pragma warning disable CA5394 // Deterministic demo content, not security.
    private static string Pick(Random random, string[] items) => items[random.Next(items.Length)];

    private static List<string> Sample(Random random, List<string> items, int count) =>
        items.OrderBy(_ => random.Next()).Take(Math.Max(0, Math.Min(count, items.Count))).ToList();
#pragma warning restore CA5394

    /// <summary>"María López" → "maria.lopez".</summary>
    private static string Slug(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '.');
        }

        return builder.ToString().Trim('.');
    }
}
