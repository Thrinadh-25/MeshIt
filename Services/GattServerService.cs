using System.Text;
using InTheHand.Net;
using meshIt.Helpers;
using meshIt.Models;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Routes incoming Bluetooth data to the appropriate handler based on packet type.
/// Acts as a dispatcher for messages, files, handshakes, and mesh routing/channel control packets.
/// </summary>
public sealed class GattServerService : IDisposable
{
    private readonly BleConnectionManager _connectionManager;
    private bool _isRunning;

    /// <summary>Fired when data is received on the message channel.</summary>
    public event Action<byte[]>? MessageDataReceived;

    /// <summary>Fired when data is received on the file channel.</summary>
    public event Action<byte[]>? FileDataReceived;

    /// <summary>Fired when a mesh-routed message packet arrives.</summary>
    public event Action<Packet>? RoutedPacketReceived;

    /// <summary>Fired when a route discovery packet arrives.</summary>
    public event Action<Packet>? RouteDiscoveryReceived;

    /// <summary>Fired when a route reply packet arrives.</summary>
    public event Action<Packet>? RouteReplyReceived;

    /// <summary>Fired when a channel message packet arrives.</summary>
    public event Action<Packet>? ChannelMessageReceived;

    /// <summary>Fired when a channel join packet arrives.</summary>
    public event Action<Packet>? ChannelJoinReceived;

    /// <summary>Fired when a channel leave packet arrives.</summary>
    public event Action<Packet>? ChannelLeaveReceived;

    /// <summary>Fired when a channel announce packet arrives.</summary>
    public event Action<Packet>? ChannelAnnounceReceived;

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

        Log.Information("Bluetooth data dispatcher started (all packet types)");
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
                    Log.Debug("Routing mesh message from {Address} ({Length} bytes)", address, data.Length);
                    RoutedPacketReceived?.Invoke(packet);
                    break;

                case PacketType.RouteDiscovery:
                    Log.Debug("Route discovery from {Address}", address);
                    RouteDiscoveryReceived?.Invoke(packet);
                    break;

                case PacketType.RouteReply:
                    Log.Debug("Route reply from {Address}", address);
                    RouteReplyReceived?.Invoke(packet);
                    break;

                case PacketType.ChannelMessage:
                    Log.Debug("Channel message from {Address}", address);
                    ChannelMessageReceived?.Invoke(packet);
                    break;

                case PacketType.ChannelJoin:
                    Log.Debug("Channel join from {Address}", address);
                    ChannelJoinReceived?.Invoke(packet);
                    break;

                case PacketType.ChannelLeave:
                    Log.Debug("Channel leave from {Address}", address);
                    ChannelLeaveReceived?.Invoke(packet);
                    break;

                case PacketType.ChannelAnnounce:
                    Log.Debug("Channel announce from {Address}", address);
                    ChannelAnnounceReceived?.Invoke(packet);
                    break;

                case PacketType.NoiseHandshakeMsg1:
                case PacketType.NoiseHandshakeMsg2:
                case PacketType.NoiseHandshakeMsg3:
                    Log.Debug("Noise handshake from {Address}", address);
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
