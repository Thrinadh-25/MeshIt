using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;
using meshIt.Models;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Makes the app discoverable by running a BluetoothListener on our service UUID.
/// Accepts incoming RFCOMM connections and notifies the connection manager.
/// </summary>
public sealed class BleAdvertiser : IDisposable
{
    private BluetoothListener? _listener;
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    /// <summary>Fired when a remote peer connects to us.</summary>
    public event Action<BluetoothClient, BluetoothAddress>? IncomingConnection;

    /// <summary>
    /// Start listening for incoming Bluetooth connections on the meshIt service UUID.
    /// </summary>
    public void Start(Guid userId, string displayName)
    {
        if (_isRunning) return;

        try
        {
            _listener = new BluetoothListener(BleConstants.ServiceUuid);
            _listener.ServiceName = $"meshIt-{displayName}";
            _listener.Start();

            _cts = new CancellationTokenSource();
            _isRunning = true;

            Task.Run(() => AcceptLoopAsync(_cts.Token));

            Log.Information("Bluetooth Advertiser started — listening as '{DisplayName}'", displayName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start Bluetooth advertiser");
            throw;
        }
    }

    /// <summary>Stop advertising and close the listener.</summary>
    public void Stop()
    {
        if (!_isRunning) return;

        _cts?.Cancel();

        try
        {
            _listener?.Stop();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error stopping Bluetooth listener");
        }

        _isRunning = false;
        Log.Information("Bluetooth Advertiser stopped");
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            try
            {
                // Accept incoming connection (blocking call run on thread pool)
                var client = await Task.Run(() =>
                {
                    if (_listener.Pending())
                        return _listener.AcceptBluetoothClient();
                    return null;
                }, ct);

                if (client is not null)
                {
                    // Get the remote address from the client
                    var remoteAddress = BluetoothAddress.None;
                    try
                    {
                        var remoteName = client.RemoteMachineName;
                        Log.Information("Incoming Bluetooth connection from {Name}", remoteName);
                    }
                    catch
                    {
                        Log.Information("Incoming Bluetooth connection accepted");
                    }

                    IncomingConnection?.Invoke(client, remoteAddress);
                }
                else
                {
                    // No pending connection, wait briefly before checking again
                    await Task.Delay(500, ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    Log.Warning(ex, "Error accepting Bluetooth connection");
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
