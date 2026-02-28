using System.Text;
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;
using meshIt.Models;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Scans for nearby Bluetooth devices by periodically running device discovery.
/// Uses 32feet.NET BluetoothClient instead of UWP BLE advertisement watcher.
/// </summary>
public sealed class BleScanner : IDisposable
{
    private readonly Dictionary<BluetoothAddress, DateTime> _lastSeenMap = new();
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    /// <summary>Timeout after which a peer is considered lost.</summary>
    private static readonly TimeSpan PeerTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Fired when a new peer is found or an existing peer's info updates.</summary>
    public event Action<Peer>? PeerDiscovered;

    /// <summary>Fired when a peer has not been seen within the timeout window.</summary>
    public event Action<BluetoothAddress>? PeerLost;

    /// <summary>Start scanning for meshIt devices in a background loop.</summary>
    public void Start()
    {
        if (_isRunning) return;

        _cts = new CancellationTokenSource();
        _isRunning = true;

        Task.Run(() => DiscoveryLoopAsync(_cts.Token));
        Log.Information("Bluetooth Scanner started");
    }

    /// <summary>Stop scanning.</summary>
    public void Stop()
    {
        if (!_isRunning) return;

        _cts?.Cancel();
        _isRunning = false;
        Log.Information("Bluetooth Scanner stopped");
    }

    private async Task DiscoveryLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = new BluetoothClient();

                // Discover devices in range
                var devices = await Task.Run(() => client.DiscoverDevices(255), ct);

                foreach (var device in devices)
                {
                    if (ct.IsCancellationRequested) break;

                    var peer = new Peer
                    {
                        Id = GeneratePeerGuid(device.DeviceAddress),
                        Name = string.IsNullOrWhiteSpace(device.DeviceName)
                            ? device.DeviceAddress.ToString()
                            : device.DeviceName,
                        Status = device.Connected ? PeerStatus.Online : PeerStatus.Offline,
                        SignalStrength = 0,
                        BluetoothAddress = device.DeviceAddress,
                        LastSeen = DateTime.UtcNow
                    };

                    _lastSeenMap[device.DeviceAddress] = DateTime.UtcNow;

                    Log.Debug("Peer discovered: {Name} ({Address})", peer.Name, device.DeviceAddress);
                    PeerDiscovered?.Invoke(peer);
                }

                client.Dispose();

                // Prune lost peers
                PruneLostPeers();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Bluetooth discovery cycle failed");
            }

            // Wait before next discovery cycle
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(BleConstants.DiscoveryIntervalSeconds), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void PruneLostPeers()
    {
        var now = DateTime.UtcNow;
        var lost = new List<BluetoothAddress>();

        foreach (var (address, lastSeen) in _lastSeenMap)
        {
            if (now - lastSeen > PeerTimeout)
                lost.Add(address);
        }

        foreach (var address in lost)
        {
            _lastSeenMap.Remove(address);
            Log.Debug("Peer lost: {Address}", address);
            PeerLost?.Invoke(address);
        }
    }

    /// <summary>
    /// Generate a deterministic GUID from a Bluetooth address so the same device
    /// always gets the same Peer.Id across discoveries.
    /// </summary>
    private static Guid GeneratePeerGuid(BluetoothAddress address)
    {
        var bytes = Encoding.UTF8.GetBytes($"meshIt-peer-{address}");
        var hash = System.Security.Cryptography.MD5.HashData(bytes);
        return new Guid(hash);
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
