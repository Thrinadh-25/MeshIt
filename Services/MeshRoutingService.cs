using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using InTheHand.Net;
using meshIt.Helpers;
using meshIt.Models;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Multi-hop mesh routing engine with:
///   • Routing table (best-path next-hop selection)
///   • TTL enforcement (max 7 hops)
///   • Route discovery protocol
///   • Message deduplication and loop prevention
///   • Channel message forwarding
/// </summary>
public class MeshRoutingService : IDisposable
{
    private readonly IdentityService _identityService;
    private readonly BleConnectionManager _connectionManager;
    private readonly NoiseProtocolService _noiseService;

    /// <summary>Routing table: destination fingerprint → route info.</summary>
    private readonly ConcurrentDictionary<string, RouteInfo> _routingTable = new();

    /// <summary>Direct connections: fingerprint → Peer.</summary>
    private readonly ConcurrentDictionary<string, Peer> _directPeers = new();

    /// <summary>Message deduplication cache: hash → timestamp.</summary>
    private readonly ConcurrentDictionary<string, DateTime> _seenMessages = new();

    /// <summary>Sequence counter for outgoing packets.</summary>
    private uint _seqCounter;

    /// <summary>Timer for periodic route table cleanup.</summary>
    private readonly System.Threading.Timer _cleanupTimer;

    private const int RouteExpiryMinutes = 5;
    private const int SeenCacheMaxSize = 10000;

    // ---- Events ----

    /// <summary>Fired when a message routed to us is delivered locally.</summary>
    public event Action<RoutedMessage>? MessageDelivered;

    /// <summary>Fired when a channel message is received and should be displayed.</summary>
    public event Action<string, string, string, string?>? ChannelMessageDelivered;
    // channelName, senderFingerprint, messageText, routeTrace

    /// <summary>Fired when a route discovery arrives (for logging/UI).</summary>
    public event Action<string, string>? RouteDiscovered;
    // destinationFingerprint, route path description

    public Guid MyPeerId => _identityService.CurrentIdentity is not null
        ? new Guid(_identityService.CurrentIdentity.NoiseStaticPublicKey[..16])
        : Guid.Empty;

    public string MyFingerprint => _identityService.CurrentIdentity?.Fingerprint ?? string.Empty;

    public MeshRoutingService(
        IdentityService identityService,
        BleConnectionManager connectionManager,
        NoiseProtocolService noiseService)
    {
        _identityService = identityService;
        _connectionManager = connectionManager;
        _noiseService = noiseService;

        // Cleanup stale routes every 60 seconds
        _cleanupTimer = new System.Threading.Timer(CleanupRoutingTable, null,
            TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
    }

    // ============================================================
    // Direct Peer Management
    // ============================================================

    /// <summary>Register a directly connected peer.</summary>
    public void RegisterDirectPeer(Peer peer, string fingerprint)
    {
        _directPeers[fingerprint] = peer;
        // Direct peers are always 1 hop away
        _routingTable[fingerprint] = new RouteInfo
        {
            NextHopFingerprint = fingerprint,
            HopCount = 1,
            LastSeen = DateTime.UtcNow
        };
        Log.Information("Mesh: Registered direct peer {Name} ({Fp})", peer.Name, fingerprint[..8]);
    }

    /// <summary>Remove a direct peer from routing.</summary>
    public void UnregisterDirectPeer(string fingerprint)
    {
        _directPeers.TryRemove(fingerprint, out _);
        _routingTable.TryRemove(fingerprint, out _);
        Log.Information("Mesh: Unregistered peer {Fp}", fingerprint[..8]);
    }

    /// <summary>Register a peer by public key (legacy compatibility).</summary>
    public void RegisterPeer(Peer peer, byte[]? publicKey)
    {
        if (publicKey is not null)
        {
            var fp = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(publicKey)).ToLowerInvariant();
            RegisterDirectPeer(peer, fp);
        }
    }

    /// <summary>Remove a peer from the routing table.</summary>
    public void UnregisterPeer(string fingerprint) => UnregisterDirectPeer(fingerprint);

    // ============================================================
    // Core Routing
    // ============================================================

    /// <summary>
    /// Route a RoutedMessage through the mesh. Handles dedup, TTL, loop prevention,
    /// local delivery, and forwarding.
    /// </summary>
    public async Task RouteMessageAsync(RoutedMessage message, IEnumerable<Peer> activePeers)
    {
        var identity = _identityService.CurrentIdentity!;

        // 1. Deduplication
        var msgHash = $"{message.MessageId}";
        if (!_seenMessages.TryAdd(msgHash, DateTime.UtcNow))
        {
            Log.Debug("Mesh: Dropping duplicate message {MsgId}", message.MessageId);
            return;
        }
        TrimSeenCache();

        // 2. Check TTL
        if (message.TTL <= 0)
        {
            Log.Debug("Mesh: Dropping message {MsgId} — TTL expired", message.MessageId);
            return;
        }

        // 3. Check loop prevention
        if (message.SeenByNodes.Contains(identity.Fingerprint))
        {
            Log.Debug("Mesh: Dropping message {MsgId} — loop detected", message.MessageId);
            return;
        }

        // 4. Is it for me? (or broadcast)
        var isForMe = message.DestinationPublicKey.Length == 32 &&
                      message.DestinationPublicKey.AsSpan().SequenceEqual(identity.NoiseStaticPublicKey);
        var isBroadcast = message.DestinationPublicKey.All(b => b == 0);

        if (isForMe || isBroadcast)
        {
            Log.Information("Mesh: Message {MsgId} delivered to us (hop {Hop}, TTL {TTL})",
                message.MessageId, message.HopCount, message.TTL);
            MessageDelivered?.Invoke(message);

            // If unicast for me, don't forward
            if (isForMe) return;
        }

        // 5. Forward the message
        await ForwardRoutedMessage(message, identity, activePeers);
    }

    /// <summary>Route a raw Packet through the mesh (used for routing/channel control packets).</summary>
    public async Task RoutePacketAsync(Packet packet)
    {
        var identity = _identityService.CurrentIdentity!;
        var myFp = identity.Fingerprint;

        // Dedup using origin + sequence
        var msgHash = $"{Convert.ToHexString(packet.OriginatorPublicKey)}:{packet.SequenceNumber}";
        if (!_seenMessages.TryAdd(msgHash, DateTime.UtcNow))
        {
            Log.Debug("Mesh: Dropping duplicate packet seq={Seq}", packet.SequenceNumber);
            return;
        }
        TrimSeenCache();

        // TTL check
        if (packet.TTL <= 0)
        {
            Log.Debug("Mesh: Dropping packet — TTL expired");
            return;
        }

        // Loop check
        if (packet.RouteHistory.Contains(myFp))
        {
            Log.Debug("Mesh: Dropping packet — loop detected");
            return;
        }

        // Decrement TTL and add to route history
        packet.TTL--;
        packet.HopCount++;
        packet.RouteHistory.Add(myFp);
        packet.SenderId = identity.NoiseStaticPublicKey[..16];

        // Forward to all direct peers except those in route history
        var serialized = PacketBuilder.Serialize(packet);
        var forwarded = 0;

        foreach (var (fp, peer) in _directPeers)
        {
            if (packet.RouteHistory.Contains(fp)) continue;
            try
            {
                await _connectionManager.SendMessageDataAsync(peer.BluetoothAddress, serialized);
                forwarded++;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Mesh: Failed to forward packet to {Peer}", peer.Name);
            }
        }

        Log.Debug("Mesh: Forwarded packet to {Count} peers (TTL={TTL})", forwarded, packet.TTL);
    }

    // ============================================================
    // Route Discovery
    // ============================================================

    /// <summary>Initiate route discovery for a destination fingerprint.</summary>
    public async Task DiscoverRouteAsync(string destinationFingerprint)
    {
        var identity = _identityService.CurrentIdentity!;
        var seq = Interlocked.Increment(ref _seqCounter);

        var packet = PacketBuilder.CreateMeshPacket(
            PacketType.RouteDiscovery,
            identity.NoiseStaticPublicKey,
            new byte[32], // broadcast
            7, // TTL
            null,
            new List<string>(),
            Encoding.UTF8.GetBytes(destinationFingerprint),
            seq);

        Log.Information("Mesh: Initiating route discovery for {Fp}", destinationFingerprint[..8]);
        await RoutePacketAsync(packet);
    }

    /// <summary>Handle an incoming RouteDiscovery packet.</summary>
    public async Task HandleRouteDiscovery(Packet packet)
    {
        var identity = _identityService.CurrentIdentity!;
        var targetFp = Encoding.UTF8.GetString(packet.Payload);

        // Update routing table from the discovery path
        UpdateRoutesFromHistory(packet);

        // Am I the target?
        if (targetFp == identity.Fingerprint)
        {
            Log.Information("Mesh: Route discovery for me — sending reply");
            var seq = Interlocked.Increment(ref _seqCounter);

            var reply = PacketBuilder.CreateMeshPacket(
                PacketType.RouteReply,
                identity.NoiseStaticPublicKey,
                packet.OriginatorPublicKey,
                7,
                null,
                new List<string>(packet.RouteHistory),
                Encoding.UTF8.GetBytes(identity.Fingerprint),
                seq);

            await RoutePacketAsync(reply);
        }
        else
        {
            // Forward discovery
            await RoutePacketAsync(packet);
        }
    }

    /// <summary>Handle an incoming RouteReply packet.</summary>
    public void HandleRouteReply(Packet packet)
    {
        var identity = _identityService.CurrentIdentity!;

        if (packet.RouteHistory.Count == 0) return;

        // The originator of the reply is reachable via the first hop in the route history
        var originFp = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(packet.OriginatorPublicKey)).ToLowerInvariant();

        var nextHop = packet.RouteHistory.FirstOrDefault(fp => _directPeers.ContainsKey(fp));
        if (nextHop != null)
        {
            _routingTable[originFp] = new RouteInfo
            {
                NextHopFingerprint = nextHop,
                HopCount = packet.RouteHistory.Count,
                LastSeen = DateTime.UtcNow
            };

            var routeStr = string.Join(" → ", packet.RouteHistory.Select(f => f[..8]));
            Log.Information("Mesh: Route to {Fp} discovered: {Route} ({Hops} hops)",
                originFp[..8], routeStr, packet.RouteHistory.Count);
            RouteDiscovered?.Invoke(originFp, routeStr);
        }
    }

    // ============================================================
    // Channel Message Routing
    // ============================================================

    /// <summary>Send a channel message to the mesh (broadcast).</summary>
    public async Task SendChannelMessageAsync(string channelName, string text)
    {
        var identity = _identityService.CurrentIdentity!;
        var seq = Interlocked.Increment(ref _seqCounter);

        var packet = PacketBuilder.CreateMeshPacket(
            PacketType.ChannelMessage,
            identity.NoiseStaticPublicKey,
            new byte[32], // broadcast
            7,
            channelName,
            new List<string>(),
            Encoding.UTF8.GetBytes(text),
            seq);

        await RoutePacketAsync(packet);
    }

    /// <summary>Handle an incoming channel message packet.</summary>
    public async Task HandleChannelMessage(Packet packet)
    {
        var senderFp = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(packet.OriginatorPublicKey)).ToLowerInvariant();
        var text = Encoding.UTF8.GetString(packet.Payload);
        var channel = packet.ChannelName ?? "#general";

        // Build route trace
        var routeTrace = packet.RouteHistory.Count > 0
            ? string.Join(" → ", packet.RouteHistory.Select(f => f[..8]))
            : null;

        Log.Information("Mesh: Channel message in {Channel} from {Sender} (hops: {Hops})",
            channel, senderFp[..8], packet.HopCount);

        ChannelMessageDelivered?.Invoke(channel, senderFp, text, routeTrace);

        // Forward to other peers
        await RoutePacketAsync(packet);
    }

    /// <summary>Send a channel control packet (join/leave/announce).</summary>
    public async Task SendChannelControlAsync(PacketType type, string channelName, string extraData = "")
    {
        var identity = _identityService.CurrentIdentity!;
        var seq = Interlocked.Increment(ref _seqCounter);

        var payload = string.IsNullOrEmpty(extraData)
            ? Encoding.UTF8.GetBytes(identity.Nickname)
            : Encoding.UTF8.GetBytes($"{identity.Nickname}|{extraData}");

        var packet = PacketBuilder.CreateMeshPacket(
            type,
            identity.NoiseStaticPublicKey,
            new byte[32], // broadcast
            7,
            channelName,
            new List<string>(),
            payload,
            seq);

        await RoutePacketAsync(packet);
    }

    // ============================================================
    // Routing Table Access
    // ============================================================

    /// <summary>Get a snapshot of the current routing table.</summary>
    public Dictionary<string, RouteInfo> GetRoutingTableSnapshot()
        => new(_routingTable);

    /// <summary>Get the list of directly connected peer fingerprints.</summary>
    public List<string> GetDirectPeerFingerprints()
        => _directPeers.Keys.ToList();

    /// <summary>Find the next hop for a given destination fingerprint.</summary>
    public Peer? FindNextHop(string destinationFingerprint)
    {
        // Direct connection?
        if (_directPeers.TryGetValue(destinationFingerprint, out var directPeer))
            return directPeer;

        // Check routing table
        if (_routingTable.TryGetValue(destinationFingerprint, out var route))
        {
            if (_directPeers.TryGetValue(route.NextHopFingerprint, out var nextHopPeer))
                return nextHopPeer;
        }

        return null;
    }

    // ============================================================
    // Private Helpers
    // ============================================================

    private async Task ForwardRoutedMessage(RoutedMessage message, UserIdentity identity, IEnumerable<Peer> activePeers)
    {
        // Decrement TTL and increment hop count
        message.TTL--;
        message.HopCount++;
        message.SeenByNodes.Add(identity.Fingerprint);
        message.RouteHistory.Add(identity.Fingerprint);

        // Serialize as packet
        var payload = JsonSerializer.SerializeToUtf8Bytes(message);
        var packet = new Packet
        {
            Version = 0x02,
            Type = PacketType.RoutedMessage,
            SenderId = identity.NoiseStaticPublicKey[..16],
            OriginatorPublicKey = message.OriginatorPublicKey,
            DestinationPublicKey = message.DestinationPublicKey,
            HopCount = message.HopCount,
            TTL = message.TTL,
            Payload = payload
        };

        var raw = PacketBuilder.Serialize(packet);
        var forwarded = 0;

        // Try specific next hop first
        var destFp = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(message.DestinationPublicKey)).ToLowerInvariant();
        var nextHop = FindNextHop(destFp);

        if (nextHop != null && !message.SeenByNodes.Contains(nextHop.Id.ToString()))
        {
            try
            {
                await _connectionManager.SendMessageDataAsync(nextHop.BluetoothAddress, raw);
                forwarded++;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Mesh: Failed to forward to next hop {Peer}", nextHop.Name);
            }
        }

        // Flood to remaining peers if broadcast or no specific route
        if (nextHop == null || message.DestinationPublicKey.All(b => b == 0))
        {
            foreach (var peer in activePeers)
            {
                if (message.SeenByNodes.Contains(peer.Id.ToString())) continue;

                try
                {
                    await _connectionManager.SendMessageDataAsync(peer.BluetoothAddress, raw);
                    forwarded++;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Mesh: Failed to forward to {Peer}", peer.Name);
                }
            }
        }

        Log.Debug("Mesh: Forwarded message {MsgId} to {Count} peers (hop {Hop}, TTL {TTL})",
            message.MessageId, forwarded, message.HopCount, message.TTL);
    }

    private void UpdateRoutesFromHistory(Packet packet)
    {
        var history = packet.RouteHistory;
        if (history.Count == 0) return;

        // Each node in the history is reachable via the first directly connected node
        var firstDirectHop = history.FirstOrDefault(fp => _directPeers.ContainsKey(fp));
        if (firstDirectHop == null) return;

        for (var i = 0; i < history.Count; i++)
        {
            var fp = history[i];
            var hopCount = i + 1;

            if (!_routingTable.TryGetValue(fp, out var existing) || existing.HopCount > hopCount)
            {
                _routingTable[fp] = new RouteInfo
                {
                    NextHopFingerprint = firstDirectHop,
                    HopCount = hopCount,
                    LastSeen = DateTime.UtcNow
                };
            }
        }
    }

    private void TrimSeenCache()
    {
        if (_seenMessages.Count > SeenCacheMaxSize)
        {
            // Remove oldest entries
            var toRemove = _seenMessages
                .OrderBy(kv => kv.Value)
                .Take(SeenCacheMaxSize / 2)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var key in toRemove)
                _seenMessages.TryRemove(key, out _);
        }
    }

    private void CleanupRoutingTable(object? state)
    {
        var expiry = DateTime.UtcNow.AddMinutes(-RouteExpiryMinutes);
        var stale = _routingTable
            .Where(kv => kv.Value.LastSeen < expiry && !_directPeers.ContainsKey(kv.Key))
            .Select(kv => kv.Key)
            .ToList();

        foreach (var fp in stale)
        {
            _routingTable.TryRemove(fp, out _);
            Log.Debug("Mesh: Removed stale route to {Fp}", fp[..8]);
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }
}
