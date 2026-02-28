using System.IO.Hashing;
using System.Text;
using System.Text.Json;
using meshIt.Models;

namespace meshIt.Helpers;

/// <summary>
/// Serializes and deserializes <see cref="Packet"/> objects (v1 and v2) to/from byte arrays.
/// </summary>
public static class PacketBuilder
{
    /// <summary>v1 header: Version(1)+Type(1)+Seq(4)+SenderId(16)+CRC(4) = 26</summary>
    private const int V1HeaderSize = 26;
    /// <summary>v2 header: v1(26)+OriginPub(32)+DestPub(32)+Hop(1)+Flags(1)+TTL(1) = 93</summary>
    private const int V2HeaderSize = 93;

    // ---- Serialize ----

    public static byte[] Serialize(Packet packet)
    {
        return packet.Version >= 0x02 ? SerializeV2(packet) : SerializeV1(packet);
    }

    private static byte[] SerializeV1(Packet packet)
    {
        var payloadLen = packet.Payload?.Length ?? 0;
        var buffer = new byte[V1HeaderSize + payloadLen];
        var offset = 0;

        buffer[offset++] = packet.Version;
        buffer[offset++] = (byte)packet.Type;
        WriteBigEndianUInt32(buffer, ref offset, packet.SequenceNumber);
        WriteBytes(buffer, ref offset, packet.SenderId, 16);

        if (payloadLen > 0)
            WriteBytes(buffer, ref offset, packet.Payload!, payloadLen);

        var crc = ComputeCrc32(buffer.AsSpan(0, offset));
        Array.Copy(crc, 0, buffer, offset, 4);
        return buffer;
    }

    private static byte[] SerializeV2(Packet packet)
    {
        // Encode routing metadata as JSON into payload if present
        var payload = EncodePayloadWithMetadata(packet);
        var payloadLen = payload.Length;
        var buffer = new byte[V2HeaderSize + payloadLen];
        var offset = 0;

        buffer[offset++] = packet.Version;
        buffer[offset++] = (byte)packet.Type;
        WriteBigEndianUInt32(buffer, ref offset, packet.SequenceNumber);
        WriteBytes(buffer, ref offset, packet.SenderId, 16);

        // v2 routing fields
        WriteBytes(buffer, ref offset, packet.OriginatorPublicKey, 32);
        WriteBytes(buffer, ref offset, packet.DestinationPublicKey, 32);
        buffer[offset++] = packet.HopCount;
        buffer[offset++] = (byte)(packet.IsCompressed ? 1 : 0);
        buffer[offset++] = packet.TTL;

        if (payloadLen > 0)
            WriteBytes(buffer, ref offset, payload, payloadLen);

        var crc = ComputeCrc32(buffer.AsSpan(0, offset));
        Array.Copy(crc, 0, buffer, offset, 4);
        return buffer;
    }

    // ---- Deserialize ----

    public static Packet? Deserialize(byte[] buffer)
    {
        if (buffer.Length < V1HeaderSize) return null;

        var version = buffer[0];
        return version >= 0x02 ? DeserializeV2(buffer) : DeserializeV1(buffer);
    }

    private static Packet? DeserializeV1(byte[] buffer)
    {
        if (buffer.Length < V1HeaderSize) return null;
        var packet = new Packet();
        var offset = 0;

        packet.Version = buffer[offset++];
        packet.Type = (PacketType)buffer[offset++];
        packet.SequenceNumber = ReadBigEndianUInt32(buffer, ref offset);
        packet.SenderId = ReadBytes(buffer, ref offset, 16);

        var payloadLen = buffer.Length - V1HeaderSize;
        packet.Payload = payloadLen > 0 ? ReadBytes(buffer, ref offset, payloadLen) : Array.Empty<byte>();

        packet.Checksum = ReadBytes(buffer, ref offset, 4);
        var expected = ComputeCrc32(buffer.AsSpan(0, offset - 4));
        if (!expected.AsSpan().SequenceEqual(packet.Checksum)) return null;

        return packet;
    }

    private static Packet? DeserializeV2(byte[] buffer)
    {
        if (buffer.Length < V2HeaderSize) return null;
        var packet = new Packet();
        var offset = 0;

        packet.Version = buffer[offset++];
        packet.Type = (PacketType)buffer[offset++];
        packet.SequenceNumber = ReadBigEndianUInt32(buffer, ref offset);
        packet.SenderId = ReadBytes(buffer, ref offset, 16);
        packet.OriginatorPublicKey = ReadBytes(buffer, ref offset, 32);
        packet.DestinationPublicKey = ReadBytes(buffer, ref offset, 32);
        packet.HopCount = buffer[offset++];
        packet.IsCompressed = buffer[offset++] != 0;
        packet.TTL = buffer[offset++];

        var payloadLen = buffer.Length - V2HeaderSize;
        packet.Payload = payloadLen > 0 ? ReadBytes(buffer, ref offset, payloadLen) : Array.Empty<byte>();

        packet.Checksum = ReadBytes(buffer, ref offset, 4);
        var expected = ComputeCrc32(buffer.AsSpan(0, offset - 4));
        if (!expected.AsSpan().SequenceEqual(packet.Checksum)) return null;

        // Decode routing metadata from payload
        DecodePayloadMetadata(packet);

        return packet;
    }

    // ---- Factory methods ----

    public static Packet CreateTextMessage(Guid senderId, string text, uint seq)
    {
        return new Packet
        {
            Type = PacketType.TextMessage,
            SequenceNumber = seq,
            SenderId = senderId.ToByteArray(),
            Payload = Encoding.UTF8.GetBytes(text)
        };
    }

    public static Packet CreateFileMetadata(Guid senderId, Guid fileId, string fileName, long fileSize, int totalChunks, uint seq)
    {
        var nameBytes = Encoding.UTF8.GetBytes(fileName);
        var payload = new byte[16 + 8 + 4 + nameBytes.Length];
        var off = 0;
        Array.Copy(fileId.ToByteArray(), 0, payload, off, 16); off += 16;
        BitConverter.GetBytes(fileSize).CopyTo(payload, off); off += 8;
        BitConverter.GetBytes(totalChunks).CopyTo(payload, off); off += 4;
        Array.Copy(nameBytes, 0, payload, off, nameBytes.Length);

        return new Packet { Type = PacketType.FileMetadata, SequenceNumber = seq, SenderId = senderId.ToByteArray(), Payload = payload };
    }

    public static Packet CreateFileChunk(Guid senderId, Guid fileId, byte[] chunkData, uint seq)
    {
        var payload = new byte[16 + chunkData.Length];
        Array.Copy(fileId.ToByteArray(), 0, payload, 0, 16);
        Array.Copy(chunkData, 0, payload, 16, chunkData.Length);
        return new Packet { Type = PacketType.FileChunk, SequenceNumber = seq, SenderId = senderId.ToByteArray(), Payload = payload };
    }

    public static Packet CreateAck(Guid senderId, uint ackedSeq, uint seq)
    {
        return new Packet { Type = PacketType.Ack, SequenceNumber = seq, SenderId = senderId.ToByteArray(), Payload = BitConverter.GetBytes(ackedSeq) };
    }

    // ---- Noise handshake packet helpers ----

    public static Packet CreateNoiseHandshakePacket(PacketType type, byte[] senderPubKey, byte[] payload, uint seq)
    {
        return new Packet
        {
            Version = 0x02,
            Type = type,
            SequenceNumber = seq,
            SenderId = senderPubKey.Length >= 16 ? senderPubKey[..16] : senderPubKey,
            OriginatorPublicKey = senderPubKey,
            Payload = payload
        };
    }

    /// <summary>Create a mesh routing or channel packet with JSON-encoded metadata.</summary>
    public static Packet CreateMeshPacket(PacketType type, byte[] originPubKey, byte[] destPubKey,
        byte ttl, string? channelName, List<string>? routeHistory, byte[] payload, uint seq)
    {
        return new Packet
        {
            Version = 0x02,
            Type = type,
            SequenceNumber = seq,
            SenderId = originPubKey.Length >= 16 ? originPubKey[..16] : originPubKey,
            OriginatorPublicKey = originPubKey,
            DestinationPublicKey = destPubKey,
            TTL = ttl,
            ChannelName = channelName,
            RouteHistory = routeHistory ?? new List<string>(),
            HopCount = 0,
            Payload = payload
        };
    }

    // ---- Metadata encoding helpers ----

    /// <summary>Encode RouteHistory + ChannelName as a JSON prefix on the payload for mesh packets.</summary>
    private static byte[] EncodePayloadWithMetadata(Packet packet)
    {
        var needsMeta = packet.RouteHistory.Count > 0 || packet.ChannelName != null;
        if (!needsMeta)
            return packet.Payload ?? Array.Empty<byte>();

        var meta = new PacketMetadata
        {
            RouteHistory = packet.RouteHistory,
            ChannelName = packet.ChannelName
        };
        var metaJson = JsonSerializer.SerializeToUtf8Bytes(meta);

        // Format: [4 bytes metaLen][metaJson][originalPayload]
        var origPayload = packet.Payload ?? Array.Empty<byte>();
        var result = new byte[4 + metaJson.Length + origPayload.Length];
        BitConverter.GetBytes(metaJson.Length).CopyTo(result, 0);
        metaJson.CopyTo(result, 4);
        origPayload.CopyTo(result, 4 + metaJson.Length);
        return result;
    }

    /// <summary>Decode RouteHistory + ChannelName from payload if present.</summary>
    private static void DecodePayloadMetadata(Packet packet)
    {
        if (packet.Payload.Length < 4) return;

        // Check if payload starts with a metadata length prefix
        var metaLen = BitConverter.ToInt32(packet.Payload, 0);
        if (metaLen <= 0 || metaLen > packet.Payload.Length - 4) return;

        try
        {
            var metaJson = packet.Payload.AsSpan(4, metaLen);
            var meta = JsonSerializer.Deserialize<PacketMetadata>(metaJson);
            if (meta != null)
            {
                packet.RouteHistory = meta.RouteHistory ?? new List<string>();
                packet.ChannelName = meta.ChannelName;
                // Strip metadata from payload, leaving only the original content
                packet.Payload = packet.Payload.AsSpan(4 + metaLen).ToArray();
            }
        }
        catch
        {
            // Not metadata — treat entire payload as raw data
        }
    }

    private class PacketMetadata
    {
        public List<string>? RouteHistory { get; set; }
        public string? ChannelName { get; set; }
    }

    // ---- Private helpers ----

    private static void WriteBigEndianUInt32(byte[] buf, ref int offset, uint val)
    {
        buf[offset++] = (byte)(val >> 24);
        buf[offset++] = (byte)(val >> 16);
        buf[offset++] = (byte)(val >> 8);
        buf[offset++] = (byte)val;
    }

    private static uint ReadBigEndianUInt32(byte[] buf, ref int offset)
    {
        uint val = (uint)((buf[offset] << 24) | (buf[offset + 1] << 16) | (buf[offset + 2] << 8) | buf[offset + 3]);
        offset += 4;
        return val;
    }

    private static void WriteBytes(byte[] buf, ref int offset, byte[] src, int len)
    {
        var actualLen = Math.Min(src.Length, len);
        Array.Copy(src, 0, buf, offset, actualLen);
        offset += len; // advance by full field length even if src is shorter
    }

    private static byte[] ReadBytes(byte[] buf, ref int offset, int len)
    {
        var result = new byte[len];
        Array.Copy(buf, offset, result, 0, len);
        offset += len;
        return result;
    }

    private static byte[] ComputeCrc32(ReadOnlySpan<byte> data)
    {
        var crc = new Crc32();
        crc.Append(data);
        return crc.GetCurrentHash();
    }
}
