namespace meshIt.Models;

/// <summary>
/// A message queued for delivery to an offline peer (store-and-forward).
/// </summary>
public class PendingMessage
{
    public Guid MessageId { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Destination peer's fingerprint.</summary>
    public string DestinationFingerprint { get; set; } = string.Empty;

    /// <summary>Encrypted payload bytes (Base64 for JSON serialization).</summary>
    public string EncryptedPayloadBase64 { get; set; } = string.Empty;

    /// <summary>When this pending message expires (default: 7 days from creation).</summary>
    public DateTime Expiry { get; set; } = DateTime.UtcNow.AddDays(7);

    /// <summary>Whether the message has expired.</summary>
    public bool IsExpired => DateTime.UtcNow > Expiry;
}
