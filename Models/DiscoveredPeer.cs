using InTheHand.Net;

namespace meshIt.Models;

/// <summary>
/// Represents a peer discovered by the hybrid scanner, including protocol information.
/// </summary>
public class DiscoveredPeer : Peer
{
    /// <summary>Which Bluetooth protocol this peer was discovered on.</summary>
    public BluetoothProtocol Protocol { get; set; } = BluetoothProtocol.Unknown;

    /// <summary>Whether this peer also supports the other protocol.</summary>
    public bool SupportsDualProtocol { get; set; }

    /// <summary>BLE device identifier (from Plugin.BLE) for GATT connections.</summary>
    public Guid BleDeviceId { get; set; }

    /// <summary>Emoji indicator for the protocol.</summary>
    public string ProtocolIcon => Protocol switch
    {
        BluetoothProtocol.BLE => SupportsDualProtocol ? "📶🔵" : "📶",
        BluetoothProtocol.Classic => SupportsDualProtocol ? "📶🔵" : "🔵",
        _ => "❓"
    };

    /// <summary>Human-readable protocol name.</summary>
    public string ProtocolText => Protocol switch
    {
        BluetoothProtocol.BLE => "BLE",
        BluetoothProtocol.Classic => "Classic",
        _ => "Unknown"
    };

    /// <summary>Signal bars based on strength.</summary>
    public string SignalBars => SignalStrength switch
    {
        >= -50 => "▂▄▆█",
        >= -60 => "▂▄▆",
        >= -70 => "▂▄",
        >= -80 => "▂",
        _ => "▁"
    };
}
