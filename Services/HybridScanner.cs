using System.Collections.Concurrent;
using InTheHand.Net;
using meshIt.Models;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Unified hybrid scanner that runs both BLE GATT (Plugin.BLE) and Classic BT (32feet.NET)
/// scans in parallel. Deduplicates peers by address/name and merges protocol info.
/// </summary>
public sealed class HybridScanner : IDisposable
{
    private readonly BleScanner _classicScanner;
    private readonly BleGattScanner _bleScanner;
    private readonly ConcurrentDictionary<string, DiscoveredPeer> _knownPeers = new();

    /// <summary>Fired when a peer is discovered or updated (includes protocol info).</summary>
    public event Action<DiscoveredPeer>? PeerDiscovered;

    /// <summary>Fired when a peer is lost (not seen in any protocol).</summary>
    public event Action<BluetoothAddress>? PeerLost;

    public HybridScanner()
    {
        _classicScanner = new BleScanner();
        _bleScanner = new BleGattScanner();

        _classicScanner.PeerDiscovered += OnClassicPeerDiscovered;
        _classicScanner.PeerLost += addr => PeerLost?.Invoke(addr);
        _bleScanner.PeerDiscovered += OnBlePeerDiscovered;
    }

    /// <summary>Start both scanners in parallel.</summary>
    public void Start()
    {
        var (classic, ble) = BluetoothCapabilities.Detect();
        Log.Information("Hybrid scanner starting — Classic: {Classic}, BLE: {Ble}", classic, ble);

        if (classic) _classicScanner.Start();
        if (ble) _bleScanner.Start();

        if (!classic && !ble)
            Log.Error("No Bluetooth protocols available!");
    }

    /// <summary>Stop both scanners.</summary>
    public void Stop()
    {
        _classicScanner.Stop();
        _bleScanner.Stop();
    }

    private void OnClassicPeerDiscovered(Peer peer)
    {
        var key = peer.Name?.ToLower() ?? peer.Id.ToString();

        var discovered = _knownPeers.AddOrUpdate(key,
            _ =>
            {
                var dp = new DiscoveredPeer
                {
                    Id = peer.Id,
                    Name = peer.Name,
                    Protocol = BluetoothProtocol.Classic,
                    Status = peer.Status,
                    BluetoothAddress = peer.BluetoothAddress,
                    SignalStrength = peer.SignalStrength,
                    LastSeen = DateTime.UtcNow
                };
                return dp;
            },
            (_, existing) =>
            {
                existing.BluetoothAddress = peer.BluetoothAddress;
                existing.Status = peer.Status;
                existing.LastSeen = DateTime.UtcNow;
                if (existing.Protocol == BluetoothProtocol.BLE)
                    existing.SupportsDualProtocol = true;
                else
                    existing.Protocol = BluetoothProtocol.Classic;
                return existing;
            });

        PeerDiscovered?.Invoke(discovered);
    }

    private void OnBlePeerDiscovered(DiscoveredPeer peer)
    {
        var key = peer.Name?.ToLower() ?? peer.Id.ToString();

        var discovered = _knownPeers.AddOrUpdate(key,
            _ => peer,
            (_, existing) =>
            {
                existing.BleDeviceId = peer.BleDeviceId;
                existing.SignalStrength = peer.SignalStrength;
                existing.LastSeen = DateTime.UtcNow;
                if (existing.Protocol == BluetoothProtocol.Classic)
                    existing.SupportsDualProtocol = true;
                else
                    existing.Protocol = BluetoothProtocol.BLE;
                return existing;
            });

        PeerDiscovered?.Invoke(discovered);
    }

    public void Dispose()
    {
        _classicScanner.Dispose();
        _bleScanner.Dispose();
    }
}
