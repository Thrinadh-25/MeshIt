namespace meshIt.Models;

/// <summary>
/// Supported Bluetooth transport protocols.
/// </summary>
public enum BluetoothProtocol
{
    /// <summary>Unknown or undetected protocol.</summary>
    Unknown = 0,

    /// <summary>Bluetooth Low Energy (BLE GATT).</summary>
    BLE = 1,

    /// <summary>Classic Bluetooth (RFCOMM via 32feet.NET).</summary>
    Classic = 2
}
