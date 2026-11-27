using System.IO.Compression;
using System.Text;
using Audorix.KnockTwice.Application.Attachments;
using Audorix.KnockTwice.Application.Common;
using Audorix.KnockTwice.Application.PrivateChats;
using Audorix.KnockTwice.Domain;
using Audorix.KnockTwice.Domain.Attachments;
using Audorix.KnockTwice.Infrastructure.Storage;

namespace Audorix.KnockTwice.UnitTests;

public sealed class AttachmentRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("photo.png", "photo.png")]
    [InlineData("C:\\Users\\ana\\Desktop\\plan.pdf", "plan.pdf")]
    [InlineData("../../etc/passwd.txt", "passwd.txt")]
    [InlineData("a\u0000b<c>.txt", "a_b_c_.txt")]
    [InlineData("  report .docx ", "report .docx")]
    [InlineData("\u202Egnp.exe", "_gnp.exe")]
    public void File_names_are_reduced_to_a_safe_last_segment(string input, string expected) =>
        Assert.Equal(expected, Attachment.NormalizeFileName(input));

    [Fact]
    public void Long_file_names_keep_their_extension()
    {
        var name = Attachment.NormalizeFileName(new string('a', 300) + ".pdf");
        Assert.Equal(Attachment.FileNameMaxLength, name.Length);
        Assert.EndsWith(".pdf", name, StringComparison.Ordinal);
        Assert.Equal("attachment_name_invalid", Assert.Throws<DomainRuleException>(() => Attachment.NormalizeFileName("../")).Code);
    }

    [Fact]
    public void Lifecycle_allows_linking_only_ready_files_of_the_same_sender_and_conversation()
    {
        var conversation = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var attachment = Attachment.Begin(Guid.NewGuid(), conversation, owner, "2026/11/abc", "a.png", Now);
        Assert.Equal(AttachmentStatus.PendingUpload, attachment.Status);
        Assert.Throws<DomainRuleException>(() => attachment.Link(conversation, owner, Guid.NewGuid()));

        attachment.Stored(10, "image/png", "hash");
        Assert.Equal(AttachmentStatus.Quarantined, attachment.Status);
        Assert.Throws<DomainRuleException>(() => attachment.Link(conversation, owner, Guid.NewGuid()));

        attachment.MarkReady(Now);
        Assert.Throws<DomainRuleException>(() => attachment.Link(Guid.NewGuid(), owner, Guid.NewGuid()));
        Assert.Throws<DomainRuleException>(() => attachment.Link(conversation, Guid.NewGuid(), Guid.NewGuid()));
        attachment.Link(conversation, owner, Guid.NewGuid());
        Assert.Throws<DomainRuleException>(() => attachment.Link(conversation, owner, Guid.NewGuid()));

        attachment.Delete(Now);
        attachment.Delete(Now.AddMinutes(1));
        Assert.Equal((AttachmentStatus.Deleted, Now), (attachment.Status, attachment.DeletedAtUtc!.Value));
    }

    [Fact]
    public void Attachment_records_the_provider_that_owns_its_bytes()
    {
        var attachment = Attachment.Begin(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "2026/11/abc", "a.png", Now, " AzureBlob ");
        Assert.Equal("AzureBlob", attachment.StorageProvider);

        var legacy = Attachment.Begin(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "2026/11/old", "old.png", Now);
        Assert.Null(legacy.StorageProvider);
        Assert.Throws<DomainRuleException>(() => Attachment.Begin(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "2026/11/abc", "a.png", Now, new string('x', 33)));
    }

    [Theory]
    [InlineData("a.png", "image/png", "image/png")]
    [InlineData("a.png", "", "image/png")]
    [InlineData("a.png", "application/octet-stream", "image/png")]
    [InlineData("a.jpg", "image/jpg", "image/jpeg")]
    [InlineData("a.csv", "application/vnd.ms-excel", "text/csv")]
    public void Declared_types_must_match_or_be_generic(string name, string declared, string expected)
    {
        var type = FileTypes.RequireAllowed(name, AttachmentSettings.DefaultExtensions);
        FileTypes.RequireDeclaredMatches(type, declared);
        Assert.Equal(expected, type.ContentType);
    }

    [Theory]
    [InlineData("a.png", "text/html")]
    [InlineData("a.pdf", "image/png")]
    public void Mismatching_declared_types_are_rejected(string name, string declared)
    {
        var type = FileTypes.RequireAllowed(name, AttachmentSettings.DefaultExtensions);
        var error = Assert.Throws<RequestValidationException>(() => FileTypes.RequireDeclaredMatches(type, declared));
        Assert.Equal("attachment_type_mismatch", error.Errors["file"][0]);
    }

    [Theory]
    [InlineData("a.exe")]
    [InlineData("a.svg")]
    [InlineData("a.html")]
    [InlineData("a.js")]
    [InlineData("noextension")]
    public void Active_and_unknown_types_are_never_allowed(string name)
    {
        var error = Assert.Throws<RequestValidationException>(() => FileTypes.RequireAllowed(name, [.. FileTypes.Known.Keys]));
        Assert.Equal("attachment_type_not_allowed", error.Errors["file"][0]);
    }

    [Fact]
    public void The_allow_list_can_be_narrowed()
    {
        Assert.Throws<RequestValidationException>(() => FileTypes.RequireAllowed("a.zip", ["png", "pdf"]));
        Assert.Contains("Attachments:AllowedExtensions", new AttachmentSettings { AllowedExtensions = ["png", "exe"] }.Validate().Single(), StringComparison.Ordinal);
        Assert.Empty(new AttachmentSettings().Validate());
    }

    [Theory]
    [MemberData(nameof(Signatures))]
    public void Content_must_match_the_signature_of_its_type(string name, byte[] content, bool valid)
    {
        var type = FileTypes.RequireAllowed(name, AttachmentSettings.DefaultExtensions);
        using var stream = new MemoryStream(content);
        if (valid)
        {
            FileTypes.RequireSignature(type, stream);
            Assert.Equal(0, stream.Position);
        }
        else
        {
            Assert.Equal("attachment_signature_mismatch",
                Assert.Throws<RequestValidationException>(() => FileTypes.RequireSignature(type, stream)).Errors["file"][0]);
        }
    }

    public static TheoryData<string, byte[], bool> Signatures => new()
    {
        { "a.jpg", [0xFF, 0xD8, 0xFF, 0xE0, 0, 0], true },
        { "a.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1], true },
        { "a.gif", Encoding.ASCII.GetBytes("GIF89a......"), true },
        { "a.webp", Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 "), true },
        { "a.pdf", Encoding.ASCII.GetBytes("%PDF-1.4"), true },
        { "a.txt", Encoding.UTF8.GetBytes("Grüße\r\n\ttab"), true },
        { "a.zip", Zip(("x.txt", "x")), true },
        { "a.docx", Zip(("[Content_Types].xml", "<Types/>"), ("word/document.xml", "<d/>")), true },
        { "a.png", Encoding.ASCII.GetBytes("%PDF-1.4"), false },
        { "a.jpg", [0x89, 0x50, 0x4E, 0x47], false },
        { "a.webp", Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVEfmt "), false },
        { "a.txt", [0x48, 0x00, 0x49], false },
        { "a.txt", [0xC3, 0x28], false },
        { "a.docx", Zip(("[Content_Types].xml", "<Types/>"), ("xl/workbook.xml", "<w/>")), false },
        { "a.xlsx", Encoding.ASCII.GetBytes("PK\u0003\u0004 not really a zip"), false },
    };

    [Fact]
    public void Storage_keys_are_random_and_never_derived_from_names()
    {
        var a = AttachmentService.NewStorageKey(Now);
        var b = AttachmentService.NewStorageKey(Now);
        Assert.Matches("^2026/11/[0-9a-f]{32}$", a);
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("2026/../../x")]
    [InlineData("/abs")]
    [InlineData("a\\b")]
    [InlineData("C:/x")]
    [InlineData("")]
    [InlineData("UPPER/case")]
    public void Local_storage_refuses_keys_that_could_leave_its_root(string key)
    {
        var storage = new LocalAttachmentStorage(Path.Combine(Path.GetTempPath(), "chatx-unit-root"));
        Assert.Throws<ArgumentException>(() => storage.PathFor(key));
    }

    [Fact]
    public async Task Local_storage_writes_reads_and_deletes_idempotently()
    {
        var root = Path.Combine(Path.GetTempPath(), "chatx-unit", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new LocalAttachmentStorage(root);
            var key = AttachmentService.NewStorageKey(Now);
            await storage.PutAsync(key, new MemoryStream([1, 2, 3]), 3, "image/png", TestContext.Current.CancellationToken);
            await using (var read = await storage.OpenReadAsync(key, TestContext.Current.CancellationToken))
            {
                using var copy = new MemoryStream();
                await read!.CopyToAsync(copy, TestContext.Current.CancellationToken);
                Assert.Equal([1, 2, 3], copy.ToArray());
            }

            Assert.StartsWith(Path.GetFullPath(root), storage.PathFor(key), StringComparison.Ordinal);
            await storage.DeleteAsync(key, TestContext.Current.CancellationToken);
            await storage.DeleteAsync(key, TestContext.Current.CancellationToken);
            Assert.Null(await storage.OpenReadAsync(key, TestContext.Current.CancellationToken));
            Assert.Empty(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>The "GET Object" example of the AWS Signature Version 4 documentation (S3 API reference).</summary>
    [Fact]
    public void S3_signatures_match_the_published_aws_example()
    {
        var uri = new Uri("https://examplebucket.s3.amazonaws.com/test.txt");
        var headers = new Dictionary<string, string>
        {
            ["host"] = "examplebucket.s3.amazonaws.com",
            ["range"] = "bytes=0-9",
            ["x-amz-content-sha256"] = S3Signer.EmptyPayloadHash,
            ["x-amz-date"] = "20130524T000000Z",
        };
        var authorization = S3Signer.Authorization("GET", uri, headers, S3Signer.EmptyPayloadHash,
            "AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", "us-east-1", new DateTimeOffset(2013, 5, 24, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(
            "AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20130524/us-east-1/s3/aws4_request,SignedHeaders=host;range;x-amz-content-sha256;x-amz-date,Signature=f0e8bdb87c964420e857bd35b5d6ed310bd44f0170aba48dd91039c6036bdb41",
            authorization);
    }

    [Fact]
    public void S3_object_urls_support_path_and_virtual_hosted_style()
    {
        var options = new StorageOptions { Provider = "S3", S3 = { Endpoint = "http://minio:9000", Bucket = "chatx", Prefix = "/files/" } };
        var storage = new S3AttachmentStorage(new HttpClient(), Microsoft.Extensions.Options.Options.Create(options), TimeProvider.System);
        Assert.Equal("http://minio:9000/chatx/files/2026/11/abc", storage.ObjectUri("2026/11/abc").AbsoluteUri);
        options.S3.ForcePathStyle = false;
        options.S3.Endpoint = "https://s3.eu-west-1.amazonaws.com";
        options.S3.Prefix = string.Empty;
        Assert.Equal("https://chatx.s3.eu-west-1.amazonaws.com/2026/11/abc", storage.ObjectUri("2026/11/abc").AbsoluteUri);
        Assert.Throws<ArgumentException>(() => storage.ObjectUri("../x"));
    }

    [Fact]
    public void Storage_and_scanning_settings_are_validated()
    {
        var validator = new StorageOptionsValidator();
        Assert.True(validator.Validate(null, new StorageOptions()).Succeeded);
        Assert.False(validator.Validate(null, new StorageOptions { Provider = "Ftp" }).Succeeded);
        Assert.False(validator.Validate(null, new StorageOptions { Provider = "S3" }).Succeeded);
        Assert.True(validator.Validate(null, new StorageOptions
        {
            Provider = "s3",
            S3 = { Endpoint = "https://s3.test", Bucket = "chatx-files", AccessKeyId = "a", SecretAccessKey = "b" },
        }).Succeeded);
        Assert.False(validator.Validate(null, new StorageOptions
        {
            Provider = "S3",
            S3 = { Endpoint = "https://s3.test", Bucket = "Bad_Bucket", AccessKeyId = "a", SecretAccessKey = "b" },
        }).Succeeded);
        Assert.True(validator.Validate(null, new StorageOptions
        {
            Provider = "AzureBlob",
            AzureBlob = { ConnectionString = "UseDevelopmentStorage=true", Container = "chatx-files", Prefix = "attachments/" },
        }).Succeeded);
        Assert.False(validator.Validate(null, new StorageOptions
        {
            Provider = "AzureBlob",
            AzureBlob = { ConnectionString = "UseDevelopmentStorage=true", Container = "Bad--Container" },
        }).Succeeded);

        var scanning = new ScanningOptionsValidator();
        Assert.True(scanning.Validate(null, new ScanningOptions()).Succeeded);
        Assert.True(scanning.Validate(null, new ScanningOptions { Provider = "ClamAv" }).Succeeded);
        Assert.False(scanning.Validate(null, new ScanningOptions { Provider = "Other" }).Succeeded);
        Assert.False(scanning.Validate(null, new ScanningOptions { Provider = "ClamAv", ClamAv = { Port = 0 } }).Succeeded);
    }

    [Theory]
    [InlineData("stream: OK\0", ScanVerdict.Clean)]
    [InlineData("stream: Win.Test.EICAR_HDB-1 FOUND\0", ScanVerdict.Infected)]
    [InlineData("INSTREAM size limit exceeded. ERROR\0", ScanVerdict.Unavailable)]
    [InlineData("", ScanVerdict.Unavailable)]
    public void ClamAv_replies_are_interpreted_conservatively(string reply, ScanVerdict expected) =>
        Assert.Equal(expected, ClamAvScanner.Interpret(reply));

    [Fact]
    public async Task An_unreachable_scanner_means_unavailable_never_clean()
    {
        var scanner = new ClamAvScanner(Microsoft.Extensions.Options.Options.Create(new ScanningOptions
        {
            Provider = "ClamAv",
            ClamAv = { Host = "127.0.0.1", Port = 1, Timeout = TimeSpan.FromSeconds(2) },
        }));
        Assert.Equal(ScanVerdict.Unavailable, await scanner.ScanAsync(new MemoryStream([1, 2, 3]), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Private_chat_settings_are_validated()
    {
        Assert.Empty(new PrivateChatSettings().Validate());
        Assert.NotEmpty(new PrivateChatSettings { MaxBytesPerChat = 1024, MaxFileSize = 4096 }.Validate());
        Assert.NotEmpty(new PrivateChatSettings { InvitationTimeout = TimeSpan.FromSeconds(1) }.Validate());
    }

    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }
}
