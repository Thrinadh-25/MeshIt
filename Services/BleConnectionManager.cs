using System.IO;
using System.Net.Sockets;
using InTheHand.Net;
using InTheHand.Net.Sockets;
using meshIt.Models;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Manages Bluetooth RFCOMM connections to peers.
/// Uses 32feet.NET BluetoothClient for outgoing connections.
/// All data is sent/received through NetworkStream with length-prefix framing:
///   [4 bytes: payload length (big-endian)] [N bytes: payload]
/// </summary>
public sealed class BleConnectionManager : IDisposable
{
    /// <summary>Active connections keyed by Bluetooth address.</summary>
    private readonly Dictionary<BluetoothAddress, BluetoothClient> _clients = new();
    private readonly Dictionary<BluetoothAddress, NetworkStream> _streams = new();
    private readonly Dictionary<BluetoothAddress, CancellationTokenSource> _readCts = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Fired when a connection to a peer is established.</summary>
    public event Action<BluetoothAddress>? Connected;

    /// <summary>Fired when a connection to a peer is lost.</summary>
    public event Action<BluetoothAddress>? Disconnected;

    /// <summary>Fired when data is received from any peer. (address, data)</summary>
    public event Action<BluetoothAddress, byte[]>? DataReceived;

    /// <summary>
    /// Connect to a peer via RFCOMM on the meshIt service UUID.
    /// Uses exponential back-off with up to <see cref="BleConstants.MaxRetries"/> attempts.
    /// </summary>
    public async Task<bool> ConnectAsync(BluetoothAddress bluetoothAddress)
    {
        await _lock.WaitAsync();
        try
        {
            if (_clients.ContainsKey(bluetoothAddress))
                return true; // already connected

            for (var attempt = 1; attempt <= BleConstants.MaxRetries; attempt++)
            {
                try
                {
                    Log.Information("Connecting to Bluetooth device {Address} (attempt {Attempt}/{Max})",
                        bluetoothAddress, attempt, BleConstants.MaxRetries);

                    var client = new BluetoothClient();
                    await Task.Run(() =>
                        client.Connect(new BluetoothEndPoint(bluetoothAddress, BleConstants.ServiceUuid)));

                    var stream = client.GetStream();

                    _clients[bluetoothAddress] = client;
                    _streams[bluetoothAddress] = stream;

                    // Start background read loop
                    var cts = new CancellationTokenSource();
                    _readCts[bluetoothAddress] = cts;
                    _ = Task.Run(() => ReadLoopAsync(bluetoothAddress, stream, cts.Token));

                    Log.Information("Connected to Bluetooth device {Address}", bluetoothAddress);
                    Connected?.Invoke(bluetoothAddress);
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Connection attempt {Attempt} failed for {Address}", attempt, bluetoothAddress);
                    if (attempt < BleConstants.MaxRetries)
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
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

    /// <summary>
    /// Register an already-accepted incoming connection (from BleAdvertiser).
    /// </summary>
    public void RegisterIncomingConnection(BluetoothClient client, BluetoothAddress address)
    {
        _lock.Wait();
        try
        {
            // Close any existing connection to the same peer
            CleanupConnection(address);

            var stream = client.GetStream();
            _clients[address] = client;
            _streams[address] = stream;

            var cts = new CancellationTokenSource();
            _readCts[address] = cts;
            _ = Task.Run(() => ReadLoopAsync(address, stream, cts.Token));

            Log.Information("Registered incoming connection from {Address}", address);
            Connected?.Invoke(address);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Send data to a connected peer using length-prefix framing.
    /// </summary>
    public async Task<bool> SendDataAsync(BluetoothAddress address, byte[] data)
    {
        if (!_streams.TryGetValue(address, out var stream))
        {
            Log.Warning("No active stream for {Address}", address);
            return false;
        }

        try
        {
            // Write length prefix (4 bytes, big-endian)
            var lengthPrefix = BitConverter.GetBytes(data.Length);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(lengthPrefix);

            await stream.WriteAsync(lengthPrefix);
            await stream.WriteAsync(data);
            await stream.FlushAsync();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error sending data to {Address}", address);
            Disconnect(address);
            return false;
        }
    }

    /// <summary>Write data to peer — used for text messages.</summary>
    public Task<bool> SendMessageDataAsync(BluetoothAddress bluetoothAddress, byte[] data)
        => SendDataAsync(bluetoothAddress, data);

    /// <summary>Write data to peer — used for file transfers.</summary>
    public Task<bool> SendFileDataAsync(BluetoothAddress bluetoothAddress, byte[] data)
        => SendDataAsync(bluetoothAddress, data);

    /// <summary>Disconnect from a specific peer.</summary>
    public void Disconnect(BluetoothAddress address)
    {
        CleanupConnection(address);
        Log.Information("Disconnected from {Address}", address);
        Disconnected?.Invoke(address);
    }

    private void CleanupConnection(BluetoothAddress address)
    {
        if (_readCts.Remove(address, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        if (_streams.Remove(address, out var stream))
        {
            try { stream.Close(); } catch { }
        }

        if (_clients.Remove(address, out var client))
        {
            try { client.Close(); } catch { }
        }
    }

    /// <summary>
    /// Background read loop: reads length-prefixed messages from the stream
    /// and fires DataReceived events.
    /// </summary>
    private async Task ReadLoopAsync(BluetoothAddress address, NetworkStream stream, CancellationToken ct)
    {
        var lengthBuffer = new byte[4];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Read 4-byte length prefix
                var bytesRead = await ReadExactAsync(stream, lengthBuffer, 0, 4, ct);
                if (bytesRead < 4) break; // Connection closed

                if (BitConverter.IsLittleEndian)
                    Array.Reverse(lengthBuffer);
                var payloadLength = BitConverter.ToInt32(lengthBuffer, 0);

                if (payloadLength <= 0 || payloadLength > 10 * 1024 * 1024) // 10 MB max
                {
                    Log.Warning("Invalid payload length {Length} from {Address}", payloadLength, address);
                    break;
                }

                // Read payload
                var payload = new byte[payloadLength];
                bytesRead = await ReadExactAsync(stream, payload, 0, payloadLength, ct);
                if (bytesRead < payloadLength) break;

                Log.Debug("Received {Length} bytes from {Address}", payloadLength, address);
                DataReceived?.Invoke(address, payload);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                Log.Warning(ex, "Read loop error for {Address}", address);
        }

        if (!ct.IsCancellationRequested)
        {
            Log.Information("Connection to {Address} lost", address);
            Disconnect(address);
        }
    }

    private static async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset + totalRead, count - totalRead), ct);
            if (read == 0) return totalRead; // Connection closed
            totalRead += read;
        }
        return totalRead;
    }

    public void Dispose()
    {
        foreach (var address in _clients.Keys.ToList())
        {
            CleanupConnection(address);
        }
        _lock.Dispose();
    }
}
