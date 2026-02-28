namespace meshIt.Models;

/// <summary>
/// Entry in the mesh routing table — tracks the best known route to a peer.
/// </summary>
public class RouteInfo
{
    /// <summary>Fingerprint of the next-hop peer to reach the destination.</summary>
    public string NextHopFingerprint { get; set; } = string.Empty;

    /// <summary>Total hops to reach the destination via this route.</summary>
    public int HopCount { get; set; }

    /// <summary>When this route was last confirmed/updated.</summary>
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}
