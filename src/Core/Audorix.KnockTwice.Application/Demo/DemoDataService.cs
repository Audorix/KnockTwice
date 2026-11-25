using Audorix.KnockTwice.Application.Attachments;
using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.Users;
using Microsoft.Extensions.Options;

namespace Audorix.KnockTwice.Application.Demo;

/// <summary>
/// Demonstration data switch. Enabled in Development and on installations set up in development
/// mode; demo accounts share a published password, so never enable it where others can sign in.
/// </summary>
public sealed class DemoDataSettings
{
    public const string SectionName = "DemoData";

    public bool Enabled { get; set; }

    /// <summary>
    /// Folder of profile pictures for demo people (owner request 2026-11-24): <c>avatar-001.jpg</c>
    /// goes to the first demo person, up to <c>avatar-300.jpg</c>. Relative paths are resolved from the
    /// API's content root. Empty or missing: demo people keep their initials.
    /// </summary>
    public string? AvatarsPath { get; set; }
}

/// <param name="Created">What this run added; all zeros when everything already existed.</param>
/// <param name="UserEmails">A few demo accounts to sign in with (all share <paramref name="Password"/>).</param>
public sealed record DemoDataResult(DemoDataCounts Created, DemoDataCounts Total, IReadOnlyList<string> UserEmails, string Password);

/// <param name="Activity">Mentions, saved messages, reminders and scheduled messages in demo conversations (2026-11-24).</param>
/// <param name="Support">Support conversations from demo customers (2026-11-24); also counted in <paramref name="Conversations"/>.</param>
/// <param name="Photos">Demo people with a profile photo (2026-11-24).</param>
public sealed record DemoDataCounts(int Users, int Organizations, int Contacts, int Conversations, int Messages, int Activity = 0, int Support = 0, int Photos = 0);

/// <summary>Writes and removes demo records in bulk (implemented by Infrastructure).</summary>
public interface IDemoDataStore
{
    /// <summary>
    /// Adds whatever of <paramref name="plan"/> is missing, flags it as demo data and gives every demo
    /// account <paramref name="password"/>. Nothing a person later left, removed or deleted is put back.
    /// </summary>
    Task<DemoDataCounts> PopulateAsync(
        DemoDataPlan plan, Guid callerId, string password, DateTimeOffset nowUtc, IDemoDataProgress? progress, CancellationToken cancellationToken);

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
    IOptions<DemoDataSettings> settings,
    IUserProfileStore profiles,
    ProfileMediaService media)
{
    /// <summary>The shared password of every demo account (owner request: easy to test with).</summary>
    public const string Password = "Demo123!";

    public const string EmailDomain = DemoDataPlan.EmailDomain;

    private static readonly Lazy<DemoDataPlan> Plan = new(DemoDataPlan.Create);

    /// <summary>Refuses with <c>demo_data_disabled</c> unless demo data is switched on.</summary>
    public void EnsureEnabled()
    {
        if (!settings.Value.Enabled)
        {
            throw new FeatureDisabledException("demo_data_disabled");
        }
    }

    /// <summary>The parts a load goes through and their sizes; photos only when the avatars folder exists.</summary>
    public IReadOnlyList<DemoDataStep> Steps()
    {
        var plan = Plan.Value;
        var steps = new List<DemoDataStep>
        {
            new(DemoDataSteps.People, plan.People.Count),
            new(DemoDataSteps.Companies, plan.Companies.Count),
            new(DemoDataSteps.Contacts, plan.Contacts.Count),
            new(DemoDataSteps.Chats, plan.Groups.Count + plan.Directs.Count),
            new(DemoDataSteps.Channels, plan.Channels.Count),
            new(DemoDataSteps.Messages, plan.Messages.Count),
            new(DemoDataSteps.Activity, plan.Saved.Count + plan.Reminders.Count + plan.Scheduled.Count),
            new(DemoDataSteps.Support, plan.Support.Count),
        };
        if (AvatarsFolder() is not null)
        {
            steps.Add(new(DemoDataSteps.Photos, plan.People.Count));
        }

        return steps;
    }

    public async Task<DemoDataResult> PopulateAsync(Guid callerId, CancellationToken cancellationToken) =>
        await PopulateAsync(callerId, null, cancellationToken).ConfigureAwait(false);

    public async Task<DemoDataResult> PopulateAsync(Guid callerId, IDemoDataProgress? progress, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var plan = Plan.Value;
        var created = await store.PopulateAsync(plan, callerId, Password, time.GetUtcNow(), progress, cancellationToken).ConfigureAwait(false);
        created = created with { Photos = await AssignPhotosAsync(plan, progress, cancellationToken).ConfigureAwait(false) };
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

    /// <summary>
    /// Gives demo person N the picture <c>avatar-NNN.jpg</c> (or .png/.webp) from
    /// <see cref="DemoDataSettings.AvatarsPath"/>. Idempotent: people who already have a photo (from
    /// an earlier load, or one they chose) are left alone, and so are deleted accounts. Returns how
    /// many photos were set.
    /// </summary>
    private async Task<int> AssignPhotosAsync(DemoDataPlan plan, IDemoDataProgress? progress, CancellationToken cancellationToken)
    {
        if (AvatarsFolder() is not { } folder)
        {
            return 0;
        }

        var assigned = 0;
        for (var i = 0; i < plan.People.Count; i++)
        {
            progress?.Report(DemoDataSteps.Photos, i + 1);
            var file = AvatarFile(folder, i + 1);
            if (file is null)
            {
                continue;
            }

            // Only people who never had a photo: one that was removed stays removed (PhotoVersion counts every change).
            var profile = await profiles.FindAsync(plan.People[i].Id, cancellationToken).ConfigureAwait(false);
            if (profile is null || profile.IsDeleted || profile.HasPhoto || profile.PhotoVersion > 0)
            {
                continue;
            }

            try
            {
                var stream = File.OpenRead(file);
                await using (stream.ConfigureAwait(false))
                {
                    await media.SetPhotoAsync(profile.UserId, stream, stream.Length, cancellationToken).ConfigureAwait(false);
                }

                assigned++;
            }
            catch (Exception ex) when (ex is PayloadTooLargeException or RequestValidationException or IOException or UnauthorizedAccessException)
            {
                // A picture that is too large, not PNG/JPEG/WebP, unreadable or locked is skipped; the others still load.
            }
        }

        return assigned;
    }

    private string? AvatarsFolder() =>
        settings.Value.AvatarsPath is { Length: > 0 } folder && !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder) ? folder : null;

    private static readonly string[] AvatarExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    private static string? AvatarFile(string folder, int number) =>
        AvatarExtensions
            .Select(extension => Path.Combine(folder, $"avatar-{number:000}{extension}"))
            .FirstOrDefault(File.Exists);

    public static string Email(string key) => DemoDataPlan.Email(key);

    public static Guid DemoId(string key) => DemoDataPlan.DemoId(key);
}
