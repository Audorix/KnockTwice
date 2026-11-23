using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Audorix.ChatX.Application.Attachments;
using Audorix.ChatX.Domain.Attachments;
using Audorix.ChatX.Infrastructure.Persistence;
using Audorix.ChatX.Infrastructure.Storage;
using Audorix.ChatX.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Audorix.ChatX.IntegrationTests.Support.ApiSession;
using static Audorix.ChatX.IntegrationTests.Support.TestUsers;

namespace Audorix.ChatX.IntegrationTests;

/// <summary>
/// Phase 7 acceptance: both storage providers work, metadata persists, unauthorized people cannot
/// read attachments, invalid types are rejected, configured limits are enforced, quarantine holds
/// files back, and cleanup removes what is no longer needed — on real SQLite and PostgreSQL.
/// </summary>
public sealed class AttachmentTests
{
    public static TheoryData<string> Providers => ["Sqlite", "PostgreSql"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Files_are_uploaded_sent_with_a_message_and_downloaded_only_by_participants(string provider)
    {
        await using var db = await TestDatabase.CreateAsync(provider);
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (carl, carlId) = await SignedInAsync(api, "carl@example.org", "Carl");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = carl;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        await BecomeContactsAsync(ana, anaId, carl, carlId);
        var direct = await DirectAsync(ana, benId);

        var png = TestFiles.Png(4096);
        var upload = await ana.UploadAsync($"/api/v1/conversations/{direct}/attachments", "../../holiday photo.PNG", png, "image/png");
        AssertStatus(HttpStatusCode.Created, upload, await upload.Content.ReadAsStringAsync(Ct));
        var uploaded = await JsonAsync(upload);
        var attachmentId = uploaded.GetProperty("id").GetGuid();
        Assert.Equal(("holiday photo.PNG", "image/png", "image", 4096L, "ready"), (
            uploaded.GetProperty("fileName").GetString(), uploaded.GetProperty("contentType").GetString(), uploaded.GetProperty("kind").GetString(),
            uploaded.GetProperty("size").GetInt64(), uploaded.GetProperty("status").GetString()));

        // Unsent: only the uploader may see it.
        AssertStatus(HttpStatusCode.OK, await ana.GetAsync($"/api/v1/attachments/{attachmentId}/content"));
        Assert.Equal("attachment_not_found", await ErrorCodeAsync(await ben.GetAsync($"/api/v1/attachments/{attachmentId}/content")));

        // A message with only an attachment.
        var send = await ana.PostAsync($"/api/v1/conversations/{direct}/messages", new { clientMessageId = "m1", body = "", attachmentIds = new[] { attachmentId } });
        AssertStatus(HttpStatusCode.Created, send, await send.Content.ReadAsStringAsync(Ct));
        var message = await JsonAsync(send);
        Assert.Equal(attachmentId, message.GetProperty("attachments")[0].GetProperty("id").GetGuid());

        // Retrying the same send is idempotent; reusing the file elsewhere is refused.
        AssertStatus(HttpStatusCode.OK, await ana.PostAsync($"/api/v1/conversations/{direct}/messages", new { clientMessageId = "m1", body = "", attachmentIds = new[] { attachmentId } }));
        Assert.Equal("validation_failed", await ErrorCodeAsync(await ana.PostAsync($"/api/v1/conversations/{direct}/messages",
            new { clientMessageId = "m2", body = "again", attachmentIds = new[] { attachmentId } })));

        // Ben sees it in history and downloads the exact bytes; images are inline and sandboxed.
        var history = (await JsonAsync(await ben.GetAsync($"/api/v1/conversations/{direct}/history"))).GetProperty("items").EnumerateArray()
            .Single(e => e.GetProperty("kind").GetString() == "message");
        Assert.Equal("holiday photo.PNG", history.GetProperty("attachments")[0].GetProperty("fileName").GetString());
        var download = await ben.GetAsync($"/api/v1/attachments/{attachmentId}/content");
        AssertStatus(HttpStatusCode.OK, download);
        Assert.Equal(png, await download.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal("image/png", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("inline", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Contains("sandbox", download.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("attachment", (await ben.GetAsync($"/api/v1/attachments/{attachmentId}/content?download=true")).Content.Headers.ContentDisposition!.DispositionType);

        // Conditional requests revalidate after authorization.
        using (var conditional = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/attachments/{attachmentId}/content"))
        {
            conditional.Headers.IfNoneMatch.Add(download.Headers.ETag!);
            AssertStatus(HttpStatusCode.NotModified, await ben.Client.SendAsync(conditional, Ct));
        }

        // Outsiders and anonymous callers get nothing, not even metadata.
        Assert.Equal("attachment_not_found", await ErrorCodeAsync(await carl.GetAsync($"/api/v1/attachments/{attachmentId}/content")));
        Assert.Equal("attachment_not_found", await ErrorCodeAsync(await carl.GetAsync($"/api/v1/attachments/{attachmentId}")));
        using (var conditional = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/attachments/{attachmentId}/content"))
        {
            conditional.Headers.IfNoneMatch.Add(download.Headers.ETag!);
            AssertStatus(HttpStatusCode.NotFound, await carl.Client.SendAsync(conditional, Ct));
        }

        using var anonymous = api.CreateHttpsClient();
        AssertStatus(HttpStatusCode.Unauthorized, await anonymous.GetAsync(new Uri($"/api/v1/attachments/{attachmentId}/content", UriKind.Relative), Ct));

        // Carl cannot upload into a conversation he is not part of, nor attach Ana's file.
        Assert.Equal("conversation_not_found", await ErrorCodeAsync(await carl.UploadAsync($"/api/v1/conversations/{direct}/attachments", "x.png", png)));

        // Metadata persisted with the generated key, never the user's name.
        await using var scope = api.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<ChatXDbContext>().Attachments.AsNoTracking().SingleAsync(a => a.Id == attachmentId, Ct);
        Assert.Equal((anaId, direct, AttachmentStatus.Ready, message.GetProperty("id").GetGuid()), (stored.OwnerUserId, stored.ConversationId, stored.Status, stored.MessageEntryId!.Value));
        Assert.Matches("^[0-9]{4}/[0-9]{2}/[0-9a-f]{32}$", stored.StorageKey);
        Assert.Equal(64, stored.Sha256.Length);
        Assert.True(File.Exists(Path.Combine(api.StorageRoot, stored.StorageKey.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task Invalid_types_and_content_are_rejected()
    {
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        var direct = await DirectAsync(ana, benId);
        var path = $"/api/v1/conversations/{direct}/attachments";

        async Task<string?> FieldError(HttpResponseMessage response)
        {
            AssertStatus(HttpStatusCode.BadRequest, response, await response.Content.ReadAsStringAsync(Ct));
            return (await JsonAsync(response)).GetProperty("errors").GetProperty("file")[0].GetString();
        }

        Assert.Equal("attachment_type_not_allowed", await FieldError(await ana.UploadAsync(path, "setup.exe", [0x4D, 0x5A, 0x90, 0x00])));
        Assert.Equal("attachment_type_not_allowed", await FieldError(await ana.UploadAsync(path, "drawing.svg", TestFiles.Text("<svg onload=alert(1)/>"), "image/svg+xml")));
        Assert.Equal("attachment_type_not_allowed", await FieldError(await ana.UploadAsync(path, "page.html", TestFiles.Text("<script></script>"), "text/html")));
        Assert.Equal("attachment_type_not_allowed", await FieldError(await ana.UploadAsync(path, "no-extension", TestFiles.Png())));
        Assert.Equal("attachment_signature_mismatch", await FieldError(await ana.UploadAsync(path, "photo.png", TestFiles.Pdf(), "image/png")));
        Assert.Equal("attachment_signature_mismatch", await FieldError(await ana.UploadAsync(path, "report.docx", TestFiles.Zip())));
        Assert.Equal("attachment_signature_mismatch", await FieldError(await ana.UploadAsync(path, "notes.txt", [0x00, 0x01, 0x02, 0xFF], "text/plain")));
        Assert.Equal("attachment_type_mismatch", await FieldError(await ana.UploadAsync(path, "photo.png", TestFiles.Png(), "text/html")));
        Assert.Equal("attachment_empty", await FieldError(await ana.UploadAsync(path, "empty.pdf", [])));

        // The allowed types with genuine content are accepted, with the detected type.
        foreach (var (name, bytes, type) in new[]
        {
            ("paper.pdf", TestFiles.Pdf(), "application/pdf"),
            ("report.docx", TestFiles.OfficePackage("word/document.xml"), "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
            ("sheet.xlsx", TestFiles.OfficePackage("xl/workbook.xml"), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            ("bundle.zip", TestFiles.Zip(), "application/zip"),
            ("data.csv", TestFiles.Text("a;b\n1;2\n"), "text/csv"),
            ("readme.txt", TestFiles.Text("Hola, ¿qué tal?\n"), "text/plain"),
        })
        {
            var ok = await ana.UploadAsync(path, name, bytes);
            AssertStatus(HttpStatusCode.Created, ok, await ok.Content.ReadAsStringAsync(Ct));
            Assert.Equal(type, (await JsonAsync(ok)).GetProperty("contentType").GetString());
        }

        // Not multipart, or no file part.
        var json = await ana.PostAsync(path, new { file = "x" });
        Assert.Equal("multipart_required", (await JsonAsync(json)).GetProperty("errors").GetProperty("file")[0].GetString());
    }

    [Fact]
    public async Task Size_count_and_quota_limits_are_enforced()
    {
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db, new()
        {
            ["Attachments:MaxFileSize"] = "4096",
            ["Attachments:UserQuota"] = "10000",
            ["Attachments:MaxPerMessage"] = "2",
        });
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        var direct = await DirectAsync(ana, benId);
        var path = $"/api/v1/conversations/{direct}/attachments";

        var tooLarge = await ana.UploadAsync(path, "big.png", TestFiles.Png(5000));
        AssertStatus(HttpStatusCode.RequestEntityTooLarge, tooLarge);
        Assert.Equal("attachment_too_large", await ErrorCodeAsync(tooLarge));

        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var ok = await ana.UploadAsync(path, $"p{i}.png", TestFiles.Png(3000));
            AssertStatus(HttpStatusCode.Created, ok);
            ids.Add((await JsonAsync(ok)).GetProperty("id").GetGuid());
        }

        var quota = await ana.UploadAsync(path, "p4.png", TestFiles.Png(3000));
        AssertStatus(HttpStatusCode.RequestEntityTooLarge, quota);
        Assert.Equal("attachment_quota_exceeded", await ErrorCodeAsync(quota));

        // Too many for one message.
        var many = await ana.PostAsync($"/api/v1/conversations/{direct}/messages", new { clientMessageId = "m", body = "x", attachmentIds = ids });
        Assert.Equal("too_many_attachments", (await JsonAsync(many)).GetProperty("errors").GetProperty("attachmentIds")[0].GetString());

        // Removing an unsent upload frees the quota (and the stored file).
        AssertStatus(HttpStatusCode.NoContent, await ana.SendAsync(HttpMethod.Delete, $"/api/v1/attachments/{ids[2]}"));
        AssertStatus(HttpStatusCode.Created, await ana.UploadAsync(path, "p5.png", TestFiles.Png(3000)));

        var limits = await JsonAsync(await ana.GetAsync("/api/v1/attachments/limits"));
        Assert.Equal((4096L, 2, 10000L, false), (limits.GetProperty("maxFileSize").GetInt64(), limits.GetProperty("maxPerMessage").GetInt32(),
            limits.GetProperty("userQuota").GetInt64(), limits.GetProperty("scanningEnabled").GetBoolean()));
        Assert.Contains("pdf", limits.GetProperty("allowedExtensions").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Deleting_a_message_removes_its_files_and_leavers_lose_access()
    {
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db);
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        var (carl, carlId) = await SignedInAsync(api, "carl@example.org", "Carl");
        using var _1 = ana;
        using var _2 = ben;
        using var _3 = carl;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        await BecomeContactsAsync(ana, anaId, carl, carlId);
        var group = (await JsonAsync(await ana.PostAsync("/api/v1/conversations/groups", new { title = "Trip", userIds = new[] { benId, carlId } })))
            .GetProperty("id").GetGuid();

        var first = await UploadReadyAsync(ana, group, "plan.pdf", TestFiles.Pdf());
        var second = await UploadReadyAsync(ana, group, "map.png", TestFiles.Png());
        var m1 = (await JsonAsync(await ana.PostAsync($"/api/v1/conversations/{group}/messages", new { clientMessageId = "a", body = "Plan", attachmentIds = new[] { first } })))
            .GetProperty("id").GetGuid();
        AssertStatus(HttpStatusCode.Created, await ana.PostAsync($"/api/v1/conversations/{group}/messages", new { clientMessageId = "b", body = "Map", attachmentIds = new[] { second } }));
        AssertStatus(HttpStatusCode.OK, await carl.GetAsync($"/api/v1/attachments/{second}/content"));

        // Sent files cannot be removed on their own.
        Assert.Equal("attachment_in_use", await ErrorCodeAsync(await ana.SendAsync(HttpMethod.Delete, $"/api/v1/attachments/{first}")));

        // Deleting the message deletes its file: metadata says deleted and the stored bytes are gone.
        AssertStatus(HttpStatusCode.NoContent, await ana.SendAsync(HttpMethod.Delete, $"/api/v1/conversations/{group}/messages/{m1}"));
        Assert.Equal("attachment_not_found", await ErrorCodeAsync(await ben.GetAsync($"/api/v1/attachments/{first}/content")));
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<ChatXDbContext>().Attachments.AsNoTracking().SingleAsync(a => a.Id == first, Ct);
            Assert.Equal((AttachmentStatus.Deleted, true), (row.Status, row.ContentRemoved));
            Assert.False(File.Exists(Path.Combine(api.StorageRoot, row.StorageKey.Replace('/', Path.DirectorySeparatorChar))));
        }

        var tombstone = (await JsonAsync(await ben.GetAsync($"/api/v1/conversations/{group}/history"))).GetProperty("items").EnumerateArray()
            .Single(e => e.GetProperty("id").GetGuid() == m1);
        Assert.Equal(JsonValueKind.Null, tombstone.GetProperty("attachments").ValueKind);

        // Carl leaves: his access to the group's files ends at once.
        AssertStatus(HttpStatusCode.NoContent, await carl.PostAsync($"/api/v1/conversations/{group}/leave"));
        Assert.Equal("attachment_not_found", await ErrorCodeAsync(await carl.GetAsync($"/api/v1/attachments/{second}/content")));
        AssertStatus(HttpStatusCode.OK, await ben.GetAsync($"/api/v1/attachments/{second}/content"));
    }

    [Fact]
    public async Task Cleanup_removes_abandoned_uploads_and_is_idempotent()
    {
        var clock = new AdjustableTime();
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db, new() { ["Attachments:CleanupInterval"] = "01:00:00" }, services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        var direct = await DirectAsync(ana, benId);
        var abandoned = await UploadReadyAsync(ana, direct, "draft.pdf", TestFiles.Pdf());
        var sent = await UploadReadyAsync(ana, direct, "final.pdf", TestFiles.Pdf());
        AssertStatus(HttpStatusCode.Created, await ana.PostAsync($"/api/v1/conversations/{direct}/messages", new { clientMessageId = "f", body = "Final", attachmentIds = new[] { sent } }));

        // A day later the unsent upload goes; the sent one stays.
        clock.Offset = TimeSpan.FromHours(25);
        await using var scope = api.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AttachmentService>();
        var result = await service.CleanUpAsync(100, Ct);
        Assert.Equal((1, 1), (result.Abandoned, result.ContentRemoved));
        Assert.Equal(new AttachmentService.CleanupResult(0, 0, 0), await service.CleanUpAsync(100, Ct));

        var rows = await scope.ServiceProvider.GetRequiredService<ChatXDbContext>().Attachments.AsNoTracking().ToDictionaryAsync(a => a.Id, Ct);
        Assert.Equal((AttachmentStatus.Deleted, true), (rows[abandoned].Status, rows[abandoned].ContentRemoved));
        Assert.Equal(AttachmentStatus.Ready, rows[sent].Status);
        Assert.Single(Directory.GetFiles(api.StorageRoot, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Quarantined_files_stay_unavailable_until_a_clean_scan()
    {
        var scanner = new FakeScanner { Verdict = ScanVerdict.Unavailable };
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db, new() { ["Attachments:CleanupInterval"] = "01:00:00" }, services =>
        {
            services.RemoveAll<IMalwareScanner>();
            services.AddSingleton<IMalwareScanner>(scanner);
        });
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        var direct = await DirectAsync(ana, benId);

        var upload = await ana.UploadAsync($"/api/v1/conversations/{direct}/attachments", "scan-me.pdf", TestFiles.Pdf());
        AssertStatus(HttpStatusCode.Created, upload);
        var id = (await JsonAsync(upload)).GetProperty("id").GetGuid();
        Assert.Equal("quarantined", (await JsonAsync(upload)).GetProperty("status").GetString());
        Assert.Equal("attachment_not_found", await ErrorCodeAsync(await ana.GetAsync($"/api/v1/attachments/{id}/content")));
        var early = await ana.PostAsync($"/api/v1/conversations/{direct}/messages", new { clientMessageId = "q", body = "x", attachmentIds = new[] { id } });
        Assert.Equal("attachment_not_ready", (await JsonAsync(early)).GetProperty("errors").GetProperty("attachmentIds")[0].GetString());

        // The scanner comes back with a clean verdict: the cleanup job releases the file.
        scanner.Verdict = ScanVerdict.Clean;
        await using (var scope = api.Services.CreateAsyncScope())
        {
            Assert.Equal(1, (await scope.ServiceProvider.GetRequiredService<AttachmentService>().CleanUpAsync(100, Ct)).Rescanned);
        }

        Assert.Equal("ready", (await JsonAsync(await ana.GetAsync($"/api/v1/attachments/{id}"))).GetProperty("status").GetString());
        AssertStatus(HttpStatusCode.Created, await ana.PostAsync($"/api/v1/conversations/{direct}/messages", new { clientMessageId = "q2", body = "x", attachmentIds = new[] { id } }));

        // An infected file is rejected and nothing is kept.
        scanner.Verdict = ScanVerdict.Infected;
        var infected = await ana.UploadAsync($"/api/v1/conversations/{direct}/attachments", "eicar.txt", TestFiles.Text("X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR"));
        Assert.Equal("attachment_infected", (await JsonAsync(infected)).GetProperty("errors").GetProperty("file")[0].GetString());
        Assert.Single(Directory.GetFiles(api.StorageRoot, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task S3_compatible_storage_stores_serves_and_deletes_objects()
    {
        await using var s3 = await FakeS3.StartAsync();
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db, new(FakeS3.Settings("chatx")), services =>
            services.AddHttpClient(S3AttachmentStorage.HttpClientName).ConfigurePrimaryHttpMessageHandler(s3.CreateHandler));
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        var direct = await DirectAsync(ana, benId);

        var png = TestFiles.Png(10000);
        var id = await UploadReadyAsync(ana, direct, "photo.png", png);
        var (key, stored) = Assert.Single(s3.Objects);
        Assert.StartsWith("chatx/", key, StringComparison.Ordinal);
        Assert.Equal(png, stored.Data);
        Assert.Equal("image/png", stored.ContentType);
        Assert.False(Directory.Exists(api.StorageRoot));

        var m = (await JsonAsync(await ana.PostAsync($"/api/v1/conversations/{direct}/messages", new { clientMessageId = "s", body = "S3", attachmentIds = new[] { id } })))
            .GetProperty("id").GetGuid();
        var download = await ben.GetAsync($"/api/v1/attachments/{id}/content");
        AssertStatus(HttpStatusCode.OK, download);
        Assert.Equal(png, await download.Content.ReadAsByteArrayAsync(Ct));

        AssertStatus(HttpStatusCode.NoContent, await ana.SendAsync(HttpMethod.Delete, $"/api/v1/conversations/{direct}/messages/{m}"));
        Assert.Empty(s3.Objects);
        Assert.Contains(s3.Requests, r => r.StartsWith("DELETE /chatx-attachments/chatx/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task S3_failures_never_leave_an_available_attachment()
    {
        await using var db = await TestDatabase.CreateAsync("Sqlite");
        await using var api = await StartAsync(db, new(FakeS3.Settings()), services =>
            services.AddHttpClient(S3AttachmentStorage.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new FailingHandler()));
        var (ana, anaId) = await SignedInAsync(api, "ana@example.org", "Ana");
        var (ben, benId) = await SignedInAsync(api, "ben@example.org", "Ben");
        using var _1 = ana;
        using var _2 = ben;
        await BecomeContactsAsync(ana, anaId, ben, benId);
        var direct = await DirectAsync(ana, benId);

        var upload = await ana.UploadAsync($"/api/v1/conversations/{direct}/attachments", "photo.png", TestFiles.Png());
        AssertStatus(HttpStatusCode.ServiceUnavailable, upload);
        Assert.Equal("attachment_storage_unavailable", await ErrorCodeAsync(upload));

        await using var scope = api.Services.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<ChatXDbContext>().Attachments.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(AttachmentStatus.Deleted, row.Status);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static async Task<Guid> DirectAsync(ApiSession session, Guid otherId) =>
        (await JsonAsync(await session.PostAsync("/api/v1/conversations/direct", new { userId = otherId }))).GetProperty("id").GetGuid();

    private static async Task<Guid> UploadReadyAsync(ApiSession session, Guid conversationId, string name, byte[] content)
    {
        var response = await session.UploadAsync($"/api/v1/conversations/{conversationId}/attachments", name, content);
        AssertStatus(HttpStatusCode.Created, response, await response.Content.ReadAsStringAsync(Ct));
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused (test).");
    }
}
