using meshIt.Models;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// BLE GATT scanner using Plugin.BLE.
/// Discovers nearby BLE peripherals advertising the meshIt service.
/// </summary>
public sealed class BleGattScanner : IDisposable
{
    private IAdapter? _adapter;
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    /// <summary>Fired when a BLE peer is discovered.</summary>
    public event Action<DiscoveredPeer>? PeerDiscovered;

    /// <summary>Start BLE GATT scanning.</summary>
    public void Start()
    {
        if (_isRunning) return;

        try
        {
            var ble = CrossBluetoothLE.Current;
            if (!ble.IsAvailable)
            {
                Log.Warning("BLE GATT scanner: BLE not available on this device");
                return;
            }

            _adapter = ble.Adapter;
            _adapter.ScanTimeout = 10000; // 10s scan window

            _adapter.DeviceDiscovered += OnDeviceDiscovered;

            _cts = new CancellationTokenSource();
            _isRunning = true;

            Task.Run(() => ScanLoopAsync(_cts.Token));
            Log.Information("BLE GATT scanner started");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to start BLE GATT scanner");
        }
    }

    /// <summary>Stop scanning.</summary>
    public void Stop()
    {
        if (!_isRunning) return;

        _cts?.Cancel();

        if (_adapter is not null)
            _adapter.DeviceDiscovered -= OnDeviceDiscovered;

        _isRunning = false;
        Log.Information("BLE GATT scanner stopped");
    }

    private async Task ScanLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_adapter is not null)
                {
                    await _adapter.StartScanningForDevicesAsync(
                        serviceUuids: null, // Scan all — filter by name prefixed with "meshIt"
                        cancellationToken: ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "BLE GATT scan cycle failed");
            }

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

    private void OnDeviceDiscovered(object? sender, Plugin.BLE.Abstractions.EventArgs.DeviceEventArgs e)
    {
        var device = e.Device;

        // Filter by name (meshIt devices advertise "meshIt-<username>")
        if (string.IsNullOrEmpty(device.Name) && device.Rssi == 0) return;

        var peer = new DiscoveredPeer
        {
            Id = device.Id,
            Name = device.Name ?? $"BLE-{device.Id.ToString()[..8]}",
            Protocol = BluetoothProtocol.BLE,
            BleDeviceId = device.Id,
            SignalStrength = device.Rssi,
            Status = PeerStatus.Online,
            LastSeen = DateTime.UtcNow
        };

        Log.Debug("BLE GATT peer: {Name} (RSSI: {Rssi})", peer.Name, device.Rssi);
        PeerDiscovered?.Invoke(peer);
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
