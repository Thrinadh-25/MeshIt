namespace meshIt.Models;

/// <summary>
/// Type of data carried by a <see cref="Packet"/>.
/// </summary>
public enum PacketType : byte
{
    // Phase 1
    TextMessage  = 0x01,
    FileMetadata = 0x02,
    FileChunk    = 0x03,
    Ack          = 0x04,

    // Phase 2 — Noise handshake
    NoiseHandshakeMsg1 = 0x10,
    NoiseHandshakeMsg2 = 0x11,
    NoiseHandshakeMsg3 = 0x12,

    // Phase 2 — Mesh routing
    RoutedMessage  = 0x20,
    ChannelMessage = 0x21,
    ChannelJoin    = 0x22,
    ChannelLeave   = 0x23,
    RouteDiscovery = 0x24,
    RouteReply     = 0x25,
    ChannelAnnounce = 0x26,

    // Phase 2 — Presence
    PeerAnnouncement = 0x30
}

/// <summary>
/// Binary packet structure for all BLE communication (v2).
///
/// v1 wire format (26 + N bytes):
///   [1  Version] [1 Type] [4 SeqNum] [16 SenderId] [N Payload] [4 CRC32]
///
/// v2 wire format (additional routing fields):
///   [1  Version=0x02] [1 Type] [4 SeqNum] [16 SenderId]
///   [32 OriginatorPubKey] [32 DestinationPubKey]
///   [1  HopCount] [1 Flags (bit0=isCompressed)]
///   [N  Payload]
///   [4  CRC32]
///
/// v2 header size: 1+1+4+16+32+32+1+1+4 = 92 bytes
/// </summary>
public class Packet
{
    /// <summary>Protocol version. 0x01 = Phase 1, 0x02 = Phase 2.</summary>
    public byte Version { get; set; } = 0x02;

    /// <summary>Determines how the payload should be interpreted.</summary>
    public PacketType Type { get; set; }

    /// <summary>Monotonically increasing sequence number per sender.</summary>
    public uint SequenceNumber { get; set; }

    /// <summary>GUID of the sender (16 bytes). Used for Phase 1 compat.</summary>
    public byte[] SenderId { get; set; } = new byte[16];

    // ---- Phase 2 routing fields ----

    /// <summary>Noise static public key of the originator (32 bytes). Zero for v1.</summary>
    public byte[] OriginatorPublicKey { get; set; } = new byte[32];

    /// <summary>Noise static public key of the destination (32 bytes). Zero = broadcast.</summary>
    public byte[] DestinationPublicKey { get; set; } = new byte[32];

    /// <summary>Current hop count for mesh routing.</summary>
    public byte HopCount { get; set; }

    /// <summary>TTL — hops remaining before the packet is dropped. Max 7.</summary>
    public byte TTL { get; set; } = 7;

    /// <summary>Whether the payload is LZ4-compressed.</summary>
    public bool IsCompressed { get; set; }

    /// <summary>Channel name for channel messages (null = direct/broadcast).</summary>
    public string? ChannelName { get; set; }

    /// <summary>Fingerprints of nodes that have relayed this packet (loop prevention).</summary>
    public List<string> RouteHistory { get; set; } = new();

    // ---- Payload + integrity ----

    /// <summary>Variable-length payload.</summary>
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    /// <summary>CRC32 checksum.</summary>
    public byte[] Checksum { get; set; } = new byte[4];
}
