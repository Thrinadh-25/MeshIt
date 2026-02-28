namespace meshIt.Models;

/// <summary>
/// Protocol-agnostic Bluetooth connection interface.
/// Implemented by both BLE GATT and Classic RFCOMM connection classes.
/// </summary>
public interface IBluetoothConnection : IDisposable
{
    /// <summary>Unique identifier for the connected peer.</summary>
    Guid PeerId { get; }

    /// <summary>Which protocol is used for this connection.</summary>
    BluetoothProtocol Protocol { get; }

    /// <summary>Whether the connection is currently active.</summary>
    bool IsConnected { get; }

    /// <summary>Connect to the peer.</summary>
    Task<bool> ConnectAsync();

    /// <summary>Send data to the connected peer.</summary>
    Task SendAsync(byte[] data);

    /// <summary>Disconnect from the peer.</summary>
    Task DisconnectAsync();

    /// <summary>Fired when data is received from the peer.</summary>
    event EventHandler<byte[]> DataReceived;

    /// <summary>Fired when the connection is lost.</summary>
    event EventHandler Disconnected;
}
