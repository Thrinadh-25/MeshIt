using InTheHand.Net;
using meshIt.Models;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Classic Bluetooth RFCOMM connection — wraps BleConnectionManager as an IBluetoothConnection.
/// </summary>
public class ClassicBtConnection : IBluetoothConnection
{
    private readonly BleConnectionManager _connectionManager;
    private readonly BluetoothAddress _address;
    private bool _isConnected;

    public Guid PeerId { get; }
    public BluetoothProtocol Protocol => BluetoothProtocol.Classic;
    public bool IsConnected => _isConnected;

    public event EventHandler<byte[]>? DataReceived;
    public event EventHandler? Disconnected;

    public ClassicBtConnection(BleConnectionManager connectionManager, BluetoothAddress address, Guid peerId)
    {
        _connectionManager = connectionManager;
        _address = address;
        PeerId = peerId;

        // Wire up events from the connection manager
        _connectionManager.DataReceived += OnDataReceived;
        _connectionManager.Disconnected += OnDisconnected;
    }

    public async Task<bool> ConnectAsync()
    {
        try
        {
            await _connectionManager.ConnectAsync(_address);
            _isConnected = true;
            Log.Information("Classic BT: Connected to {Address}", _address);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Classic BT: Connection failed to {Address}", _address);
            return false;
        }
    }

    public async Task SendAsync(byte[] data)
    {
        if (!_isConnected)
        {
            Log.Warning("Classic BT: Not connected — cannot send");
            return;
        }

        await _connectionManager.SendMessageDataAsync(_address, data);
    }

    public Task DisconnectAsync()
    {
        _connectionManager.Disconnect(_address);
        _isConnected = false;
        return Task.CompletedTask;
    }

    private void OnDataReceived(BluetoothAddress address, byte[] data)
    {
        if (address == _address)
            DataReceived?.Invoke(this, data);
    }

    private void OnDisconnected(BluetoothAddress address)
    {
        if (address == _address)
        {
            _isConnected = false;
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        _connectionManager.DataReceived -= OnDataReceived;
        _connectionManager.Disconnected -= OnDisconnected;

        if (_isConnected)
            _connectionManager.Disconnect(_address);
    }
}
