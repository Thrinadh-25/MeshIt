using meshIt.Models;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// BLE GATT connection using Plugin.BLE.
/// Connects to a BLE peripheral, discovers the meshIt service characteristic,
/// and handles data transfer with automatic message chunking.
/// </summary>
public class BleGattConnection : IBluetoothConnection
{
    private IDevice? _device;
    private ICharacteristic? _txCharacteristic;
    private ICharacteristic? _rxCharacteristic;
    private readonly Guid _bleDeviceId;
    private readonly MessageChunker _chunker = new();
    private bool _isConnected;

    public Guid PeerId { get; }
    public BluetoothProtocol Protocol => BluetoothProtocol.BLE;
    public bool IsConnected => _isConnected;

    public event EventHandler<byte[]>? DataReceived;
    public event EventHandler? Disconnected;

    public BleGattConnection(Guid peerId, Guid bleDeviceId)
    {
        PeerId = peerId;
        _bleDeviceId = bleDeviceId;
    }

    public async Task<bool> ConnectAsync()
    {
        try
        {
            var ble = CrossBluetoothLE.Current;
            var adapter = ble.Adapter;

            // Get device by ID
            _device = await adapter.ConnectToKnownDeviceAsync(_bleDeviceId);
            if (_device == null)
            {
                Log.Warning("BLE GATT: Could not connect to device {Id}", _bleDeviceId);
                return false;
            }

            // Discover the meshIt service
            var services = await _device.GetServicesAsync();
            var meshService = services.FirstOrDefault(s =>
                s.Id == BleConstants.ServiceUuid);

            if (meshService == null)
            {
                // Try finding by any writable characteristic
                meshService = services.FirstOrDefault();
                if (meshService == null)
                {
                    Log.Warning("BLE GATT: meshIt service not found on device");
                    await DisconnectAsync();
                    return false;
                }
            }

            var characteristics = await meshService.GetCharacteristicsAsync();

            // TX characteristic (we write to peer)
            _txCharacteristic = characteristics.FirstOrDefault(c =>
                c.CanWrite || c.CanUpdate);

            // RX characteristic (we read from peer)
            _rxCharacteristic = characteristics.FirstOrDefault(c =>
                c.CanRead || c.CanUpdate);

            if (_rxCharacteristic?.CanUpdate == true)
            {
                _rxCharacteristic.ValueUpdated += OnCharacteristicValueUpdated;
                await _rxCharacteristic.StartUpdatesAsync();
            }

            _isConnected = true;
            Log.Information("BLE GATT: Connected to {Id}", _bleDeviceId);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "BLE GATT: Connection failed to {Id}", _bleDeviceId);
            return false;
        }
    }

    public async Task SendAsync(byte[] data)
    {
        if (!_isConnected || _txCharacteristic == null)
        {
            Log.Warning("BLE GATT: Not connected — cannot send");
            return;
        }

        // Chunk for BLE MTU
        var chunks = MessageChunker.Split(data);

        foreach (var chunk in chunks)
        {
            try
            {
                await _txCharacteristic.WriteAsync(chunk);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "BLE GATT: Write failed");
                break;
            }
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            if (_rxCharacteristic?.CanUpdate == true)
            {
                _rxCharacteristic.ValueUpdated -= OnCharacteristicValueUpdated;
                await _rxCharacteristic.StopUpdatesAsync();
            }

            if (_device != null)
            {
                var adapter = CrossBluetoothLE.Current.Adapter;
                await adapter.DisconnectDeviceAsync(_device);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "BLE GATT: Error during disconnect");
        }
        finally
        {
            _isConnected = false;
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnCharacteristicValueUpdated(object? sender,
        Plugin.BLE.Abstractions.EventArgs.CharacteristicUpdatedEventArgs e)
    {
        var chunk = e.Characteristic.Value;
        if (chunk == null || chunk.Length == 0) return;

        var reassembled = MessageChunker.Reassemble(chunk);
        if (reassembled != null)
        {
            DataReceived?.Invoke(this, reassembled);
        }
    }

    public void Dispose()
    {
        _ = DisconnectAsync();
    }
}
