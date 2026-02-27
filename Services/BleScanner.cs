using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using meshIt.Models;
using Serilog;
using Windows.Devices.Bluetooth.Advertisement;

namespace meshIt.Services;

/// <summary>
/// Scans for nearby meshIt peers by watching for BLE advertisements that carry our
/// service UUID. Raises <see cref="PeerDiscovered"/> and <see cref="PeerLost"/> events.
/// </summary>
public sealed class BleScanner : IDisposable
{
    private BluetoothLEAdvertisementWatcher? _watcher;
    private readonly Dictionary<ulong, DateTime> _lastSeenMap = new();
    private Timer? _pruneTimer;
    private bool _isRunning;

    /// <summary>Timeout after which a peer is considered lost (no ads received).</summary>
    private static readonly TimeSpan PeerTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Fired when a new peer is found or an existing peer's info updates.</summary>
    public event Action<Peer>? PeerDiscovered;

    /// <summary>Fired when a peer has not been seen within the timeout window.</summary>
    public event Action<ulong>? PeerLost;

    /// <summary>Start scanning for meshIt advertisements.</summary>
    public void Start()
    {
        if (_isRunning) return;

        try
        {
            _watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };

            // Filter for our service UUID
            var filter = new BluetoothLEAdvertisementFilter();
            filter.Advertisement.ServiceUuids.Add(BleConstants.ServiceUuid);
            _watcher.AdvertisementFilter = filter;

            _watcher.Received += OnAdvertisementReceived;
            _watcher.Stopped += OnStopped;
            _watcher.Start();

            // Prune lost peers every 5 seconds
            _pruneTimer = new Timer(PruneLostPeers, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            _isRunning = true;

            Log.Information("BLE Scanner started");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start BLE scanner");
            throw;
        }
    }

    /// <summary>Stop scanning.</summary>
    public void Stop()
    {
        if (!_isRunning || _watcher is null) return;

        _watcher.Stop();
        _pruneTimer?.Dispose();
        _pruneTimer = null;
        _isRunning = false;
        Log.Information("BLE Scanner stopped");
    }

    private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementReceivedEventArgs args)
    {
        try
        {
            // Extract manufacturer data
            foreach (var mfr in args.Advertisement.ManufacturerData)
            {
                if (mfr.CompanyId != BleConstants.CompanyId) continue;

                var data = mfr.Data.ToArray();
                if (data.Length < 17) continue; // at least 16 (guid) + 1 (name)

                var peerId = new Guid(data.AsSpan(0, 16));
                var name = Encoding.UTF8.GetString(data, 16, data.Length - 16);

                var peer = new Peer
                {
                    Id = peerId,
                    Name = name,
                    Status = PeerStatus.Online,
                    SignalStrength = args.RawSignalStrengthInDBm,
                    BluetoothAddress = args.BluetoothAddress,
                    LastSeen = DateTime.UtcNow
                };

                _lastSeenMap[args.BluetoothAddress] = DateTime.UtcNow;

                Log.Debug("Peer discovered: {Name} (RSSI {Rssi})", name, args.RawSignalStrengthInDBm);
                PeerDiscovered?.Invoke(peer);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error parsing BLE advertisement");
        }
    }

    private void OnStopped(BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        Log.Debug("BLE Scanner stopped: {Error}", args.Error);
    }

    private void PruneLostPeers(object? state)
    {
        var now = DateTime.UtcNow;
        var lost = new List<ulong>();

        foreach (var (address, lastSeen) in _lastSeenMap)
        {
            if (now - lastSeen > PeerTimeout)
                lost.Add(address);
        }

        foreach (var address in lost)
        {
            _lastSeenMap.Remove(address);
            Log.Debug("Peer lost: BLE address {Address}", address);
            PeerLost?.Invoke(address);
        }
    }

    public void Dispose()
    {
        Stop();
        if (_watcher is not null)
        {
            _watcher.Received -= OnAdvertisementReceived;
            _watcher.Stopped -= OnStopped;
            _watcher = null;
        }
    }
}
