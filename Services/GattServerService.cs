using System.Runtime.InteropServices.WindowsRuntime;
using meshIt.Models;
using Serilog;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace meshIt.Services;

/// <summary>
/// Hosts a local GATT server with meshIt service and characteristics so that
/// remote peers can write text messages and file data to us.
/// </summary>
public sealed class GattServerService : IDisposable
{
    private GattServiceProvider? _serviceProvider;
    private GattLocalCharacteristic? _messageCharacteristic;
    private GattLocalCharacteristic? _fileCharacteristic;
    private bool _isRunning;

    /// <summary>Fired when data is received on the message characteristic.</summary>
    public event Action<byte[]>? MessageDataReceived;

    /// <summary>Fired when data is received on the file characteristic.</summary>
    public event Action<byte[]>? FileDataReceived;

    /// <summary>Start the GATT server.</summary>
    public async Task StartAsync()
    {
        if (_isRunning) return;

        try
        {
            var result = await GattServiceProvider.CreateAsync(BleConstants.ServiceUuid);
            if (result.Error != Windows.Devices.Bluetooth.BluetoothError.Success)
            {
                Log.Error("Failed to create GATT service provider: {Error}", result.Error);
                return;
            }

            _serviceProvider = result.ServiceProvider;

            // --- Message characteristic (write-without-response) ---
            _messageCharacteristic = await CreateWriteCharacteristicAsync(
                BleConstants.MessageCharacteristicUuid);
            if (_messageCharacteristic is not null)
                _messageCharacteristic.WriteRequested += OnMessageWriteRequested;

            // --- File characteristic (write-without-response) ---
            _fileCharacteristic = await CreateWriteCharacteristicAsync(
                BleConstants.FileCharacteristicUuid);
            if (_fileCharacteristic is not null)
                _fileCharacteristic.WriteRequested += OnFileWriteRequested;

            // Start advertising the GATT service
            var advParams = new GattServiceProviderAdvertisingParameters
            {
                IsConnectable = true,
                IsDiscoverable = true
            };
            _serviceProvider.StartAdvertising(advParams);
            _isRunning = true;

            Log.Information("GATT Server started with message and file characteristics");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start GATT server");
        }
    }

    /// <summary>Stop the GATT server and clean up.</summary>
    public void Stop()
    {
        if (!_isRunning || _serviceProvider is null) return;

        _serviceProvider.StopAdvertising();
        _isRunning = false;
        Log.Information("GATT Server stopped");
    }

    private async Task<GattLocalCharacteristic?> CreateWriteCharacteristicAsync(Guid uuid)
    {
        if (_serviceProvider is null) return null;

        var charParams = new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.WriteWithoutResponse
                                       | GattCharacteristicProperties.Write,
            WriteProtectionLevel = GattProtectionLevel.Plain,
            ReadProtectionLevel = GattProtectionLevel.Plain
        };

        var charResult = await _serviceProvider.Service.CreateCharacteristicAsync(uuid, charParams);
        if (charResult.Error != Windows.Devices.Bluetooth.BluetoothError.Success)
        {
            Log.Error("Failed to create characteristic {Uuid}: {Error}", uuid, charResult.Error);
            return null;
        }

        return charResult.Characteristic;
    }

    private async void OnMessageWriteRequested(GattLocalCharacteristic sender,
        GattWriteRequestedEventArgs args)
    {
        using var deferral = args.GetDeferral();
        var request = await args.GetRequestAsync();
        if (request is null) return;

        var data = request.Value.ToArray();
        Log.Debug("GATT message data received: {Length} bytes", data.Length);
        MessageDataReceived?.Invoke(data);

        if (request.Option == GattWriteOption.WriteWithResponse)
            request.Respond();
    }

    private async void OnFileWriteRequested(GattLocalCharacteristic sender,
        GattWriteRequestedEventArgs args)
    {
        using var deferral = args.GetDeferral();
        var request = await args.GetRequestAsync();
        if (request is null) return;

        var data = request.Value.ToArray();
        Log.Debug("GATT file data received: {Length} bytes", data.Length);
        FileDataReceived?.Invoke(data);

        if (request.Option == GattWriteOption.WriteWithResponse)
            request.Respond();
    }

    public void Dispose()
    {
        Stop();
        _serviceProvider = null;
    }
}
