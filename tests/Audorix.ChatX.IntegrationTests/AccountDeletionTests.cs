using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Audorix.ChatX.Application.Administration;
using Audorix.ChatX.Application.Identity;
using Audorix.ChatX.Domain.Authorization;
using Audorix.ChatX.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using static Audorix.ChatX.IntegrationTests.Support.ApiSession;
using static Audorix.ChatX.IntegrationTests.Support.TestUsers;

namespace Audorix.ChatX.IntegrationTests;

/// <summary>
/// Account deletion, organization deletion and data retention (Phase 13, owner decisions of
/// 2026-11-23): messages stay as "Deleted user"; deletion waits while someone is the only owner of
/// an organization; inactive anonymous visitors, never confirmed accounts and (when switched on)
/// old messages are removed by retention.
/// </summary>
public sealed class AccountDeletionTests
{
    private const string Shop = "https://shop.example";

    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Deleting_an_account_erases_the_person_and_keeps_the_history(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana Lopez");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (carl, carlId) = await SignedInAsync(api, "carl@example.org", "Carl");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = carl;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        await BecomeContactsAsync(ana, anaId, carl, carlId);

        var direct = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/direct", new { userId = benId }))).GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.Created, await ana.PostAsync($"/api/v1/conversations/{direct}/messages", new { clientMessageId = "a1", body = "Hi Ben" }));
        var group = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/groups", new { title = "Trip", userIds = new[] { benId, carlId } }))).GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.Created, await ana.PostAsync($"/api/v1/conversations/{group}/messages", new { clientMessageId = "a2", body = "Who drives?" }));

        // Ana is the only owner of an organization: deletion waits until she hands it over.
        var org = await CreateOrganizationAsync(ana, (ben, "ben@example.org"));
        var check = await JsonAsync(await ana.GetAsync("/api/v1/users/me/deletion"));
        Assert.False(check.GetProperty("canDelete").GetBoolean());
        Assert.True(check.GetProperty("requiresPassword").GetBoolean());
        Assert.Equal(org, check.GetProperty("blockingOrganizations").EnumerateArray().Single().GetProperty("organizationId").GetGuid());
        var blocked = await ana.PostAsync("/api/v1/users/me/deletion", new { password = Password });
        Assert.Equal((HttpStatusCode.Conflict, "sole_owner"), (blocked.StatusCode, await ErrorCodeAsync(blocked)));

        AssertStatus(HttpStatusCode.OK, await ana.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{org}/members/{benId}", new { role = "owner" }));
        Assert.True((await JsonAsync(await ana.GetAsync("/api/v1/users/me/deletion"))).GetProperty("canDelete").GetBoolean());

        // The password is confirmed again.
        var wrong = await ana.PostAsync("/api/v1/users/me/deletion", new { password = "not the password" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("password_incorrect", await wrong.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        AssertStatus(HttpStatusCode.NoContent, await ana.PostAsync("/api/v1/users/me/deletion", new { password = Password }));

        // Signed out, and the credentials no longer work.
        Assert.Equal(HttpStatusCode.Unauthorized, (await ana.GetAsync("/api/v1/users/me")).StatusCode);
        using (var again = new ApiSession(api.CreateHttpsClient()))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await again.LoginAsync("ana@example.org", Password)).StatusCode);
        }

        // Ben still reads the direct conversation, now with "Deleted user", and cannot write into it.
        var summaries = await JsonAsync(await ben.GetAsync("/api/v1/conversations"));
        var directSummary = summaries.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == direct);
        Assert.True(directSummary.GetProperty("counterpart").GetProperty("deleted").GetBoolean());
        Assert.DoesNotContain("Ana", directSummary.GetProperty("title").GetString(), StringComparison.Ordinal);
        var history = await JsonAsync(await ben.GetAsync($"/api/v1/conversations/{direct}/history"));
        var hi = history.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("body").ValueKind == JsonValueKind.String);
        Assert.Equal("Hi Ben", hi.GetProperty("body").GetString());
        Assert.True(hi.GetProperty("actor").GetProperty("deleted").GetBoolean());
        var reply = await ben.PostAsync($"/api/v1/conversations/{direct}/messages", new { clientMessageId = "b1", body = "Hello?" });
        Assert.Equal((HttpStatusCode.Forbidden, "recipient_deleted"), (reply.StatusCode, await ErrorCodeAsync(reply)));

        // She left the group with an ordinary entry; someone else owns it now; her message stays.
        var detail = await JsonAsync(await ben.GetAsync($"/api/v1/conversations/{group}"));
        var participants = detail.GetProperty("participants").EnumerateArray().ToList();
        Assert.DoesNotContain(participants, p => p.GetProperty("user").GetProperty("id").GetGuid() == anaId);
        Assert.Contains(participants, p => p.GetProperty("role").GetString() == "owner");
        var groupHistory = (await JsonAsync(await carl.GetAsync($"/api/v1/conversations/{group}/history"))).GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(groupHistory, i => i.GetProperty("body").ValueKind == JsonValueKind.String && i.GetProperty("body").GetString() == "Who drives?");
        Assert.Contains(groupHistory, i => i.GetProperty("kind").GetString() == "member_left" && i.GetProperty("actor").GetProperty("deleted").GetBoolean());

        // Contacts, organization membership and the address are gone; the address can register again.
        Assert.DoesNotContain((await JsonAsync(await ben.GetAsync("/api/v1/contacts"))).EnumerateArray(), c => c.GetProperty("user").GetProperty("id").GetGuid() == anaId);
        Assert.DoesNotContain((await JsonAsync(await ben.GetAsync($"/api/v1/organizations/{org}/members"))).GetProperty("members").EnumerateArray(),
            m => m.GetProperty("user").GetProperty("id").GetGuid() == anaId);
        Assert.Empty((await JsonAsync(await ben.GetAsync("/api/v1/users/search?q=ana@example.org"))).EnumerateArray());
        using var newcomer = new ApiSession(api.CreateHttpsClient());
        AssertStatus(HttpStatusCode.Accepted, await newcomer.PostAsync("/api/v1/auth/register",
            new { email = "ana@example.org", password = Password, displayName = "Ana again", captchaToken = "t" }));
        Assert.Equal(2, api.Emails.To("ana@example.org", "email-confirmation").Count); // a new account, not the old one

        // Nothing personal is left on the account row.
        await using var scope = api.Services.CreateAsyncScope();
        var account = await scope.ServiceProvider.GetRequiredService<IUserAccountManager>().FindByIdAsync(anaId, Ct);
        Assert.NotNull(account);
        Assert.True(account.IsDeleted);
        Assert.DoesNotContain("ana", account.Email, StringComparison.OrdinalIgnoreCase);
        Assert.False(account.HasPassword);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Administrators_delete_accounts_and_owners_delete_organizations(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (admin, adminId) = await SignedInAsync(api, "admin@example.org", "Admin");
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = admin;
        using var _2 = ana;
        using var _3 = ben;
        await using (var scope = api.Services.CreateAsyncScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<IUserAccountManager>().AddToRoleAsync(adminId, Roles.InstallationAdministrator, Ct)).Succeeded);
        }

        // The last administrator cannot delete themselves; nor can an administrator use the admin route on themselves.
        var lastAdmin = await JsonAsync(await admin.GetAsync("/api/v1/users/me/deletion"));
        Assert.True(lastAdmin.GetProperty("lastAdministrator").GetBoolean());
        Assert.Equal("last_administrator", await ErrorCodeAsync(await admin.PostAsync("/api/v1/users/me/deletion", new { password = Password })));
        Assert.Equal("cannot_delete_self", await ErrorCodeAsync(await admin.SendAsync(HttpMethod.Delete, $"/api/v1/admin/users/{adminId}")));

        // Ana owns an organization alone, with a channel message and a file: she deletes it, then she can go.
        var org = await CreateOrganizationAsync(ana, (ben, "ben@example.org"));
        var direct = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/direct", new { userId = benId, organizationId = org }))).GetProperty("id").GetGuid();
        var upload = await ana.UploadAsync($"/api/v1/conversations/{direct}/attachments", "plan.png", TestFiles.Png(2048), "image/png");
        AssertStatus(HttpStatusCode.Created, upload, await upload.Content.ReadAsStringAsync(Ct));
        var attachmentId = (await JsonAsync(upload)).GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.Created, await ana.PostAsync($"/api/v1/conversations/{direct}/messages",
            new { clientMessageId = "a1", body = "The plan", attachmentIds = new[] { attachmentId } }));
        Assert.NotEmpty(Directory.EnumerateFiles(api.StorageRoot, "*", SearchOption.AllDirectories));

        Assert.Equal(HttpStatusCode.Forbidden, (await ben.PostAsync($"/api/v1/organizations/{org}/delete", new { confirmName = "Acme" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsync($"/api/v1/organizations/{org}/delete", new { confirmName = "Wrong" })).StatusCode);
        AssertStatus(HttpStatusCode.NoContent, await ana.PostAsync($"/api/v1/organizations/{org}/delete", new { confirmName = "acme" }));
        Assert.Equal(HttpStatusCode.NotFound, (await ben.GetAsync($"/api/v1/organizations/{org}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ben.GetAsync($"/api/v1/conversations/{direct}")).StatusCode);
        Assert.Empty(Directory.EnumerateFiles(api.StorageRoot, "*", SearchOption.AllDirectories));
        Assert.True((await JsonAsync(await ana.GetAsync("/api/v1/users/me/deletion"))).GetProperty("canDelete").GetBoolean());

        // An administrator deletes Ben; he is signed out at once and no longer listed.
        AssertStatus(HttpStatusCode.NoContent, await admin.SendAsync(HttpMethod.Delete, $"/api/v1/admin/users/{benId}"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await ben.GetAsync("/api/v1/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/admin/users/{benId}")).StatusCode);
        var listed = await JsonAsync(await admin.GetAsync("/api/v1/admin/users?take=100"));
        Assert.DoesNotContain(listed.GetProperty("items").EnumerateArray(), u => u.GetProperty("id").GetGuid() == benId);
        var audit = await JsonAsync(await admin.GetAsync("/api/v1/admin/audit?action=admin.user.deleted"));
        Assert.Equal(benId.ToString(), audit.GetProperty("items").EnumerateArray().Single().GetProperty("targetId").GetString());
        Assert.Single((await JsonAsync(await admin.GetAsync("/api/v1/admin/audit?action=organization.deleted"))).GetProperty("items").EnumerateArray());
        _ = anaId;
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Retention_removes_inactive_visitors_unconfirmed_accounts_and_old_messages(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        // Closed sessions are covered by their own test; here the signed-in test users must stay signed in.
        await using var api = await StartAsync(db, new Dictionary<string, string?> { ["Retention:MessageDays"] = "365", ["Retention:ClosedSessionDays"] = "0" });
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;
        await BecomeContactsAsync(ana, anaId, ben, benId);

        // Old messages with a file and a reply.
        var direct = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/direct", new { userId = benId }))).GetProperty("id").GetGuid();
        var upload = await ana.UploadAsync($"/api/v1/conversations/{direct}/attachments", "photo.png", TestFiles.Png(2048), "image/png");
        var attachmentId = (await JsonAsync(upload)).GetProperty("id").GetGuid();
        var first = await JsonAsync(await ana.PostAsync($"/api/v1/conversations/{direct}/messages", new { clientMessageId = "a1", body = "Photo", attachmentIds = new[] { attachmentId } }));
        AssertStatus(HttpStatusCode.Created, await ben.PostAsync($"/api/v1/conversations/{direct}/messages",
            new { clientMessageId = "b1", body = "Nice", replyToId = first.GetProperty("id").GetGuid() }));

        // An anonymous website visitor writes once.
        var org = await CreateOrganizationAsync(ana, (ben, "ben@example.org"));
        var widget = await JsonAsync(await ana.PostAsync($"/api/v1/organizations/{org}/widgets", new { name = "Shop" }));
        var widgetId = widget.GetProperty("widget").GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.OK, await ana.SendAsync(HttpMethod.Put, $"/api/v1/organizations/{org}/widgets/{widgetId}",
            new { title = "Chat", allowedOrigins = new[] { Shop }, responderIds = new[] { anaId }, allowAnonymous = true, enabled = true }));
        using var browser = api.CreateHttpsClient();
        var session = await browser.PostAsJsonAsync($"/api/v1/widget/{widgetId}/sessions", new { parentOrigin = Shop, language = "en" }, Ct);
        var token = (await JsonAsync(session)).GetProperty("token").GetString()!;
        using var visitor = api.CreateHttpsClient();
        visitor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        AssertStatus(HttpStatusCode.Created, await visitor.PostAsJsonAsync("/api/v1/widget/visitor/messages", new { clientMessageId = "v1", body = "Hello shop" }, Ct));

        // Someone registers and never confirms.
        using var stranger = new ApiSession(api.CreateHttpsClient());
        AssertStatus(HttpStatusCode.Accepted, await stranger.PostAsync("/api/v1/auth/register",
            new { email = "stranger@example.org", password = Password, displayName = "Stranger", captchaToken = "t" }));

        // Today nothing is due.
        await using (var scope = api.Services.CreateAsyncScope())
        {
            Assert.False((await scope.ServiceProvider.GetRequiredService<DataRetentionService>().ApplyAsync(DateTimeOffset.UtcNow, Ct)).Any);
        }

        // A year and a half later.
        RetentionResult result;
        await using (var scope = api.Services.CreateAsyncScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<DataRetentionService>().ApplyAsync(DateTimeOffset.UtcNow.AddDays(550), Ct);
        }

        Assert.Equal((1, 1), (result.Visitors, result.UnconfirmedAccounts));
        Assert.True(result.Messages >= 3, $"messages: {result.Messages}");

        // The visitor's session ends; the organization keeps the conversation, without the visitor's name.
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/v1/widget/visitor/conversation", Ct)).StatusCode);
        var website = (await JsonAsync(await ana.GetAsync("/api/v1/conversations"))).EnumerateArray().Single(c => c.GetProperty("type").GetString() == "website");
        var websiteDetail = await JsonAsync(await ana.GetAsync($"/api/v1/conversations/{website.GetProperty("id").GetGuid()}"));
        Assert.Contains(websiteDetail.GetProperty("participants").EnumerateArray(), p => p.GetProperty("user").GetProperty("deleted").GetBoolean());

        // Messages and the file are gone; the conversations remain.
        var history = (await JsonAsync(await ben.GetAsync($"/api/v1/conversations/{direct}/history"))).GetProperty("items").EnumerateArray();
        Assert.DoesNotContain(history, i => i.GetProperty("kind").GetString() == "message");
        Assert.Empty(Directory.EnumerateFiles(api.StorageRoot, "*", SearchOption.AllDirectories));

        // The unconfirmed account is gone entirely: the address registers again as new.
        using var retry = new ApiSession(api.CreateHttpsClient());
        AssertStatus(HttpStatusCode.Accepted, await retry.PostAsync("/api/v1/auth/register",
            new { email = "stranger@example.org", password = Password, displayName = "Stranger", captchaToken = "t" }));
        Assert.Equal(2, api.Emails.To("stranger@example.org", "email-confirmation").Count);
    }
}
