using InTheHand.Net;
using meshIt.Helpers;
using meshIt.Models;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Replaces the UWP GATT server. In the 32feet.NET architecture, incoming data
/// arrives via the BleConnectionManager's DataReceived event. This service
/// routes incoming packets to the appropriate handler (messages vs files)
/// based on packet type. It acts as a dispatcher rather than a server.
/// </summary>
public sealed class GattServerService : IDisposable
{
    private readonly BleConnectionManager _connectionManager;
    private bool _isRunning;

    /// <summary>Fired when data is received on the message channel.</summary>
    public event Action<byte[]>? MessageDataReceived;

    /// <summary>Fired when data is received on the file channel.</summary>
    public event Action<byte[]>? FileDataReceived;

    public GattServerService(BleConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
    }

    /// <summary>Start listening for incoming data.</summary>
    public Task StartAsync()
    {
        if (_isRunning) return Task.CompletedTask;

        _connectionManager.DataReceived += OnDataReceived;
        _isRunning = true;

        Log.Information("Bluetooth data dispatcher started (message + file routing)");
        return Task.CompletedTask;
    }

    /// <summary>Stop listening.</summary>
    public void Stop()
    {
        if (!_isRunning) return;

        _connectionManager.DataReceived -= OnDataReceived;
        _isRunning = false;
        Log.Information("Bluetooth data dispatcher stopped");
    }

    private void OnDataReceived(BluetoothAddress address, byte[] data)
    {
        try
        {
            // Peek at the packet type to route to the correct handler
            var packet = PacketBuilder.Deserialize(data);
            if (packet is null)
            {
                Log.Warning("Received invalid packet from {Address}", address);
                return;
            }

            switch (packet.Type)
            {
                case PacketType.TextMessage:
                    Log.Debug("Routing text message from {Address} ({Length} bytes)", address, data.Length);
                    MessageDataReceived?.Invoke(data);
                    break;

                case PacketType.FileMetadata:
                case PacketType.FileChunk:
                    Log.Debug("Routing file data from {Address} ({Length} bytes)", address, data.Length);
                    FileDataReceived?.Invoke(data);
                    break;

                case PacketType.RoutedMessage:
                    // Routed mesh messages go through the message channel
                    Log.Debug("Routing mesh message from {Address} ({Length} bytes)", address, data.Length);
                    MessageDataReceived?.Invoke(data);
                    break;

                default:
                    Log.Warning("Unknown packet type {Type} from {Address}", packet.Type, address);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error routing incoming data from {Address}", address);
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
