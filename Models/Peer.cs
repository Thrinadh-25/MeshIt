using CommunityToolkit.Mvvm.ComponentModel;
using InTheHand.Net;

namespace meshIt.Models;

/// <summary>
/// Connection status of a discovered peer.
/// </summary>
public enum PeerStatus
{
    Offline,
    Connecting,
    Online
}

/// <summary>
/// Represents a discovered meshIt user on the Bluetooth network.
/// Observable so the UI updates automatically when properties change.
/// </summary>
public partial class Peer : ObservableObject
{
    /// <summary>Unique identifier (GUID) for this peer.</summary>
    [ObservableProperty] private Guid _id;

    /// <summary>Display name chosen by the peer.</summary>
    [ObservableProperty] private string _name = string.Empty;

    /// <summary>Current connection status.</summary>
    [ObservableProperty] private PeerStatus _status = PeerStatus.Offline;

    /// <summary>RSSI signal strength (dBm). Higher (closer to 0) = stronger.</summary>
    [ObservableProperty] private int _signalStrength;

    /// <summary>Bluetooth hardware address of the peer device.</summary>
    [ObservableProperty] private BluetoothAddress _bluetoothAddress = BluetoothAddress.None;

    /// <summary>Last time we received an advertisement from this peer.</summary>
    [ObservableProperty] private DateTime _lastSeen = DateTime.UtcNow;
}
