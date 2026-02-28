namespace meshIt.Models;

/// <summary>
/// Constants for the meshIt Bluetooth protocol.
/// Uses RFCOMM (Bluetooth Classic) via 32feet.NET instead of BLE GATT.
/// </summary>
public static class BleConstants
{
    /// <summary>meshIt RFCOMM service UUID – used for discovery and connections.</summary>
    public static readonly Guid ServiceUuid =
        Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

    /// <summary>Maximum payload per RFCOMM write (before chunking).</summary>
    public const int MaxPayloadSize = 4096;

    /// <summary>Read buffer size for RFCOMM streams.</summary>
    public const int StreamBufferSize = 8192;

    /// <summary>Number of chunks to send before waiting for an ACK.</summary>
    public const int AckWindowSize = 10;

    /// <summary>Connection retry limit.</summary>
    public const int MaxRetries = 3;

    /// <summary>Discovery interval in seconds.</summary>
    public const int DiscoveryIntervalSeconds = 10;

    /// <summary>Company ID embedded in peer identification payloads (arbitrary).</summary>
    public const ushort CompanyId = 0xFFFF;
}
