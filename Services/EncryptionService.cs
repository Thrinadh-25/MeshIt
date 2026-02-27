using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// AES-256-CBC encryption/decryption using a pre-shared key.
/// In Phase 1 (MVP) a hardcoded key is used for simplicity.
/// Phase 2 will replace this with ECDH Diffie-Hellman key exchange.
/// </summary>
public static class EncryptionService
{
    /// <summary>
    /// Temporary pre-shared 32-byte key (AES-256 requires exactly 32 bytes).
    /// This is the SAME key on every meshIt instance so all users can communicate.
    /// ⚠️ Replace with per-peer ECDH keys in Phase 2.
    /// </summary>
    private static readonly byte[] SharedKey =
        Encoding.UTF8.GetBytes("meshIt-pre-shared-key-32bytes!!");  // exactly 32 ASCII chars = 32 bytes

    /// <summary>
    /// Encrypt <paramref name="plainData"/> using AES-256-CBC.
    /// The returned byte array is: [16-byte IV][ciphertext].
    /// </summary>
    public static byte[] Encrypt(byte[] plainData)
    {
        try
        {
            using var aes = Aes.Create();
            aes.Key = SharedKey;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.GenerateIV(); // random IV per message

            using var encryptor = aes.CreateEncryptor();
            var cipherText = encryptor.TransformFinalBlock(plainData, 0, plainData.Length);

            // Prepend IV so the receiver can decrypt
            var result = new byte[aes.IV.Length + cipherText.Length];
            Array.Copy(aes.IV, 0, result, 0, aes.IV.Length);
            Array.Copy(cipherText, 0, result, aes.IV.Length, cipherText.Length);
            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Encryption failed");
            throw;
        }
    }

    /// <summary>
    /// Decrypt data previously encrypted with <see cref="Encrypt"/>.
    /// Expects the first 16 bytes to be the IV.
    /// </summary>
    public static byte[] Decrypt(byte[] encryptedData)
    {
        try
        {
            using var aes = Aes.Create();
            aes.Key = SharedKey;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            // Extract IV from the first 16 bytes
            var iv = new byte[16];
            Array.Copy(encryptedData, 0, iv, 0, 16);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(encryptedData, 16, encryptedData.Length - 16);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Decryption failed");
            throw;
        }
    }
}
