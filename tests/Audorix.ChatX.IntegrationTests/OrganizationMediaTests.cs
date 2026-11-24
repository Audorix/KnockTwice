using System.Net;
using System.Text.Json;
using Audorix.ChatX.Infrastructure.Persistence;
using Audorix.ChatX.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Audorix.ChatX.IntegrationTests.Support.ApiSession;
using static Audorix.ChatX.IntegrationTests.Support.TestUsers;

namespace Audorix.ChatX.IntegrationTests;

/// <summary>Organization picture and background image (redesign 2026-11-24, owner mockup "Organization profile").</summary>
public sealed class OrganizationMediaTests
{
    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Managers_set_the_picture_and_background_members_see_them_and_they_go_with_the_organization(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, _) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, _) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (carl, carlId) = await SignedInAsync(api, "carl@example.org", "Carl");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = carl;
        var org = await CreateOrganizationAsync(ana, (ben, "ben@example.org"));
        var basePath = $"/api/v1/organizations/{org}";

        // Members may not change them; checked by signature and size (picture 1 MB, background 2 MB).
        Assert.Equal(HttpStatusCode.Forbidden, (await ben.UploadAsync($"{basePath}/photo", "p.png", TestFiles.Png(1024), "image/png")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.UploadAsync($"{basePath}/photo", "p.png", TestFiles.Pdf(), "image/png")).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await ana.UploadAsync($"{basePath}/photo", "p.png", TestFiles.Png(1024 * 1024 + 1), "image/png")).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await ana.UploadAsync($"{basePath}/banner", "b.png", TestFiles.Png(2 * 1024 * 1024 + 1), "image/png")).StatusCode);

        var picture = TestFiles.Png(4096);
        var banner = TestFiles.Png((1024 * 1024) + 512);
        var withPicture = await ana.UploadAsync($"{basePath}/photo", "p.png", picture, "image/png");
        AssertStatus(HttpStatusCode.OK, withPicture, await withPicture.Content.ReadAsStringAsync(Ct));
        Assert.Equal($"{basePath}/photo?v=1", (await JsonAsync(withPicture)).GetProperty("photoUrl").GetString());
        var both = await JsonAsync(await ana.UploadAsync($"{basePath}/banner", "b.png", banner, "image/png"));
        Assert.Equal($"{basePath}/banner?v=1", both.GetProperty("bannerUrl").GetString());

        // Members see both (in the organization and in their list); others get 404.
        var seen = await ben.GetAsync($"{basePath}/photo?v=1");
        AssertStatus(HttpStatusCode.OK, seen);
        Assert.Equal(picture, await seen.Content.ReadAsByteArrayAsync(Ct));
        Assert.Contains("immutable", seen.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(banner, await (await ben.GetAsync($"{basePath}/banner?v=1")).Content.ReadAsByteArrayAsync(Ct));
        var listed = (await JsonAsync(await ben.GetAsync("/api/v1/organizations"))).EnumerateArray().Single();
        Assert.Equal($"{basePath}/photo?v=1", listed.GetProperty("photoUrl").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await carl.GetAsync($"{basePath}/photo")).StatusCode);

        // An installation administrator who is not a member sees it in the administration list.
        await using (var scope = api.Services.CreateAsyncScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<Application.Identity.IUserAccountManager>()
                .AddToRoleAsync(carlId, Domain.Authorization.Roles.InstallationAdministrator, Ct)).Succeeded);
        }

        using var admin = new ApiSession(api.CreateHttpsClient());
        AssertStatus(HttpStatusCode.OK, await admin.LoginAsync("carl@example.org", Password));
        var adminRow = (await JsonAsync(await admin.GetAsync("/api/v1/admin/organizations"))).GetProperty("items").EnumerateArray().Single();
        var adminUrl = adminRow.GetProperty("photoUrl").GetString();
        Assert.Equal($"/api/v1/admin/organizations/{org}/photo?v=1", adminUrl);
        Assert.Equal(picture, await (await admin.GetAsync(adminUrl!)).Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal(HttpStatusCode.Forbidden, (await ben.GetAsync(adminUrl!)).StatusCode);

        // Removing the background leaves the picture.
        var removed = await JsonAsync(await ana.SendAsync(HttpMethod.Delete, $"{basePath}/banner"));
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("bannerUrl").ValueKind);
        Assert.Equal($"{basePath}/photo?v=1", removed.GetProperty("photoUrl").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await ben.GetAsync($"{basePath}/banner")).StatusCode);

        // Deleting the organization deletes the picture's file too.
        string key;
        await using (var scope = api.Services.CreateAsyncScope())
        {
            key = (await scope.ServiceProvider.GetRequiredService<ChatXDbContext>().Organizations.SingleAsync(o => o.Id == org, Ct)).PhotoKey!;
        }

        AssertStatus(HttpStatusCode.NoContent, await ana.PostAsync($"{basePath}/delete", new { confirmName = "Acme" }));
        await using (var scope = api.Services.CreateAsyncScope())
        {
            Assert.Null(await scope.ServiceProvider.GetRequiredService<Application.Attachments.IAttachmentStorage>().OpenReadAsync(key, Ct));
        }
    }
}
