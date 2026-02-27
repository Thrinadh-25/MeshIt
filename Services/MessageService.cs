using System.Text;
using meshIt.Data;
using meshIt.Helpers;
using meshIt.Models;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Handles sending and receiving text messages through the BLE connection manager
/// and persists them in the SQLite database.
/// </summary>
public sealed class MessageService
{
    private readonly BleConnectionManager _connectionManager;
    private readonly GattServerService _gattServer;
    private readonly AppDbContext _db;
    private Guid _localUserId;
    private string _localUserName = string.Empty;
    private uint _seqCounter;

    /// <summary>Fired on the calling thread when a new message is received from a peer.</summary>
    public event Action<Message>? MessageReceived;

    public MessageService(BleConnectionManager connectionManager, GattServerService gattServer, AppDbContext db)
    {
        _connectionManager = connectionManager;
        _gattServer = gattServer;
        _db = db;

        // Subscribe to incoming data on the GATT server's message characteristic
        _gattServer.MessageDataReceived += OnMessageDataReceived;
    }

    /// <summary>Set the local identity (called once on startup).</summary>
    public void SetIdentity(Guid userId, string userName)
    {
        _localUserId = userId;
        _localUserName = userName;
    }

    /// <summary>
    /// Send a text message to a specific peer.
    /// </summary>
    public async Task<Message?> SendMessageAsync(Peer peer, string text)
    {
        try
        {
            // Ensure we have a connection
            var connected = await _connectionManager.ConnectAsync(peer.BluetoothAddress);
            if (!connected)
            {
                Log.Warning("Cannot send message — not connected to {PeerName}", peer.Name);
                return null;
            }

            // Build packet
            var seq = Interlocked.Increment(ref _seqCounter);
            var packet = PacketBuilder.CreateTextMessage(_localUserId, text, seq);

            // Encrypt the payload
            packet.Payload = EncryptionService.Encrypt(packet.Payload);

            var rawBytes = PacketBuilder.Serialize(packet);
            var sent = await _connectionManager.SendMessageDataAsync(peer.BluetoothAddress, rawBytes);

            if (!sent)
            {
                Log.Warning("Failed to send message to {PeerName}", peer.Name);
                return null;
            }

            // Create local message record
            var message = new Message
            {
                SenderId = _localUserId,
                SenderName = _localUserName,
                Content = text,
                Timestamp = DateTime.UtcNow,
                IsOutgoing = true,
                PeerId = peer.Id
            };

            _db.Messages.Add(message);
            await _db.SaveChangesAsync();

            Log.Information("Sent message to {PeerName}: {Preview}",
                peer.Name, text.Length > 50 ? text[..50] + "…" : text);
            return message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error sending message to {PeerName}", peer.Name);
            return null;
        }
    }

    /// <summary>
    /// Load stored messages for a given peer from the database.
    /// </summary>
    public List<Message> LoadMessages(Guid peerId, int limit = 1000)
    {
        return _db.Messages
            .Where(m => m.PeerId == peerId)
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .OrderBy(m => m.Timestamp)
            .ToList();
    }

    // ---- Private ----

    private void OnMessageDataReceived(byte[] rawData)
    {
        try
        {
            var packet = PacketBuilder.Deserialize(rawData);
            if (packet is null || packet.Type != PacketType.TextMessage)
            {
                Log.Warning("Received invalid or non-text packet on message channel");
                return;
            }

            // Decrypt
            var decrypted = EncryptionService.Decrypt(packet.Payload);
            var text = Encoding.UTF8.GetString(decrypted);
            var senderId = new Guid(packet.SenderId);

            var message = new Message
            {
                SenderId = senderId,
                SenderName = string.Empty, // will be resolved by the ViewModel
                Content = text,
                Timestamp = DateTime.UtcNow,
                IsOutgoing = false,
                PeerId = senderId
            };

            _db.Messages.Add(message);
            _db.SaveChanges();

            Log.Information("Received message from {SenderId}: {Preview}",
                senderId, text.Length > 50 ? text[..50] + "…" : text);
            MessageReceived?.Invoke(message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing incoming message");
        }
    }
}
