using InTheHand.Net.Bluetooth;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Detects which Bluetooth protocols the current machine supports.
/// </summary>
public static class BluetoothCapabilities
{
    /// <summary>Whether Classic Bluetooth (RFCOMM) is available.</summary>
    public static bool SupportsClassic { get; private set; }

    /// <summary>Whether BLE is available (via Plugin.BLE adapter check).</summary>
    public static bool SupportsBle { get; private set; }

    /// <summary>Detect available Bluetooth capabilities.</summary>
    public static (bool classic, bool ble) Detect()
    {
        // Classic BT: check via 32feet.NET BluetoothRadio
        try
        {
            var radio = BluetoothRadio.Default;
            SupportsClassic = radio is not null;
            if (SupportsClassic)
                Log.Information("Classic Bluetooth: available (radio={Name})", radio!.Name);
            else
                Log.Warning("Classic Bluetooth: not available");
        }
        catch (Exception ex)
        {
            SupportsClassic = false;
            Log.Warning(ex, "Classic Bluetooth detection failed");
        }

        // BLE: check via Plugin.BLE
        try
        {
            var ble = Plugin.BLE.CrossBluetoothLE.Current;
            SupportsBle = ble.IsAvailable;
            Log.Information("BLE: {Available}", SupportsBle ? "available" : "not available");
        }
        catch
        {
            // Fallback: try to create adapter
            try
            {
                var adapter = Plugin.BLE.CrossBluetoothLE.Current;
                SupportsBle = adapter.IsAvailable;
                Log.Information("BLE: {Available}", SupportsBle ? "available" : "not available");
            }
            catch (Exception ex)
            {
                SupportsBle = false;
                Log.Warning(ex, "BLE detection failed");
            }
        }

        return (SupportsClassic, SupportsBle);
    }

    /// <summary>Get a human-readable summary of capabilities.</summary>
    public static string GetSummary()
    {
        var classic = SupportsClassic ? "✅" : "❌";
        var ble = SupportsBle ? "✅" : "❌";
        return $"Classic BT: {classic} | BLE: {ble}";
    }
}
