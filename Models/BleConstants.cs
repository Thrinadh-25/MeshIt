namespace meshIt.Models;

/// <summary>
/// Custom BLE UUIDs and constants for the meshIt protocol.
/// </summary>
public static class BleConstants
{
    /// <summary>meshIt service UUID – advertised so peers can find each other.</summary>
    public static readonly Guid ServiceUuid =
        Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

    /// <summary>GATT characteristic for text messages.</summary>
    public static readonly Guid MessageCharacteristicUuid =
        Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567891");

    /// <summary>GATT characteristic for file transfer data.</summary>
    public static readonly Guid FileCharacteristicUuid =
        Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567892");

    /// <summary>Company ID embedded in manufacturer-specific advertisement data (arbitrary).</summary>
    public const ushort CompanyId = 0xFFFF;

    /// <summary>Maximum Attribute Protocol payload per BLE packet.</summary>
    public const int MaxMtuSize = 247;

    /// <summary>
    /// Usable payload per chunk after subtracting the packet header.
    /// Header = 1 (version) + 1 (type) + 4 (seq) + 16 (senderId) + 4 (checksum) = 26 bytes.
    /// </summary>
    public const int MaxPayloadSize = MaxMtuSize - 26;

    /// <summary>Number of chunks to send before waiting for an ACK.</summary>
    public const int AckWindowSize = 10;

    /// <summary>Connection retry limit.</summary>
    public const int MaxRetries = 3;
}
