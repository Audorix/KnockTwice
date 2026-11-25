using System.IO.Compression;
using System.Text;

namespace Audorix.KnockTwice.IntegrationTests.Support;

/// <summary>Small files with genuine signatures for upload tests.</summary>
internal static class TestFiles
{
    public static byte[] Png(int size = 256)
    {
        var bytes = new byte[size];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        for (var i = 8; i < size; i++)
        {
            bytes[i] = (byte)(i * 7);
        }

        return bytes;
    }

    public static byte[] Pdf() => Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << >> endobj\ntrailer << >>\n%%EOF\n");

    public static byte[] Text(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>A minimal Office Open XML package with the given main part.</summary>
    public static byte[] OfficePackage(string mainPart)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "[Content_Types].xml", "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
            Write(zip, mainPart, "<?xml version=\"1.0\"?><document/>");
        }

        return buffer.ToArray();
    }

    public static byte[] Zip()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "notes.txt", "hello");
        }

        return buffer.ToArray();
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(content);
    }
}
