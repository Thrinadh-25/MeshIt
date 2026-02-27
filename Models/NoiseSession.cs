using System.Security.Cryptography;
using NSec.Cryptography;

namespace meshIt.Models;

/// <summary>
/// Represents an established Noise session with a specific peer.
/// Holds the symmetric transport keys for encrypt/decrypt.
/// </summary>
public class NoiseSession
{
    public Guid PeerId { get; set; }

    /// <summary>32-byte key for encrypting outgoing messages to this peer.</summary>
    public byte[] SendKey { get; set; } = new byte[32];

    /// <summary>32-byte key for decrypting incoming messages from this peer.</summary>
    public byte[] ReceiveKey { get; set; } = new byte[32];

    /// <summary>Remote peer's Noise static public key (Curve25519).</summary>
    public byte[] RemoteStaticPublicKey { get; set; } = new byte[32];

    /// <summary>SHA-256 fingerprint of the remote peer's static key.</summary>
    public string RemoteFingerprint =>
        Convert.ToHexString(SHA256.HashData(RemoteStaticPublicKey)).ToLowerInvariant();

    /// <summary>Short fingerprint (first 8 hex chars).</summary>
    public string RemoteShortFingerprint => RemoteFingerprint[..8];

    /// <summary>Whether the handshake is complete and keys are valid.</summary>
    public bool IsEstablished { get; set; }

    /// <summary>Monotonically increasing nonce for sending (prevents replay).</summary>
    private long _sendNonce;
    public long SendNonce { get => _sendNonce; set => _sendNonce = value; }

    /// <summary>Last received nonce (for replay protection).</summary>
    public long ReceiveNonce { get; set; }

    /// <summary>When this session was established.</summary>
    public DateTime EstablishedAt { get; set; } = DateTime.UtcNow;

    // ---- Encrypt / Decrypt using ChaCha20-Poly1305 ----

    private static readonly AeadAlgorithm ChaCha = AeadAlgorithm.ChaCha20Poly1305;

    /// <summary>Encrypt plaintext with the send key. Prepends 8-byte nonce to output.</summary>
    public byte[] Encrypt(byte[] plaintext)
    {
        var nonceVal = Interlocked.Increment(ref _sendNonce);
        var nonceBytes = BitConverter.GetBytes(nonceVal);

        using var key = Key.Import(ChaCha, SendKey, KeyBlobFormat.RawSymmetricKey,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });

        // 12-byte nonce: 4 zero bytes + 8-byte counter
        var fullNonce = new byte[ChaCha.NonceSize];
        Array.Copy(nonceBytes, 0, fullNonce, 4, Math.Min(nonceBytes.Length, fullNonce.Length - 4));

        var ciphertext = ChaCha.Encrypt(key, fullNonce, Array.Empty<byte>(), plaintext);

        // Output: [8 bytes nonce counter] [ciphertext + auth tag]
        var result = new byte[8 + ciphertext.Length];
        Array.Copy(nonceBytes, 0, result, 0, 8);
        Array.Copy(ciphertext, 0, result, 8, ciphertext.Length);
        return result;
    }

    /// <summary>Decrypt ciphertext with the receive key. Expects 8-byte nonce prefix.</summary>
    public byte[]? Decrypt(byte[] data)
    {
        if (data.Length < 9) return null;

        var nonceBytes = data[..8];
        var ciphertext = data[8..];
        var nonceVal = BitConverter.ToInt64(nonceBytes);

        // Replay protection
        if (nonceVal <= ReceiveNonce) return null;
        ReceiveNonce = nonceVal;

        using var key = Key.Import(ChaCha, ReceiveKey, KeyBlobFormat.RawSymmetricKey,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });

        var fullNonce = new byte[ChaCha.NonceSize];
        Array.Copy(nonceBytes, 0, fullNonce, 4, Math.Min(nonceBytes.Length, fullNonce.Length - 4));

        return ChaCha.Decrypt(key, fullNonce, Array.Empty<byte>(), ciphertext);
    }
}
