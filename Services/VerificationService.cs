using System.Drawing;
using meshIt.Models;
using QRCoder;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Generates and parses QR codes for out-of-band identity verification.
/// QR format: meshit://verify?fp={fingerprint}&nick={nickname}
/// </summary>
public class VerificationService
{
    /// <summary>
    /// Generate a QR code PNG byte array for the given identity.
    /// </summary>
    public byte[] GenerateQrCodePng(UserIdentity identity)
    {
        var verifyUrl = $"meshit://verify?fp={identity.Fingerprint}" +
                        $"&nick={Uri.EscapeDataString(identity.Nickname)}";

        using var qrGenerator = new QRCodeGenerator();
        var qrCodeData = qrGenerator.CreateQrCode(verifyUrl, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(qrCodeData);
        var pngBytes = qrCode.GetGraphic(10, new byte[] { 124, 77, 255 }, new byte[] { 15, 15, 35 });

        Log.Debug("Generated QR code for {Fp} ({Size} bytes)", identity.ShortFingerprint, pngBytes.Length);
        return pngBytes;
    }

    /// <summary>
    /// Parse a meshit:// verification URL.
    /// Returns (fingerprint, nickname) or nulls if parsing fails.
    /// </summary>
    public (string? fingerprint, string? nickname) ParseVerificationUrl(string url)
    {
        try
        {
            if (!url.StartsWith("meshit://verify")) return (null, null);

            var uri = new Uri(url);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            return (query["fp"], query["nick"]);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse verification URL");
            return (null, null);
        }
    }

    /// <summary>
    /// Verify that a fingerprint matches a given public key.
    /// </summary>
    public bool VerifyFingerprint(string fingerprint, byte[] publicKey)
    {
        var expected = System.Security.Cryptography.SHA256.HashData(publicKey);
        var expectedHex = Convert.ToHexString(expected).ToLowerInvariant();
        return fingerprint.Equals(expectedHex, StringComparison.OrdinalIgnoreCase);
    }
}
