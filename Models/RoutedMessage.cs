namespace meshIt.Models;

/// <summary>
/// A message wrapped with mesh routing metadata for multi-hop delivery.
/// </summary>
public class RoutedMessage
{
    public Guid MessageId { get; set; } = Guid.NewGuid();

    /// <summary>Noise static public key of the original sender.</summary>
    public byte[] OriginatorPublicKey { get; set; } = Array.Empty<byte>();

    /// <summary>Noise static public key of the intended recipient. Empty = broadcast.</summary>
    public byte[] DestinationPublicKey { get; set; } = Array.Empty<byte>();

    /// <summary>Current hop count; incremented at each relay.</summary>
    public byte HopCount { get; set; }

    /// <summary>Maximum allowed hops before the message is dropped.</summary>
    public const byte MaxHops = 7;

    /// <summary>TTL — hops remaining. Starts at MaxHops, decremented each relay.</summary>
    public byte TTL { get; set; } = MaxHops;

    /// <summary>Encrypted payload (only the destination can read it).</summary>
    public byte[] EncryptedPayload { get; set; } = Array.Empty<byte>();

    /// <summary>Fingerprints of nodes that have already seen this message (loop prevention).</summary>
    public List<string> SeenByNodes { get; set; } = new();

    /// <summary>Ordered list of node fingerprints forming the route taken.</summary>
    public List<string> RouteHistory { get; set; } = new();

    /// <summary>Channel name if this is a channel message (null = direct/broadcast).</summary>
    public string? ChannelName { get; set; }
}
