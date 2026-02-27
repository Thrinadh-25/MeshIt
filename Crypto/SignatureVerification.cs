using NSec.Cryptography;
using Serilog;

namespace meshIt.Crypto;

/// <summary>
/// Ed25519 digital signature helpers for packet authentication.
/// </summary>
public static class SignatureVerification
{
    private static readonly SignatureAlgorithm Ed25519 = SignatureAlgorithm.Ed25519;

    /// <summary>Sign data with an Ed25519 private key. Returns 64-byte signature.</summary>
    public static byte[] Sign(byte[] data, byte[] privateKeyBytes)
    {
        using var key = Key.Import(Ed25519, privateKeyBytes, KeyBlobFormat.RawPrivateKey);
        return Ed25519.Sign(key, data);
    }

    /// <summary>Verify an Ed25519 signature against data and public key.</summary>
    public static bool Verify(byte[] data, byte[] signature, byte[] publicKeyBytes)
    {
        try
        {
            var pubKey = PublicKey.Import(Ed25519, publicKeyBytes, KeyBlobFormat.RawPublicKey);
            return Ed25519.Verify(pubKey, data, signature);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Signature verification failed");
            return false;
        }
    }

    /// <summary>Generate an Ed25519 keypair. Returns (privateKey, publicKey).</summary>
    public static (byte[] privateKey, byte[] publicKey) GenerateSigningKeyPair()
    {
        using var key = Key.Create(Ed25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var priv = key.Export(KeyBlobFormat.RawPrivateKey);
        var pub = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        return (priv, pub);
    }
}
