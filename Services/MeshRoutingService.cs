using System.Collections.Concurrent;
using InTheHand.Net;
using meshIt.Models;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Multi-hop mesh routing: forward messages through intermediate peers using flood routing.
/// Deduplicates messages and enforces the 7-hop limit.
/// </summary>
public class MeshRoutingService
{
    private readonly IdentityService _identityService;
    private readonly BleConnectionManager _connectionManager;
    private readonly NoiseProtocolService _noiseService;

    /// <summary>Set of message IDs we've already processed (deduplication).</summary>
    private readonly ConcurrentDictionary<Guid, byte> _seenMessages = new();

    /// <summary>Cached routing table: fingerprint → active peer.</summary>
    private readonly ConcurrentDictionary<string, Peer> _routingTable = new();

    /// <summary>Fired when a message is routed to us (we are the destination).</summary>
    public event Action<RoutedMessage>? MessageDelivered;

    public MeshRoutingService(
        IdentityService identityService,
        BleConnectionManager connectionManager,
        NoiseProtocolService noiseService)
    {
        _identityService = identityService;
        _connectionManager = connectionManager;
        _noiseService = noiseService;
    }

    /// <summary>Register a peer in the routing table.</summary>
    public void RegisterPeer(Peer peer, byte[]? publicKey)
    {
        if (publicKey is not null)
        {
            var fp = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(publicKey)).ToLowerInvariant();
            _routingTable[fp] = peer;
        }
    }

    /// <summary>Remove a peer from the routing table.</summary>
    public void UnregisterPeer(string fingerprint) => _routingTable.TryRemove(fingerprint, out _);

    /// <summary>
    /// Route a message through the mesh. Handles dedup, hop limit, loop prevention,
    /// and local delivery.
    /// </summary>
    public async Task RouteMessageAsync(RoutedMessage message, IEnumerable<Peer> activePeers)
    {
        var identity = _identityService.CurrentIdentity!;

        // 1. Deduplication
        if (!_seenMessages.TryAdd(message.MessageId, 0))
        {
            Log.Debug("Mesh: Dropping duplicate message {MsgId}", message.MessageId);
            return;
        }

        // Trim seen messages cache if it gets too large
        if (_seenMessages.Count > 5000)
        {
            _seenMessages.Clear();
        }

        // 2. Check if we are the destination
        if (message.DestinationPublicKey.Length == 32 &&
            message.DestinationPublicKey.AsSpan().SequenceEqual(identity.NoiseStaticPublicKey))
        {
            Log.Information("Mesh: Message {MsgId} delivered to us (hop {Hop})",
                message.MessageId, message.HopCount);
            MessageDelivered?.Invoke(message);
            return;
        }

        // 3. Check hop limit
        if (message.HopCount >= RoutedMessage.MaxHops)
        {
            Log.Debug("Mesh: Dropping message {MsgId} — exceeded {Max} hops",
                message.MessageId, RoutedMessage.MaxHops);
            return;
        }

        // 4. Check if we've already relayed this (loop prevention)
        if (message.SeenByNodes.Contains(identity.Fingerprint))
        {
            Log.Debug("Mesh: Dropping message {MsgId} — loop detected", message.MessageId);
            return;
        }

        // 5. Add ourselves and increment hop count
        message.SeenByNodes.Add(identity.Fingerprint);
        message.HopCount++;

        // 6. Flood-forward to all connected peers (except those who already saw it)
        var forwarded = 0;
        foreach (var peer in activePeers)
        {
            if (message.SeenByNodes.Contains(peer.Id.ToString()))
                continue;

            try
            {
                // Serialize and send the routed message as a packet
                var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(message);
                var packet = new Packet
                {
                    Version = 0x02,
                    Type = PacketType.RoutedMessage,
                    SenderId = identity.NoiseStaticPublicKey[..16],
                    OriginatorPublicKey = message.OriginatorPublicKey,
                    DestinationPublicKey = message.DestinationPublicKey,
                    HopCount = message.HopCount,
                    Payload = payload
                };

                var raw = Helpers.PacketBuilder.Serialize(packet);
                await _connectionManager.SendMessageDataAsync(peer.BluetoothAddress, raw);
                forwarded++;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Mesh: Failed to forward to {Peer}", peer.Name);
            }
        }

        Log.Debug("Mesh: Forwarded message {MsgId} to {Count} peers (hop {Hop})",
            message.MessageId, forwarded, message.HopCount);
    }
}
