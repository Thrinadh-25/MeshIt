using System.Security.Cryptography;

namespace meshIt.Models;

/// <summary>
/// Represents the local user's cryptographic identity.
/// Contains Curve25519 (Noise) and Ed25519 (signing) keypairs.
/// </summary>
public class UserIdentity
{
    // --- Noise Static Key (Curve25519 / X25519) - for encryption ---
    public byte[] NoiseStaticPrivateKey { get; set; } = new byte[32];
    public byte[] NoiseStaticPublicKey { get; set; } = new byte[32];

    // --- Signing Key (Ed25519) - for packet authentication ---
    public byte[] SigningPrivateKey { get; set; } = Array.Empty<byte>();
    public byte[] SigningPublicKey { get; set; } = new byte[32];

    // --- User-chosen nickname ---
    public string Nickname { get; set; } = string.Empty;

    /// <summary>Full SHA-256 fingerprint of the Noise static public key (64 hex chars).</summary>
    public string Fingerprint => ComputeFingerprint();

    /// <summary>Short fingerprint for display (first 8 hex chars).</summary>
    public string ShortFingerprint => Fingerprint[..8];

    private string ComputeFingerprint()
    {
        var hash = SHA256.HashData(NoiseStaticPublicKey);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
