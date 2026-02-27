using System.Collections.Concurrent;
using meshIt.Crypto;
using meshIt.Models;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Manages Noise XX handshakes with peers and provides per-session encryption/decryption.
/// Falls back to Phase 1 AES-256 pre-shared key for v1 peers.
/// </summary>
public class NoiseProtocolService
{
    private readonly IdentityService _identityService;
    private readonly ConcurrentDictionary<Guid, NoiseSession> _sessions = new();
    private readonly ConcurrentDictionary<Guid, NoiseHandshake> _pendingHandshakes = new();

    /// <summary>Fired when a handshake completes and a session is established.</summary>
    public event Action<Guid, NoiseSession>? SessionEstablished;

    public NoiseProtocolService(IdentityService identityService)
    {
        _identityService = identityService;
    }

    /// <summary>Get an existing session, or null if none established.</summary>
    public NoiseSession? GetSession(Guid peerId) =>
        _sessions.TryGetValue(peerId, out var s) ? s : null;

    /// <summary>Check whether we have an established session with this peer.</summary>
    public bool HasSession(Guid peerId) =>
        _sessions.TryGetValue(peerId, out var s) && s.IsEstablished;

    // ---- Initiator side ----

    /// <summary>Start a Noise XX handshake as the initiator. Returns Message 1 bytes.</summary>
    public byte[] CreateHandshakeMessage1(Guid peerId)
    {
        var identity = _identityService.CurrentIdentity!;
        var hs = new NoiseHandshake(identity.NoiseStaticPrivateKey, identity.NoiseStaticPublicKey);
        _pendingHandshakes[peerId] = hs;

        var msg1 = hs.CreateMessage1();
        Log.Information("Noise: Initiating handshake with {PeerId}", peerId);
        return msg1;
    }

    /// <summary>Initiator processes Message 2 and creates Message 3.</summary>
    public byte[] ProcessHandshakeMessage2(Guid peerId, byte[] message2)
    {
        if (!_pendingHandshakes.TryGetValue(peerId, out var hs))
            throw new InvalidOperationException("No pending handshake for this peer");

        var msg3 = hs.ProcessMessage2AndCreateMessage3(message2);

        // Handshake complete on initiator side
        var session = hs.DeriveSession(peerId, isInitiator: true);
        _sessions[peerId] = session;
        _pendingHandshakes.TryRemove(peerId, out _);

        Log.Information("Noise: Handshake completed (initiator) with {Fingerprint}",
            session.RemoteShortFingerprint);
        SessionEstablished?.Invoke(peerId, session);
        return msg3;
    }

    // ---- Responder side ----

    /// <summary>Responder processes Message 1 and creates Message 2.</summary>
    public byte[] ProcessHandshakeMessage1(Guid peerId, byte[] message1)
    {
        var identity = _identityService.CurrentIdentity!;
        var hs = new NoiseHandshake(identity.NoiseStaticPrivateKey, identity.NoiseStaticPublicKey);
        _pendingHandshakes[peerId] = hs;

        var msg2 = hs.ProcessMessage1AndCreateMessage2(message1);
        Log.Information("Noise: Responding to handshake from {PeerId}", peerId);
        return msg2;
    }

    /// <summary>Responder processes Message 3 to complete the handshake.</summary>
    public void ProcessHandshakeMessage3(Guid peerId, byte[] message3)
    {
        if (!_pendingHandshakes.TryGetValue(peerId, out var hs))
            throw new InvalidOperationException("No pending handshake for this peer");

        hs.ProcessMessage3(message3);

        var session = hs.DeriveSession(peerId, isInitiator: false);
        _sessions[peerId] = session;
        _pendingHandshakes.TryRemove(peerId, out _);

        Log.Information("Noise: Handshake completed (responder) with {Fingerprint}",
            session.RemoteShortFingerprint);
        SessionEstablished?.Invoke(peerId, session);
    }

    // ---- Encrypt / Decrypt ----

    /// <summary>
    /// Encrypt data for a peer. Uses Noise session if available, falls back to AES PSK.
    /// </summary>
    public byte[] EncryptForPeer(Guid peerId, byte[] plaintext)
    {
        if (_sessions.TryGetValue(peerId, out var session) && session.IsEstablished)
            return session.Encrypt(plaintext);

        // Fallback to Phase 1 AES pre-shared key
        return EncryptionService.Encrypt(plaintext);
    }

    /// <summary>
    /// Decrypt data from a peer. Uses Noise session if available, falls back to AES PSK.
    /// </summary>
    public byte[] DecryptFromPeer(Guid peerId, byte[] ciphertext)
    {
        if (_sessions.TryGetValue(peerId, out var session) && session.IsEstablished)
        {
            var result = session.Decrypt(ciphertext);
            if (result is not null) return result;
            Log.Warning("Noise decrypt failed for {PeerId}, trying AES fallback", peerId);
        }

        // Fallback to Phase 1 AES pre-shared key
        return EncryptionService.Decrypt(ciphertext);
    }
}
