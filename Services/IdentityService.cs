using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using meshIt.Crypto;
using meshIt.Models;
using NSec.Cryptography;
using Serilog;

namespace meshIt.Services;

/// <summary>
/// Generates, loads, and persists the user's cryptographic identity.
/// Keys are stored encrypted with DPAPI at %APPDATA%\meshIt\identity.json.
/// </summary>
public class IdentityService
{
    private static readonly string AppDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "meshIt");
    private static readonly string IdentityPath = Path.Combine(AppDataDir, "identity.json");

    public UserIdentity? CurrentIdentity { get; private set; }

    /// <summary>
    /// Load identity from disk or generate a new one if first launch.
    /// </summary>
    public UserIdentity LoadOrCreateIdentity(string? nickname = null)
    {
        Directory.CreateDirectory(AppDataDir);

        if (File.Exists(IdentityPath))
        {
            try
            {
                var json = File.ReadAllText(IdentityPath);
                var stored = JsonSerializer.Deserialize<StoredIdentity>(json);
                if (stored is not null)
                {
                    CurrentIdentity = new UserIdentity
                    {
                        NoiseStaticPrivateKey = ProtectedData.Unprotect(
                            Convert.FromBase64String(stored.NoisePrivateKeyProtected),
                            null, DataProtectionScope.CurrentUser),
                        NoiseStaticPublicKey = Convert.FromBase64String(stored.NoisePublicKey),
                        SigningPrivateKey = ProtectedData.Unprotect(
                            Convert.FromBase64String(stored.SigningPrivateKeyProtected),
                            null, DataProtectionScope.CurrentUser),
                        SigningPublicKey = Convert.FromBase64String(stored.SigningPublicKey),
                        Nickname = stored.Nickname
                    };

                    if (!string.IsNullOrEmpty(nickname))
                        CurrentIdentity.Nickname = nickname;

                    Log.Information("Identity loaded — fingerprint {Fp}", CurrentIdentity.ShortFingerprint);
                    return CurrentIdentity;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load stored identity, generating new one");
            }
        }

        // Generate brand-new identity
        CurrentIdentity = GenerateNewIdentity(nickname ?? "User");
        SaveIdentity(CurrentIdentity);
        Log.Information("New identity generated — fingerprint {Fp}", CurrentIdentity.ShortFingerprint);
        return CurrentIdentity;
    }

    /// <summary>Save identity to disk (DPAPI-protected private keys).</summary>
    public void SaveIdentity(UserIdentity identity)
    {
        Directory.CreateDirectory(AppDataDir);

        var stored = new StoredIdentity
        {
            NoisePrivateKeyProtected = Convert.ToBase64String(
                ProtectedData.Protect(identity.NoiseStaticPrivateKey, null, DataProtectionScope.CurrentUser)),
            NoisePublicKey = Convert.ToBase64String(identity.NoiseStaticPublicKey),
            SigningPrivateKeyProtected = Convert.ToBase64String(
                ProtectedData.Protect(identity.SigningPrivateKey, null, DataProtectionScope.CurrentUser)),
            SigningPublicKey = Convert.ToBase64String(identity.SigningPublicKey),
            Nickname = identity.Nickname
        };

        var json = JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(IdentityPath, json);
    }

    private static UserIdentity GenerateNewIdentity(string nickname)
    {
        // X25519 keypair for Noise
        var x25519 = KeyAgreementAlgorithm.X25519;
        using var noiseKey = Key.Create(x25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });

        // Ed25519 keypair for signatures
        var (sigPriv, sigPub) = SignatureVerification.GenerateSigningKeyPair();

        return new UserIdentity
        {
            NoiseStaticPrivateKey = noiseKey.Export(KeyBlobFormat.RawPrivateKey),
            NoiseStaticPublicKey = noiseKey.PublicKey.Export(KeyBlobFormat.RawPublicKey),
            SigningPrivateKey = sigPriv,
            SigningPublicKey = sigPub,
            Nickname = nickname
        };
    }

    // Internal serialization model (private keys stored DPAPI-encrypted)
    private class StoredIdentity
    {
        public string NoisePrivateKeyProtected { get; set; } = "";
        public string NoisePublicKey { get; set; } = "";
        public string SigningPrivateKeyProtected { get; set; } = "";
        public string SigningPublicKey { get; set; } = "";
        public string Nickname { get; set; } = "";
    }
}
