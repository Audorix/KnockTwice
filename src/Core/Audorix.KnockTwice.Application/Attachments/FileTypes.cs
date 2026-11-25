using System.IO.Compression;
using System.Text;
using Audorix.KnockTwice.Application.Common;

namespace Audorix.KnockTwice.Application.Attachments;

/// <summary>How the web app presents a file: images get an inline preview, everything else a download card.</summary>
public enum FileKind
{
    File = 1,
    Image = 2,
}

/// <summary>A file type the installation may accept: extension, canonical MIME type and accepted client-declared aliases.</summary>
public sealed record FileType(string Extension, string ContentType, FileKind Kind, IReadOnlyList<string> DeclaredAliases);

/// <summary>
/// Extension, MIME and content-signature validation (Architecture §9). The detected type comes from
/// the bytes, never from the name or the declared type alone: the extension selects the expected
/// type, the signature must match it, and a declared type must be the same type, an alias or the
/// generic <c>application/octet-stream</c>. Office documents must be ZIP packages with the right
/// main part; text files must be UTF-8 without binary control characters. Active content (HTML,
/// SVG, scripts, executables) has no entry and is therefore never accepted.
/// </summary>
public static class FileTypes
{
    public const string Generic = "application/octet-stream";

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Gif87 = "GIF87a"u8.ToArray();
    private static readonly byte[] Gif89 = "GIF89a"u8.ToArray();
    private static readonly byte[] Riff = "RIFF"u8.ToArray();
    private static readonly byte[] Webp = "WEBP"u8.ToArray();
    private static readonly byte[] Pdf = "%PDF-"u8.ToArray();
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] EmptyZip = [0x50, 0x4B, 0x05, 0x06];

    /// <summary>Every type ChatX knows how to validate. <see cref="AttachmentSettings.AllowedExtensions"/> selects among them.</summary>
    public static IReadOnlyDictionary<string, FileType> Known { get; } = new Dictionary<string, FileType>(StringComparer.Ordinal)
    {
        ["jpg"] = new("jpg", "image/jpeg", FileKind.Image, ["image/jpg", "image/pjpeg"]),
        ["jpeg"] = new("jpeg", "image/jpeg", FileKind.Image, ["image/jpg", "image/pjpeg"]),
        ["png"] = new("png", "image/png", FileKind.Image, []),
        ["gif"] = new("gif", "image/gif", FileKind.Image, []),
        ["webp"] = new("webp", "image/webp", FileKind.Image, []),
        ["pdf"] = new("pdf", "application/pdf", FileKind.File, ["application/x-pdf"]),
        ["docx"] = new("docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", FileKind.File, ["application/zip"]),
        ["xlsx"] = new("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", FileKind.File, ["application/zip"]),
        ["pptx"] = new("pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation", FileKind.File, ["application/zip"]),
        ["txt"] = new("txt", "text/plain", FileKind.File, []),
        ["csv"] = new("csv", "text/csv", FileKind.File, ["text/plain", "application/csv", "application/vnd.ms-excel", "text/comma-separated-values"]),
        ["zip"] = new("zip", "application/zip", FileKind.File, ["application/x-zip-compressed", "application/x-zip", "multipart/x-zip"]),
    };

    /// <summary>Images that may be shown inline (they cannot carry script).</summary>
    public static bool IsInlineImage(string contentType) =>
        contentType is "image/jpeg" or "image/png" or "image/gif" or "image/webp";

    public static FileKind KindOf(string contentType) => IsInlineImage(contentType) ? FileKind.Image : FileKind.File;

    /// <summary>The type expected for an allowed extension; <c>attachment_type_not_allowed</c> otherwise.</summary>
    public static FileType RequireAllowed(string fileName, IReadOnlyCollection<string> allowedExtensions)
    {
        var extension = Domain.Attachments.Attachment.ExtensionOf(fileName);
        if (extension.Length == 0 || !allowedExtensions.Contains(extension) || !Known.TryGetValue(extension, out var type))
        {
            throw new RequestValidationException("file", "attachment_type_not_allowed");
        }

        return type;
    }

    /// <summary>The declared type must be the expected type, an alias, generic or absent.</summary>
    public static void RequireDeclaredMatches(FileType expected, string? declared)
    {
        var value = (declared ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        if (value.Length == 0 || value == Generic || value == expected.ContentType || expected.DeclaredAliases.Contains(value))
        {
            return;
        }

        throw new RequestValidationException("file", "attachment_type_mismatch");
    }

    /// <summary>Checks the content of a seekable stream against the expected type. The position is restored to 0.</summary>
    public static void RequireSignature(FileType expected, Stream content)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(content);
        content.Position = 0;
        var head = new byte[16];
        var read = content.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        var span = head.AsSpan(0, read);
        content.Position = 0;

        var valid = expected.Extension switch
        {
            "jpg" or "jpeg" => span.StartsWith(Jpeg),
            "png" => span.StartsWith(Png),
            "gif" => span.StartsWith(Gif87) || span.StartsWith(Gif89),
            "webp" => span.Length >= 12 && span.StartsWith(Riff) && span[8..12].SequenceEqual(Webp),
            "pdf" => span.StartsWith(Pdf),
            "zip" => (span.StartsWith(Zip) || span.StartsWith(EmptyZip)) && IsZip(content, null),
            "docx" => span.StartsWith(Zip) && IsZip(content, "word/document.xml"),
            "xlsx" => span.StartsWith(Zip) && IsZip(content, "xl/workbook.xml"),
            "pptx" => span.StartsWith(Zip) && IsZip(content, "ppt/presentation.xml"),
            "txt" or "csv" => IsText(content),
            _ => false,
        };

        content.Position = 0;
        if (!valid)
        {
            throw new RequestValidationException("file", "attachment_signature_mismatch");
        }
    }

    private static bool IsZip(Stream content, string? requiredEntry)
    {
        content.Position = 0;
        try
        {
            using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            return requiredEntry is null
                || (archive.GetEntry("[Content_Types].xml") is not null && archive.GetEntry(requiredEntry) is not null);
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>Strict UTF-8 (optional BOM) with no control characters other than tab, line breaks and form feed.</summary>
    private static bool IsText(Stream content)
    {
        content.Position = 0;
        var decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetDecoder();
        var bytes = new byte[64 * 1024];
        var chars = new char[64 * 1024 + 4];
        int read;
        try
        {
            while ((read = content.Read(bytes, 0, bytes.Length)) > 0)
            {
                var count = decoder.GetChars(bytes, 0, read, chars, 0, flush: false);
                for (var i = 0; i < count; i++)
                {
                    var c = chars[i];
                    if (char.IsControl(c) && c is not '\t' and not '\n' and not '\r' and not '\f')
                    {
                        return false;
                    }
                }
            }

            decoder.GetChars([], 0, 0, chars, 0, flush: true);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
