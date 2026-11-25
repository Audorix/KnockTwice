using System.Net;
using Audorix.KnockTwice.Infrastructure.Persistence;
using Audorix.KnockTwice.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Audorix.KnockTwice.IntegrationTests.Support.ApiSession;
using static Audorix.KnockTwice.IntegrationTests.Support.TestUsers;

namespace Audorix.KnockTwice.IntegrationTests;

/// <summary>Profile photos and links (redesign 2026-11, owner mockups "My profile").</summary>
public sealed class ProfileMediaTests
{
    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Photos_show_wherever_the_person_does_and_go_with_the_account(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;
        await BecomeContactsAsync(ana, anaId, ben, benId);

        // Only PNG, JPEG or WebP by signature, at most 1 MB.
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.UploadAsync("/api/v1/users/me/photo", "photo.png", TestFiles.Pdf(), "image/png")).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await ana.UploadAsync("/api/v1/users/me/photo", "big.png", TestFiles.Png(1024 * 1024 + 1), "image/png")).StatusCode);

        var png = TestFiles.Png(4096);
        var set = await ana.UploadAsync("/api/v1/users/me/photo", "me.png", png, "image/png");
        AssertStatus(HttpStatusCode.OK, set, await set.Content.ReadAsStringAsync(Ct));
        var url = (await JsonAsync(set)).GetProperty("avatar").GetProperty("photoUrl").GetString();
        Assert.Equal($"/api/v1/users/{anaId}/photo?v=1", url);

        // Any signed-in person sees it; the versioned address is cached for good.
        var photo = await ben.GetAsync(url!);
        AssertStatus(HttpStatusCode.OK, photo);
        Assert.Equal("image/png", photo.Content.Headers.ContentType?.MediaType);
        Assert.Equal(png, await photo.Content.ReadAsByteArrayAsync(Ct));
        Assert.Contains("immutable", photo.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains(url!, await (await ben.GetAsync("/api/v1/contacts")).Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        // A new photo gets a new address; removing it brings the initials back.
        var again = await JsonAsync(await ana.UploadAsync("/api/v1/users/me/photo", "me.png", TestFiles.Png(2048), "image/png"));
        Assert.Equal($"/api/v1/users/{anaId}/photo?v=2", again.GetProperty("avatar").GetProperty("photoUrl").GetString());
        var removed = await JsonAsync(await ana.SendAsync(HttpMethod.Delete, "/api/v1/users/me/photo"));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, removed.GetProperty("avatar").GetProperty("photoUrl").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await ben.GetAsync($"/api/v1/users/{anaId}/photo")).StatusCode);

        // Deleting the account removes the photo and the links.
        AssertStatus(HttpStatusCode.OK, await ana.UploadAsync("/api/v1/users/me/photo", "me.png", png, "image/png"));
        AssertStatus(HttpStatusCode.OK, await ana.SendAsync(HttpMethod.Put, "/api/v1/users/me/links",
            new { links = new[] { new { platform = "github", url = "https://github.com/ana", visibility = "contacts" } } }));
        AssertStatus(HttpStatusCode.NoContent, await ana.PostAsync("/api/v1/users/me/deletion", new { password = Password }));
        Assert.Equal(HttpStatusCode.NotFound, (await ben.GetAsync($"/api/v1/users/{anaId}/photo")).StatusCode);
        await using var scope = api.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ChatXDbContext>();
        Assert.Equal(0, await context.ProfileLinks.CountAsync(l => l.UserId == anaId, Ct));
        Assert.Null((await context.UserProfiles.SingleAsync(p => p.UserId == anaId, Ct)).PhotoKey);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Links_are_checked_ordered_and_shown_to_contacts_only(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (carl, _) = await SignedInAsync(api, "carl@example.org", "Carl");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = carl;

        // Every row is checked; problems come back per row.
        var refused = await ana.SendAsync(HttpMethod.Put, "/api/v1/users/me/links", new
        {
            links = new object[]
            {
                new { platform = "github", url = "https://evil.example/ana", visibility = "contacts" },
                new { platform = "website", url = "javascript:alert(1)", visibility = "contacts" },
                new { platform = "myspace", url = "https://myspace.com/ana", visibility = "contacts" },
                new { platform = "website", url = "https://ana.example", visibility = "everyone" },
            },
        });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var errors = (await JsonAsync(refused)).GetProperty("errors");
        Assert.Equal("link_url_platform", errors.GetProperty("links[0].url")[0].GetString());
        Assert.Equal("link_url_invalid", errors.GetProperty("links[1].url")[0].GetString());
        Assert.Equal("link_platform_invalid", errors.GetProperty("links[2].platform")[0].GetString());
        Assert.Equal("link_visibility_invalid", errors.GetProperty("links[3].visibility")[0].GetString());
        var tooMany = Enumerable.Range(0, 11).Select(i => new { platform = "website", url = $"https://ana{i}.example", visibility = "contacts" }).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.SendAsync(HttpMethod.Put, "/api/v1/users/me/links", new { links = tooMany })).StatusCode);

        var saved = await ana.SendAsync(HttpMethod.Put, "/api/v1/users/me/links", new
        {
            links = new[]
            {
                new { platform = "website", url = "https://ana.example", visibility = "only_me" },
                new { platform = "github", url = "https://www.github.com/ana", visibility = "contacts" },
                new { platform = "youtube", url = "https://youtu.be/xyz", visibility = "contacts" },
            },
        });
        AssertStatus(HttpStatusCode.OK, saved, await saved.Content.ReadAsStringAsync(Ct));
        var own = (await JsonAsync(await ana.GetAsync("/api/v1/users/me/links"))).EnumerateArray().Select(l => l.GetProperty("platform").GetString()).ToList();
        Assert.Equal(["website", "github", "youtube"], own);

        // Others see nothing until they are contacts; contacts see only the links meant for them.
        Assert.Empty((await JsonAsync(await ben.GetAsync($"/api/v1/users/{anaId}/links"))).EnumerateArray());
        await BecomeContactsAsync(ana, anaId, ben, benId);
        var seen = (await JsonAsync(await ben.GetAsync($"/api/v1/users/{anaId}/links"))).EnumerateArray().Select(l => l.GetProperty("url").GetString()).ToList();
        Assert.Equal(["https://www.github.com/ana", "https://youtu.be/xyz"], seen);
        Assert.Empty((await JsonAsync(await carl.GetAsync($"/api/v1/users/{anaId}/links"))).EnumerateArray());

        // An empty list removes them all.
        AssertStatus(HttpStatusCode.OK, await ana.SendAsync(HttpMethod.Put, "/api/v1/users/me/links", new { links = Array.Empty<object>() }));
        Assert.Empty((await JsonAsync(await ana.GetAsync("/api/v1/users/me/links"))).EnumerateArray());
    }
}
