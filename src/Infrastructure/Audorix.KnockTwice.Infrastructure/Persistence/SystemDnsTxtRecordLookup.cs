using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Audorix.KnockTwice.Application.Organizations;

namespace Audorix.KnockTwice.Infrastructure.Persistence;

/// <summary>TXT lookup using the DNS resolvers configured on the host; domain names are not sent to a third-party resolver.</summary>
internal sealed class SystemDnsTxtRecordLookup : IDomainTxtRecordLookup
{
    public async Task<IReadOnlyList<string>> LookupAsync(string domain, CancellationToken cancellationToken)
    {
        var servers = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().DnsAddresses)
            .Distinct()
            .ToArray();
        foreach (var server in servers)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                using var client = new UdpClient(server.AddressFamily);
                client.Connect(server, 53);
                var request = MakeQuery(domain);
                await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
                var response = await client.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                var records = ReadResponse(response.Buffer, BinaryPrimitives.ReadUInt16BigEndian(request), domain);
                if (records is not null) return records;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            catch (SocketException) { }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Array.Empty<string>();
    }

    private static byte[] MakeQuery(string domain)
    {
        var id = (ushort)Random.Shared.Next(ushort.MaxValue + 1);
        using var stream = new MemoryStream();
        Span<byte> header = stackalloc byte[12];
        header.Clear();
        BinaryPrimitives.WriteUInt16BigEndian(header, id);
        BinaryPrimitives.WriteUInt16BigEndian(header[2..], 0x0100);
        BinaryPrimitives.WriteUInt16BigEndian(header[4..], 1);
        BinaryPrimitives.WriteUInt16BigEndian(header[10..], 1); // EDNS0 additional record
        stream.Write(header);
        foreach (var label in domain.Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }
        stream.WriteByte(0);
        Span<byte> questionType = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(questionType, 16); // TXT
        BinaryPrimitives.WriteUInt16BigEndian(questionType[2..], 1); // IN
        stream.Write(questionType);
        stream.WriteByte(0); // root owner for OPT
        Span<byte> opt = stackalloc byte[10];
        opt.Clear();
        BinaryPrimitives.WriteUInt16BigEndian(opt, 41);
        BinaryPrimitives.WriteUInt16BigEndian(opt[2..], 1232);
        stream.Write(opt);
        return stream.ToArray();
    }

    private static List<string>? ReadResponse(byte[] packet, ushort expectedId, string requestedDomain)
    {
        if (packet.Length < 12 || BinaryPrimitives.ReadUInt16BigEndian(packet) != expectedId) return null;
        var flags = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(2));
        if ((flags & 0x8000) == 0 || (flags & 0x0200) != 0 || (flags & 0x000f) != 0) return null;
        var questions = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(4));
        var answers = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(6));
        var offset = 12;
        for (var i = 0; i < questions; i++)
        {
            if (ReadName(packet, ref offset) is null || offset + 4 > packet.Length) return null;
            offset += 4;
        }

        var result = new List<string>();
        for (var i = 0; i < answers; i++)
        {
            var ownerName = ReadName(packet, ref offset);
            if (ownerName is null || offset + 10 > packet.Length) return null;
            var type = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(offset));
            var @class = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(offset + 2));
            var length = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(offset + 8));
            offset += 10;
            if (offset + length > packet.Length) return null;
            if (type == 16 && @class == 1 && string.Equals(ownerName, requestedDomain, StringComparison.OrdinalIgnoreCase))
            {
                var end = offset + length;
                var value = new StringBuilder();
                while (offset < end)
                {
                    var partLength = packet[offset++];
                    if (offset + partLength > end) return null;
                    value.Append(Encoding.UTF8.GetString(packet, offset, partLength));
                    offset += partLength;
                }
                result.Add(value.ToString());
            }
            else offset += length;
        }
        return result;
    }

    private static string? ReadName(byte[] packet, ref int offset)
    {
        var position = offset;
        var jumped = false;
        var hops = 0;
        var labels = new List<string>();
        while (position < packet.Length && hops++ < 128)
        {
            var length = packet[position++];
            if (length == 0)
            {
                if (!jumped) offset = position;
                return string.Join('.', labels);
            }
            if ((length & 0xc0) == 0xc0)
            {
                if (position >= packet.Length) return null;
                var pointer = ((length & 0x3f) << 8) | packet[position++];
                if (pointer >= packet.Length) return null;
                if (!jumped) offset = position;
                position = pointer;
                jumped = true;
                continue;
            }
            if ((length & 0xc0) != 0 || position + length > packet.Length) return null;
            labels.Add(Encoding.ASCII.GetString(packet, position, length));
            position += length;
        }
        return null;
    }
}
