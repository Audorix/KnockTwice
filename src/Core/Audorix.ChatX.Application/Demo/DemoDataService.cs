using Audorix.ChatX.Application.Attachments;
using Audorix.ChatX.Application.Common;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Application.Demo;

/// <summary>
/// Demonstration data switch. Enabled in Development and on installations set up in development
/// mode; demo accounts share a published password, so never enable it where others can sign in.
/// </summary>
public sealed class DemoDataSettings
{
    public const string SectionName = "DemoData";

    public bool Enabled { get; set; }
}

/// <param name="Created">What this run added; all zeros when everything already existed.</param>
/// <param name="UserEmails">A few demo accounts to sign in with (all share <paramref name="Password"/>).</param>
public sealed record DemoDataResult(DemoDataCounts Created, DemoDataCounts Total, IReadOnlyList<string> UserEmails, string Password);

/// <param name="Activity">Mentions, saved messages, reminders and scheduled messages in demo conversations (2026-11-24).</param>
/// <param name="Support">Support conversations from demo customers (2026-11-24); also counted in <paramref name="Conversations"/>.</param>
public sealed record DemoDataCounts(int Users, int Organizations, int Contacts, int Conversations, int Messages, int Activity = 0, int Support = 0);

/// <summary>Writes and removes demo records in bulk (implemented by Infrastructure).</summary>
public interface IDemoDataStore
{
    /// <summary>
    /// Adds whatever of <paramref name="plan"/> is missing, flags it as demo data and gives every demo
    /// account <paramref name="password"/>. Nothing a person later left, removed or deleted is put back.
    /// </summary>
    Task<DemoDataCounts> PopulateAsync(DemoDataPlan plan, Guid callerId, string password, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>What demo data exists now.</summary>
    Task<DemoDataCounts> CountAsync(CancellationToken cancellationToken);

    /// <summary>Storage keys of files that clearing demo data removes.</summary>
    Task<IReadOnlyList<string>> ListAttachmentKeysAsync(CancellationToken cancellationToken);

    /// <summary>Removes the demo data; returns what was removed.</summary>
    Task<DemoDataCounts> ClearAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Demo data (owner request of 2026-11-23): 300 people who can sign in with <see cref="Password"/>,
/// 50 companies, contacts, 150 groups, direct conversations and about 10,000 messages, with the
/// administrator who loads it included so the data is visible to them. Loading is idempotent
/// (fixed identifiers and client message ids; only what is missing is added). Everything it creates
/// is flagged as demo data, and clearing removes exactly that: demo people (with what they wrote
/// anywhere), demo organizations and demo conversations with their contents — never conversations
/// or data that real people created, except their membership in demo organizations and conversations.
/// </summary>
public sealed class DemoDataService(
    IDemoDataStore store,
    IAttachmentStorage storage,
    TimeProvider time,
    IOptions<DemoDataSettings> settings)
{
    /// <summary>The shared password of every demo account (owner request: easy to test with).</summary>
    public const string Password = "Demo123!";

    public const string EmailDomain = DemoDataPlan.EmailDomain;

    private static readonly Lazy<DemoDataPlan> Plan = new(DemoDataPlan.Create);

    public async Task<DemoDataResult> PopulateAsync(Guid callerId, CancellationToken cancellationToken)
    {
        if (!settings.Value.Enabled)
        {
            throw new FeatureDisabledException("demo_data_disabled");
        }

        var plan = Plan.Value;
        var created = await store.PopulateAsync(plan, callerId, Password, time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var total = await store.CountAsync(cancellationToken).ConfigureAwait(false);
        return new DemoDataResult(created, total, plan.People.Take(10).Select(p => p.Email).ToList(), Password);
    }

    /// <summary>Removes all demo data (allowed even after the demo switch was turned off). File content first.</summary>
    public async Task<DemoDataCounts> ClearAsync(CancellationToken cancellationToken)
    {
        foreach (var key in await store.ListAttachmentKeysAsync(cancellationToken).ConfigureAwait(false))
        {
            await storage.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
        }

        return await store.ClearAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<DemoDataCounts> CountAsync(CancellationToken cancellationToken) => store.CountAsync(cancellationToken);

    public static string Email(string key) => DemoDataPlan.Email(key);

    public static Guid DemoId(string key) => DemoDataPlan.DemoId(key);
}
