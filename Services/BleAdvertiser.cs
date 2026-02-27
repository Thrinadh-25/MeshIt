using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using meshIt.Models;
using Serilog;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;

namespace meshIt.Services;

/// <summary>
/// Continuously broadcasts a BLE advertisement so other meshIt peers can discover us.
/// Uses <see cref="BluetoothLEAdvertisementPublisher"/>.
/// </summary>
public sealed class BleAdvertiser : IDisposable
{
    private BluetoothLEAdvertisementPublisher? _publisher;
    private bool _isRunning;

    /// <summary>
    /// Start advertising our presence with the given user id and display name.
    /// </summary>
    public void Start(Guid userId, string displayName)
    {
        if (_isRunning) return;

        try
        {
            var advertisement = new BluetoothLEAdvertisement();

            // Add our custom service UUID so scanners can filter on it
            advertisement.ServiceUuids.Add(BleConstants.ServiceUuid);

            // Encode userId + displayName in manufacturer data
            var manufacturerData = new BluetoothLEManufacturerData
            {
                CompanyId = BleConstants.CompanyId
            };

            var userIdBytes = userId.ToByteArray(); // 16 bytes
            var nameBytes = Encoding.UTF8.GetBytes(displayName);
            var payload = new byte[16 + nameBytes.Length];

            Array.Copy(userIdBytes, 0, payload, 0, 16);
            Array.Copy(nameBytes, 0, payload, 16, nameBytes.Length);

            var writer = new DataWriter();
            writer.WriteBytes(payload);
            manufacturerData.Data = writer.DetachBuffer();

            advertisement.ManufacturerData.Add(manufacturerData);

            _publisher = new BluetoothLEAdvertisementPublisher(advertisement);
            _publisher.StatusChanged += OnStatusChanged;
            _publisher.Start();
            _isRunning = true;

            Log.Information("BLE Advertiser started — broadcasting as '{DisplayName}'", displayName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start BLE advertiser");
            throw;
        }
    }

    /// <summary>Stop advertising.</summary>
    public void Stop()
    {
        if (!_isRunning || _publisher is null) return;

        _publisher.Stop();
        _isRunning = false;
        Log.Information("BLE Advertiser stopped");
    }

    private void OnStatusChanged(BluetoothLEAdvertisementPublisher sender,
        BluetoothLEAdvertisementPublisherStatusChangedEventArgs args)
    {
        Log.Debug("BLE Advertiser status changed: {Status} (error: {Error})",
            args.Status, args.Error);
    }

    public void Dispose()
    {
        Stop();
        if (_publisher is not null)
        {
            _publisher.StatusChanged -= OnStatusChanged;
            _publisher = null;
        }
    }
}
