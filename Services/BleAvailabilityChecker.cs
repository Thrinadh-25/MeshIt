using Serilog;
using Windows.Devices.Bluetooth;
using Windows.Devices.Radios;

namespace meshIt.Services;

/// <summary>
/// Checks BLE hardware availability with detailed error reporting.
/// Must be called before starting any BLE services.
/// </summary>
public static class BleAvailabilityChecker
{
    /// <summary>
    /// Check whether BLE is available and ready.
    /// Returns (available, detailed error message).
    /// </summary>
    public static async Task<(bool Available, string Message)> CheckAsync()
    {
        try
        {
            // Step 1: Get the default Bluetooth adapter
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            if (adapter is null)
            {
                return (false, "No Bluetooth adapter found. Please connect a Bluetooth dongle or enable your built-in adapter.");
            }

            // Step 2: Check if adapter supports BLE
            if (!adapter.IsLowEnergySupported)
            {
                return (false, "Your Bluetooth adapter does not support Bluetooth Low Energy (BLE 4.0+). A newer adapter is required.");
            }

            // Step 3: Check if the radio is accessible
            var radio = await adapter.GetRadioAsync();
            if (radio is null)
            {
                // Radio access might be denied — try the Radios API
                var access = await Radio.RequestAccessAsync();
                if (access != RadioAccessStatus.Allowed)
                {
                    return (false, "Bluetooth radio access denied. Go to Windows Settings → Privacy → Radios and allow this app.");
                }
                return (false, "Bluetooth radio not accessible. Try restarting the Bluetooth Support service.");
            }

            // Step 4: Check radio state
            switch (radio.State)
            {
                case RadioState.Off:
                    return (false, "Bluetooth is turned OFF. Please enable it in Windows Settings → Bluetooth & devices.");

                case RadioState.Disabled:
                    return (false, "Bluetooth adapter is disabled. Enable it in Device Manager → Bluetooth.");

                case RadioState.Unknown:
                    return (false, "Bluetooth state is unknown. Try restarting your computer.");

                case RadioState.On:
                    break; // All good
            }

            // Step 5: Check advertisement support
            if (!adapter.IsAdvertisementOffloadSupported)
            {
                Log.Warning("BLE advertisement offload not supported — software advertising will be used");
            }

            Log.Information("BLE available: Adapter={AdapterId}, Radio={RadioState}, BLE={BleSupported}",
                adapter.DeviceId, radio.State, adapter.IsLowEnergySupported);

            return (true, $"BLE ready — {adapter.DeviceId}");
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "Bluetooth permission denied. Run the app as Administrator or add Bluetooth capability.");
        }
        catch (TypeInitializationException ex)
        {
            Log.Error(ex, "WinRT type initialization failed");
            return (false, "Windows Runtime Bluetooth APIs not available. Ensure you're running Windows 10 1809+ and the project targets windows10.0.19041.0.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "BLE availability check failed");
            return (false, $"BLE check failed: {ex.Message}");
        }
    }
}
