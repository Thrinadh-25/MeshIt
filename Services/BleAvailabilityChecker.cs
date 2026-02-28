using InTheHand.Net.Bluetooth;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Checks Bluetooth hardware availability using 32feet.NET Win32 APIs.
/// No MSIX packaging or app manifest required.
/// </summary>
public static class BleAvailabilityChecker
{
    /// <summary>
    /// Check whether Bluetooth is available and ready.
    /// Returns (available, detailed status message).
    /// </summary>
    public static Task<(bool Available, string Message)> CheckAsync()
    {
        try
        {
            var radio = BluetoothRadio.Default;
            if (radio is null)
            {
                return Task.FromResult((false,
                    "No Bluetooth adapter found. Please connect a Bluetooth dongle or enable your built-in adapter in Device Manager."));
            }

            if (radio.Mode == RadioMode.PowerOff)
            {
                return Task.FromResult((false,
                    "Bluetooth is turned OFF. Please enable it in Windows Settings → Bluetooth & devices."));
            }

            var info = $"Bluetooth ready — {radio.Name} ({radio.LocalAddress}), Mode: {radio.Mode}";
            Log.Information("Bluetooth available: {Info}", info);
            return Task.FromResult((true, info));
        }
        catch (PlatformNotSupportedException)
        {
            return Task.FromResult((false,
                "Bluetooth is not supported on this platform. Ensure Bluetooth drivers are installed."));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bluetooth availability check failed");
            return Task.FromResult((false, $"Bluetooth check failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Get detailed hardware diagnostics for display in a startup dialog.
    /// </summary>
    public static BluetoothDiagnostics GetDiagnostics()
    {
        var diag = new BluetoothDiagnostics();

        try
        {
            var radio = BluetoothRadio.Default;
            if (radio is null)
            {
                diag.AdapterFound = false;
                diag.ErrorMessage = "No Bluetooth adapter detected";
                return diag;
            }

            diag.AdapterFound = true;
            diag.AdapterName = radio.Name ?? "Unknown Adapter";
            diag.AdapterAddress = radio.LocalAddress?.ToString() ?? "Unknown";
            diag.RadioMode = radio.Mode.ToString();
            diag.IsEnabled = radio.Mode != RadioMode.PowerOff;

            if (!diag.IsEnabled)
            {
                diag.ErrorMessage = "Bluetooth radio is powered off";
            }
        }
        catch (Exception ex)
        {
            diag.AdapterFound = false;
            diag.ErrorMessage = ex.Message;
        }

        return diag;
    }
}

/// <summary>
/// Diagnostic details about the Bluetooth hardware state.
/// </summary>
public class BluetoothDiagnostics
{
    public bool AdapterFound { get; set; }
    public string AdapterName { get; set; } = "Not found";
    public string AdapterAddress { get; set; } = "N/A";
    public string RadioMode { get; set; } = "Unknown";
    public bool IsEnabled { get; set; }
    public string? ErrorMessage { get; set; }

    public bool IsReady => AdapterFound && IsEnabled;
}
