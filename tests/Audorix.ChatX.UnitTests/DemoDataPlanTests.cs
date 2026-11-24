using Audorix.ChatX.Application.Demo;

namespace Audorix.ChatX.UnitTests;

/// <summary>
/// The demo data plan (owner requests of 2026-11-23 and 2026-11-24): 800 channels, everyone in at
/// least ten, and activity (mentions, saved messages, reminders, scheduled messages) of 500 items for
/// the administrator who loads it and 50–100 for everyone else. Checked on the plan, without a database.
/// </summary>
public sealed class DemoDataPlanTests
{
    private static readonly DemoDataPlan Plan = DemoDataPlan.Create();

    [Fact]
    public void The_plan_is_the_same_every_time()
    {
        var again = DemoDataPlan.Create();
        Assert.Equal(Plan.Channels.Select(c => (c.Key, c.Name, c.Members.Count)), again.Channels.Select(c => (c.Key, c.Name, c.Members.Count)));
        Assert.Equal(Plan.Messages.Count, again.Messages.Count);
        Assert.Equal(Plan.Messages.Select(m => m.ClientId).Take(100), again.Messages.Select(m => m.ClientId).Take(100));
    }

    [Fact]
    public void There_are_800_channels_and_everyone_is_in_at_least_ten()
    {
        Assert.Equal(DemoDataPlan.ChannelCount, Plan.Channels.Count);
        Assert.Equal(Plan.Channels.Count, Plan.Channels.Select(c => c.Key).Distinct().Count());
        Assert.Equal(Plan.Channels.Count, Plan.Channels.Select(c => $"{c.CompanyKey}|{c.TeamWorkspace}|{c.Name}").Distinct().Count());

        var channelsOf = Plan.Channels.SelectMany(c => c.Members.Append(c.Manager).Distinct()).GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());
        foreach (var person in Plan.People.Select(p => p.Key).Append(DemoDataPlan.Me))
        {
            Assert.True(channelsOf.GetValueOrDefault(person) >= DemoDataPlan.MinimumChannelsPerPerson, $"{person}: {channelsOf.GetValueOrDefault(person)} channels");
        }

        // Channel members belong to the channel's company (General workspace) or its team.
        var companies = Plan.Companies.ToDictionary(c => c.Key);
        Assert.All(Plan.Channels, c => Assert.All(c.Members.Append(c.Manager), k =>
            Assert.True(c.TeamWorkspace ? companies[c.CompanyKey].TeamMembers.Contains(k) || k == DemoDataPlan.Me : companies[c.CompanyKey].Members.Any(m => m.Key == k))));
    }

    [Fact]
    public void The_administrator_has_500_activity_items_and_everyone_else_50_to_100()
    {
        var activity = Plan.Messages.Where(m => m.Mentions is not null).SelectMany(m => m.Mentions!)
            .Concat(Plan.Saved.Select(s => s.Person))
            .Concat(Plan.Reminders.Select(r => r.Person))
            .Concat(Plan.Scheduled.Select(s => s.Person))
            .GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(500, activity[DemoDataPlan.Me]);
        Assert.Equal(DemoDataPlan.CallerSaved, Plan.Saved.Count(s => s.Person == DemoDataPlan.Me));
        Assert.Equal(DemoDataPlan.CallerReminders, Plan.Reminders.Count(r => r.Person == DemoDataPlan.Me));
        Assert.Equal(DemoDataPlan.CallerScheduled, Plan.Scheduled.Count(s => s.Person == DemoDataPlan.Me));
        foreach (var person in Plan.People)
        {
            Assert.InRange(activity.GetValueOrDefault(person.Key), DemoDataPlan.MinimumActivity, DemoDataPlan.MaximumActivity);
        }

        // Within the app's limits per person.
        Assert.All(Plan.Scheduled.GroupBy(s => s.Person), g => Assert.True(g.Count() <= Domain.Conversations.ScheduledMessage.MaxPendingPerUser));
        Assert.All(Plan.Reminders.GroupBy(r => r.Person), g => Assert.True(g.Count() <= Domain.Conversations.Reminder.MaxPendingPerUser));
        Assert.All(Plan.Scheduled, s => Assert.InRange(s.HoursAhead, 1, Domain.Conversations.ScheduledMessage.MaxDaysAhead * 24 - 1));
    }

    [Fact]
    public void Mentions_name_the_people_they_mention_and_only_people_of_that_conversation()
    {
        var rosters = Plan.Groups.Select(g => (g.Key, Roster: g.Members.Append(g.Owner).ToHashSet()))
            .Concat(Plan.Directs.Select(d => (d.Key, Roster: new HashSet<string> { d.A, d.B })))
            .Concat(Plan.Channels.Select(c => (c.Key, Roster: c.Members.Append(c.Manager).ToHashSet())))
            .ToDictionary(r => r.Key, r => r.Roster);
        var names = Plan.People.ToDictionary(p => p.Key, p => "@" + p.Name);
        names[DemoDataPlan.Me] = DemoDataPlan.MeToken;

        var mentions = Plan.Messages.Where(m => m.Mentions is not null).ToList();
        Assert.NotEmpty(mentions);
        Assert.Equal(mentions.Count, mentions.Select(m => m.ClientId).Distinct().Count());
        Assert.All(mentions, m =>
        {
            Assert.InRange(m.Mentions!.Count, 1, 4);
            Assert.DoesNotContain(m.Author, m.Mentions);
            Assert.Contains(m.Author, rosters[m.ConversationKey]);
            Assert.All(m.Mentions, k =>
            {
                Assert.Contains(k, rosters[m.ConversationKey]);
                Assert.Contains(names[k], m.Body, StringComparison.Ordinal);
            });
        });

        // Some recent mentions (left unread when loading).
        var recent = DemoDataPlan.PeriodDays * 24 * 60 - DemoDataPlan.UnreadMinutes;
        Assert.Contains(mentions, m => m.At >= recent && m.Mentions!.Contains(DemoDataPlan.Me));
    }
}
