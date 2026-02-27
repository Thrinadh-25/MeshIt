using System.Runtime.InteropServices.WindowsRuntime;
using meshIt.Models;
using Serilog;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace meshIt.Services;

/// <summary>
/// Manages outgoing GATT connections to discovered peers.
/// Caches connections, handles disconnections, and provides methods to write data
/// to a peer's message or file characteristic.
/// </summary>
public sealed class BleConnectionManager : IDisposable
{
    /// <summary>Cached GATT sessions keyed by Bluetooth address.</summary>
    private readonly Dictionary<ulong, BluetoothLEDevice> _devices = new();
    private readonly Dictionary<ulong, GattCharacteristic?> _messageChars = new();
    private readonly Dictionary<ulong, GattCharacteristic?> _fileChars = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Fired when a connection to a peer is established.</summary>
    public event Action<ulong>? Connected;

    /// <summary>Fired when a connection to a peer is lost.</summary>
    public event Action<ulong>? Disconnected;

    /// <summary>
    /// Connect to a peer's GATT server and discover our service / characteristics.
    /// Uses exponential back-off with up to <see cref="BleConstants.MaxRetries"/> attempts.
    /// </summary>
    public async Task<bool> ConnectAsync(ulong bluetoothAddress)
    {
        await _lock.WaitAsync();
        try
        {
            if (_devices.ContainsKey(bluetoothAddress))
                return true; // already connected

            for (var attempt = 1; attempt <= BleConstants.MaxRetries; attempt++)
            {
                try
                {
                    Log.Information("Connecting to BLE device {Address} (attempt {Attempt}/{Max})",
                        bluetoothAddress, attempt, BleConstants.MaxRetries);

                    var device = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);
                    if (device is null)
                    {
                        Log.Warning("Device not found for address {Address}", bluetoothAddress);
                        continue;
                    }

                    var gattResult = await device.GetGattServicesForUuidAsync(BleConstants.ServiceUuid);
                    if (gattResult.Status != GattCommunicationStatus.Success || gattResult.Services.Count == 0)
                    {
                        Log.Warning("GATT service not found on device {Address}", bluetoothAddress);
                        device.Dispose();
                        continue;
                    }

                    var service = gattResult.Services[0];

                    // Discover characteristics
                    var msgCharResult = await service.GetCharacteristicsForUuidAsync(BleConstants.MessageCharacteristicUuid);
                    var fileCharResult = await service.GetCharacteristicsForUuidAsync(BleConstants.FileCharacteristicUuid);

                    GattCharacteristic? msgChar = msgCharResult.Status == GattCommunicationStatus.Success && msgCharResult.Characteristics.Count > 0
                        ? msgCharResult.Characteristics[0] : null;
                    GattCharacteristic? fileChar = fileCharResult.Status == GattCommunicationStatus.Success && fileCharResult.Characteristics.Count > 0
                        ? fileCharResult.Characteristics[0] : null;

                    device.ConnectionStatusChanged += OnConnectionStatusChanged;

                    _devices[bluetoothAddress] = device;
                    _messageChars[bluetoothAddress] = msgChar;
                    _fileChars[bluetoothAddress] = fileChar;

                    Log.Information("Connected to BLE device {Address}", bluetoothAddress);
                    Connected?.Invoke(bluetoothAddress);
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Connection attempt {Attempt} failed for {Address}", attempt, bluetoothAddress);
                    if (attempt < BleConstants.MaxRetries)
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt))); // exponential back-off
                }
            }

            Log.Error("Failed to connect to {Address} after {MaxRetries} attempts",
                bluetoothAddress, BleConstants.MaxRetries);
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Write data to the peer's message characteristic.</summary>
    public async Task<bool> SendMessageDataAsync(ulong bluetoothAddress, byte[] data)
    {
        return await WriteToCharacteristicAsync(bluetoothAddress, _messageChars, data);
    }

    /// <summary>Write data to the peer's file characteristic.</summary>
    public async Task<bool> SendFileDataAsync(ulong bluetoothAddress, byte[] data)
    {
        return await WriteToCharacteristicAsync(bluetoothAddress, _fileChars, data);
    }

    /// <summary>Disconnect from a specific peer.</summary>
    public void Disconnect(ulong bluetoothAddress)
    {
        if (_devices.Remove(bluetoothAddress, out var device))
        {
            device.ConnectionStatusChanged -= OnConnectionStatusChanged;
            device.Dispose();
        }
        _messageChars.Remove(bluetoothAddress);
        _fileChars.Remove(bluetoothAddress);

        Log.Information("Disconnected from {Address}", bluetoothAddress);
        Disconnected?.Invoke(bluetoothAddress);
    }

    // ---- Private helpers ----

    private async Task<bool> WriteToCharacteristicAsync(
        ulong address,
        Dictionary<ulong, GattCharacteristic?> charMap,
        byte[] data)
    {
        if (!charMap.TryGetValue(address, out var characteristic) || characteristic is null)
        {
            Log.Warning("No characteristic available for {Address}", address);
            return false;
        }

        try
        {
            var writer = new DataWriter();
            writer.WriteBytes(data);
            var result = await characteristic.WriteValueAsync(
                writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse);

            if (result != GattCommunicationStatus.Success)
            {
                Log.Warning("Write failed for {Address}: {Status}", address, result);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error writing to characteristic for {Address}", address);
            return false;
        }
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            Log.Warning("Device {Address} disconnected unexpectedly", sender.BluetoothAddress);
            Disconnect(sender.BluetoothAddress);
        }
    }

    public void Dispose()
    {
        foreach (var device in _devices.Values)
        {
            device.ConnectionStatusChanged -= OnConnectionStatusChanged;
            device.Dispose();
        }
        _devices.Clear();
        _messageChars.Clear();
        _fileChars.Clear();
        _lock.Dispose();
    }
}
