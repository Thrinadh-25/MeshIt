using System.Security.Cryptography;
using NSec.Cryptography;
using meshIt.Models;
using Serilog;

namespace meshIt.Crypto;

/// <summary>
/// Implements a simplified Noise XX-like 3-message handshake using NSec X25519 + ChaCha20-Poly1305.
///
/// Pattern:
///   → e                       (initiator sends ephemeral public key)
///   ← e, ee, s, es            (responder sends ephemeral + static, DH results)
///   → s, se                   (initiator sends static, final DH)
///
/// After the handshake, both sides derive symmetric send/receive keys.
/// </summary>
public class NoiseHandshake
{
    private static readonly KeyAgreementAlgorithm X25519Algo = KeyAgreementAlgorithm.X25519;
    private static readonly AeadAlgorithm ChaCha = AeadAlgorithm.ChaCha20Poly1305;

    private readonly byte[] _localStaticPrivate;
    private readonly byte[] _localStaticPublic;
    private Key? _ephemeralKey;
    private byte[]? _remoteEphemeralPublic;
    private byte[]? _remoteStaticPublic;

    public NoiseHandshake(byte[] localStaticPrivate, byte[] localStaticPublic)
    {
        _localStaticPrivate = localStaticPrivate;
        _localStaticPublic = localStaticPublic;
    }

    /// <summary>
    /// Initiator creates Message 1: sends ephemeral public key (32 bytes).
    /// </summary>
    public byte[] CreateMessage1()
    {
        _ephemeralKey = Key.Create(X25519Algo, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });
        var ephPub = _ephemeralKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        Log.Debug("Noise: Initiator created Message 1 (ephemeral pub {Len} bytes)", ephPub.Length);
        return ephPub;
    }

    /// <summary>
    /// Responder processes Message 1 and creates Message 2.
    /// Returns: [32 bytes responder ephemeral pub] [32 bytes responder static pub encrypted]
    /// </summary>
    public byte[] ProcessMessage1AndCreateMessage2(byte[] message1)
    {
        _remoteEphemeralPublic = message1; // Initiator's ephemeral public key

        // Generate responder ephemeral keypair
        _ephemeralKey = Key.Create(X25519Algo, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });
        var ephPub = _ephemeralKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);

        // DH: ee = X25519(responder_ephemeral, initiator_ephemeral)
        var remoteEphPubKey = PublicKey.Import(X25519Algo, _remoteEphemeralPublic, KeyBlobFormat.RawPublicKey);
        var ee = ComputeDH(_ephemeralKey, remoteEphPubKey);

        // Encrypt our static public key with the ee result
        var encryptedStatic = SimpleEncrypt(_localStaticPublic, ee);

        // DH: es = X25519(responder_static, initiator_ephemeral)
        // (This is used in the key derivation but we send it implicitly)

        var msg2 = new byte[32 + encryptedStatic.Length];
        Array.Copy(ephPub, 0, msg2, 0, 32);
        Array.Copy(encryptedStatic, 0, msg2, 32, encryptedStatic.Length);

        Log.Debug("Noise: Responder created Message 2 ({Len} bytes)", msg2.Length);
        return msg2;
    }

    /// <summary>
    /// Initiator processes Message 2 and creates Message 3.
    /// Returns the encrypted initiator static public key.
    /// </summary>
    public byte[] ProcessMessage2AndCreateMessage3(byte[] message2)
    {
        // Parse message 2
        _remoteEphemeralPublic = message2[..32];
        var encryptedRemoteStatic = message2[32..];

        // DH: ee = X25519(initiator_ephemeral, responder_ephemeral)
        var remoteEphPubKey = PublicKey.Import(X25519Algo, _remoteEphemeralPublic, KeyBlobFormat.RawPublicKey);
        var ee = ComputeDH(_ephemeralKey!, remoteEphPubKey);

        // Decrypt responder's static public key
        _remoteStaticPublic = SimpleDecrypt(encryptedRemoteStatic, ee);

        // DH: se = X25519(initiator_ephemeral, responder_static)
        var remoteStaticPubKey = PublicKey.Import(X25519Algo, _remoteStaticPublic, KeyBlobFormat.RawPublicKey);
        var se = ComputeDH(_ephemeralKey!, remoteStaticPubKey);

        // Encrypt our static public key with combined key (ee + se)
        var combinedKey = CombineKeys(ee, se);
        var encryptedStatic = SimpleEncrypt(_localStaticPublic, combinedKey);

        Log.Debug("Noise: Initiator created Message 3");
        return encryptedStatic;
    }

    /// <summary>
    /// Responder processes Message 3.
    /// After this, both sides can call <see cref="DeriveSession"/>.
    /// </summary>
    public void ProcessMessage3(byte[] message3)
    {
        // DH: ee was already computed. Now compute se = X25519(responder_ephemeral, initiator_static)
        // First decrypt initiator's static key
        var remoteEphPubKey = PublicKey.Import(X25519Algo, _remoteEphemeralPublic!, KeyBlobFormat.RawPublicKey);
        var ee = ComputeDH(_ephemeralKey!, remoteEphPubKey);

        // Also compute es = X25519(responder_static, initiator_ephemeral)
        using var localStaticKey = Key.Import(X25519Algo, _localStaticPrivate, KeyBlobFormat.RawPrivateKey,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var es = ComputeDH(localStaticKey, PublicKey.Import(X25519Algo, _remoteEphemeralPublic!, KeyBlobFormat.RawPublicKey));

        var combinedKey = CombineKeys(ee, es);
        _remoteStaticPublic = SimpleDecrypt(message3, combinedKey);

        Log.Debug("Noise: Responder processed Message 3 — handshake complete");
    }

    /// <summary>
    /// Derive the final NoiseSession with transport keys.
    /// Must be called after the 3-message handshake is complete.
    /// </summary>
    public NoiseSession DeriveSession(Guid peerId, bool isInitiator)
    {
        if (_remoteStaticPublic is null || _ephemeralKey is null || _remoteEphemeralPublic is null)
            throw new InvalidOperationException("Handshake not complete");

        // Final shared secret: X25519(local_static, remote_static)
        using var localStaticKey = Key.Import(X25519Algo, _localStaticPrivate, KeyBlobFormat.RawPrivateKey,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var remoteStaticPubKey = PublicKey.Import(X25519Algo, _remoteStaticPublic, KeyBlobFormat.RawPublicKey);
        var ss = ComputeDH(localStaticKey, remoteStaticPubKey);

        var (sendKey, recvKey) = KeyDerivation.DeriveTransportKeys(ss, isInitiator);

        return new NoiseSession
        {
            PeerId = peerId,
            SendKey = sendKey,
            ReceiveKey = recvKey,
            RemoteStaticPublicKey = _remoteStaticPublic,
            IsEstablished = true
        };
    }

    // ---- Helpers ----

    private static byte[] ComputeDH(Key privateKey, PublicKey publicKey)
    {
        var shared = X25519Algo.Agree(privateKey, publicKey);
        return shared is null
            ? throw new CryptographicException("X25519 key agreement failed")
            : shared.Export(SharedSecretBlobFormat.RawSharedSecret);
    }

    private static byte[] SimpleEncrypt(byte[] plaintext, byte[] key)
    {
        using var aeadKey = Key.Import(ChaCha, key, KeyBlobFormat.RawSymmetricKey,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var nonceBytes = new byte[ChaCha.NonceSize];
        return ChaCha.Encrypt(aeadKey, nonceBytes, Array.Empty<byte>(), plaintext);
    }

    private static byte[] SimpleDecrypt(byte[] ciphertext, byte[] key)
    {
        using var aeadKey = Key.Import(ChaCha, key, KeyBlobFormat.RawSymmetricKey,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var nonceBytes = new byte[ChaCha.NonceSize];
        return ChaCha.Decrypt(aeadKey, nonceBytes, Array.Empty<byte>(), ciphertext)
            ?? throw new CryptographicException("Decryption failed");
    }

    private static byte[] CombineKeys(byte[] a, byte[] b)
    {
        return KeyDerivation.DeriveKey(
            a.Concat(b).ToArray(),
            "meshIt-combine"u8.ToArray(), 32);
    }
}
