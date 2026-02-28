using meshIt.Models;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Creates protocol-agnostic IBluetoothConnection instances based on discovered peer protocol.
/// Supports automatic fallback: BLE → Classic if BLE connection fails.
/// </summary>
public class ConnectionFactory
{
    private readonly BleConnectionManager _classicConnectionManager;

    public ConnectionFactory(BleConnectionManager classicConnectionManager)
    {
        _classicConnectionManager = classicConnectionManager;
    }

    /// <summary>
    /// Create a connection to the given peer using its preferred protocol.
    /// </summary>
    public IBluetoothConnection Create(DiscoveredPeer peer)
    {
        return peer.Protocol switch
        {
            BluetoothProtocol.BLE => new BleGattConnection(peer.Id, peer.BleDeviceId),
            BluetoothProtocol.Classic => new ClassicBtConnection(
                _classicConnectionManager, peer.BluetoothAddress, peer.Id),
            _ => new ClassicBtConnection(
                _classicConnectionManager, peer.BluetoothAddress, peer.Id)
        };
    }

    /// <summary>
    /// Connect to a peer with automatic fallback between protocols.
    /// Tries the peer's primary protocol first, then falls back to the other.
    /// </summary>
    public async Task<IBluetoothConnection?> ConnectWithFallbackAsync(DiscoveredPeer peer)
    {
        // Try primary protocol
        var primary = Create(peer);
        var connected = await primary.ConnectAsync();

        if (connected)
        {
            Log.Information("Connected to {Name} via {Protocol}", peer.Name, peer.Protocol);
            return primary;
        }

        // Fallback to other protocol if dual protocol supported
        if (peer.SupportsDualProtocol || peer.BluetoothAddress != null)
        {
            var fallbackProtocol = peer.Protocol == BluetoothProtocol.BLE
                ? BluetoothProtocol.Classic
                : BluetoothProtocol.BLE;

            Log.Information("Falling back to {Protocol} for {Name}", fallbackProtocol, peer.Name);

            IBluetoothConnection fallback = fallbackProtocol switch
            {
                BluetoothProtocol.BLE => new BleGattConnection(peer.Id, peer.BleDeviceId),
                BluetoothProtocol.Classic => new ClassicBtConnection(
                    _classicConnectionManager, peer.BluetoothAddress, peer.Id),
                _ => primary
            };

            connected = await fallback.ConnectAsync();
            if (connected)
            {
                primary.Dispose();
                Log.Information("Fallback connection to {Name} via {Protocol} succeeded",
                    peer.Name, fallbackProtocol);
                return fallback;
            }

            fallback.Dispose();
        }

        primary.Dispose();
        Log.Warning("Could not connect to {Name} on any protocol", peer.Name);
        return null;
    }
}
