using System.Net;
using System.Text.Json;
using Audorix.KnockTwice.Application.Identity;
using Audorix.KnockTwice.Domain.Authorization;
using Audorix.KnockTwice.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using static Audorix.KnockTwice.IntegrationTests.Support.ApiSession;
using static Audorix.KnockTwice.IntegrationTests.Support.TestUsers;

namespace Audorix.KnockTwice.IntegrationTests;

/// <summary>
/// Phase 9 acceptance: installation administrators configure the platform, nobody else reaches the
/// administration, provider configuration is validated, and feature settings are respected at once.
/// </summary>
public sealed class AdministrationTests
{
    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Only_permitted_roles_reach_the_administration_and_role_changes_apply_at_once(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (admin, adminId) = await SignedInAsync(api, "admin@example.org", "Admin");
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        using var _1 = admin;
        using var _2 = ana;
        await GrantAsync(api, adminId, Roles.InstallationAdministrator);

        string[] reads = ["/api/v1/admin/overview", "/api/v1/admin/settings", "/api/v1/admin/providers", "/api/v1/admin/users", "/api/v1/admin/roles",
            "/api/v1/admin/organizations", "/api/v1/admin/audit"];
        using var anonymous = api.CreateHttpsClient();
        foreach (var path in reads)
        {
            AssertStatus(HttpStatusCode.Unauthorized, await anonymous.GetAsync(new Uri(path, UriKind.Relative), Ct));
            AssertStatus(HttpStatusCode.Forbidden, await ana.GetAsync(path), path);
            AssertStatus(HttpStatusCode.OK, await admin.GetAsync(path), path);
        }

        AssertStatus(HttpStatusCode.Forbidden, await ana.SendAsync(HttpMethod.Put, "/api/v1/admin/settings", new { values = new { } }));
        AssertStatus(HttpStatusCode.Forbidden, await ana.PostAsync($"/api/v1/admin/users/{adminId}/disable"));

        // An auditor reads but cannot change anything; granting and revoking work without signing in again.
        AssertStatus(HttpStatusCode.OK, await admin.PostAsync($"/api/v1/admin/users/{anaId}/roles", new { role = "Auditor" }));
        AssertStatus(HttpStatusCode.OK, await ana.GetAsync("/api/v1/admin/users"));
        AssertStatus(HttpStatusCode.OK, await ana.GetAsync("/api/v1/admin/audit"));
        AssertStatus(HttpStatusCode.Forbidden, await ana.SendAsync(HttpMethod.Put, "/api/v1/admin/settings", new { values = new Dictionary<string, object> { ["PrivateChats:Enabled"] = false } }));
        AssertStatus(HttpStatusCode.Forbidden, await ana.PostAsync($"/api/v1/admin/users/{adminId}/disable"));
        var me = await JsonAsync(await ana.GetAsync("/api/v1/users/me"));
        Assert.Contains("audit.read", me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
        Assert.DoesNotContain("users.manage", me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));

        AssertStatus(HttpStatusCode.OK, await admin.SendAsync(HttpMethod.Delete, $"/api/v1/admin/users/{anaId}/roles/Auditor"));
        AssertStatus(HttpStatusCode.Forbidden, await ana.GetAsync("/api/v1/admin/users"));

        // The installation keeps an administrator.
        Assert.Equal("cannot_disable_self", await ErrorCodeAsync(await admin.PostAsync($"/api/v1/admin/users/{adminId}/disable")));
        Assert.Equal("last_administrator", await ErrorCodeAsync(await admin.SendAsync(HttpMethod.Delete, $"/api/v1/admin/users/{adminId}/roles/InstallationAdministrator")));

        var roles = await JsonAsync(await admin.GetAsync("/api/v1/admin/roles"));
        var adminRole = roles.EnumerateArray().Single(r => r.GetProperty("name").GetString() == "InstallationAdministrator");
        Assert.Equal(1, adminRole.GetProperty("members").GetInt32());
    }

    [Fact]
    public async Task Disabling_an_account_signs_it_out_and_blocks_sign_in_until_enabled()
    {
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db);
        var (admin, adminId) = await SignedInAsync(api, "admin@example.org", "Admin");
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        using var _1 = admin;
        using var _2 = ana;
        await GrantAsync(api, adminId, Roles.InstallationAdministrator);

        var found = await JsonAsync(await admin.GetAsync("/api/v1/admin/users?q=ANA@example"));
        Assert.Equal(1, found.GetProperty("total").GetInt32());
        var row = found.GetProperty("items")[0];
        Assert.Equal((anaId, "Ana", 1, false), (row.GetProperty("id").GetGuid(), row.GetProperty("displayName").GetString(),
            row.GetProperty("activeSessions").GetInt32(), row.GetProperty("disabled").GetBoolean()));

        var disabled = await admin.PostAsync($"/api/v1/admin/users/{anaId}/disable");
        AssertStatus(HttpStatusCode.OK, disabled, await disabled.Content.ReadAsStringAsync(Ct));
        Assert.True((await JsonAsync(disabled)).GetProperty("disabled").GetBoolean());
        AssertStatus(HttpStatusCode.Unauthorized, await ana.GetAsync("/api/v1/users/me"));
        using (var again = new ApiSession(api.CreateHttpsClient()))
        {
            Assert.Equal("invalid_credentials", await ErrorCodeAsync(await again.LoginAsync("ana@example.org", Password)));
        }

        Assert.Equal(1, (await JsonAsync(await admin.GetAsync("/api/v1/admin/users?filter=disabled"))).GetProperty("total").GetInt32());

        // Redesign 2026-11: status and role are separate filters, and the order can be reversed.
        static IEnumerable<string?> Names(System.Text.Json.JsonElement page) => page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("displayName").GetString());
        Assert.Equal(["Admin"], Names(await JsonAsync(await admin.GetAsync("/api/v1/admin/users?filter=enabled"))));
        Assert.Equal(["Admin"], Names(await JsonAsync(await admin.GetAsync("/api/v1/admin/users?role=InstallationAdministrator"))));
        Assert.Equal(["Ana"], Names(await JsonAsync(await admin.GetAsync("/api/v1/admin/users?role=none"))));
        Assert.Empty(Names(await JsonAsync(await admin.GetAsync("/api/v1/admin/users?filter=disabled&role=InstallationAdministrator"))));
        Assert.Equal(["Admin", "Ana"], Names(await JsonAsync(await admin.GetAsync("/api/v1/admin/users"))));
        Assert.Equal(["Ana", "Admin"], Names(await JsonAsync(await admin.GetAsync("/api/v1/admin/users?sort=nameDesc"))));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/admin/users?sort=sideways")).StatusCode);

        AssertStatus(HttpStatusCode.OK, await admin.PostAsync($"/api/v1/admin/users/{anaId}/enable"));
        using (var back = new ApiSession(api.CreateHttpsClient()))
        {
            AssertStatus(HttpStatusCode.OK, await back.LoginAsync("ana@example.org", Password));
            Assert.Equal(1, (await JsonAsync(await admin.PostAsync($"/api/v1/admin/users/{anaId}/revoke-sessions"))).GetProperty("revoked").GetInt32());
            AssertStatus(HttpStatusCode.Unauthorized, await back.GetAsync("/api/v1/users/me"));
        }

        // Everything above is in the audit log, with actors and without secrets.
        var audit = (await JsonAsync(await admin.GetAsync("/api/v1/admin/audit?limit=200"))).GetProperty("items").EnumerateArray().ToList();
        var actions = audit.Select(e => e.GetProperty("action").GetString()).ToList();
        Assert.Contains("admin.user.disabled", actions);
        Assert.Contains("admin.user.enabled", actions);
        Assert.Contains("admin.user.sessions_revoked", actions);
        var refused = audit.First(e => e.GetProperty("action").GetString() == "auth.sign_in" && !e.GetProperty("succeeded").GetBoolean());
        Assert.Equal(anaId.ToString(), refused.GetProperty("targetId").GetString());
        Assert.Contains("disabled", refused.GetProperty("details").GetString(), StringComparison.Ordinal);
        Assert.Equal("Admin", audit.First(e => e.GetProperty("action").GetString() == "admin.user.disabled").GetProperty("actorName").GetString());
        Assert.DoesNotContain(audit, e => e.ToString().Contains(Password, StringComparison.Ordinal));

        var signIns = (await JsonAsync(await admin.GetAsync("/api/v1/admin/audit?action=auth."))).GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("action").GetString()).Distinct().ToList();
        Assert.All(signIns, a => Assert.StartsWith("auth.", a, StringComparison.Ordinal));

        // Redesign 2026-11: page numbers with a total, oldest first, a date range and text search.
        var total = audit.Count;
        var page = await JsonAsync(await admin.GetAsync("/api/v1/admin/audit?skip=2&limit=3"));
        Assert.Equal(total, page.GetProperty("total").GetInt32());
        Assert.Equal(audit.Skip(2).Take(3).Select(e => e.GetProperty("id").GetGuid()), page.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()));
        var oldest = await JsonAsync(await admin.GetAsync("/api/v1/admin/audit?skip=0&limit=1&order=oldest"));
        Assert.Equal(audit[^1].GetProperty("id").GetGuid(), oldest.GetProperty("items")[0].GetProperty("id").GetGuid());
        var plain = await JsonAsync(await admin.GetAsync("/api/v1/admin/audit"));
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("total").ValueKind); // the cursor form has no total
        var future = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));
        Assert.Equal(0, (await JsonAsync(await admin.GetAsync($"/api/v1/admin/audit?skip=0&from={future}"))).GetProperty("total").GetInt32());
        Assert.Equal(total, (await JsonAsync(await admin.GetAsync($"/api/v1/admin/audit?skip=0&limit=200&to={future}"))).GetProperty("total").GetInt32());
        var byActor = await JsonAsync(await admin.GetAsync("/api/v1/admin/audit?skip=0&limit=200&q=admi"));
        Assert.Contains(byActor.GetProperty("items").EnumerateArray(), e => e.GetProperty("actorName").GetString() == "Admin");
        var byAction = await JsonAsync(await admin.GetAsync("/api/v1/admin/audit?skip=0&limit=200&q=sessions_rev"));
        Assert.All(byAction.GetProperty("items").EnumerateArray(), e => Assert.Contains("sessions_rev", e.GetProperty("action").GetString(), StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/admin/audit?order=sideways")).StatusCode);
    }

    [Fact]
    public async Task Live_settings_are_validated_and_respected_on_the_next_request()
    {
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db, new() { ["Conversations:MaxGroupParticipants"] = "300" });
        var (admin, adminId) = await SignedInAsync(api, "admin@example.org", "Admin");
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = admin;
        using var _2 = ana;
        using var _3 = ben;
        await GrantAsync(api, adminId, Roles.InstallationAdministrator);
        await BecomeContactsAsync(ana, anaId, ben, benId);

        var settings = await SettingsAsync(admin);
        Assert.Equal(("appsettings", false, true), (settings["PrivateChats:Enabled"].GetProperty("source").GetString(),
            settings["PrivateChats:Enabled"].GetProperty("locked").GetBoolean(), settings["PrivateChats:Enabled"].GetProperty("value").GetBoolean()));
        Assert.True(settings["Conversations:MaxGroupParticipants"].GetProperty("locked").GetBoolean());
        Assert.Equal(300, settings["Conversations:MaxGroupParticipants"].GetProperty("value").GetInt32());

        // Invalid values and combinations are refused before anything is stored.
        Assert.Equal("out_of_range", await FieldErrorAsync(await Put(admin, new() { ["Messaging:MaxLength"] = 0 }), "Messaging:MaxLength"));
        Assert.Equal("setting_unknown", await FieldErrorAsync(await Put(admin, new() { ["Database:ConnectionString"] = "x" }), "Database:ConnectionString"));
        Assert.Equal("setting_locked", await FieldErrorAsync(await Put(admin, new() { ["Conversations:MaxGroupParticipants"] = 10 }), "Conversations:MaxGroupParticipants"));
        var inconsistent = await Put(admin, new() { ["PrivateChats:MaxFileSize"] = 20_000_000, ["PrivateChats:MaxBytesPerChat"] = 10_000_000 });
        Assert.Equal("settings_invalid", await ErrorCodeAsync(inconsistent));
        Assert.Contains("MaxBytesPerChat", (await JsonAsync(inconsistent)).GetProperty("details")[0].GetString(), StringComparison.Ordinal);
        Assert.Equal("settings_invalid", await ErrorCodeAsync(await Put(admin, new() { ["Localization:SupportedLanguages"] = new[] { "es" } })));

        var direct = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/direct", new { userId = benId }))).GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.Created, await ana.PostAsync($"/api/v1/conversations/{direct}/messages", new { clientMessageId = "a", body = "A fairly long message" }));

        // Features and limits change for the very next request.
        var saved = await Put(admin, new()
        {
            ["PrivateChats:Enabled"] = false,
            ["Attachments:Enabled"] = false,
            ["Messaging:MaxLength"] = 10,
            ["Organizations:AllowCreation"] = false,
            ["Identity:RegistrationMode"] = "closed",
        });
        AssertStatus(HttpStatusCode.OK, saved, await saved.Content.ReadAsStringAsync(Ct));
        Assert.Equal("database", (await SettingsAsync(admin))["Messaging:MaxLength"].GetProperty("source").GetString());

        Assert.Equal("private_chats_disabled", await ErrorCodeAsync(await ana.PostAsync("/api/v1/private-chats", new { userId = benId })));
        Assert.Equal("attachments_disabled", await ErrorCodeAsync(await ana.UploadAsync($"/api/v1/conversations/{direct}/attachments", "a.png", TestFiles.Png())));
        Assert.Equal("message_body_too_long", await ErrorCodeAsync(await ana.PostAsync($"/api/v1/conversations/{direct}/messages", new { clientMessageId = "b", body = "A fairly long message" })));
        Assert.Equal("organization_creation_disabled", await ErrorCodeAsync(await ana.PostAsync("/api/v1/organizations", new { name = "Ana Co" })));
        AssertStatus(HttpStatusCode.Created, await admin.PostAsync("/api/v1/organizations", new { name = "Admin Co" }));
        using (var visitor = new ApiSession(api.CreateHttpsClient()))
        {
            Assert.Equal("registration_disabled", await ErrorCodeAsync(await visitor.PostAsync("/api/v1/auth/register",
                new { email = "new@example.org", password = Password, displayName = "New", captchaToken = "t" })));
            var config = await JsonAsync(await visitor.GetAsync("/api/v1/auth/config"));
            Assert.Equal((false, "closed"), (config.GetProperty("registrationEnabled").GetBoolean(), config.GetProperty("registrationMode").GetString()));
        }

        // Invitation-only: only addresses with an open organization invitation may register.
        AssertStatus(HttpStatusCode.OK, await Put(admin, new() { ["Identity:RegistrationMode"] = "invitation" }));
        var organization = (await JsonAsync(await admin.GetAsync("/api/v1/organizations"))).EnumerateArray().Single().GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.Created, await admin.PostAsync($"/api/v1/organizations/{organization}/invitations", new { email = "invited@example.org", role = "member" }));
        using (var visitor = new ApiSession(api.CreateHttpsClient()))
        {
            Assert.Equal("registration_invitation_required", await ErrorCodeAsync(await visitor.PostAsync("/api/v1/auth/register",
                new { email = "stranger@example.org", password = Password, displayName = "Stranger", captchaToken = "t" })));
            AssertStatus(HttpStatusCode.Accepted, await visitor.PostAsync("/api/v1/auth/register",
                new { email = "invited@example.org", password = Password, displayName = "Invited", captchaToken = "t" }));
        }

        // Resetting returns to the default.
        AssertStatus(HttpStatusCode.OK, await Put(admin, new() { ["Messaging:MaxLength"] = null, ["Attachments:Enabled"] = null }));
        AssertStatus(HttpStatusCode.Created, await ana.PostAsync($"/api/v1/conversations/{direct}/messages", new { clientMessageId = "c", body = "A fairly long message" }));
        Assert.Equal("appsettings", (await SettingsAsync(admin))["Messaging:MaxLength"].GetProperty("source").GetString());

        // Languages: the default language applies to responses at once.
        AssertStatus(HttpStatusCode.OK, await Put(admin, new() { ["Localization:DefaultLanguage"] = "es" }));
        using var anonymous = api.CreateHttpsClient();
        var problem = await anonymous.GetAsync(new Uri("/api/v1/users/me", UriKind.Relative), Ct);
        Assert.Equal("es", (await JsonAsync(await anonymous.GetAsync(new Uri("/api/v1/meta", UriKind.Relative), Ct))).GetProperty("defaultLanguage").GetString());
        AssertStatus(HttpStatusCode.Unauthorized, problem);

        var changes = (await JsonAsync(await admin.GetAsync("/api/v1/admin/audit?action=admin.settings.changed"))).GetProperty("items");
        Assert.True(changes.GetArrayLength() >= 4);
        Assert.Contains("Messaging:MaxLength", changes[changes.GetArrayLength() - 1].GetProperty("details").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provider_settings_are_validated_saved_to_the_settings_file_and_secrets_never_returned()
    {
        var settingsFile = Path.Combine(Path.GetTempPath(), "chatx-tests", $"settings-{Guid.NewGuid():N}.json");
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db, new() { ["Setup:SettingsPath"] = settingsFile, ["Email:Smtp:Host"] = "mail.locked.test" });
        var (admin, adminId) = await SignedInAsync(api, "admin@example.org", "Admin");
        using var _1 = admin;
        await GrantAsync(api, adminId, Roles.InstallationAdministrator);
        try
        {
            var email = await ProviderAsync(admin, "email");
            Assert.True(email["Email:Smtp:Host"].GetProperty("locked").GetBoolean());
            Assert.Equal("setting_locked", await FieldErrorAsync(await admin.SendAsync(HttpMethod.Put, "/api/v1/admin/providers/email",
                new { values = new Dictionary<string, object> { ["Email:Smtp:Host"] = "other.test" } }), "Email:Smtp:Host"));

            // Postmark without a server token fails the startup rules; nothing is written.
            var invalid = await admin.SendAsync(HttpMethod.Put, "/api/v1/admin/providers/email", new { values = new Dictionary<string, object?> { ["Email:Provider"] = "Postmark" } });
            Assert.Equal("settings_invalid", await ErrorCodeAsync(invalid));
            Assert.False(File.Exists(settingsFile));

            var saved = await admin.SendAsync(HttpMethod.Put, "/api/v1/admin/providers/email", new
            {
                values = new Dictionary<string, object?> { ["Email:FromName"] = "Team Chat", ["Email:Smtp:Username"] = "chat", ["Email:Smtp:Password"] = "s3cret-value", ["Email:Smtp:Port"] = 2525 },
            });
            AssertStatus(HttpStatusCode.OK, saved, await saved.Content.ReadAsStringAsync(Ct));
            var body = await saved.Content.ReadAsStringAsync(Ct);
            Assert.DoesNotContain("s3cret-value", body, StringComparison.Ordinal);
            var group = await JsonAsync(saved);
            Assert.True(group.GetProperty("restartRequired").GetBoolean());

            using (var file = JsonDocument.Parse(await File.ReadAllTextAsync(settingsFile, Ct)))
            {
                var smtp = file.RootElement.GetProperty("Email").GetProperty("Smtp");
                Assert.Equal(("s3cret-value", "2525"), (smtp.GetProperty("Password").GetString(), smtp.GetProperty("Port").GetString()));
                Assert.Equal("Team Chat", file.RootElement.GetProperty("Email").GetProperty("FromName").GetString());
            }

            // Storage: a probe through the candidate settings, without saving.
            var test = await admin.PostAsync("/api/v1/admin/providers/storage/test",
                new { values = new Dictionary<string, object?> { ["Storage:Provider"] = "Local" } });
            AssertStatus(HttpStatusCode.OK, test, await test.Content.ReadAsStringAsync(Ct));
            Assert.Equal("ok", (await JsonAsync(test)).GetProperty("outcome").GetString());
            Assert.Equal("disabled", (await JsonAsync(await admin.PostAsync("/api/v1/admin/providers/scanning/test", new { values = new { } }))).GetProperty("outcome").GetString());
            Assert.Equal("settings_invalid", await ErrorCodeAsync(await admin.PostAsync("/api/v1/admin/providers/storage/test",
                new { values = new Dictionary<string, object?> { ["Storage:Provider"] = "S3" } })));

            var overview = await JsonAsync(await admin.GetAsync("/api/v1/admin/overview"));
            Assert.Contains("email", overview.GetProperty("restartRequiredFor").EnumerateArray().Select(e => e.GetString()));

            var audit = await admin.GetAsync("/api/v1/admin/audit?action=admin.provider.");
            var auditText = await audit.Content.ReadAsStringAsync(Ct);
            Assert.Contains("Email:Smtp:Password", auditText, StringComparison.Ordinal);
            Assert.DoesNotContain("s3cret-value", auditText, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(settingsFile);
        }
    }

    [Fact]
    public async Task Administrators_manage_organizations_but_always_keep_an_owner()
    {
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db);
        var (admin, adminId) = await SignedInAsync(api, "admin@example.org", "Admin");
        var (ana, _) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = admin;
        using var _2 = ana;
        using var _3 = ben;
        await GrantAsync(api, adminId, Roles.InstallationAdministrator);
        var organization = await CreateOrganizationAsync(ana, (ben, "ben@example.org"));

        var list = await JsonAsync(await admin.GetAsync("/api/v1/admin/organizations?q=acm"));
        Assert.Equal((1, 2, 1), (list.GetProperty("total").GetInt32(), list.GetProperty("items")[0].GetProperty("members").GetInt32(),
            list.GetProperty("items")[0].GetProperty("owners").GetInt32()));

        // Sorting (redesign 2026-11): by name either way, or by members (most first).
        AssertStatus(HttpStatusCode.Created, await ben.PostAsync("/api/v1/organizations", new { name = "Zeta Solo" }));
        static IEnumerable<string?> Names(System.Text.Json.JsonElement page) => page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString());
        Assert.Equal(["Zeta Solo", "Acme"], Names(await JsonAsync(await admin.GetAsync("/api/v1/admin/organizations?sort=nameDesc"))));
        Assert.Equal(["Acme", "Zeta Solo"], Names(await JsonAsync(await admin.GetAsync("/api/v1/admin/organizations?sort=members"))));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/admin/organizations?sort=newest")).StatusCode);

        var renamed = await JsonAsync(await admin.SendAsync(HttpMethod.Put, $"/api/v1/admin/organizations/{organization}", new { name = "Acme Renamed" }));
        Assert.Equal("Acme Renamed", renamed.GetProperty("organization").GetProperty("name").GetString());
        var ownerId = renamed.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("role").GetString() == "owner").GetProperty("userId").GetGuid();
        Assert.Equal("last_owner", await ErrorCodeAsync(await admin.SendAsync(HttpMethod.Put, $"/api/v1/admin/organizations/{organization}/members/{ownerId}", new { role = "member" })));
        Assert.Equal("last_owner", await ErrorCodeAsync(await admin.SendAsync(HttpMethod.Delete, $"/api/v1/admin/organizations/{organization}/members/{ownerId}")));

        AssertStatus(HttpStatusCode.OK, await admin.SendAsync(HttpMethod.Put, $"/api/v1/admin/organizations/{organization}/members/{benId}", new { role = "owner" }));
        AssertStatus(HttpStatusCode.NoContent, await admin.SendAsync(HttpMethod.Delete, $"/api/v1/admin/organizations/{organization}/members/{ownerId}"));
        var detail = await JsonAsync(await admin.GetAsync($"/api/v1/admin/organizations/{organization}"));
        Assert.Equal(["Ben"], detail.GetProperty("members").EnumerateArray().Select(m => m.GetProperty("displayName").GetString()));
        Assert.Equal("organization_not_found", await ErrorCodeAsync(await ana.GetAsync($"/api/v1/organizations/{organization}")));
    }

    [Fact]
    public async Task Administration_shows_profile_photos_of_people_who_have_one()
    {
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db);
        var (admin, adminId) = await SignedInAsync(api, "admin@example.org", "Admin");
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, _) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = admin;
        using var _2 = ana;
        using var _3 = ben;
        await GrantAsync(api, adminId, Roles.InstallationAdministrator);
        AssertStatus(HttpStatusCode.OK, await admin.UploadAsync("/api/v1/users/me/photo", "admin.png", TestFiles.Png(2048), "image/png"));
        AssertStatus(HttpStatusCode.OK, await ana.UploadAsync("/api/v1/users/me/photo", "ana.png", TestFiles.Png(2048), "image/png"));
        var organization = await CreateOrganizationAsync(ana, (ben, "ben@example.org"));

        // Users: Ana and the administrator have photos, Ben shows initials.
        var users = (await JsonAsync(await admin.GetAsync("/api/v1/admin/users"))).GetProperty("items").EnumerateArray()
            .ToDictionary(u => u.GetProperty("displayName").GetString()!, u => u.GetProperty("photoUrl").GetString());
        Assert.Equal($"/api/v1/users/{anaId}/photo?v=1", users["Ana"]);
        Assert.Null(users["Ben"]);

        // Organization members.
        var members = (await JsonAsync(await admin.GetAsync($"/api/v1/admin/organizations/{organization}"))).GetProperty("members").EnumerateArray()
            .ToDictionary(m => m.GetProperty("displayName").GetString()!, m => m.GetProperty("photoUrl").GetString());
        Assert.Equal(($"/api/v1/users/{anaId}/photo?v=1", (string?)null), (members["Ana"], members["Ben"]));

        // The audit log: the actor's photo next to what they did.
        AssertStatus(HttpStatusCode.OK, await admin.PostAsync($"/api/v1/admin/users/{anaId}/disable"));
        var byAdmin = (await JsonAsync(await admin.GetAsync("/api/v1/admin/audit?limit=200"))).GetProperty("items").EnumerateArray()
            .First(e => e.TryGetProperty("actorUserId", out var actor) && actor.ValueKind == JsonValueKind.String && actor.GetGuid() == adminId);
        Assert.Equal($"/api/v1/users/{adminId}/photo?v=1", byAdmin.GetProperty("actorPhotoUrl").GetString());
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static async Task GrantAsync(ChatXApiFactory api, Guid userId, string role)
    {
        await using var scope = api.Services.CreateAsyncScope();
        Assert.True((await scope.ServiceProvider.GetRequiredService<IUserAccountManager>().AddToRoleAsync(userId, role, Ct)).Succeeded);
    }

    private static Task<HttpResponseMessage> Put(ApiSession session, Dictionary<string, object?> values) =>
        session.SendAsync(HttpMethod.Put, "/api/v1/admin/settings", new { values });

    private static async Task<Dictionary<string, JsonElement>> SettingsAsync(ApiSession session) =>
        (await JsonAsync(await session.GetAsync("/api/v1/admin/settings"))).EnumerateArray().ToDictionary(s => s.GetProperty("key").GetString()!);

    private static async Task<Dictionary<string, JsonElement>> ProviderAsync(ApiSession session, string group) =>
        (await JsonAsync(await session.GetAsync("/api/v1/admin/providers"))).EnumerateArray()
            .Single(g => g.GetProperty("name").GetString() == group).GetProperty("fields").EnumerateArray()
            .ToDictionary(f => f.GetProperty("key").GetString()!);

    private static async Task<string?> FieldErrorAsync(HttpResponseMessage response, string field)
    {
        var body = await JsonAsync(response);
        return body.GetProperty("errors").GetProperty(field)[0].GetString();
    }
}
