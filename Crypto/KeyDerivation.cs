using System.Security.Cryptography;
using NSec.Cryptography;
using Serilog;

namespace meshIt.Crypto;

/// <summary>
/// HKDF (HMAC-based Key Derivation Function) for deriving transport keys
/// from Noise handshake shared secrets.
/// </summary>
public static class KeyDerivation
{
    /// <summary>
    /// Derive two 32-byte transport keys (send + receive) from a shared secret.
    /// Uses HKDF-SHA256: Extract → Expand with "meshIt-send" / "meshIt-recv" info.
    /// </summary>
    public static (byte[] sendKey, byte[] receiveKey) DeriveTransportKeys(
        byte[] sharedSecret, bool isInitiator)
    {
        var key1 = HkdfExpand(sharedSecret, "meshIt-key-1"u8.ToArray(), 32);
        var key2 = HkdfExpand(sharedSecret, "meshIt-key-2"u8.ToArray(), 32);

        // Initiator sends on key1, receives on key2. Responder is reversed.
        return isInitiator ? (key1, key2) : (key2, key1);
    }

    /// <summary>
    /// Derive a single 32-byte key from input keying material using HKDF-SHA256.
    /// </summary>
    public static byte[] DeriveKey(byte[] ikm, byte[] info, int length = 32)
    {
        return HkdfExpand(ikm, info, length);
    }

    private static byte[] HkdfExpand(byte[] ikm, byte[] info, int length)
    {
        // HKDF-Extract (using zero salt)
        var prk = HMACSHA256.HashData(new byte[32], ikm);

        // HKDF-Expand
        var result = new byte[length];
        var t = Array.Empty<byte>();
        var offset = 0;
        byte counter = 1;

        while (offset < length)
        {
            var input = new byte[t.Length + info.Length + 1];
            Array.Copy(t, 0, input, 0, t.Length);
            Array.Copy(info, 0, input, t.Length, info.Length);
            input[^1] = counter++;

            t = HMACSHA256.HashData(prk, input);
            var toCopy = Math.Min(t.Length, length - offset);
            Array.Copy(t, 0, result, offset, toCopy);
            offset += toCopy;
        }

        return result;
    }
}
