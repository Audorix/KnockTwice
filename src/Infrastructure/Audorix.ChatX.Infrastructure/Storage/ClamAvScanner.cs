using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Audorix.ChatX.Application.Attachments;
using Microsoft.Extensions.Options;

namespace Audorix.ChatX.Infrastructure.Storage;

/// <summary>
/// Scans content with a clamd daemon using the <c>zINSTREAM</c> command over TCP. Any failure
/// (unreachable, timeout, size limit, unexpected answer) is <see cref="ScanVerdict.Unavailable"/>,
/// which keeps the file quarantined: nothing becomes available without a clean verdict.
/// </summary>
public sealed class ClamAvScanner(IOptions<ScanningOptions> options) : IMalwareScanner
{
    private const int ChunkSize = 64 * 1024;

    public bool Enabled => true;

    public async Task<ScanVerdict> ScanAsync(Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var settings = options.Value.ClamAv;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.Timeout);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(settings.Host, settings.Port, timeout.Token).ConfigureAwait(false);
            var stream = client.GetStream();
            await stream.WriteAsync("zINSTREAM\0"u8.ToArray(), timeout.Token).ConfigureAwait(false);

            var buffer = new byte[ChunkSize];
            var length = new byte[4];
            int read;
            while ((read = await content.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                BinaryPrimitives.WriteUInt32BigEndian(length, (uint)read);
                await stream.WriteAsync(length, timeout.Token).ConfigureAwait(false);
                await stream.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
            }

            BinaryPrimitives.WriteUInt32BigEndian(length, 0);
            await stream.WriteAsync(length, timeout.Token).ConfigureAwait(false);
            await stream.FlushAsync(timeout.Token).ConfigureAwait(false);

            var reply = await ReadReplyAsync(stream, timeout.Token).ConfigureAwait(false);
            return Interpret(reply);
        }
        catch (Exception ex) when (ex is SocketException or IOException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return ScanVerdict.Unavailable;
        }
    }

    /// <summary><c>stream: OK</c> is clean, <c>stream: NAME FOUND</c> infected, anything else (for example <c>ERROR</c>) unavailable.</summary>
    internal static ScanVerdict Interpret(string reply)
    {
        var text = reply.TrimEnd('\0', '\n', '\r', ' ');
        if (text.EndsWith(": OK", StringComparison.Ordinal))
        {
            return ScanVerdict.Clean;
        }

        return text.EndsWith(" FOUND", StringComparison.Ordinal) ? ScanVerdict.Infected : ScanVerdict.Unavailable;
    }

    private static async Task<string> ReadReplyAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var reply = new MemoryStream();
        var buffer = new byte[512];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            reply.Write(buffer, 0, read);
            if (buffer[read - 1] == 0 || reply.Length > 4096)
            {
                break;
            }
        }

        return Encoding.ASCII.GetString(reply.ToArray());
    }
}
